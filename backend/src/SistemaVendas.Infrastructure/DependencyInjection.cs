using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Infrastructure.Dados;

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

        return services;
    }
}
