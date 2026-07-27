using Evoque.Billing.Api.Integrations.Evo;

namespace Evoque.Billing.Api.Services;

public sealed class EvoCorporatePartnershipResolver
{
    public EvoCorporatePartnershipResolution Resolve(EvoSale sale)
    {
        var corporateSaleItems = (sale.Items ?? [])
            .Where(saleItem => saleItem.CorporatePartnershipId is > 0)
            .ToArray();
        var itemPartnershipIds = corporateSaleItems
            .Select(saleItem => saleItem.CorporatePartnershipId!.Value)
            .Distinct()
            .ToArray();

        if (itemPartnershipIds.Length > 1)
        {
            return EvoCorporatePartnershipResolution.Conflict(
                "A venda possui itens vinculados a mais de uma parceria corporativa.");
        }

        var salePartnershipId = sale.CorporatePartnershipId is > 0
            ? sale.CorporatePartnershipId
            : null;
        var itemPartnershipId = itemPartnershipIds.SingleOrDefault();
        if (salePartnershipId is not null
            && itemPartnershipId > 0
            && salePartnershipId.Value != itemPartnershipId)
        {
            return EvoCorporatePartnershipResolution.Conflict(
                "A parceria corporativa da venda é diferente da parceria encontrada nos itens.");
        }

        var resolvedPartnershipId = itemPartnershipId > 0
            ? itemPartnershipId
            : salePartnershipId;
        if (resolvedPartnershipId is null)
        {
            return EvoCorporatePartnershipResolution.NotCorporate();
        }

        var matchingSaleItems = corporateSaleItems
            .Where(saleItem => saleItem.CorporatePartnershipId == resolvedPartnershipId)
            .ToArray();
        var partnershipName = matchingSaleItems
            .Select(saleItem => saleItem.CorporatePartnershipName?.Trim())
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? sale.CorporatePartnershipName?.Trim()
            ?? $"Convênio {resolvedPartnershipId}";
        var memberMembershipId = matchingSaleItems.Length == 1
            ? matchingSaleItems[0].MemberMembershipId
            : null;

        return EvoCorporatePartnershipResolution.Resolved(
            resolvedPartnershipId.Value,
            partnershipName,
            memberMembershipId);
    }
}

public sealed record EvoCorporatePartnershipResolution(
    bool IsResolved,
    bool HasConflict,
    int? PartnershipId,
    string? PartnershipName,
    int? MemberMembershipId,
    string? ConflictMessage)
{
    public static EvoCorporatePartnershipResolution Resolved(
        int partnershipId,
        string partnershipName,
        int? memberMembershipId)
    {
        return new EvoCorporatePartnershipResolution(
            true,
            false,
            partnershipId,
            partnershipName,
            memberMembershipId,
            null);
    }

    public static EvoCorporatePartnershipResolution NotCorporate()
    {
        return new EvoCorporatePartnershipResolution(false, false, null, null, null, null);
    }

    public static EvoCorporatePartnershipResolution Conflict(string message)
    {
        return new EvoCorporatePartnershipResolution(false, true, null, null, null, message);
    }
}
