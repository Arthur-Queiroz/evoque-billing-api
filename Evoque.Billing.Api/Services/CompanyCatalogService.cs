using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Regras do catálogo interno de empresas: consulta, cadastro manual, edição,
/// inativação, reativação e atualização cadastral.
///
/// Nenhuma operação deste serviço cria cliente ou cobrança no Asaas. Configurar
/// um identificador de cliente aqui é apenas registrar um vínculo existente.
/// </summary>
public sealed class CompanyCatalogService(
    ICompanyRepository companyRepository,
    ICompanyCatalogImportRepository companyCatalogImportRepository,
    ICorporateMemberRepository corporateMemberRepository,
    CorporateMemberService corporateMemberService,
    ICompanyBillingScheduleRepository companyBillingScheduleRepository,
    IBillingDraftRepository billingDraftRepository,
    CompanyRegistryEnrichmentService companyRegistryEnrichmentService,
    IAuditLogRepository auditLogRepository)
{
    public async Task<IReadOnlyCollection<CompanyResponse>> ListAsync(
        ListCompaniesQuery query,
        CancellationToken cancellationToken)
    {
        var companies = await companyRepository.ListAsync(cancellationToken);
        var schedulesByCompany = await ReadSchedulesByCompanyAsync(cancellationToken);
        var latestImportId = await ReadLatestImportIdAsync(cancellationToken);
        var memberCountsByCompany = await ReadActiveMemberCountsByCompanyAsync(cancellationToken);

        return companies
            .Select(company => CompanyResponse.FromDomain(
                company,
                schedulesByCompany.GetValueOrDefault(company.TaxId),
                latestImportId,
                memberCountsByCompany.GetValueOrDefault(company.TaxId)))
            .Where(companyResponse => MatchesQuery(companyResponse, query))
            .ToArray();
    }

    public async Task<CompanyResponse> GetAsync(string taxId, CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<CompanyResponse> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedTaxId = CompanyTaxId.Normalize(request.TaxId);
        ValidateOptionalClosingDay(request.ClosingDay);
        var existingCompany = await companyRepository.FindByTaxIdAsync(normalizedTaxId, cancellationToken);
        if (existingCompany is not null)
        {
            throw new ConflictException(
                $"Já existe uma empresa cadastrada com o CNPJ {CompanyTaxId.Format(normalizedTaxId)}.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var requestedDisplayName = request.DisplayName?.Trim();
        var fallbackDisplayName = $"Empresa {CompanyTaxId.Format(normalizedTaxId)}";
        var company = Company.CreateManually(
            normalizedTaxId,
            string.IsNullOrWhiteSpace(requestedDisplayName)
                ? fallbackDisplayName
                : requestedDisplayName,
            request.OperatorId,
            createdAt);
        await companyRepository.UpsertAsync(company, cancellationToken);
        await ApplyClosingDayAsync(company, request.ClosingDay, request.OperatorId, cancellationToken);

        // O CNPJ é suficiente para o cadastro. Quando o operador não informa
        // um nome, o cadastro público fornece o nome fantasia ou a razão social.
        // Se a consulta estiver indisponível, a empresa continua cadastrada com
        // um nome provisório e pode ser editada depois.
        await companyRegistryEnrichmentService.RefreshAsync(company, cancellationToken);
        if (string.IsNullOrWhiteSpace(requestedDisplayName))
        {
            var registryDisplayName = company.TradeName ?? company.LegalName;
            if (!string.IsNullOrWhiteSpace(registryDisplayName))
            {
                company.UpdateManualData(registryDisplayName, request.OperatorId, DateTimeOffset.UtcNow);
                await companyRepository.UpsertAsync(company, cancellationToken);
            }
        }

        await RegisterAuditAsync(
            "company.created",
            request.OperatorId,
            createdAt,
            $"Empresa {company.DisplayName} ({CompanyTaxId.Format(company.TaxId)}) cadastrada manualmente.",
            cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<CompanyResponse> UpdateAsync(
        string taxId,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        ValidateOptionalClosingDay(request.ClosingDay);
        var updatedAt = DateTimeOffset.UtcNow;
        company.UpdateManualData(
            request.DisplayName,
            request.OperatorId,
            updatedAt);
        await companyRepository.UpsertAsync(company, cancellationToken);
        await ApplyClosingDayAsync(company, request.ClosingDay, request.OperatorId, cancellationToken);
        await RegisterAuditAsync(
            "company.updated",
            request.OperatorId,
            updatedAt,
            $"Empresa {CompanyTaxId.Format(company.TaxId)} atualizada.",
            cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<CompanyResponse> DeactivateAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var deactivatedAt = DateTimeOffset.UtcNow;
        company.Deactivate(request.OperatorId, deactivatedAt);
        await companyRepository.UpsertAsync(company, cancellationToken);

        // A agenda é desligada junto, para que nenhum lote antigo continue
        // selecionando uma empresa que saiu do corporativo.
        await DeactivateScheduleAsync(company, request.OperatorId, cancellationToken);
        await RegisterAuditAsync(
            "company.deactivated",
            request.OperatorId,
            deactivatedAt,
            $"Empresa {CompanyTaxId.Format(company.TaxId)} inativada.",
            cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<CompanyResponse> ReactivateAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var reactivatedAt = DateTimeOffset.UtcNow;
        company.Reactivate(request.OperatorId, reactivatedAt);
        await companyRepository.UpsertAsync(company, cancellationToken);
        await RegisterAuditAsync(
            "company.reactivated",
            request.OperatorId,
            reactivatedAt,
            $"Empresa {CompanyTaxId.Format(company.TaxId)} reativada.",
            cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<CompanyResponse> RefreshRegistryAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var lookupStatus = await companyRegistryEnrichmentService.RefreshAsync(company, cancellationToken);
        await RegisterAuditAsync(
            "company.registry-refreshed",
            request.OperatorId,
            DateTimeOffset.UtcNow,
            $"Consulta cadastral da empresa {CompanyTaxId.Format(company.TaxId)} retornou {lookupStatus}.",
            cancellationToken);
        return await CreateResponseAsync(company, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CorporateMemberResponse>> ListMembersAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        return await corporateMemberService.ListAsync(
            new ListCorporateMembersQuery(
                Status: "all",
                CompanyTaxId: company.TaxId),
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<CompanyBillingHistoryEntryResponse>> ListBillingHistoryAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var billingDrafts = await billingDraftRepository.ListByExternalCompanyIdAsync(
            company.TaxId,
            cancellationToken);
        return billingDrafts.Select(CompanyBillingHistoryEntryResponse.FromDomain).ToArray();
    }

    /// <summary>
    /// Empresas ativas do catálogo com agenda ativa no dia informado. É a lista
    /// que a tela de faturamento por dia e o lote agendado devem usar.
    /// </summary>
    public async Task<IReadOnlyCollection<CompanyResponse>> ListActiveByClosingDayAsync(
        int closingDay,
        CancellationToken cancellationToken)
    {
        var schedules = await companyBillingScheduleRepository.ListActiveByClosingDayAsync(
            closingDay,
            cancellationToken);
        var latestImportId = await ReadLatestImportIdAsync(cancellationToken);
        var memberCountsByCompany = await ReadActiveMemberCountsByCompanyAsync(cancellationToken);

        var companies = new List<CompanyResponse>();
        foreach (var schedule in schedules)
        {
            var company = await companyRepository.FindByTaxIdAsync(
                schedule.ExternalCompanyId,
                cancellationToken);
            if (company is null || !company.IsActive)
            {
                continue;
            }

            companies.Add(CompanyResponse.FromDomain(
                company,
                schedule,
                latestImportId,
                memberCountsByCompany.GetValueOrDefault(company.TaxId)));
        }

        return companies
            .OrderBy(company => company.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CompanyCatalogImportSummaryResponse?> FindLatestImportAsync(
        CancellationToken cancellationToken)
    {
        var latestImport = await companyCatalogImportRepository.FindLatestAsync(cancellationToken);
        return latestImport is null
            ? null
            : CompanyCatalogImportSummaryResponse.FromDomain(latestImport);
    }

    private static bool MatchesQuery(CompanyResponse company, ListCompaniesQuery query)
    {
        if (!MatchesSearch(company, query.Search))
        {
            return false;
        }

        if (query.Status is not null
            && !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var wantsActive = string.Equals(query.Status, "active", StringComparison.OrdinalIgnoreCase);
            if (company.IsActive != wantsActive)
            {
                return false;
            }
        }

        if (query.ClosingDay is not null && company.ClosingDay != query.ClosingDay)
        {
            return false;
        }

        if (query.WithoutClosingDay == true && company.ClosingDay is not null)
        {
            return false;
        }

        if (query.Source is not null
            && !string.Equals(company.Source, query.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.SeenInLastImport is not null && company.SeenInLastImport != query.SeenInLastImport)
        {
            return false;
        }

        return MatchesAsaasLink(company, query.AsaasLink);
    }

    private static bool MatchesSearch(CompanyResponse company, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var normalizedSearch = SpreadsheetText.Normalize(search);
        var searchedDigits = new string(search.Where(char.IsAsciiDigit).ToArray());
        if (searchedDigits.Length > 0 && company.TaxId.Contains(searchedDigits, StringComparison.Ordinal))
        {
            return true;
        }

        var searchableNames = new[]
        {
            company.DisplayName,
            company.EvoName,
            company.LegalName,
            company.TradeName,
        };
        return searchableNames.Any(name =>
            !string.IsNullOrWhiteSpace(name)
            && SpreadsheetText.Normalize(name).Contains(normalizedSearch, StringComparison.Ordinal));
    }

    private static bool MatchesAsaasLink(CompanyResponse company, string? asaasLink)
    {
        if (string.IsNullOrWhiteSpace(asaasLink))
        {
            return true;
        }

        var hasAsaasCustomer = !string.IsNullOrWhiteSpace(company.AsaasSandboxCustomerId)
            || !string.IsNullOrWhiteSpace(company.AsaasProductionCustomerId);
        return string.Equals(asaasLink, "configured", StringComparison.OrdinalIgnoreCase)
            ? hasAsaasCustomer
            : !hasAsaasCustomer;
    }

    private async Task<Company> RequireCompanyAsync(string taxId, CancellationToken cancellationToken)
    {
        var normalizedTaxId = CompanyTaxId.Normalize(taxId);
        return await companyRepository.FindByTaxIdAsync(normalizedTaxId, cancellationToken)
            ?? throw new NotFoundException(
                $"Empresa {CompanyTaxId.Format(normalizedTaxId)} não encontrada no catálogo.");
    }

    private async Task<CompanyResponse> CreateResponseAsync(
        Company company,
        CancellationToken cancellationToken)
    {
        var schedulesByCompany = await ReadSchedulesByCompanyAsync(cancellationToken);
        var latestImportId = await ReadLatestImportIdAsync(cancellationToken);
        var memberCountsByCompany = await ReadActiveMemberCountsByCompanyAsync(cancellationToken);
        return CompanyResponse.FromDomain(
            company,
            schedulesByCompany.GetValueOrDefault(company.TaxId),
            latestImportId,
            memberCountsByCompany.GetValueOrDefault(company.TaxId));
    }

    private async Task<Dictionary<string, int?>> ReadActiveMemberCountsByCompanyAsync(
        CancellationToken cancellationToken)
    {
        var corporateMembers = await corporateMemberRepository.ListAsync(cancellationToken);
        var memberCountsByCompany = corporateMembers
            .GroupBy(member => member.CompanyTaxId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (int?)group.Count(member => member.IsActive),
                StringComparer.Ordinal);
        return memberCountsByCompany;
    }

    private async Task<Dictionary<string, CompanyBillingSchedule>> ReadSchedulesByCompanyAsync(
        CancellationToken cancellationToken)
    {
        var schedules = await companyBillingScheduleRepository.ListAsync(cancellationToken);
        return schedules.ToDictionary(schedule => schedule.ExternalCompanyId, StringComparer.Ordinal);
    }

    private async Task<Guid?> ReadLatestImportIdAsync(CancellationToken cancellationToken)
    {
        var latestImport = await companyCatalogImportRepository.FindLatestAsync(cancellationToken);
        return latestImport?.Id;
    }

    /// <summary>
    /// Um dia informado liga a agenda no CNPJ da empresa. Um dia ausente desliga
    /// a agenda existente, que é o significado operacional de "empresa sem dia".
    /// </summary>
    private async Task ApplyClosingDayAsync(
        Company company,
        int? closingDay,
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (closingDay is not null)
        {
            await companyBillingScheduleRepository.UpsertAsync(
                CompanyBillingSchedule.Create(
                    company.TaxId,
                    closingDay.Value,
                    isActive: true,
                    operatorId,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            return;
        }

        await DeactivateScheduleAsync(company, operatorId, cancellationToken);
    }

    private static void ValidateOptionalClosingDay(int? closingDay)
    {
        if (closingDay is not null)
        {
            CompanyBillingSchedule.ValidateClosingDay(closingDay.Value);
        }
    }

    private async Task DeactivateScheduleAsync(
        Company company,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var schedulesByCompany = await ReadSchedulesByCompanyAsync(cancellationToken);
        if (!schedulesByCompany.TryGetValue(company.TaxId, out var existingSchedule)
            || !existingSchedule.IsActive)
        {
            return;
        }

        await companyBillingScheduleRepository.UpsertAsync(
            CompanyBillingSchedule.Create(
                company.TaxId,
                existingSchedule.ClosingDay,
                isActive: false,
                operatorId,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task RegisterAuditAsync(
        string action,
        string operatorId,
        DateTimeOffset occurredAt,
        string details,
        CancellationToken cancellationToken)
    {
        await auditLogRepository.AddAsync(
            AuditLog.Create(action, operatorId, occurredAt, null, null, details),
            cancellationToken);
    }
}
