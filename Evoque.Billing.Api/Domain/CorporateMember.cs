namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Colaborador corporativo identificado pelo IdCliente estável do EVO.
/// O vínculo com a empresa não é alterado automaticamente: uma divergência de
/// CNPJ deve ser revisada por uma pessoa.
/// </summary>
public sealed class CorporateMember
{
    private readonly List<CorporateMemberContract> contracts;

    private CorporateMember(
        long evoMemberId,
        string memberName,
        string companyTaxId,
        IEnumerable<CorporateMemberContract> contracts,
        bool isActive,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? deactivatedAt,
        Guid lastImportId,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        EvoMemberId = evoMemberId;
        MemberName = memberName;
        CompanyTaxId = companyTaxId;
        this.contracts = contracts.ToList();
        IsActive = isActive;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        DeactivatedAt = deactivatedAt;
        LastImportId = lastImportId;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public long EvoMemberId { get; }

    public string MemberName { get; private set; }

    public string CompanyTaxId { get; }

    public IReadOnlyCollection<CorporateMemberContract> Contracts => contracts;

    public bool IsActive { get; private set; }

    public DateTimeOffset FirstSeenAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public Guid LastImportId { get; private set; }

    public string UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CorporateMember Create(
        long evoMemberId,
        string memberName,
        string companyTaxId,
        IEnumerable<CorporateMemberContract> contracts,
        Guid importId,
        string operatorId,
        DateTimeOffset observedAt)
    {
        Validate(evoMemberId, memberName, companyTaxId, operatorId);
        return new CorporateMember(
            evoMemberId,
            memberName.Trim(),
            global::Evoque.Billing.Api.Domain.CompanyTaxId.Normalize(companyTaxId),
            NormalizeContracts(contracts),
            isActive: true,
            observedAt,
            observedAt,
            deactivatedAt: null,
            importId,
            operatorId.Trim(),
            observedAt);
    }

    public static CorporateMember Restore(
        long evoMemberId,
        string memberName,
        string companyTaxId,
        IEnumerable<CorporateMemberContract> contracts,
        bool isActive,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? deactivatedAt,
        Guid lastImportId,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        return new CorporateMember(
            evoMemberId,
            memberName,
            companyTaxId,
            contracts,
            isActive,
            firstSeenAt,
            lastSeenAt,
            deactivatedAt,
            lastImportId,
            updatedBy,
            updatedAt);
    }

    public void RegisterObservation(
        string memberName,
        IEnumerable<CorporateMemberContract> observedContracts,
        Guid importId,
        string operatorId,
        DateTimeOffset observedAt)
    {
        Validate(EvoMemberId, memberName, CompanyTaxId, operatorId);
        MemberName = memberName.Trim();
        contracts.Clear();
        contracts.AddRange(NormalizeContracts(observedContracts));
        IsActive = true;
        LastSeenAt = observedAt;
        DeactivatedAt = null;
        LastImportId = importId;
        UpdatedBy = operatorId.Trim();
        UpdatedAt = observedAt;
    }

    public void Deactivate(Guid importId, string operatorId, DateTimeOffset deactivatedAt)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAt = deactivatedAt;
        LastImportId = importId;
        UpdatedBy = operatorId.Trim();
        UpdatedAt = deactivatedAt;
    }

    private static IReadOnlyCollection<CorporateMemberContract> NormalizeContracts(
        IEnumerable<CorporateMemberContract> sourceContracts)
    {
        return sourceContracts
            .GroupBy(contract => contract.ContractKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(contract => contract.ContractName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contract => contract.ContractKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Validate(
        long evoMemberId,
        string memberName,
        string companyTaxId,
        string operatorId)
    {
        if (evoMemberId <= 0)
        {
            throw new ValidationException("O IdCliente do colaborador deve ser maior que zero.");
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ValidationException("O nome do colaborador é obrigatório.");
        }

        global::Evoque.Billing.Api.Domain.CompanyTaxId.Normalize(companyTaxId);
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela atualização do colaborador é obrigatório.");
        }
    }
}

public sealed record CorporateMemberContract(
    string ContractKey,
    string? EvoContractId,
    string? ContractName);
