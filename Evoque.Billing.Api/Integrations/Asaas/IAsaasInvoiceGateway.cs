using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasInvoiceGateway
{
    Task<AsaasInvoiceCreation> ScheduleInvoiceAsync(
        AsaasEnvironment asaasEnvironment,
        AsaasInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<AsaasInvoiceState> GetInvoiceAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasInvoiceId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dados de `POST /v3/invoices`. A nota é sempre vinculada a uma cobrança: as
/// 264 notas da produção têm `payment` preenchido e nenhuma é avulsa.
///
/// As propriedades são `required init` de propósito: são quatro textos em
/// sequência e trocar dois deles compilaria sem erro, emitindo nota fiscal com
/// serviço municipal ou descrição errados — o que a prefeitura não desfaz.
/// </summary>
public sealed record AsaasInvoiceRequest
{
    public required string AsaasPaymentId { get; init; }

    public required decimal Value { get; init; }

    public required DateOnly EffectiveDate { get; init; }

    public required string ServiceDescription { get; init; }

    public required string MunicipalServiceId { get; init; }

    public required string MunicipalServiceName { get; init; }

    public required string ExternalReference { get; init; }

    public required bool RetainsIss { get; init; }

    public required decimal IssTaxRate { get; init; }
}

public sealed record AsaasInvoiceCreation(string InvoiceId, string Status);

public sealed record AsaasInvoiceState(string InvoiceId, string Status, string? StatusDescription);
