using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SistemaVendas.Infrastructure.Email;

/// <summary>
/// E-mail transacional por SMTP (grátis com uma conta Gmail e "senha de app", até ~500 por dia). Variáveis:
/// Email__Smtp__Host, Email__Smtp__Porta, Email__Smtp__Usuario, Email__Smtp__Senha, Email__Remetente.
/// Ou pelo Resend (camada grátis: 3.000 por mês), com Email__Resend__ApiKey: exige um domínio verificado no Resend
/// para mandar a qualquer pessoa; sem domínio ele só entrega no e-mail da própria conta.
/// Sem nenhum dos dois, nada é enviado: o texto aparece no log.
/// </summary>
public class EmailOptions
{
    public const string Secao = "Email";

    public string? Remetente { get; set; }
    public string NomeRemetente { get; set; } = "Sistema de Vendas";
    public SmtpOptions Smtp { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();

    public bool Configurado => !string.IsNullOrWhiteSpace(Smtp.Host) || Resend.Configurado;

    public class ResendOptions
    {
        public string? ApiKey { get; set; }
        public string Url { get; set; } = "https://api.resend.com/emails";
        public bool Configurado => !string.IsNullOrWhiteSpace(ApiKey);
    }

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

public class EnvioEmailSmtp(IOptions<EmailOptions> opcoes, ILogger<EnvioEmailSmtp> logger, IHttpClientFactory? http = null) : IEnvioEmail
{
    public bool Configurado => opcoes.Value.Configurado;

    public async Task<bool> EnviarAsync(string para, string assunto, string texto, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (!o.Configurado)
        {
            logger.LogWarning("E-mail não configurado (Email__Smtp__Host ou Email__Resend__ApiKey). Para {Para}: {Assunto}\n{Texto}", para, assunto, texto);
            return false;
        }
        if (o.Resend.Configurado && http is not null)
            return await EnviarPeloResendAsync(o, para, assunto, texto, ct);
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

    private async Task<bool> EnviarPeloResendAsync(EmailOptions o, string para, string assunto, string texto, CancellationToken ct)
    {
        try
        {
            using var cliente = http!.CreateClient();
            cliente.Timeout = TimeSpan.FromSeconds(15);
            using var req = new HttpRequestMessage(HttpMethod.Post, o.Resend.Url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.Resend.ApiKey);
            var de = $"{o.NomeRemetente} <{o.Remetente ?? "onboarding@resend.dev"}>";
            req.Content = System.Net.Http.Json.JsonContent.Create(new { from = de, to = new[] { para }, subject = assunto, text = texto });
            using var resp = await cliente.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return true;
            logger.LogError("Resend recusou o e-mail para {Para} ({Codigo}): {Erro}", para, (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
            return false;
        }
        catch (HttpRequestException e)
        {
            logger.LogError("Falha ao enviar e-mail pelo Resend para {Para}: {Erro}", para, e.Message);
            return false;
        }
    }
}
