using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SistemaVendas.Infrastructure.Email;

/// <summary>
/// E-mail transacional por SMTP (grátis com uma conta Gmail e "senha de app", até ~500 por dia). Variáveis:
/// Email__Smtp__Host, Email__Smtp__Porta, Email__Smtp__Usuario, Email__Smtp__Senha, Email__Remetente.
/// Sem host, nada é enviado: o link aparece no log e o administrador da plataforma pode confirmar a loja à mão.
/// </summary>
public class EmailOptions
{
    public const string Secao = "Email";

    public string? Remetente { get; set; }
    public string NomeRemetente { get; set; } = "Sistema de Vendas";
    public SmtpOptions Smtp { get; set; } = new();

    public bool Configurado => !string.IsNullOrWhiteSpace(Smtp.Host);

    public class SmtpOptions
    {
        public string? Host { get; set; }
        public int Porta { get; set; } = 587;
        public string? Usuario { get; set; }
        public string? Senha { get; set; }
    }
}

public interface IEnvioEmail
{
    bool Configurado { get; }

    /// <summary>Envia o e-mail. Devolve false quando não deu (o motivo vai para o log).</summary>
    Task<bool> EnviarAsync(string para, string assunto, string texto, CancellationToken ct);
}

public class EnvioEmailSmtp(IOptions<EmailOptions> opcoes, ILogger<EnvioEmailSmtp> logger) : IEnvioEmail
{
    public bool Configurado => opcoes.Value.Configurado;

    public async Task<bool> EnviarAsync(string para, string assunto, string texto, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (!o.Configurado)
        {
            logger.LogWarning("E-mail não configurado (Email__Smtp__Host). Para {Para}: {Assunto}\n{Texto}", para, assunto, texto);
            return false;
        }
        try
        {
            using var smtp = new SmtpClient(o.Smtp.Host, o.Smtp.Porta)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = string.IsNullOrEmpty(o.Smtp.Usuario) ? null : new NetworkCredential(o.Smtp.Usuario, o.Smtp.Senha),
                Timeout = 15000
            };
            using var msg = new MailMessage(new MailAddress(o.Remetente ?? o.Smtp.Usuario!, o.NomeRemetente), new MailAddress(para))
            {
                Subject = assunto,
                Body = texto,
                IsBodyHtml = false
            };
            await smtp.SendMailAsync(msg, ct);
            return true;
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError("Falha ao enviar e-mail para {Para}: {Erro}", para, e.Message);
            return false;
        }
    }
}
