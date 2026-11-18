namespace Evoque.Billing.Api.Contracts;

/// <summary>
/// O resultado dá o mesmo destaque ao que foi criado e ao que ficou de fora.
/// Erro silencioso aqui é receita que ninguém procura.
/// </summary>
public sealed record GenerateCorporateBillingDraftsResponse(
    IReadOnlyCollection<GeneratedBillingDraftResponse> Created,
    IReadOnlyCollection<SkippedCompanyResponse> Skipped,
    IReadOnlyCollection<string> UnknownContracts,
    IReadOnlyCollection<MemberWithoutCompanyResponse> MembersWithoutCompany,
    IReadOnlyCollection<MemberOutsidePeriodResponse> MembersOutsidePeriod,
    IReadOnlyCollection<ProrationWarningResponse> ProrationWarnings);

public sealed record GeneratedBillingDraftResponse(
    Guid BillingDraftId,
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    decimal AmountPerMember,
    decimal TotalAmount,
    DateOnly? ClosingDate,
    IReadOnlyCollection<ProratedMemberResponse> ProratedMembers);

public sealed record SkippedCompanyResponse(
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    string Reason);

public sealed record MemberWithoutCompanyResponse(
    long EvoMemberId,
    string MemberName);

public sealed record ProratedMemberResponse(
    long EvoMemberId,
    string MemberName,
    DateOnly EntryDate,
    int BilledDays,
    decimal Amount);

/// <summary>
/// Colaborador que entrou no convênio no dia do fechamento ou depois. Não é
/// cobrado nesta competência; o primeiro mês dele cai no período seguinte.
/// </summary>
public sealed record MemberOutsidePeriodResponse(
    long EvoMemberId,
    string MemberName,
    string CompanyName,
    DateOnly EntryDate);

/// <summary>
/// O proporcional não pôde ser verificado. O colaborador foi cobrado pelo valor
/// cheio, e a pessoa que aprova a prévia precisa saber disso.
/// </summary>
public sealed record ProrationWarningResponse(
    string CompanyTaxId,
    string CompanyName,
    long? EvoMemberId,
    string? MemberName,
    string Message);
