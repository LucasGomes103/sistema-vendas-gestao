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
        return (new MapasGratuitos(new HttpClient(handler), Options.Create(new GeoOptions { RespeitarLimite = false }), NullLogger<MapasGratuitos>.Instance), handler);
    }

    private const string CentroSp = """[{"lat":"-23.5506507","lon":"-46.6333824"}]""";

    private static string BrasilApi(string cep, string rua, string bairro, string coords = "{}") =>
        "{\"cep\":\"" + cep + "\",\"state\":\"SP\",\"city\":\"São Paulo\",\"neighborhood\":\"" + bairro + "\",\"street\":\"" + rua
        + "\",\"location\":{\"type\":\"Point\",\"coordinates\":" + coords + "}}";

    [Fact]
    public async Task Acha_o_CEP_pela_rua_no_OpenStreetMap()
    {
        var (mapas, h) = Mapas(url =>
            url.Contains("brasilapi") ? (HttpStatusCode.OK, BrasilApi("03359040", "Rua Antônio Camardo", "Vila Gomes Cardim"))
            : url.Contains("street=") ? (HttpStatusCode.OK, """[{"lat":"-23.5490","lon":"-46.5520"}]""")
            : (HttpStatusCode.OK, CentroSp));

        var achado = await mapas.LocalizarAsync(3359040, default);

        Assert.Equal(-23.5490, achado!.Ponto.Lat, 4);
        Assert.Equal("osm-rua", achado.Fonte);
        Assert.Equal("Rua Antônio Camardo, Vila Gomes Cardim, São Paulo - SP", achado.Endereco);
        Assert.Contains(h.Chamadas, c => c.Contains("street=Rua"));
    }

    [Fact]
    public async Task Ponto_no_centro_da_cidade_e_recusado()
    {
        // O erro que aconteceu: CEPs diferentes caindo no mesmo ponto (centro de SP) e o frete saindo 0 km.
        var (mapas, _) = Mapas(url =>
            url.Contains("brasilapi")
                ? (HttpStatusCode.OK, BrasilApi("02464100", "Rua Y", "Santana", """{"latitude":"-23.5506","longitude":"-46.6334"}"""))
                : (HttpStatusCode.OK, CentroSp));

        Assert.Null(await mapas.LocalizarAsync(2464100, default));
    }

    [Fact]
    public async Task Sem_rua_usa_o_bairro()
    {
        var (mapas, h) = Mapas(url =>
            url.Contains("brasilapi") ? (HttpStatusCode.OK, """{"cep":"02464100","state":"SP","city":"São Paulo","neighborhood":"Santana","location":{}}""")
            : url.Contains("Santana") ? (HttpStatusCode.OK, """[{"lat":"-23.5020","lon":"-46.6250"}]""")
            : (HttpStatusCode.OK, CentroSp));

        var achado = await mapas.LocalizarAsync(2464100, default);

        Assert.Equal("osm-bairro", achado!.Fonte);
        Assert.DoesNotContain(h.Chamadas, c => c.Contains("street="));
    }

    [Fact]
    public async Task Coordenada_da_BrasilAPI_fica_por_ultimo()
    {
        var (mapas, _) = Mapas(url =>
            url.Contains("brasilapi")
                ? (HttpStatusCode.OK, BrasilApi("07010000", "Rua X", "Centro", """{"latitude":"-23.4628","longitude":"-46.5333"}"""))
                : url.Contains("city=") && !url.Contains("street=") ? (HttpStatusCode.OK, CentroSp) : (HttpStatusCode.OK, "[]"));

        var achado = await mapas.LocalizarAsync(7010000, default);

        Assert.Equal("brasilapi", achado!.Fonte);
        Assert.Equal(-23.4628, achado.Ponto.Lat, 4);
    }

    [Fact]
    public async Task CEP_que_ninguem_acha_devolve_nulo()
    {
        var (mapas, _) = Mapas(url => url.Contains("brasilapi") ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.OK, "[]"));
        Assert.Null(await mapas.LocalizarAsync(99999999, default));
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
