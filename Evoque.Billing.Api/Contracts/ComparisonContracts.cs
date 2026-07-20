using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

public sealed record CompareBillingSnapshotsRequest(
    IReadOnlyCollection<CompanyBillingSnapshotRequest> PreviousCompanies,
    IReadOnlyCollection<CompanyBillingSnapshotRequest> CurrentCompanies)
{
    public IReadOnlyCollection<CompanyBillingSnapshot> ToPreviousCompanies()
    {
        return PreviousCompanies.Select(CompanyBillingSnapshotRequest.ToDomain).ToArray();
    }

    public IReadOnlyCollection<CompanyBillingSnapshot> ToCurrentCompanies()
    {
        return CurrentCompanies.Select(CompanyBillingSnapshotRequest.ToDomain).ToArray();
    }
}

public sealed record CompanyBillingSnapshotRequest(
    string ExternalCompanyId,
    string CompanyName,
    IReadOnlyCollection<MemberBillingSnapshotRequest> Members)
{
    public static CompanyBillingSnapshot ToDomain(CompanyBillingSnapshotRequest request)
    {
        return new CompanyBillingSnapshot(
            request.ExternalCompanyId,
            request.CompanyName,
            request.Members.Select(MemberBillingSnapshotRequest.ToDomain).ToArray());
    }
}

public sealed record MemberBillingSnapshotRequest(
    string ExternalMemberId,
    string MemberName,
    decimal MonthlyAmount,
    bool IsActive)
{
    public static MemberBillingSnapshot ToDomain(MemberBillingSnapshotRequest request)
    {
        return new MemberBillingSnapshot(
            request.ExternalMemberId,
            request.MemberName,
            request.MonthlyAmount,
            request.IsActive);
    }
}

public sealed record ComparisonResultResponse(
    string ExternalCompanyId,
    string CompanyName,
    decimal PreviousTotalAmount,
    decimal CurrentTotalAmount,
    IReadOnlyCollection<MemberComparisonResponse> Changes)
{
    public static ComparisonResultResponse FromDomain(ComparisonResult comparisonResult)
    {
        return new ComparisonResultResponse(
            comparisonResult.ExternalCompanyId,
            comparisonResult.CompanyName,
            comparisonResult.PreviousTotalAmount,
            comparisonResult.CurrentTotalAmount,
            comparisonResult.Changes.Select(MemberComparisonResponse.FromDomain).ToArray());
    }
}

public sealed record MemberComparisonResponse(
    string ExternalMemberId,
    string MemberName,
    string Type,
    decimal PreviousAmount,
    decimal CurrentAmount)
{
    public static MemberComparisonResponse FromDomain(MemberComparison memberComparison)
    {
        return new MemberComparisonResponse(
            memberComparison.ExternalMemberId,
            memberComparison.MemberName,
            memberComparison.Type.ToString(),
            memberComparison.PreviousAmount,
            memberComparison.CurrentAmount);
    }
}
