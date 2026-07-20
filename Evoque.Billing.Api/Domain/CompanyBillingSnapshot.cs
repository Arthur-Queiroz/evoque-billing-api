namespace Evoque.Billing.Api.Domain;

public sealed record CompanyBillingSnapshot(
    string ExternalCompanyId,
    string CompanyName,
    IReadOnlyCollection<MemberBillingSnapshot> Members)
{
    public decimal TotalAmount
    {
        get
        {
            return Members
                .Where(member => member.IsActive)
                .Sum(member => member.MonthlyAmount);
        }
    }
}

public sealed record MemberBillingSnapshot(
    string ExternalMemberId,
    string MemberName,
    decimal MonthlyAmount,
    bool IsActive)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExternalMemberId))
        {
            throw new ValidationException("O identificador externo do colaborador é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(MemberName))
        {
            throw new ValidationException("O nome do colaborador é obrigatório.");
        }

        if (MonthlyAmount < 0)
        {
            throw new ValidationException("A mensalidade não pode ser negativa.");
        }
    }
}
