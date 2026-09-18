using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasChargeGateway
{
    Task<AsaasChargeCreation> CreateChargeAsync(
        AsaasEnvironment asaasEnvironment,
        AsaasChargeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lê a situação atual de uma cobrança. É leitura pura: nada aqui cria,
    /// altera ou cancela cobrança, e por isso vale nos dois ambientes.
    /// </summary>
    Task<AsaasChargeState> GetChargeAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        CancellationToken cancellationToken);
}

public sealed record AsaasChargeRequest(
    string CustomerId,
    decimal Amount,
    DateOnly DueDate,
    string Description,
    string ExternalReference);

public sealed record AsaasChargeCreation(string PaymentId, string? BankSlipUrl);

/// <summary>
/// Situação da cobrança como o Asaas a descreve agora. `Status` vem cru, em
/// maiúsculas, e quem traduz é <see cref="Domain.ChargeBatchItem"/> — o gateway
/// não conhece os estados do nosso domínio.
/// </summary>
public sealed record AsaasChargeState(string PaymentId, string Status, DateOnly? PaymentDate);
