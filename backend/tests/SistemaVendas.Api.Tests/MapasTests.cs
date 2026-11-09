using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SistemaVendas.Infrastructure.Entregas;

namespace SistemaVendas.Api.Tests;

/// <summary>Leitura das respostas dos serviços de mapa, com respostas gravadas (sem internet).</summary>
public class MapasTests
{
    private sealed class Respostas(Func<string, (HttpStatusCode, string)> responder) : HttpMessageHandler
    {
        public List<string> Chamadas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Chamadas.Add(url);
            var (status, corpo) = responder(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo) });
        }
    }

    private static (MapasGratuitos, Respostas) Mapas(Func<string, (HttpStatusCode, string)> responder)
    {
        var handler = new Respostas(responder);
        return (new MapasGratuitos(new HttpClient(handler), Options.Create(new GeoOptions()), NullLogger<MapasGratuitos>.Instance), handler);
    }

    [Fact]
    public async Task Usa_a_coordenada_da_BrasilAPI_quando_vem()
    {
        var (mapas, h) = Mapas(_ => (HttpStatusCode.OK,
            """{"cep":"07010000","state":"SP","city":"Guarulhos","street":"Rua X","location":{"type":"Point","coordinates":{"longitude":"-46.5333","latitude":"-23.4628"}}}"""));

        var (coord, fonte) = await mapas.LocalizarAsync(7010000, default);

        Assert.Equal(-23.4628, coord!.Lat, 4);
        Assert.Equal(-46.5333, coord.Lon, 4);
        Assert.Equal("brasilapi", fonte);
        Assert.Single(h.Chamadas);
    }

    [Fact]
    public async Task Sem_coordenada_procura_o_endereco_no_OpenStreetMap()
    {
        var (mapas, h) = Mapas(url => url.Contains("brasilapi")
            ? (HttpStatusCode.OK, """{"cep":"01001000","state":"SP","city":"São Paulo","street":"Praça da Sé","location":{"type":"Point","coordinates":{}}}""")
            : (HttpStatusCode.OK, """[{"lat":"-23.5503","lon":"-46.6339"}]"""));

        var (coord, fonte) = await mapas.LocalizarAsync(1001000, default);

        Assert.Equal(-23.5503, coord!.Lat, 4);
        Assert.Equal("osm", fonte);
        Assert.Contains(h.Chamadas, c => c.Contains("street=Pra") && c.Contains("city=S"));
    }

    [Fact]
    public async Task CEP_que_ninguem_acha_devolve_nulo()
    {
        var (mapas, _) = Mapas(url => url.Contains("brasilapi") ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.OK, "[]"));
        var (coord, _) = await mapas.LocalizarAsync(99999999, default);
        Assert.Null(coord);
    }

    [Fact]
    public async Task Distancia_vem_da_rota_de_carro()
    {
        var (mapas, _) = Mapas(_ => (HttpStatusCode.OK, """{"code":"Ok","routes":[{"distance":7420.5,"duration":900}]}"""));
        var d = await mapas.DistanciaAsync(new(-23.55, -46.63), new(-23.50, -46.60), default);
        Assert.True(d.PorRota);
        Assert.Equal(7.42, d.Km, 2);
    }

    [Fact]
    public async Task Sem_rota_usa_a_linha_reta_corrigida()
    {
        var (mapas, _) = Mapas(_ => (HttpStatusCode.ServiceUnavailable, ""));
        var d = await mapas.DistanciaAsync(new(-23.5505, -46.6333), new(-23.4628, -46.5333), default);
        Assert.False(d.PorRota);
        Assert.InRange(d.Km, 13 * 1.3, 15 * 1.3);
    }
}
