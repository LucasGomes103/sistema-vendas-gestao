using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SistemaVendas.Application.Comum;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>
/// Usado só pelo `dotnet ef` para gerar migrações sem compilar a API
/// (assim dá para criar migração com a API rodando no Visual Studio).
/// </summary>
public class AppDbContextDesignTime : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opcoes = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=sistema_vendas")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(opcoes, new SemLoja());
    }

    private sealed class SemLoja : ILojaAtual
    {
        public Guid? LojaId => null;
        public void Definir(Guid lojaId) { }
    }
}
