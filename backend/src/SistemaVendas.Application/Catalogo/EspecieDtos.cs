using FluentValidation;

namespace SistemaVendas.Application.Catalogo;

public record EspecieEntrada(
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    string? Observacoes);

public record EspecieSaida(
    Guid Id,
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    string? Observacoes,
    bool Ativo);

public record ItemCatalogoPublico(
    Guid Id,
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    int Disponivel,
    string? FotoUrl);

public class EspecieEntradaValidator : AbstractValidator<EspecieEntrada>
{
    public EspecieEntradaValidator()
    {
        RuleFor(x => x.NomePopular).NotEmpty().MaximumLength(120);
        RuleFor(x => x.NomeCientifico).MaximumLength(160);
        RuleFor(x => x.Preco).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TamanhoCm).GreaterThan(0).When(x => x.TamanhoCm.HasValue);
    }
}
