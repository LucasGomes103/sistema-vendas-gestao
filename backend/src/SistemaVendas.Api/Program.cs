using Microsoft.EntityFrameworkCore;
using Serilog;
using SistemaVendas.Api.Comum;
using SistemaVendas.Api.Endpoints;
using SistemaVendas.Application;
using SistemaVendas.Application.Comum;
using SistemaVendas.Infrastructure;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILojaAtual, LojaAtualDaRequisicao>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorkers();

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

app.MapGet("/", () => Results.Ok(new { servico = "sistema-vendas-gestao", versao = "0.1.0" }));
app.MapHealthEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapCatalogoPublicoEndpoints();
v1.MapEspecieEndpoints();
v1.MapTanqueEndpoints();

app.Run();

public partial class Program;
