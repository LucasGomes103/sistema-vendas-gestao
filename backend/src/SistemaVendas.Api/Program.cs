using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Sistema de Vendas v1");
        o.DocumentTitle = "Sistema de Vendas · API";
    });

    if (app.Configuration.GetValue<bool>("Banco:MigrarAoIniciar"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SeedDesenvolvimento.ExecutarAsync(db);
    }
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { servico = "sistema-vendas-gestao", versao = "0.2.0" }));
app.MapHealthEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapCatalogoPublicoEndpoints();
v1.MapEspecieEndpoints();
v1.MapEstoqueEndpoints();
v1.MapConversaEndpoints();
v1.MapWebhookEndpoints();
if (app.Environment.IsDevelopment())
    v1.MapSimuladorEndpoints();

app.Run();

public partial class Program;
