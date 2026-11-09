namespace SistemaVendas.Domain.Lojas;

/// <summary>
/// Regras do gerenciamento de usuários. A principal: a loja nunca fica sem um dono ativo,
/// senão ninguém mais consegue cadastrar usuários nem mexer na configuração.
/// </summary>
public static class RegrasUsuario
{
    public const int TamanhoMinimoSenha = 8;

    /// <summary>Senha com pelo menos 8 caracteres, uma letra e um número. Devolve o erro ou null.</summary>
    public static string? ValidarSenha(string? senha)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < TamanhoMinimoSenha)
            return $"A senha precisa ter pelo menos {TamanhoMinimoSenha} caracteres.";
        if (!senha.Any(char.IsLetter) || !senha.Any(char.IsDigit))
            return "A senha precisa ter pelo menos uma letra e um número.";
        return null;
    }

    /// <summary>Confere se o usuário pode passar para o papel e o estado pedidos. Devolve o erro ou null.</summary>
    public static string? ValidarAlteracao(Usuario alvo, Papel novoPapel, bool novoAtivo, Guid quemPede, int donosAtivos)
    {
        if (alvo.Id == quemPede)
        {
            if (!novoAtivo)
                return "Você não pode desativar o seu próprio usuário.";
            if (novoPapel != alvo.Papel)
                return "Você não pode mudar o seu próprio papel. Peça para outro dono.";
        }

        var deixaDeSerDonoAtivo = alvo is { Papel: Papel.Dono, Ativo: true } && (novoPapel != Papel.Dono || !novoAtivo);
        if (deixaDeSerDonoAtivo && donosAtivos <= 1)
            return "A loja precisa ter pelo menos um dono ativo.";

        return null;
    }

    /// <summary>Confere se o usuário pode ser excluído. Devolve o erro ou null.</summary>
    public static string? ValidarExclusao(Usuario alvo, Guid quemPede, int donosAtivos)
    {
        if (alvo.Id == quemPede)
            return "Você não pode excluir o seu próprio usuário.";
        if (alvo is { Papel: Papel.Dono, Ativo: true } && donosAtivos <= 1)
            return "A loja precisa ter pelo menos um dono ativo.";
        return null;
    }
}
