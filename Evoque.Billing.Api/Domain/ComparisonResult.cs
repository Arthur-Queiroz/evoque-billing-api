namespace Evoque.Billing.Api.Domain;

public sealed record ComparisonResult(
    string ExternalCompanyId,
    string CompanyName,
    decimal PreviousTotalAmount,
    decimal CurrentTotalAmount,
    IReadOnlyCollection<MemberComparison> Changes);

public sealed record MemberComparison(
    string ExternalMemberId,
    string MemberName,
    MemberComparisonType Type,
    decimal PreviousAmount,
    decimal CurrentAmount);

public enum MemberComparisonType
{
    Added,
    Removed,
    AmountChanged,
    Activated,
    Deactivated,
}
