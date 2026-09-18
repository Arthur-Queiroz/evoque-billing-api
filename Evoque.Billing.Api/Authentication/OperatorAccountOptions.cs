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
