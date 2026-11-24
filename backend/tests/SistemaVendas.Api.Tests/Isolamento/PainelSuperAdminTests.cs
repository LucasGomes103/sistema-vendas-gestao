using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Painel do super admin: visão geral, uso de IA, último acesso, auditoria e modo suporte (só leitura).</summary>
public class PainelSuperAdminTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:Admins", banco.A.EmailDono));

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    [FactComBanco]
    public async Task Visao_geral_e_detalhe_so_para_o_administrador()
    {
        await using var app = Fabrica();
        var donoB = await EntrarAsync(app, banco.B.EmailDono);
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.GetAsync("/api/v1/admin/visao-geral")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.GetAsync($"/api/v1/admin/lojas/{banco.A.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.GetAsync("/api/v1/admin/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.PostAsync($"/api/v1/admin/lojas/{banco.A.Id}/ver-como", null)).StatusCode);

        var admin = await EntrarAsync(app, banco.A.EmailDono);
        var visao = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/visao-geral?dias=7");
        Assert.Equal(7, visao.GetProperty("dias").GetInt32());
        Assert.True(visao.GetProperty("lojas").GetProperty("total").GetInt32() >= 2);
        Assert.True(visao.GetProperty("totais").GetProperty("pedidos").GetInt32() >= 2);
        Assert.True(visao.GetProperty("totais").GetProperty("tokensEntrada").GetInt64() >= 200);
        Assert.Equal(7, visao.GetProperty("porDia").GetArrayLength());

        var detalhe = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/lojas/{banco.B.Id}");
        Assert.Equal(banco.B.Slug, detalhe.GetProperty("loja").GetProperty("slug").GetString());
        Assert.Equal(banco.B.EmailDono, detalhe.GetProperty("usuarios")[0].GetProperty("email").GetString());
        // O dono da B acabou de entrar: o último acesso aparece.
        Assert.NotEqual(JsonValueKind.Null, detalhe.GetProperty("loja").GetProperty("ultimoAcessoEm").ValueKind);
        // Só números: nada de mensagem ou telefone de cliente.
        Assert.DoesNotContain("Olá", detalhe.GetRawText());
        Assert.DoesNotContain("5511900000", detalhe.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/lojas/{Guid.NewGuid()}")).StatusCode);
    }

    [FactComBanco]
    public async Task Uso_de_IA_soma_por_loja_e_dia()
    {
        await using (var db = banco.Criar(null))
        {
            await RegistroUsoIa.RegistrarAsync(db, banco.B.Id, new ConsumoIa(2, 1000, 50, false), DateTimeOffset.UtcNow, default);
            await RegistroUsoIa.RegistrarAsync(db, banco.B.Id, new ConsumoIa(1, 0, 0, true), DateTimeOffset.UtcNow, default);
            await RegistroUsoIa.RegistrarAsync(db, banco.B.Id, null, DateTimeOffset.UtcNow, default); // respondeu pelas regras
        }

        await using var app = Fabrica();
        var admin = await EntrarAsync(app, banco.A.EmailDono);
        var lojas = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/lojas");
        var b = lojas.EnumerateArray().Single(l => l.GetProperty("slug").GetString() == banco.B.Slug);
        Assert.True(b.GetProperty("respostasRobo30Dias").GetInt32() >= 4); // 1 da carga inicial + 3
        Assert.True(b.GetProperty("tokensIa30Dias").GetInt64() >= 1170);
        Assert.True(b.GetProperty("falhasIa30Dias").GetInt32() >= 1);

        await using var db2 = banco.Criar(null);
        var hoje = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).Date);
        Assert.Equal(1, await db2.UsosIa.IgnoreQueryFilters().CountAsync(u => u.LojaId == banco.B.Id && u.Dia == hoje));
    }

    [FactComBanco]
    public async Task Modo_suporte_ve_a_loja_sem_mudar_nada_e_fica_na_auditoria()
    {
        await using var app = Fabrica();
        var admin = await EntrarAsync(app, banco.A.EmailDono);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync($"/api/v1/admin/lojas/{banco.A.Id}/ver-como", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/lojas/{banco.B.Id}/ver-como", null)).StatusCode);

        var eu = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/eu");
        Assert.Equal(banco.B.Id, eu.GetProperty("lojaId").GetGuid());
        Assert.True(eu.GetProperty("modoSuporte").GetBoolean());
        Assert.True(eu.GetProperty("adminPlataforma").GetBoolean());

        // Lê o painel da loja B...
        var especies = await admin.GetAsync("/api/v1/especies");
        Assert.Equal(HttpStatusCode.OK, especies.StatusCode);
        Assert.Contains(banco.B.EspecieId.ToString(), await especies.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/conversas/{banco.B.ConversaId}/mensagens")).StatusCode);

        // ...mas não muda nada, nem pelo próprio painel do administrador.
        var negado = await admin.PostAsJsonAsync($"/api/v1/conversas/{banco.B.ConversaId}/mensagens", new { texto = "oi" });
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        Assert.Contains("modo_suporte", await negado.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/v1/config/pix", new { chave = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.B.Id}/suspender", new { motivo = "x" })).StatusCode);
        await using (var db = banco.Criar(null))
            Assert.Equal(StatusLoja.Ativa, (await db.Lojas.SingleAsync(l => l.Id == banco.B.Id)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/admin/suporte/sair", null)).StatusCode);
        eu = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/eu");
        Assert.Equal(banco.A.Id, eu.GetProperty("lojaId").GetGuid());
        Assert.False(eu.GetProperty("modoSuporte").GetBoolean());

        var auditoria = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/auditoria?lojaId={banco.B.Id}");
        var acoes = auditoria.EnumerateArray().Select(a => a.GetProperty("acao").GetString()).ToList();
        Assert.Contains(AcaoAdmin.VerComoLoja, acoes);
        Assert.Contains(AcaoAdmin.SairDaLoja, acoes);
        Assert.All(auditoria.EnumerateArray(), a => Assert.Equal(banco.A.EmailDono, a.GetProperty("adminEmail").GetString()));
    }

    [FactComBanco]
    public async Task Suspender_e_reativar_ficam_na_auditoria()
    {
        await using var app = Fabrica();
        var admin = await EntrarAsync(app, banco.A.EmailDono);
        (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.B.Id}/suspender", new { motivo = "Mensalidade atrasada" })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/v1/admin/lojas/{banco.B.Id}/ativar", null)).EnsureSuccessStatusCode();

        var auditoria = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/auditoria");
        var daB = auditoria.EnumerateArray().Where(a => a.GetProperty("lojaSlug").GetString() == banco.B.Slug).ToList();
        Assert.Contains(daB, a => a.GetProperty("acao").GetString() == AcaoAdmin.Suspender && a.GetProperty("detalhe").GetString() == "Mensalidade atrasada");
        Assert.Contains(daB, a => a.GetProperty("acao").GetString() == AcaoAdmin.Ativar && a.GetProperty("detalhe").GetString() == "reativada");
    }
}
