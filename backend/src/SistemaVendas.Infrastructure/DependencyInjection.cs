using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Infrastructure.Dados;
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
        if (string.IsNullOrWhiteSpace(whatsApp[nameof(WhatsAppOptions.AccessToken)]))
            services.AddSingleton<IWhatsAppCliente, WhatsAppSimulado>();
        else
            services.AddHttpClient<IWhatsAppCliente, WhatsAppCloudCliente>(c => c.Timeout = TimeSpan.FromSeconds(20));

        return services;
    }
}
