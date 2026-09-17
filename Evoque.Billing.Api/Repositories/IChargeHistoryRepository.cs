using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

/// <summary>
/// Consulta somente leitura sobre as cobranças emitidas, atravessando
/// competências. `IChargeBatchRepository` responde por uma competência de cada
/// vez, que é o que o fluxo de emissão precisa; o histórico precisa do oposto.
/// </summary>
public interface IChargeHistoryRepository
{
    Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken);
}
