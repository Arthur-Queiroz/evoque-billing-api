using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.CompanyRegistry;
using Evoque.Billing.Api.Repositories;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Preenche razão social, nome fantasia, situação e endereço a partir do
/// cadastro público, e persiste o resultado.
///
/// O enriquecimento é sempre secundário: uma falha do serviço externo é
/// registrada na empresa e devolvida como contagem, nunca propagada como erro
/// que desfaria a sincronização vinda da planilha. O nome operacional
/// (`DisplayName`) jamais é sobrescrito aqui.
/// </summary>
public sealed class CompanyRegistryEnrichmentService(
    ICompanyRegistryGateway companyRegistryGateway,
    ICompanyRepository companyRepository,
    IOptions<CompanyRegistryOptions> companyRegistryOptions,
    ILogger<CompanyRegistryEnrichmentService> logger)
{
    public async Task<CompanyRegistryLookupStatus> RefreshAsync(
        Company company,
        CancellationToken cancellationToken)
    {
        CompanyRegistryLookupResult lookupResult;
        try
        {
            lookupResult = await companyRegistryGateway.FindByTaxIdAsync(company.TaxId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "A consulta cadastral inesperadamente falhou para o CNPJ {TaxId}.",
                company.TaxId);
            lookupResult = CompanyRegistryLookupResult.Unavailable();
        }

        ApplyLookupResult(company, lookupResult);
        await companyRepository.UpsertAsync(company, cancellationToken);
        return lookupResult.Status;
    }

    /// <summary>
    /// Consulta em lote com concorrência pequena e controlada, para não
    /// sobrecarregar um serviço externo que se apresenta como experimental.
    /// </summary>
    public async Task<CompanyRegistryEnrichmentSummary> RefreshManyAsync(
        IReadOnlyCollection<Company> companies,
        CancellationToken cancellationToken)
    {
        if (companies.Count == 0)
        {
            return new CompanyRegistryEnrichmentSummary(0, 0);
        }

        var maximumConcurrentLookups = Math.Max(1, companyRegistryOptions.Value.MaximumConcurrentLookups);
        using var concurrencyLimiter = new SemaphoreSlim(maximumConcurrentLookups, maximumConcurrentLookups);
        var lookupTasks = companies.Select(async company =>
        {
            await concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                return await RefreshAsync(company, cancellationToken);
            }
            finally
            {
                concurrencyLimiter.Release();
            }
        });

        var lookupStatuses = await Task.WhenAll(lookupTasks);
        return new CompanyRegistryEnrichmentSummary(
            lookupStatuses.Count(status => status == CompanyRegistryLookupStatus.Found),
            lookupStatuses.Count(status => status != CompanyRegistryLookupStatus.Found));
    }

    private static void ApplyLookupResult(Company company, CompanyRegistryLookupResult lookupResult)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        if (lookupResult.Status == CompanyRegistryLookupStatus.Found)
        {
            company.ApplyRegistryData(
                lookupResult.LegalName,
                lookupResult.TradeName,
                lookupResult.RegistrationStatus,
                lookupResult.Address,
                checkedAt);
            return;
        }

        company.RegisterRegistryLookupWithoutData(lookupResult.Status, checkedAt);
    }
}

public sealed record CompanyRegistryEnrichmentSummary(int EnrichedCount, int UnavailableCount);
