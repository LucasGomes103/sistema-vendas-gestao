using SistemaVendas.Domain.Comum;

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

    public static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    public bool DentroDaJanela(DateTimeOffset agora) =>
        UltimaMsgClienteEm is { } ultima && agora - ultima < Janela;
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
