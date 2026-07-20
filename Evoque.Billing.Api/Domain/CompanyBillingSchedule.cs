namespace Evoque.Billing.Api.Domain;

public sealed record CompanyBillingSchedule(
    string ExternalCompanyId,
    int BillingDay,
    bool IsActive,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    private static readonly int[] SupportedBillingDays = [2, 18, 20, 25];

    public static CompanyBillingSchedule Create(
        string externalCompanyId,
        int billingDay,
        bool isActive,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(externalCompanyId))
        {
            throw new ValidationException("O identificador externo da empresa é obrigatório.");
        }

        if (!SupportedBillingDays.Contains(billingDay))
        {
            throw new ValidationException("O dia de faturamento deve ser 02, 18, 20 ou 25.");
        }

        if (string.IsNullOrWhiteSpace(updatedBy))
        {
            throw new ValidationException("O responsável pela atualização da agenda é obrigatório.");
        }

        return new CompanyBillingSchedule(externalCompanyId.Trim(), billingDay, isActive, updatedBy.Trim(), updatedAt);
    }
}
