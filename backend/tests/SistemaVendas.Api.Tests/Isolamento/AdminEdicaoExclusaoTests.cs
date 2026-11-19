using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Administrador edita e exclui loja. Banco próprio: a loja B some no teste de exclusão.</summary>
public class AdminEdicaoExclusaoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:Admins", banco.A.EmailDono));

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> app)
    {
        var c = app.CreateClient();
        (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.A.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return c;
    }

    /// <summary>Linhas por tabela (todas as que têm loja_id) da loja pedida.</summary>
    private async Task<Dictionary<string, long>> ContarAsync(Guid loja)
    {
        await using var c = new NpgsqlConnection(banco.Conexao);
        await c.OpenAsync();
        var tabelas = new List<string>();
        await using (var cmd = new NpgsqlCommand("select table_name from information_schema.columns where column_name = 'loja_id' and table_schema = 'public'", c))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                tabelas.Add(r.GetString(0));
        var contagem = new Dictionary<string, long>();
        foreach (var t in tabelas)
        {
            await using var cmd = new NpgsqlCommand($"select count(*) from \"{t}\" where loja_id = @l", c);
            cmd.Parameters.AddWithValue("l", loja);
            contagem[t] = (long)(await cmd.ExecuteScalarAsync())!;
        }
        return contagem;
    }

    [Fact]
    public void Ordem_de_exclusao_apaga_quem_aponta_antes_do_apontado()
    {
        var ordem = ExclusaoDeLoja.Ordenar(new HashSet<string> { "pedido", "item_pedido", "especie", "cliente" },
            [("item_pedido", "pedido"), ("item_pedido", "especie"), ("pedido", "cliente"), ("pedido", "loja")]);
        Assert.True(ordem.IndexOf("item_pedido") < ordem.IndexOf("pedido"));
        Assert.True(ordem.IndexOf("item_pedido") < ordem.IndexOf("especie"));
        Assert.True(ordem.IndexOf("pedido") < ordem.IndexOf("cliente"));
        Assert.Throws<InvalidOperationException>(() => ExclusaoDeLoja.Ordenar(new HashSet<string> { "x", "y" }, [("x", "y"), ("y", "x")]));
    }

    [FactComBanco]
    public async Task Excluir_apaga_so_a_loja_pedida_e_exige_digitar_o_endereco()
    {
        await using var app = Fabrica();
        var admin = await AdminAsync(app);
        var donoB = app.CreateClient();
        (await donoB.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.B.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();

        var antesA = await ContarAsync(banco.A.Id);
        var antesB = await ContarAsync(banco.B.Id);
        Assert.All(antesB.Where(t => t.Key is "usuario" or "pedido" or "especie" or "conversa"), t => Assert.True(t.Value > 0, t.Key));

        // A própria loja (e a original) nunca, nem com o endereço certo.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.A.Id}/excluir", new { confirmacao = banco.A.Slug })).StatusCode);
        // Endereço errado não apaga nada.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.B.Id}/excluir", new { confirmacao = banco.A.Slug })).StatusCode);
        Assert.Equal(antesB, await ContarAsync(banco.B.Id));
        // Dono de loja comum não exclui nada.
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.A.Id}/excluir", new { confirmacao = banco.A.Slug })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.B.Id}/excluir", new { confirmacao = banco.B.Slug })).StatusCode);

        Assert.All(await ContarAsync(banco.B.Id), t => Assert.Equal(0, t.Value));
        Assert.Equal(antesA, await ContarAsync(banco.A.Id));
        await using (var db = banco.Criar(null))
        {
            Assert.False(await db.Lojas.AnyAsync(l => l.Id == banco.B.Id));
            Assert.True(await db.Lojas.AnyAsync(l => l.Id == banco.A.Id));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient().GetAsync($"/api/v1/public/{banco.B.Slug}/catalogo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = banco.B.EmailDono, senha = BancoDeTeste.Senha })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync($"/api/v1/public/{banco.A.Slug}/catalogo")).StatusCode);
    }

    [FactComBanco]
    public async Task Editar_troca_nome_endereco_e_dono_e_recusa_repetidos()
    {
        await using var app = Fabrica();
        var admin = await AdminAsync(app);
        // Nesta classe a loja B pode já ter sido excluída: edita uma loja nova.
        Guid id;
        await using (var db = banco.Criar(null))
        {
            var loja = new SistemaVendas.Domain.Lojas.Loja { Nome = "Velha", Slug = "loja-velha" };
            db.Lojas.Add(loja);
            await db.SaveChangesAsync();
            id = loja.Id;
            db.Usuarios.Add(new SistemaVendas.Domain.Lojas.Usuario
            {
                LojaId = id, Nome = "Fulano", Email = "fulano@velha.teste", SenhaHash = "x", Papel = SistemaVendas.Domain.Lojas.Papel.Dono,
            });
            await db.SaveChangesAsync();
        }

        var url = $"/api/v1/admin/lojas/{id}";
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(url, new { nome = "Nova", slug = banco.A.Slug, segmento = "Pet", nomeDono = "Ciclano", emailDono = "ciclano@nova.teste" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(url, new { nome = "Nova", slug = "loja-nova", segmento = "Pet", nomeDono = "Ciclano", emailDono = banco.A.EmailDono })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync(url, new { nome = "Nova", slug = "Loja Nova!", segmento = "Pet", nomeDono = "Ciclano", emailDono = "ciclano@nova.teste" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(url, new { nome = "Nova", slug = "loja-nova", segmento = "Pet", nomeDono = "Ciclano", emailDono = "Ciclano@Nova.teste" })).StatusCode);
        await using (var db = banco.Criar(null))
        {
            var loja = await db.Lojas.SingleAsync(l => l.Id == id);
            Assert.Equal(("Nova", "loja-nova", "Pet"), (loja.Nome, loja.Slug, loja.Segmento));
            var dono = await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.LojaId == id);
            Assert.Equal(("Ciclano", "ciclano@nova.teste"), (dono.Nome, dono.Email));
        }
    }
}
