using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Domain.Atendimento;

public enum DirecaoMensagem
{
    Entrada,
    Saida
}

public enum AutorMensagem
{
    Cliente,
    Robo,
    Vendedor,
    Sistema
}

public enum StatusMensagem
{
    Pendente,
    Enviada,
    Entregue,
    Lida,
    Falhou,
    /// <summary>Mensagem recebida do cliente.</summary>
    Recebida
}

public enum CategoriaMensagem
{
    Servico,
    Utilidade,
    Marketing
}

public class Cliente : EntidadeDaLoja
{
    public string? Nome { get; set; }

    /// <summary>Telefone no formato da Meta (só dígitos, com DDI). Único por loja.</summary>
    public required string Telefone { get; set; }

    public string? Cep { get; set; }
    public string? Endereco { get; set; }
    public bool AceitaMarketing { get; set; }
    public DateTimeOffset? AceitaMarketingEm { get; set; }
    public DateTimeOffset? AnonimizadoEm { get; set; }
}

public class Conversa : EntidadeDaLoja
{
    public Guid ClienteId { get; set; }

    /// <summary>Controla a janela de 24 h da Meta: dentro dela a loja escreve livremente.</summary>
    public DateTimeOffset? UltimaMsgClienteEm { get; set; }

    public DateTimeOffset? UltimaMensagemEm { get; set; }
    public bool Aberta { get; set; } = true;

    /// <summary>Conversa criada pelo simulador do painel: nunca envia nada pela Meta.</summary>
    public bool Simulada { get; set; }

    /// <summary>Mensagens do cliente ainda não vistas no painel.</summary>
    public int NaoLidas { get; set; }

    /// <summary>Vendedor assumiu: o robô não responde até a conversa ser devolvida.</summary>
    public bool RoboPausado { get; set; }

    /// <summary>Há mensagem do cliente esperando resposta do robô.</summary>
    public bool AguardandoRobo { get; set; }

    /// <summary>Cliente quente: quer comprar, reservar ou falar com uma pessoa. O vendedor recebe aviso.</summary>
    public bool ChamouVendedor { get; set; }
    public string? MotivoVendedor { get; set; }
    public DateTimeOffset? ChamouVendedorEm { get; set; }

    /// <summary>Quando um vendedor assumiu ou respondeu o cliente quente. Vazio = cliente "sem dono".</summary>
    public DateTimeOffset? AtendidoEm { get; set; }

    /// <summary>
    /// Vendedor que cuida da conversa: quem assumiu ou respondeu primeiro, ou quem o dono alocou. Os outros vendedores
    /// veem a conversa, mas não respondem por cima; o dono pode responder e transferir. Vazio = ninguém pegou ainda.
    /// </summary>
    public Guid? ResponsavelId { get; set; }
    public DateTimeOffset? ResponsavelDesde { get; set; }

    /// <summary>Avisos já dados sobre o cliente quente sem dono: 1 = lembrete de 15 min, 2 = escalada de 1 h.</summary>
    public int AvisosQuente { get; set; }

    // Funil: cada conversa é um card do kanban.
    public EtapaCard Etapa { get; set; } = EtapaCard.Novo;
    public int FollowupsEnviados { get; set; }
    public AcaoFunil? ProximaAcao { get; set; }
    public DateTimeOffset? ProximaAcaoEm { get; set; }
    public MotivoPerda? MotivoPerda { get; set; }
    public decimal? ValorEstimado { get; set; }

    public static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    public bool DentroDaJanela(DateTimeOffset agora) =>
        UltimaMsgClienteEm is { } ultima && agora - ultima < Janela;

    /// <summary>Mensagem nova do cliente: o robô responde, a menos que um vendedor tenha assumido.</summary>
    public void ReceberDoCliente(DateTimeOffset em)
    {
        UltimaMsgClienteEm = em;
        UltimaMensagemEm = em;
        NaoLidas++;
        AguardandoRobo = !RoboPausado;
    }

    /// <summary>
    /// Robô ou cliente pediram uma pessoa: marca para o vendedor e pausa o robô. Com pausarRobo falso
    /// o robô segue respondendo (ex.: pediu o CEP e precisa calcular o frete quando ele chegar).
    /// </summary>
    public void ChamarVendedor(string motivo, DateTimeOffset agora, bool pausarRobo = true)
    {
        ChamouVendedor = true;
        MotivoVendedor = motivo;
        ChamouVendedorEm ??= agora;
        if (pausarRobo)
            RoboPausado = true;
        AguardandoRobo = false;
    }

