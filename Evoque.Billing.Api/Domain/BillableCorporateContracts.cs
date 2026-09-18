namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Os contratos que a empresa paga. Um colaborador só entra em prévia se o
/// contrato dele estiver aqui.
/// </summary>
/// <remarks>
/// A lista é explícita, e não um padrão de texto, porque o EVO tem contratos
/// cujo nome diz "CORPORATIVO" e que a empresa não paga: `EVOQUE CORPORATIVO
/// RECORRENTE - 39,95` sai da exportação com valor preenchido, ou seja, quem
/// paga é a própria pessoa.
///
/// Ela vive no código, não em configuração de ambiente, porque mudar quem é
/// cobrado é mudança de regra de faturamento: deve aparecer num diff e passar
/// por revisão, não ser editada na VPS. Um contrato novo no EVO não entra em
/// cobrança sozinho, e a tela de geração mostra que ele ficou de fora.
/// </remarks>
public static class BillableCorporateContracts
{
    public static IReadOnlyCollection<string> All { get; } =
    [
        "EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA",
        "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO",
    ];

    public static bool Includes(string? contractName)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return false;
        }

        return All.Any(billable =>
            string.Equals(billable, contractName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
