using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

public enum StatusLoja
{
    /// <summary>Em uso.</summary>
    Ativa,
    /// <summary>Cadastrada pelo site, esperando o dono confirmar o e-mail.</summary>
    AguardandoConfirmacao,
    /// <summary>Suspensa pelo administrador da plataforma: ninguém entra, a vitrine some e o robô para.</summary>
    Suspensa
}

public class Loja : Entidade
{
    public StatusLoja Status { get; set; } = StatusLoja.Ativa;

    /// <summary>Motivo anotado pelo administrador da plataforma ao suspender (ex.: "mensalidade atrasada").</summary>
    public string? MotivoSuspensao { get; set; }
    public DateTimeOffset? SuspensaEm { get; set; }

    /// <summary>Tipo de loja escolhido no cadastro (ex.: "Aquarismo", "Roupas"). Usado na fase 4 para o robô.</summary>
    public string? Segmento { get; set; }

    public required string Nome { get; set; }

    /// <summary>Identificador usado na URL do catálogo público.</summary>
    public required string Slug { get; set; }

    /// <summary>phone_number_id da WhatsApp Cloud API (preenchido quando o número for conectado).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Número do WhatsApp da loja (só dígitos, com DDI), usado no botão "pedir pelo WhatsApp" da vitrine.</summary>
    public string? WhatsAppNumero { get; set; }

    /// <summary>
    /// Trava de segurança da conexão local (QR code): falso = o sistema só lê e responde os números permitidos.
    /// Só ligar em número dedicado à loja.
    /// </summary>
    public bool AtenderTodos { get; set; }

    /// <summary>Números (só dígitos, com DDI) que podem conversar com o robô quando AtenderTodos está desligado.</summary>
    public List<string> NumerosPermitidos { get; set; } = [];

    public string Fuso { get; set; } = "America/Sao_Paulo";

    /// <summary>
    /// Respostas da IA por mês que a plataforma paga para esta loja. Vazio: o padrão da plataforma
    /// (IA__LimiteRespostasMes). 0: sem limite. Acima do limite o robô responde por regras.
    /// </summary>
    public int? LimiteRespostasIAMes { get; set; }

    /// <summary>Chave Pix comum da loja (sem provedor de pagamento): o sistema gera o copia e cola de cada pedido.</summary>
    public string? PixChave { get; set; }

    /// <summary>Nome de quem recebe, como aparece no banco do cliente (até 25 letras).</summary>
    public string? PixNome { get; set; }

    /// <summary>Cidade de quem recebe (até 15 letras).</summary>
    public string? PixCidade { get; set; }
    public TimeOnly HorarioInicio { get; set; } = new(9, 0);
    public TimeOnly HorarioFim { get; set; } = new(20, 0);

    public bool PodeAtender(string telefone) => AtenderTodos || NumerosPermitidos.Contains(telefone);

    /// <summary>Normaliza a lista: só dígitos, sem repetidos; aceita número sem DDI e completa com 55.</summary>
    public static List<string> NormalizarNumeros(IEnumerable<string> numeros) =>
        numeros
            .Select(n => new string(n.Where(char.IsDigit).ToArray()))
            .Where(n => n.Length >= 10)
            .Select(n => n.Length <= 11 ? "55" + n : n)
            .Distinct()
            .ToList();
}
