using SistemaVendas.Infrastructure.Entregas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Email;
using SistemaVendas.Infrastructure.IA;
using SistemaVendas.Infrastructure.Pagamentos;
using SistemaVendas.Infrastructure.Seguranca;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conexao = config.GetConnectionString("Banco")
            ?? throw new InvalidOperationException("ConnectionStrings:Banco não configurada.");

        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(conexao)
            .UseSnakeCaseNamingConvention());
        // Credenciais das lojas, cifradas com a chave mestra da plataforma (Plataforma__ChaveMestra).
        services.Configure<PlataformaOptions>(config.GetSection(PlataformaOptions.Secao));
        services.AddSingleton<Cofre>();
        services.AddScoped<ServicoCredenciais>();
        services.AddScoped<CredenciaisDaLoja>();
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Secao));
        services.AddSingleton<IEnvioEmail, EnvioEmailSmtp>();
        services.AddScoped<ServicoFunil>();
        services.AddScoped<ServicoReservas>();
        services.AddScoped<ServicoPedidos>();
        // Mensalidade das lojas: plano, teste grátis, carência e bloqueio.
        services.Configure<AssinaturasOptions>(config.GetSection(AssinaturasOptions.Secao));
        services.AddScoped<ServicoAssinatura>();
<<<<<<< HEAD
        services.AddScoped<ServicoLimites>();
=======
>>>>>>> origin/main

        // Cartão pelo Mercado Pago: só liga com MercadoPago__AccessToken. O endereço do webhook vem do Render.
        services.Configure<MercadoPagoOptions>(config.GetSection(MercadoPagoOptions.Secao));
        services.PostConfigure<MercadoPagoOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.UrlPublicaApi))
                o.UrlPublicaApi = config["RENDER_EXTERNAL_URL"];
        });
        services.AddHttpClient<IMercadoPago, MercadoPagoCliente>(c => c.Timeout = TimeSpan.FromSeconds(15));
        // Botão "Conectar Mercado Pago" (OAuth com a aplicação da plataforma).
        services.Configure<MercadoPagoOAuthOptions>(config.GetSection(MercadoPagoOAuthOptions.Secao));
        services.AddHttpClient<ConexaoMercadoPago>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<ServicoEntregas>();
        services.AddScoped<ServicoMetricas>();

        // Loja sem token da Meta: envio simulado (dá para desenvolver tudo sem número conectado). O token é da loja.
        var whatsApp = config.GetSection(WhatsAppOptions.Secao);
        services.Configure<WhatsAppOptions>(whatsApp);
        services.AddSingleton<WhatsAppSimulado>();
        // A tela de conexão do WhatsApp usa IHttpClientFactory mesmo no modo simulado (sem ela a API nem sobe).
        services.AddHttpClient();
        services.AddHttpClient<WhatsAppLocalCliente>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<WhatsAppCloudCliente>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IWhatsAppCliente, WhatsAppRoteador>();
        services.AddHttpClient<VerificadorWhatsApp>(c => c.Timeout = TimeSpan.FromSeconds(15));
        // Botão "Conectar WhatsApp" (Embedded Signup com o app da plataforma).
        services.Configure<EmbeddedSignupOptions>(config.GetSection(EmbeddedSignupOptions.Secao));
        services.AddHttpClient<ConexaoWhatsApp>(c => c.Timeout = TimeSpan.FromSeconds(20));

        // Atendente: Gemini quando há chave; sem ela, respostas por regras (também usadas se o Gemini falhar).
        var gemini = config.GetSection(GeminiOptions.Secao);
        services.Configure<GeminiOptions>(gemini);
        services.Configure<VitrineOptions>(config.GetSection(VitrineOptions.Secao));
        services.AddSingleton<AtendentePorRegras>();
        services.Configure<GeoOptions>(config.GetSection(GeoOptions.Secao));
        // Nominatim pede um User-Agent que identifique o aplicativo (política de uso do OpenStreetMap).
        services.AddHttpClient<MapasGratuitos>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("sistema-vendas-gestao/0.2 (frete por km)");
        });
        services.AddScoped<IGeolocalizacao, Geolocalizacao>();
        services.AddScoped<ServicoFrete>();
        services.AddScoped<IFerramentasAtendimento, FerramentasAtendimento>();
        services.Configure<LimitesIAOptions>(config.GetSection(LimitesIAOptions.Secao));
        services.AddScoped<ServicoLimiteIa>();
        if (string.IsNullOrWhiteSpace(gemini[nameof(GeminiOptions.ApiKey)]))
            services.AddSingleton<IAtendenteIA>(sp => sp.GetRequiredService<AtendentePorRegras>());
        else
        {
            // IA paga pela plataforma: respeita o limite de respostas do mês de cada loja.
            services.AddHttpClient<GeminiAtendente>(c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddScoped<IAtendenteIA, AtendenteComLimite>();
        }

        return services;
    }
}
