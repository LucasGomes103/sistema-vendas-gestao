using SistemaVendas.Application.Comum;

namespace SistemaVendas.Api.Comum;

/// <summary>
/// Resolve a loja da requisição pela claim do usuário logado. Rotas públicas, que
/// identificam a loja pelo slug, definem a loja explicitamente.
/// </summary>
public class LojaAtualDaRequisicao(IHttpContextAccessor http) : ILojaAtual
{
    public const string ClaimLoja = "loja_id";

    private Guid? _definida;

    public Guid? LojaId
    {
        get
        {
            if (_definida is { } id)
                return id;

            var claim = http.HttpContext?.User.FindFirst(ClaimLoja)?.Value;
            return Guid.TryParse(claim, out var daClaim) ? daClaim : null;
        }
    }

    /// <summary>Usado por rotas públicas que identificam a loja pelo slug.</summary>
    public void Definir(Guid lojaId) => _definida = lojaId;
}
