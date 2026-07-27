using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Evo;

namespace Evoque.Billing.Api.Services;

public sealed class CorporateBillingPreviewService(
    IEvoDirectoryGateway evoDirectoryGateway,
    EvoCorporatePartnershipResolver corporatePartnershipResolver)
{
    private const int EvoPageSize = 50;
    private static readonly TimeSpan DelayBetweenSaleLookups = TimeSpan.FromMilliseconds(250);

    public async Task<CorporateBillingPreviewResponse> CreateAsync(
        CreateCorporateBillingPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var competenceStart = new DateOnly(request.Year, request.Month, 1);
        var competenceEnd = competenceStart.AddMonths(1).AddDays(-1);
        var receivablesRead = await ReadReceivablesAsync(
            competenceStart,
            competenceEnd,
            request.ReceivableLimit,
            request.ReceivableSkip,
            cancellationToken);
        var distinctReceivables = receivablesRead.Receivables
            .GroupBy(receivable => receivable.Id)
            .Select(receivableGroup => receivableGroup.First())
            .ToArray();
        var duplicateReceivableCount = receivablesRead.Receivables.Count - distinctReceivables.Length;
        var distinctSaleIds = distinctReceivables
            .Where(receivable => receivable.SaleId is > 0)
            .Select(receivable => receivable.SaleId!.Value)
            .Distinct()
            .OrderBy(saleId => saleId)
            .ToArray();
        var saleIdsToLookUp = distinctSaleIds
            .Take(request.SaleLookupLimit)
            .ToArray();

        var salesById = new Dictionary<int, EvoSale>();
        var exceptions = new List<CorporateBillingPreviewExceptionResponse>();
        await ReadSalesAsync(saleIdsToLookUp, salesById, exceptions, cancellationToken);

        var corporateReceivables = new List<ResolvedCorporateReceivable>();
        foreach (var receivable in distinctReceivables)
        {
            AddResolvedReceivableOrException(
                receivable,
                salesById,
                corporateReceivables,
                exceptions);
        }

        var companyPreviews = corporateReceivables
            .GroupBy(receivable => receivable.PartnershipId)
            .Select(CreateCompanyPreview)
            .OrderBy(company => company.PartnershipName)
            .ToArray();
        var isComplete = request.ReceivableSkip == 0
            && receivablesRead.ReachedEnd
            && saleIdsToLookUp.Length == distinctSaleIds.Length
            && exceptions.All(exception => exception.Code != "EvoRateLimitReached");
        var completionMessage = CreateCompletionMessage(
            isComplete,
            request.ReceivableSkip,
            receivablesRead.ReachedEnd,
            saleIdsToLookUp.Length,
            distinctSaleIds.Length);

        return new CorporateBillingPreviewResponse(
            request.Year,
            request.Month,
            request.ReceivableSkip,
            distinctReceivables.Length,
            duplicateReceivableCount,
            distinctSaleIds.Length,
            salesById.Count,
            isComplete,
            completionMessage,
            companyPreviews,
            exceptions);
    }

    private async Task<ReceivableReadResult> ReadReceivablesAsync(
        DateOnly competenceStart,
        DateOnly competenceEnd,
        int receivableLimit,
        int receivableSkip,
        CancellationToken cancellationToken)
    {
        var receivables = new List<EvoReceivable>();
        var reachedEnd = false;

        while (receivables.Count < receivableLimit)
        {
            var remainingCount = receivableLimit - receivables.Count;
            var pageSize = Math.Min(EvoPageSize, remainingCount);
            var page = await evoDirectoryGateway.ListReceivablesAsync(
                competenceStart,
                competenceEnd,
                pageSize,
                receivableSkip + receivables.Count,
                cancellationToken);
            receivables.AddRange(page);

            if (page.Count < pageSize)
            {
                reachedEnd = true;
                break;
            }
        }

        return new ReceivableReadResult(receivables, reachedEnd);
    }

    private async Task ReadSalesAsync(
        IReadOnlyCollection<int> saleIds,
        IDictionary<int, EvoSale> salesById,
        ICollection<CorporateBillingPreviewExceptionResponse> exceptions,
        CancellationToken cancellationToken)
    {
        var isFirstLookup = true;
        foreach (var saleId in saleIds)
        {
            if (!isFirstLookup)
            {
                await Task.Delay(DelayBetweenSaleLookups, cancellationToken);
            }

            isFirstLookup = false;
            try
            {
                var sale = await evoDirectoryGateway.GetSaleByIdAsync(saleId, cancellationToken);
                salesById[saleId] = sale;
            }
            catch (EvoSaleLookupException exception) when (exception.StatusCode == 429)
            {
                exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                    "EvoRateLimitReached",
                    "O Evo atingiu o limite de requisições. Aguarde e continue a investigação depois.",
                    null,
                    saleId));
                break;
            }
            catch (EvoSaleLookupException exception)
            {
                exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                    "SaleLookupFailed",
                    $"A venda não pôde ser consultada no Evo (HTTP {exception.StatusCode}).",
                    null,
                    saleId));
            }
        }
    }

    private void AddResolvedReceivableOrException(
        EvoReceivable receivable,
        IReadOnlyDictionary<int, EvoSale> salesById,
        ICollection<ResolvedCorporateReceivable> corporateReceivables,
        ICollection<CorporateBillingPreviewExceptionResponse> exceptions)
    {
        if (receivable.SaleId is not int saleId)
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "ReceivableWithoutSale",
                "O recebível não possui uma venda associada.",
                receivable.Id,
                null));
            return;
        }

        if (IsCanceledReceivable(receivable))
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "CanceledReceivable",
                "O recebível está cancelado e foi excluído da prévia.",
                receivable.Id,
                saleId));
            return;
        }

        if (!salesById.TryGetValue(saleId, out var sale))
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "SaleNotProcessed",
                "A venda ainda não foi processada nesta execução da prévia.",
                receivable.Id,
                saleId));
            return;
        }

        if (sale.Removed)
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "RemovedSale",
                "A venda está removida ou cancelada no Evo e foi excluída da prévia.",
                receivable.Id,
                saleId));
            return;
        }

        var partnershipResolution = corporatePartnershipResolver.Resolve(sale);
        if (partnershipResolution.HasConflict)
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "PartnershipConflict",
                partnershipResolution.ConflictMessage
                    ?? "A venda possui um conflito de parceria corporativa.",
                receivable.Id,
                saleId));
            return;
        }

        if (!partnershipResolution.IsResolved)
        {
            exceptions.Add(new CorporateBillingPreviewExceptionResponse(
                "SaleWithoutPartnership",
                "A venda não possui parceria corporativa explícita.",
                receivable.Id,
                saleId));
            return;
        }

        var amountCents = ConvertToCents(receivable.Amount);
        corporateReceivables.Add(new ResolvedCorporateReceivable(
            partnershipResolution.PartnershipId!.Value,
            partnershipResolution.PartnershipName!,
            receivable,
            sale.MemberId,
            partnershipResolution.MemberMembershipId,
            amountCents));
    }

    private static CorporateBillingCompanyPreviewResponse CreateCompanyPreview(
        IGrouping<int, ResolvedCorporateReceivable> companyGroup)
    {
        var receivables = companyGroup
            .OrderBy(receivable => receivable.Receivable.PayerName)
            .ThenBy(receivable => receivable.Receivable.Id)
            .Select(receivable => new CorporateReceivablePreviewResponse(
                receivable.Receivable.Id,
                receivable.Receivable.SaleId!.Value,
                receivable.MemberId,
                receivable.Receivable.PayerName?.Trim() ?? $"Membro {receivable.MemberId}",
                receivable.Receivable.CompetenceDate,
                receivable.Receivable.DueDate,
                receivable.AmountCents,
                ConvertFromCents(receivable.AmountCents),
                receivable.Receivable.StatusId,
                receivable.Receivable.StatusName,
                receivable.Receivable.PaymentTypeId,
                receivable.Receivable.PaymentTypeName,
                receivable.Receivable.CurrentInstallment,
                receivable.Receivable.TotalInstallments,
                receivable.MemberMembershipId))
            .ToArray();
        var totalAmountCents = receivables.Sum(receivable => receivable.AmountCents);

        return new CorporateBillingCompanyPreviewResponse(
            companyGroup.Key,
            companyGroup.Select(receivable => receivable.PartnershipName).First(),
            companyGroup.Select(receivable => receivable.MemberId).Distinct().Count(),
            receivables.Length,
            totalAmountCents,
            ConvertFromCents(totalAmountCents),
            receivables);
    }

    private static bool IsCanceledReceivable(EvoReceivable receivable)
    {
        return receivable.StatusId == 3
            || string.Equals(receivable.StatusName, "Canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(receivable.StatusName, "Cancelado", StringComparison.OrdinalIgnoreCase);
    }

    private static long ConvertToCents(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal ConvertFromCents(long amountCents)
    {
        return amountCents / 100m;
    }

    private static string CreateCompletionMessage(
        bool isComplete,
        int receivableSkip,
        bool reachedEndOfReceivables,
        int saleLookupCount,
        int distinctSaleCount)
    {
        if (isComplete)
        {
            return "Todos os recebíveis e vendas da competência foram processados.";
        }

        if (receivableSkip > 0)
        {
            return "A prévia representa um bloco de investigação iniciado após outros recebíveis.";
        }

        if (!reachedEndOfReceivables)
        {
            return "A prévia é parcial porque o limite de recebíveis foi atingido.";
        }

        if (saleLookupCount < distinctSaleCount)
        {
            return "A prévia é parcial porque o limite de consultas de vendas foi atingido.";
        }

        return "A prévia é parcial porque o Evo interrompeu uma ou mais consultas.";
    }

    private static void ValidateRequest(CreateCorporateBillingPreviewRequest request)
    {
        if (request.Year is < 2020 or > 2100)
        {
            throw new ValidationException("O ano da competência é inválido.");
        }

        if (request.Month is < 1 or > 12)
        {
            throw new ValidationException("O mês da competência deve estar entre 1 e 12.");
        }

        if (request.ReceivableLimit is < 1 or > 500)
        {
            throw new ValidationException("O limite de recebíveis deve estar entre 1 e 500.");
        }

        if (request.ReceivableSkip < 0)
        {
            throw new ValidationException("A posição inicial dos recebíveis não pode ser negativa.");
        }

        if (request.SaleLookupLimit is < 1 or > 40)
        {
            throw new ValidationException("O limite de vendas deve estar entre 1 e 40.");
        }
    }

    private sealed record ReceivableReadResult(
        IReadOnlyCollection<EvoReceivable> Receivables,
        bool ReachedEnd);

    private sealed record ResolvedCorporateReceivable(
        int PartnershipId,
        string PartnershipName,
        EvoReceivable Receivable,
        int MemberId,
        int? MemberMembershipId,
        long AmountCents);
}
