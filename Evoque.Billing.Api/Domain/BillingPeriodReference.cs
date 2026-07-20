namespace Evoque.Billing.Api.Domain;

public sealed record BillingPeriodReference
{
    public BillingPeriodReference(int year, int month)
    {
        if (year is < 2000 or > 9999)
        {
            throw new ValidationException("O ano da competência deve ter quatro dígitos.");
        }

        if (month is < 1 or > 12)
        {
            throw new ValidationException("O mês da competência deve estar entre 1 e 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public override string ToString()
    {
        return $"{Year:D4}-{Month:D2}";
    }
}
