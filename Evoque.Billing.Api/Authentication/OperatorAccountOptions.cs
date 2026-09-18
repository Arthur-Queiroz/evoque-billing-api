namespace Evoque.Billing.Api.Authentication;

/// <summary>
/// Os operadores autorizados, vindos do ambiente. Não há tabela nem cadastro:
/// são três ou quatro pessoas e esta camada inteira sai quando a autenticação
/// passar para o Azure.
/// </summary>
public sealed class OperatorAccountOptions
{
    public const string SectionName = "Auth";

    public IReadOnlyCollection<OperatorAccount> Users { get; init; } = [];

    /// <summary>
    /// Recusa uma configuração que deixaria o sistema aberto ou ambíguo. É
    /// chamada na subida, de propósito: um erro aqui derruba o deploy, e é
    /// exatamente o que se quer quando a alternativa é subir sem proteção.
    /// </summary>
    public void Validate()
    {
        if (Users.Count == 0)
        {
            throw new InvalidOperationException(
                "Nenhum operador configurado. Defina ao menos AUTH__USERS__0__USERNAME e "
                + "AUTH__USERS__0__PASSWORD no ambiente.");
        }

        foreach (var operatorAccount in Users)
        {
            if (string.IsNullOrWhiteSpace(operatorAccount.Username)
                || string.IsNullOrWhiteSpace(operatorAccount.Password))
            {
                throw new InvalidOperationException(
                    "Todo operador configurado precisa de AUTH__USERS__n__USERNAME e "
                    + "AUTH__USERS__n__PASSWORD preenchidos.");
            }
        }

        // O comparador é o mesmo StringComparer.OrdinalIgnoreCase que
        // Authenticate usa via StringComparison.OrdinalIgnoreCase, e não por
        // acaso: ToUpperInvariant/ToLowerInvariant aplicam mapeamento
        // linguístico completo (por exemplo "ß".ToUpperInvariant() é "SS",
        // uma expansão), enquanto a comparação ordinal usa dobra de
        // maiúsculas/minúsculas simples e não linguística. As duas podem
        // divergir num caractere fora do ASCII comum, e se divergissem aqui a
        // validação aceitaria dois operadores que a autenticação trata como
        // um só — reusar o comparador exato elimina essa divergência em vez
        // de só torná-la improvável.
        var distinctUsernameCount = Users
            .Select(operatorAccount => operatorAccount.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctUsernameCount != Users.Count)
        {
            throw new InvalidOperationException(
                "Há operadores repetidos em AUTH__USERS. Cada usuário precisa de um nome único.");
        }
    }
}

public sealed class OperatorAccount
{
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Em texto, sem hash. O mesmo arquivo guarda a chave de produção do Asaas,
    /// com que se cria cobrança real — proteger a senha de quem já tem aquilo
    /// não muda nada. A contrapartida é que estas senhas são geradas, nunca
    /// escolhidas: ninguém reusa dezesseis caracteres aleatórios, e é o reuso
    /// que faria um vazamento machucar fora deste sistema.
    /// </summary>
    public string Password { get; init; } = string.Empty;
}
