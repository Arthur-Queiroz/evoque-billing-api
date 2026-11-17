using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Evo;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Datas de entrada no convênio e, para quem não teve a data resolvida, o motivo.
/// </summary>
public sealed record CorporateMemberEntryDates(
    IReadOnlyDictionary<long, DateOnly> EntryDates,
    IReadOnlyDictionary<long, string> Failures);

/// <summary>
/// Descobre no EVO quando cada colaborador entrou no convênio.
/// </summary>
/// <remarks>
/// O contrato corporativo é renovado todo mês: o EVO cria um vínculo novo, com
/// <c>startDate</c> novo, a cada ciclo do aluno. A data do contrato ativo diria
/// que todo mundo acabou de entrar. A entrada é o início do primeiro contrato
/// corporativo cobrável do histórico, que a consulta por IdCliente devolve
/// inteiro.
/// </remarks>
public sealed class CorporateMemberEntryDateService(IEvoDirectoryGateway evoDirectoryGateway)
{
    public async Task<CorporateMemberEntryDates> ResolveAsync(
        IReadOnlyCollection<long> evoMemberIds,
        CancellationToken cancellationToken)
    {
        var entryDates = new Dictionary<long, DateOnly>();
        var failures = new Dictionary<long, string>();

        foreach (var memberIdBatch in evoMemberIds.Distinct().Chunk(IEvoDirectoryGateway.MaximumMembersPerLookup))
        {
            IReadOnlyCollection<EvoMember> members;
            try
            {
                members = await evoDirectoryGateway.ListMembersByIdsAsync(memberIdBatch, cancellationToken);
            }
            catch (Exception exception) when (exception is ExternalOperationNotAllowedException or HttpRequestException
                || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                var reason = $"Evo indisponível: {exception.Message}";
                foreach (var memberId in memberIdBatch)
                {
                    failures[memberId] = reason;
                }

                continue;
            }

            var membersById = members
                .GroupBy(member => (long)member.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var memberId in memberIdBatch)
            {
                if (!membersById.TryGetValue(memberId, out var member))
                {
                    failures[memberId] = "Colaborador não encontrado no Evo.";
                    continue;
                }

                var firstCorporateStart = member.Memberships
                    .Where(membership => BillableCorporateContracts.Includes(membership.Name))
                    .Where(membership => membership.StartDate.HasValue)
                    .Select(membership => DateOnly.FromDateTime(membership.StartDate!.Value.DateTime))
                    .Order()
                    .Cast<DateOnly?>()
                    .FirstOrDefault();
                if (firstCorporateStart is null)
                {
                    failures[memberId] = "Nenhum contrato corporativo com data de início no Evo.";
                    continue;
                }

                entryDates[memberId] = firstCorporateStart.Value;
            }
        }

        return new CorporateMemberEntryDates(entryDates, failures);
    }
}
