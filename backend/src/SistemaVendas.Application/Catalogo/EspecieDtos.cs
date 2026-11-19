using FluentValidation;
using SistemaVendas.Domain.Catalogo;

namespace SistemaVendas.Application.Catalogo;

public record EspecieEntrada(
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    string? Observacoes,
    List<AtributoProduto>? Atributos = null);

public record EspecieSaida(
    Guid Id,
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    string? Observacoes,
    bool Ativo,
    int Disponivel,
    string? FotoUrl = null,
    List<AtributoProduto>? Atributos = null);

public record FotoEntrada(string DataUrl);

public record ItemCatalogoPublico(
    Guid Id,
    string NomePopular,
    string? NomeCientifico,
    decimal? TamanhoCm,
    decimal Preco,
    int Disponivel,
    string? FotoUrl,
    List<AtributoProduto>? Atributos = null);

public class EspecieEntradaValidator : AbstractValidator<EspecieEntrada>
{
    public EspecieEntradaValidator()
    {
        RuleFor(x => x.NomePopular).NotEmpty().MaximumLength(120);
        RuleFor(x => x.NomeCientifico).MaximumLength(160);
        RuleFor(x => x.Preco).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TamanhoCm).GreaterThan(0).When(x => x.TamanhoCm.HasValue);
        RuleFor(x => x.Atributos).Must(a => a is null || a.Count <= RegrasAtributos.Maximo)
            .WithMessage($"Use no máximo {RegrasAtributos.Maximo} campos.");
        RuleForEach(x => x.Atributos).ChildRules(a =>
        {
            a.RuleFor(x => x.Nome).MaximumLength(RegrasAtributos.TamanhoNome).WithMessage($"Nome do campo com até {RegrasAtributos.TamanhoNome} letras.");
            a.RuleFor(x => x.Valor).MaximumLength(RegrasAtributos.TamanhoValor).WithMessage($"Valor do campo com até {RegrasAtributos.TamanhoValor} letras.");
        });
    }
}
