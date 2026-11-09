using FluentValidation;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Application.Usuarios;

public record UsuarioNovo(string Nome, string Email, Papel Papel, string Senha);

public record UsuarioEdicao(string Nome, string Email, Papel Papel, bool Ativo);

public record RedefinicaoDeSenha(string NovaSenha);

public record TrocaDeSenha(string SenhaAtual, string NovaSenha);

public record UsuarioSaida(Guid Id, string Nome, string Email, Papel Papel, bool Ativo, DateTimeOffset CriadoEm, bool EhVoce);

public class UsuarioNovoValidator : AbstractValidator<UsuarioNovo>
{
    public UsuarioNovoValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("Informe o nome.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(200);
        RuleFor(x => x.Papel).IsInEnum();
        RuleFor(x => x.Senha).Custom((senha, ctx) =>
        {
            if (RegrasUsuario.ValidarSenha(senha) is { } erro) ctx.AddFailure(erro);
        });
    }
}

public class UsuarioEdicaoValidator : AbstractValidator<UsuarioEdicao>
{
    public UsuarioEdicaoValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("Informe o nome.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(200);
        RuleFor(x => x.Papel).IsInEnum();
    }
}
