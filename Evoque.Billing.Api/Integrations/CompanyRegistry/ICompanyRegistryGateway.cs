using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.CompanyRegistry;

/// <summary>
/// Consulta o cadastro público de CNPJ. O gateway nunca lança por falha do
/// serviço externo: ele devolve o motivo, porque enriquecimento indisponível
/// não pode desfazer a sincronização vinda da planilha.
/// </summary>
public interface ICompanyRegistryGateway
{
    Task<CompanyRegistryLookupResult> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken);
}

public sealed record CompanyRegistryLookupResult(
    CompanyRegistryLookupStatus Status,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    CompanyRegistryAddress? Address)
{
    public static CompanyRegistryLookupResult Found(
        string? legalName,
        string? tradeName,
        string? registrationStatus,
        CompanyRegistryAddress? address)
    {
        return new CompanyRegistryLookupResult(
            CompanyRegistryLookupStatus.Found,
            legalName,
            tradeName,
            registrationStatus,
            address);
    }

    public static CompanyRegistryLookupResult NotFound()
    {
        return new CompanyRegistryLookupResult(
            CompanyRegistryLookupStatus.NotFound,
            null,
            null,
            null,
            null);
    }

    public static CompanyRegistryLookupResult Unavailable()
    {
        return new CompanyRegistryLookupResult(
            CompanyRegistryLookupStatus.Unavailable,
            null,
            null,
            null,
            null);
    }
}