    /// <summary>
    /// Novo motivo para um vendedor olhar a conversa (ex.: cliente mandou comprovante): o alerta de
    /// cliente sem dono recomeça, mesmo que alguém já tenha assumido antes.
    /// </summary>
    public void AlertarDeNovo(string motivo, DateTimeOffset agora)
    {
        ChamouVendedor = true;
        MotivoVendedor = motivo;
        ChamouVendedorEm = agora;
        AtendidoEm = null;
        AvisosQuente = 0;
        RoboPausado = true;
        AguardandoRobo = false;
    }

    /// <summary>Venda fechada: sai da lista de clientes quentes, mas o robô continua pausado.</summary>
    public void EncerrarAlerta()
    {
        ChamouVendedor = false;
        MotivoVendedor = null;
        ChamouVendedorEm = null;
        AtendidoEm = null;
        AvisosQuente = 0;
    }

    /// <summary>
    /// Vendedor assumiu (ou respondeu): o robô para e o cliente quente deixa de estar sem dono. Quem assume primeiro
    /// vira o responsável; se a conversa já tem responsável, ele continua (o dono troca pelo Transferir).
    /// </summary>
    public void Assumir(DateTimeOffset? em = null, Guid? usuarioId = null)
    {
        var quando = em ?? DateTimeOffset.UtcNow;
        if (ChamouVendedor)
            AtendidoEm ??= quando;
        if (ResponsavelId is null && usuarioId is { } u)
        {
            ResponsavelId = u;
            ResponsavelDesde = quando;
        }
        RoboPausado = true;
        AguardandoRobo = false;
    }

    /// <summary>A conversa está com outro vendedor: só ele ou o dono respondem.</summary>
    public bool ComOutro(Guid? usuarioId, bool ehDono) =>
        !ehDono && ResponsavelId is { } r && r != usuarioId;

    /// <summary>
    /// Dono aloca a conversa para outro vendedor (ou tira o responsável, com null): o novo segue o atendimento
    /// e o robô continua pausado.
    /// </summary>
    public void Transferir(Guid? usuarioId, DateTimeOffset agora)
    {
        if (ResponsavelId == usuarioId)
            return;
        ResponsavelId = usuarioId;
        ResponsavelDesde = usuarioId is null ? null : agora;
        if (usuarioId is not null)
        {
            if (ChamouVendedor)
                AtendidoEm = agora;
            RoboPausado = true;
            AguardandoRobo = false;
        }
        else if (ChamouVendedor)
        {
            // Sem responsável de novo: volta a ser cliente quente sem dono, com os avisos do começo.
            AtendidoEm = null;
            AvisosQuente = 0;
            ChamouVendedorEm = agora;
        }
    }

    /// <summary>Vendedor devolve ao robô; o histórico fica guardado.</summary>
    public void DevolverAoRobo()
    {
        ResponsavelId = null;
        ResponsavelDesde = null;
        RoboPausado = false;
        ChamouVendedor = false;
        MotivoVendedor = null;
        ChamouVendedorEm = null;
        AtendidoEm = null;
        AvisosQuente = 0;
    }
}

public class Mensagem : EntidadeDaLoja
{
    public Guid ConversaId { get; set; }

    /// <summary>Id da mensagem na Meta. Único: reenvio de webhook não duplica nada.</summary>
    public string? Wamid { get; set; }

    public DirecaoMensagem Direcao { get; set; }
    public AutorMensagem Autor { get; set; }
    public string Tipo { get; set; } = "text";
    public string? Template { get; set; }
    public CategoriaMensagem Categoria { get; set; } = CategoriaMensagem.Servico;
    public required string Conteudo { get; set; }
    public StatusMensagem Status { get; set; }
    public decimal? CustoEstimado { get; set; }
    public int Tentativas { get; set; }
    public DateTimeOffset? ProximaTentativaEm { get; set; }
    public string? Erro { get; set; }
    public Guid? UsuarioId { get; set; }
}

/// <summary>Fila de entrada: o corpo cru do webhook é gravado e respondido na hora; o worker processa depois.</summary>
public class WebhookRecebido : Entidade
{
    public required string Origem { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset? ProcessadoEm { get; set; }
    public string? Erro { get; set; }
}
