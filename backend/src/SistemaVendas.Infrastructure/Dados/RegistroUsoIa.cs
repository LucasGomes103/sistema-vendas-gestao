using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Soma cada resposta do robô na linha do dia da loja (uso_ia), num upsert só: seguro com vários workers.</summary>
public static class RegistroUsoIa
{
    public static Task RegistrarAsync(AppDbContext db, Guid lojaId, ConsumoIa? consumo, DateTimeOffset quando, CancellationToken ct)
    {
        var dia = DateOnly.FromDateTime(quando.ToOffset(TimeSpan.FromHours(-3)).Date); // dia no horário de Brasília
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into uso_ia (id, criado_em, loja_id, dia, respostas, respostas_ia, chamadas, tokens_entrada, tokens_saida, falhas)
            values ({Guid.CreateVersion7()}, {quando}, {lojaId}, {dia}, 1, {(consumo is { Falhou: false } ? 1 : 0)}, {consumo?.Chamadas ?? 0}, {consumo?.TokensEntrada ?? 0},
                    {consumo?.TokensSaida ?? 0}, {(consumo?.Falhou == true ? 1 : 0)})
            on conflict (loja_id, dia) do update set
                respostas = uso_ia.respostas + 1,
                respostas_ia = uso_ia.respostas_ia + excluded.respostas_ia,
                chamadas = uso_ia.chamadas + excluded.chamadas,
                tokens_entrada = uso_ia.tokens_entrada + excluded.tokens_entrada,
                tokens_saida = uso_ia.tokens_saida + excluded.tokens_saida,
                falhas = uso_ia.falhas + excluded.falhas
            """, ct);
    }
}
