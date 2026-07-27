namespace Evoque.Billing.Api.Domain;

/// <summary>Como a empresa entrou no catálogo interno.</summary>
public enum CompanySource
{
    EvoSpreadsheet,
    Manual,
}

/// <summary>Resultado da última consulta ao cadastro público de CNPJ.</summary>
public enum CompanyRegistryLookupStatus
{
    /// <summary>Nunca consultada.</summary>
    NotChecked,

    /// <summary>Consultada e encontrada; razão social e endereço estão preenchidos.</summary>
    Found,

    /// <summary>Consultada e não encontrada no cadastro público.</summary>
    NotFound,

    /// <summary>O cadastro público não respondeu; o catálogo segue com o nome do EVO.</summary>
    Unavailable,
}

/// <summary>Endereço cadastral devolvido pelo cadastro público de CNPJ.</summary>
public sealed record CompanyRegistryAddress(
    string Street,
    string Number,
    string Complement,
    string Neighborhood,
    string City,
    string State,
    string PostalCode);

/// <summary>
/// Empresa pagadora do catálogo interno, identificada pelo CNPJ normalizado.
///
/// A entidade separa deliberadamente três origens de nome: <see cref="DisplayName"/>
/// é operacional e editável por uma pessoa, <see cref="EvoName"/> é o último nome
/// observado na planilha do EVO e <see cref="LegalName"/>/<see cref="TradeName"/>
/// vêm do cadastro público. Uma sincronização nunca sobrescreve o nome operacional,
/// o status manual nem os vínculos Asaas.
/// </summary>
public sealed class Company
{
    private Company(
        string taxId,
        string displayName,
        string? evoName,
        CompanySource source,
        bool isActive,
        string createdBy,
        DateTimeOffset createdAt)
    {
        TaxId = taxId;
        DisplayName = displayName;
        EvoName = evoName;
        Source = source;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public string TaxId { get; }

    /// <summary>Nome operacional. Editável e nunca sobrescrito por importação.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Último nome observado na planilha exportada do EVO.</summary>
    public string? EvoName { get; private set; }

    public string? LegalName { get; private set; }

    public string? TradeName { get; private set; }

    /// <summary>Situação cadastral textual do cadastro público, por exemplo `ATIVA`.</summary>
    public string? RegistrationStatus { get; private set; }

    public CompanyRegistryAddress? RegistryAddress { get; private set; }

    public CompanyRegistryLookupStatus RegistryLookupStatus { get; private set; }
        = CompanyRegistryLookupStatus.NotChecked;

    public DateTimeOffset? RegistryLastCheckedAt { get; private set; }

    /// <summary>Empresa inativa não participa de nenhum lote agendado.</summary>
    public bool IsActive { get; private set; }

    public CompanySource Source { get; private set; }

    public int LastImportedMemberCount { get; private set; }

    public DateTimeOffset? FirstSeenAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public Guid? LastImportId { get; private set; }

    /// <summary>
    /// Marcado quando uma empresa inativa reaparece na planilha. A empresa continua
    /// inativa; a reativação é sempre uma decisão humana explícita.
    /// </summary>
    public bool RequiresReviewAfterReappearing { get; private set; }

    public string? AsaasSandboxCustomerId { get; private set; }

    public string? AsaasProductionCustomerId { get; private set; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public string UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Company CreateFromEvoSpreadsheet(
        string taxId,
        string evoName,
        int importedMemberCount,
        Guid importId,
        string operatorId,
        DateTimeOffset synchronizedAt)
    {
        var normalizedTaxId = CompanyTaxId.Normalize(taxId);
        var normalizedEvoName = RequireText(evoName, "O nome da empresa observado no EVO é obrigatório.");
        var company = new Company(
            normalizedTaxId,
            normalizedEvoName,
            normalizedEvoName,
            CompanySource.EvoSpreadsheet,
            isActive: true,
            RequireText(operatorId, "O responsável pela sincronização é obrigatório."),
            synchronizedAt);
        company.FirstSeenAt = synchronizedAt;
        company.LastSeenAt = synchronizedAt;
        company.LastImportId = importId;
        company.LastImportedMemberCount = importedMemberCount;
        return company;
    }

    public static Company CreateManually(
        string taxId,
        string displayName,
        string operatorId,
        DateTimeOffset createdAt)
    {
        return new Company(
            CompanyTaxId.Normalize(taxId),
            RequireText(displayName, "O nome operacional da empresa é obrigatório."),
            evoName: null,
            CompanySource.Manual,
            isActive: true,
            RequireText(operatorId, "O responsável pelo cadastro é obrigatório."),
            createdAt);
    }

    /// <summary>
    /// Restaura uma empresa já persistida. Usado somente pelos repositories.
    /// </summary>
    public static Company Restore(
        string taxId,
        string displayName,
        string? evoName,
        string? legalName,
        string? tradeName,
        string? registrationStatus,
        CompanyRegistryAddress? registryAddress,
        CompanyRegistryLookupStatus registryLookupStatus,
        DateTimeOffset? registryLastCheckedAt,
        bool isActive,
        CompanySource source,
        int lastImportedMemberCount,
        DateTimeOffset? firstSeenAt,
        DateTimeOffset? lastSeenAt,
        Guid? lastImportId,
        bool requiresReviewAfterReappearing,
        string? asaasSandboxCustomerId,
        string? asaasProductionCustomerId,
        string createdBy,
        DateTimeOffset createdAt,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var company = new Company(taxId, displayName, evoName, source, isActive, createdBy, createdAt)
        {
            LegalName = legalName,
            TradeName = tradeName,
            RegistrationStatus = registrationStatus,
            RegistryAddress = registryAddress,
            RegistryLookupStatus = registryLookupStatus,
            RegistryLastCheckedAt = registryLastCheckedAt,
            LastImportedMemberCount = lastImportedMemberCount,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            LastImportId = lastImportId,
            RequiresReviewAfterReappearing = requiresReviewAfterReappearing,
            AsaasSandboxCustomerId = asaasSandboxCustomerId,
            AsaasProductionCustomerId = asaasProductionCustomerId,
            UpdatedBy = updatedBy,
            UpdatedAt = updatedAt,
        };
        return company;
    }

    /// <summary>
    /// Aplica o que a planilha do EVO observou. Atualiza apenas o nome observado,
    /// a contagem de pessoas e as datas de aparição. Nome operacional, status,
    /// agenda e vínculos Asaas permanecem intactos.
    /// </summary>
    public void ApplyEvoSpreadsheetSynchronization(
        string evoName,
        int importedMemberCount,
        Guid importId,
        string operatorId,
        DateTimeOffset synchronizedAt)
    {
        EvoName = RequireText(evoName, "O nome da empresa observado no EVO é obrigatório.");
        LastImportedMemberCount = importedMemberCount;
        LastImportId = importId;
        LastSeenAt = synchronizedAt;
        FirstSeenAt ??= synchronizedAt;
        if (!IsActive)
        {
            RequiresReviewAfterReappearing = true;
        }

        RegisterUpdate(operatorId, synchronizedAt);
    }

    public void ApplyRegistryData(
        string? legalName,
        string? tradeName,
        string? registrationStatus,
        CompanyRegistryAddress? registryAddress,
        DateTimeOffset checkedAt)
    {
        LegalName = TrimToNull(legalName);
        TradeName = TrimToNull(tradeName);
        RegistrationStatus = TrimToNull(registrationStatus);
        RegistryAddress = registryAddress;
        RegistryLookupStatus = CompanyRegistryLookupStatus.Found;
        RegistryLastCheckedAt = checkedAt;
    }

    /// <summary>
    /// Registra que a consulta cadastral não trouxe dados. Os dados já conhecidos
    /// são preservados: uma indisponibilidade do serviço externo não pode apagar
    /// um cadastro obtido antes.
    /// </summary>
    public void RegisterRegistryLookupWithoutData(
        CompanyRegistryLookupStatus lookupStatus,
        DateTimeOffset checkedAt)
    {
        if (lookupStatus == CompanyRegistryLookupStatus.Found)
        {
            throw new ValidationException(
                "Uma consulta cadastral sem dados não pode ser registrada como encontrada.");
        }

        RegistryLookupStatus = lookupStatus;
        RegistryLastCheckedAt = checkedAt;
    }

    public void UpdateManualData(
        string displayName,
        string operatorId,
        DateTimeOffset updatedAt)
    {
        DisplayName = RequireText(displayName, "O nome operacional da empresa é obrigatório.");
        RegisterUpdate(operatorId, updatedAt);
    }

    /// <summary>
    /// Registra um cliente localizado automaticamente pelo CNPJ. O identificador
    /// nunca é aceito de um formulário porque pertence à integração, não ao
    /// cadastro operacional da empresa.
    /// </summary>
    public void LinkAsaasCustomer(
        AsaasEnvironment asaasEnvironment,
        string asaasCustomerId,
        string operatorId,
        DateTimeOffset linkedAt)
    {
        var normalizedCustomerId = RequireText(
            asaasCustomerId,
            "O identificador do cliente Asaas é obrigatório.");
        if (asaasEnvironment == AsaasEnvironment.Sandbox)
        {
            AsaasSandboxCustomerId = normalizedCustomerId;
        }
        else
        {
            AsaasProductionCustomerId = normalizedCustomerId;
        }

        RegisterUpdate(operatorId, linkedAt);
    }

    public void Deactivate(string operatorId, DateTimeOffset deactivatedAt)
    {
        IsActive = false;
        RequiresReviewAfterReappearing = false;
        RegisterUpdate(operatorId, deactivatedAt);
    }

    public void Reactivate(string operatorId, DateTimeOffset reactivatedAt)
    {
        IsActive = true;
        RequiresReviewAfterReappearing = false;
        RegisterUpdate(operatorId, reactivatedAt);
    }

    /// <summary>Indica se a empresa apareceu na sincronização informada.</summary>
    public bool WasSeenInImport(Guid? importId)
    {
        return importId is not null && LastImportId == importId;
    }

    private void RegisterUpdate(string operatorId, DateTimeOffset updatedAt)
    {
        UpdatedBy = RequireText(operatorId, "O responsável pela alteração é obrigatório.");
        UpdatedAt = updatedAt;
    }

    private static string RequireText(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(errorMessage);
        }

        return value.Trim();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
