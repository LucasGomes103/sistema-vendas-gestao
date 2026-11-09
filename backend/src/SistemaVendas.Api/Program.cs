using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SistemaVendas.Api.Comum;
using SistemaVendas.Api.Endpoints;
using SistemaVendas.Application;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILojaAtual, LojaAtualDaRequisicao>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorkers();

// Sessão por cookie: painel e API no mesmo domínio, sem token no navegador.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "sv_sessao";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        // API responde 401/403 em vez de redirecionar para uma página de login.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        // Usuário excluído, desativado ou com senha trocada perde a sessão em até 1 minuto.
        o.Events.OnValidatePrincipal = Sessao.ValidarAsync;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthEndpoints.PoliticaEquipe, p => p.RequireRole(nameof(Papel.Dono), nameof(Papel.Vendedor)))
    .AddPolicy(AuthEndpoints.PoliticaDono, p => p.RequireRole(nameof(Papel.Dono)));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LimiteLogin, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(WebhookEndpoints.LimiteWebhook, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

// Atrás do Caddy (HTTPS termina nele): confiar no X-Forwarded-Proto para o cookie sair como Secure.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Netlify -> Render: dois saltos até o IP real do cliente (usado no limite de tentativas de login).
    o.ForwardLimit = 2;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Chaves dos cookies num volume: reiniciar o contêiner não desloga ninguém.
if (builder.Configuration["ChavesProtecao:Pasta"] is { Length: > 0 } pastaChaves)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(pastaChaves));

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Configuration.GetValue<bool>("Banco:MigrarAoIniciar"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
        await SeedDesenvolvimento.ExecutarAsync(db);
    else
        await PrimeiraLoja.CriarSeNecessarioAsync(db, app.Configuration, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Sistema de Vendas v1");
        o.DocumentTitle = "Sistema de Vendas · API";
    });
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { servico = "sistema-vendas-gestao", versao = "0.2.0" }));
app.MapHealthEndpoints();
app.MapPaginasPublicasEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapUsuarioEndpoints();
v1.MapCatalogoPublicoEndpoints();
v1.MapEspecieEndpoints();
v1.MapEstoqueEndpoints();
v1.MapConversaEndpoints();
v1.MapFunilEndpoints();
v1.MapFreteEndpoints();
v1.MapReservaEndpoints();
v1.MapConexaoWhatsAppEndpoints();
v1.MapWebhookEndpoints();
// Simulador de cliente: as conversas dele nunca saem pelo WhatsApp, então fica ligado também no publicado
// (para testar sem conectar um número). Desligue com Simulador__Habilitado=false.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Simulador:Habilitado", true))
    v1.MapSimuladorEndpoints();

app.Run();

public partial class Program;
