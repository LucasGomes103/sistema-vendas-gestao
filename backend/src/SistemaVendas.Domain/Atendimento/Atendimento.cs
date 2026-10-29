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

    /// <summary>Robô ou cliente pediram uma pessoa: pausa o robô e marca para o vendedor.</summary>
    public void ChamarVendedor(string motivo, DateTimeOffset agora)
    {
        ChamouVendedor = true;
        MotivoVendedor = motivo;
        ChamouVendedorEm ??= agora;
        RoboPausado = true;
        AguardandoRobo = false;
    }

    public void Assumir()
    {
        RoboPausado = true;
        AguardandoRobo = false;
    }

    /// <summary>Vendedor devolve ao robô; o histórico fica guardado.</summary>
    public void DevolverAoRobo()
    {
        RoboPausado = false;
        ChamouVendedor = false;
        MotivoVendedor = null;
        ChamouVendedorEm = null;
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
