using SistemaVendas.Application.Comum;

namespace SistemaVendas.Api.Comum;

/// <summary>
/// Resolve a loja da requisição. Enquanto o login (ASP.NET Core Identity) não entra,
/// usa o cabeçalho X-Loja-Id ou a loja padrão da configuração. Com o login, a loja
/// passa a vir da claim do usuário autenticado.
/// </summary>
public class LojaAtualDaRequisicao(IHttpContextAccessor http, IConfiguration config) : ILojaAtual
{
    private Guid? _sobrescrita;

    public Guid? LojaId
    {
        get
        {
            if (_sobrescrita is { } id)
                return id;

            var cabecalho = http.HttpContext?.Request.Headers["X-Loja-Id"].ToString();
            if (Guid.TryParse(cabecalho, out var doCabecalho))
                return doCabecalho;

            return Guid.TryParse(config["Loja:IdPadrao"], out var padrao) ? padrao : null;
        }
    }

    /// <summary>Usado por rotas públicas que identificam a loja pelo slug.</summary>
    public void Definir(Guid lojaId) => _sobrescrita = lojaId;
}
