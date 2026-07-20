namespace Evoque.Billing.Api.Domain;

public sealed record BillingDraftItem(
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    string? ExternalMemberId)
{
    public decimal TotalAmount
    {
        get
        {
            return decimal.Round(Quantity * UnitAmount, 2, MidpointRounding.AwayFromZero);
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new ValidationException("A descrição do item é obrigatória.");
        }

        if (Quantity <= 0)
        {
            throw new ValidationException("A quantidade do item deve ser maior que zero.");
        }

        if (UnitAmount < 0)
        {
            throw new ValidationException("O valor unitário do item não pode ser negativo.");
        }
    }
}
