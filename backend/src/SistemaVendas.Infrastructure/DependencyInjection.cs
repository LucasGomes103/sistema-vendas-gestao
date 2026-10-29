using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.IA;
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

        // Sem token da Meta, o envio é simulado: dá para desenvolver tudo sem número conectado.
        var whatsApp = config.GetSection(WhatsAppOptions.Secao);
        services.Configure<WhatsAppOptions>(whatsApp);
        services.AddSingleton<WhatsAppSimulado>();
        if (string.Equals(whatsApp[nameof(WhatsAppOptions.Provedor)], "Local", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IWhatsAppCliente, WhatsAppLocalCliente>(c => c.Timeout = TimeSpan.FromSeconds(20));
        else if (string.IsNullOrWhiteSpace(whatsApp[nameof(WhatsAppOptions.AccessToken)]))
            services.AddSingleton<IWhatsAppCliente>(sp => sp.GetRequiredService<WhatsAppSimulado>());
        else
            services.AddHttpClient<IWhatsAppCliente, WhatsAppCloudCliente>(c => c.Timeout = TimeSpan.FromSeconds(20));

        // Atendente: Gemini quando há chave; sem ela, respostas por regras (também usadas se o Gemini falhar).
        var gemini = config.GetSection(GeminiOptions.Secao);
        services.Configure<GeminiOptions>(gemini);
        services.Configure<VitrineOptions>(config.GetSection(VitrineOptions.Secao));
        services.AddSingleton<AtendentePorRegras>();
        services.AddScoped<IFerramentasAtendimento, FerramentasAtendimento>();
        if (string.IsNullOrWhiteSpace(gemini[nameof(GeminiOptions.ApiKey)]))
            services.AddSingleton<IAtendenteIA>(sp => sp.GetRequiredService<AtendentePorRegras>());
        else
            services.AddHttpClient<IAtendenteIA, GeminiAtendente>(c => c.Timeout = TimeSpan.FromSeconds(30));

        return services;
    }
}
