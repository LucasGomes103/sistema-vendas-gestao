using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>
/// Apaga uma loja e todas as linhas dela, numa transação só. As tabelas vêm do próprio banco (todas que têm a coluna
/// loja_id), na ordem das chaves estrangeiras: primeiro quem aponta, depois o apontado. Assim uma tabela nova entra
/// sozinha, sem ninguém lembrar de atualizar esta lista. Só apaga linhas com loja_id = a loja pedida.
/// </summary>
public static class ExclusaoDeLoja
{
    private const string SqlTabelas = """
        select c.relname
        from pg_attribute a
        join pg_class c on c.oid = a.attrelid
        join pg_namespace n on n.oid = c.relnamespace
        where a.attname = 'loja_id' and not a.attisdropped and c.relkind = 'r' and n.nspname = current_schema()
        """;

    // Pares (tabela que aponta, tabela apontada) entre tabelas do esquema atual.
    private const string SqlChaves = """
        select filho.relname, pai.relname
        from pg_constraint k
        join pg_class filho on filho.oid = k.conrelid
        join pg_class pai on pai.oid = k.confrelid
        join pg_namespace n on n.oid = filho.relnamespace
        where k.contype = 'f' and n.nspname = current_schema() and filho.oid <> pai.oid
        """;

    public static async Task<int> ExcluirAsync(AppDbContext db, Guid lojaId, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var tabelas = (await db.Database.SqlQueryRaw<string>(SqlTabelas).ToListAsync(ct)).ToHashSet();
        var chaves = await LerPares(db, ct);

        var linhas = 0;
        foreach (var tabela in Ordenar(tabelas, chaves))
            linhas += await db.Database.ExecuteSqlRawAsync($"delete from \"{tabela.Replace("\"", "\"\"")}\" where loja_id = {{0}}", [lojaId], ct);
        linhas += await db.Database.ExecuteSqlRawAsync("delete from loja where id = {0}", [lojaId], ct);

        await transacao.CommitAsync(ct);
        return linhas;
    }

    private static async Task<List<(string Filho, string Pai)>> LerPares(AppDbContext db, CancellationToken ct)
    {
        var conexao = db.Database.GetDbConnection();
        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = SqlChaves;
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var pares = new List<(string, string)>();
        await using var leitor = await cmd.ExecuteReaderAsync(ct);
        while (await leitor.ReadAsync(ct))
            pares.Add((leitor.GetString(0), leitor.GetString(1)));
        return pares;
    }

    /// <summary>Ordem de exclusão: uma tabela só sai depois de todas as que apontam para ela.</summary>
    public static List<string> Ordenar(IReadOnlySet<string> tabelas, IEnumerable<(string Filho, string Pai)> chaves)
    {
        var apontadaPor = tabelas.ToDictionary(t => t, _ => new HashSet<string>());
        foreach (var (filho, pai) in chaves)
            if (tabelas.Contains(filho) && tabelas.Contains(pai))
                apontadaPor[pai].Add(filho);

        var ordem = new List<string>();
        var restantes = new HashSet<string>(tabelas);
        while (restantes.Count > 0)
        {
            var livres = restantes.Where(t => !apontadaPor[t].Overlaps(restantes)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (livres.Count == 0)
                throw new InvalidOperationException("Chaves estrangeiras em ciclo entre: " + string.Join(", ", restantes));
            ordem.AddRange(livres);
            restantes.ExceptWith(livres);
        }
        return ordem;
    }
}
