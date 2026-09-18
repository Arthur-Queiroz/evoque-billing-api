using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

/// <summary>
/// Consulta somente leitura sobre as cobranças emitidas, atravessando
/// competências. `IChargeBatchRepository` responde por uma competência de cada
/// vez, que é o que o fluxo de emissão precisa; o histórico precisa do oposto.
/// </summary>
public interface IChargeHistoryRepository
{
    /// <summary>
    /// Devolve as cobranças que casam com o filtro, da mais recente para a mais
    /// antiga por <see cref="ChargeHistoryEntry.IssuedAt"/>. Um filtro sem
    /// nenhum critério devolve tudo.
    /// </summary>
    /// <remarks>
    /// A ordenação usa a data de criação do lote porque ela nunca muda. Não
    /// ordene por <c>charge_batch_items.updated_at</c>: sincronizar a situação
    /// de pagamento reescreve esse campo, e o histórico se reembaralharia a cada
    /// clique em "Atualizar situação".
    ///
    /// Toda implementação precisa honrar esta ordem. Há duas — uma em memória e
    /// uma MySQL — e um teste que passa numa e não na outra é o modo de falha
    /// que este comentário existe para evitar.
    /// </remarks>
    Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken);
}
