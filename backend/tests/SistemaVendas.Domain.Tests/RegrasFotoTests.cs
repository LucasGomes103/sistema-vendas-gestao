using SistemaVendas.Domain.Catalogo;

namespace SistemaVendas.Domain.Tests;

public class RegrasFotoTests
{
    [Fact]
    public void Le_jpeg_em_data_url()
    {
        var (bytes, tipo, erro) = RegrasFoto.LerDataUrl("data:image/jpeg;base64," + Convert.ToBase64String([1, 2, 3]));
        Assert.Null(erro);
        Assert.Equal("image/jpeg", tipo);
        Assert.Equal([1, 2, 3], bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nao e imagem")]
    [InlineData("data:text/html;base64,PGgxPg==")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=")]
    [InlineData("data:image/png;base64,@@@")]
    [InlineData("data:image/png,semBase64")]
    public void Recusa_o_que_nao_e_foto_aceita(string? dataUrl) => Assert.NotNull(RegrasFoto.LerDataUrl(dataUrl).Erro);

    [Fact]
    public void Recusa_foto_grande_demais()
    {
        var grande = new byte[RegrasFoto.TamanhoMaximo + 1];
        Assert.NotNull(RegrasFoto.LerDataUrl("data:image/png;base64," + Convert.ToBase64String(grande)).Erro);
    }
}
