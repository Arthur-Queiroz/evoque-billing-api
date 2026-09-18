namespace Evoque.Billing.Api.Contracts;

/// <summary>
/// O resultado dá o mesmo destaque ao que foi criado e ao que ficou de fora.
/// Erro silencioso aqui é receita que ninguém procura.
/// </summary>
public sealed record GenerateCorporateBillingDraftsResponse(
    IReadOnlyCollection<GeneratedBillingDraftResponse> Created,
    IReadOnlyCollection<SkippedCompanyResponse> Skipped,
    IReadOnlyCollection<string> UnknownContracts,
    IReadOnlyCollection<MemberWithoutCompanyResponse> MembersWithoutCompany);

public sealed record GeneratedBillingDraftResponse(
    Guid BillingDraftId,
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    decimal AmountPerMember,
    decimal TotalAmount);

public sealed record SkippedCompanyResponse(
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    string Reason);

public sealed record MemberWithoutCompanyResponse(
    long EvoMemberId,
    string MemberName);
