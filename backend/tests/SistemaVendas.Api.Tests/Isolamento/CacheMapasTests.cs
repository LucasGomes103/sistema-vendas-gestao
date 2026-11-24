using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Entregas;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>O cache de mapas é da plataforma: uma loja aproveita a rota que outra já calculou.</summary>
public class CacheMapasTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private sealed class Osrm : HttpMessageHandler
    {
        public int Chamadas;
        public bool ForaDoAr;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Chamadas);
            return Task.FromResult(ForaDoAr
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"routes":[{"distance":12345.0}]}""") });
        }
    }

    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
        public void Advance(TimeSpan t) => Agora += t;
    }

    private static MapasGratuitos Mapas(Osrm osrm) =>
        new(new HttpClient(osrm), Options.Create(new GeoOptions { RespeitarLimite = false }), NullLogger<MapasGratuitos>.Instance);

    [FactComBanco]
    public async Task Rota_calculada_por_uma_loja_serve_para_as_outras_e_vence_depois_de_180_dias()
    {
        var osrm = new Osrm();
        var relogio = new Relogio(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
        var origem = new Coordenada(-23.548901, -46.552033);
        var destino = new Coordenada(-23.561234, -46.655678);

        await using (var dbA = banco.Criar(banco.A.Id))
        {
            var d = await new Geolocalizacao(dbA, Mapas(osrm), relogio).DistanciaAsync(origem, destino, default);
            Assert.Equal(12.345, d.Km, 3);
            Assert.True(d.PorRota);
        }

        // Loja B, mesmo trajeto (com diferença de poucos metros): vem do cache, sem chamar o serviço.
        await using (var dbB = banco.Criar(banco.B.Id))
        {
            var d = await new Geolocalizacao(dbB, Mapas(osrm), relogio).DistanciaAsync(origem with { Lat = -23.548899 }, destino, default);
            Assert.Equal(12.345, d.Km, 3);
        }
        Assert.Equal(1, osrm.Chamadas);

        // Depois da validade, calcula de novo e atualiza a mesma linha.
        relogio.Advance(TimeSpan.FromDays(181));
        await using (var db = banco.Criar(banco.A.Id))
        {
            await new Geolocalizacao(db, Mapas(osrm), relogio).DistanciaAsync(origem, destino, default);
            Assert.Equal(2, osrm.Chamadas);
            Assert.Equal(1, await db.RotasCache.CountAsync(r => r.DestinoLat == RotaCache.Arredondar(destino.Lat)));
        }
    }

    [FactComBanco]
    public async Task Linha_reta_com_o_servico_fora_do_ar_nao_fica_no_cache()
    {
        var osrm = new Osrm { ForaDoAr = true };
        var destino = new Coordenada(-22.9, -43.2);
        await using var db = banco.Criar(banco.A.Id);
        var geo = new Geolocalizacao(db, Mapas(osrm));

        Assert.False((await geo.DistanciaAsync(new Coordenada(-23.0, -43.5), destino, default)).PorRota);
        osrm.ForaDoAr = false;
        Assert.True((await geo.DistanciaAsync(new Coordenada(-23.0, -43.5), destino, default)).PorRota);
        Assert.Equal(2, osrm.Chamadas);
    }
}
