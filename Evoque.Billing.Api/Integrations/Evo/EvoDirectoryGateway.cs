using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Evo;

public sealed class EvoDirectoryGateway(
    HttpClient httpClient,
    IOptions<EvoOptions> evoOptions,
    IMemoryCache memoryCache) : IEvoDirectoryGateway
{
    private static readonly JsonSerializerOptions WebJsonSerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
        string? name,
        string? email,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        var queryParts = new List<string>
        {
            $"take={take}",
            $"skip={skip}",
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            queryParts.Add($"name={Uri.EscapeDataString(name.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            queryParts.Add($"email={Uri.EscapeDataString(email.Trim())}");
        }

        using var response = await httpClient.GetAsync(
            $"api/v2/employees?{string.Join("&", queryParts)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar colaboradores no Evo (HTTP {(int)response.StatusCode}).");
        }

        var employees = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoEmployeeResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar colaboradores.");

        return employees.Select(employee => new EvoEmployee(
            employee.IdEmployee,
            employee.IdBranch,
            employee.BranchName,
            employee.Name,
            employee.Status,
            employee.CurrentEmail,
            employee.JobPosition)).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
        int status,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync(
            $"api/v1/partnership?status={status}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar empresas e convênios no Evo (HTTP {(int)response.StatusCode}).");
        }

        var partnerships = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoPartnershipResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar empresas e convênios.");

        return partnerships.Select(partnership => new EvoPartnership(
            partnership.IdPartnership,
            partnership.Description,
            partnership.IsBlockedFlag,
            partnership.IsInactiveFlag,
            partnership.Company is null
                ? null
                : new EvoCompany(
                    partnership.Company.AgreementCompanyId,
                    partnership.Company.BranchId,
                    partnership.Company.CorporateName,
                    partnership.Company.TradeName,
                    partnership.Company.Cnpj,
                    partnership.Company.IsDeletedFlag))).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoMember>> ListMembersAsync(
        int status,
        int take,
        int skip,
        bool includeMemberships,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync(
            $"api/v2/members?status={status}&take={take}&skip={skip}&showMemberships={includeMemberships.ToString().ToLowerInvariant()}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar membros no Evo (HTTP {(int)response.StatusCode}).");
        }

        var members = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoMemberResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar membros.");

        return members.Select(member => new EvoMember(
            member.IdMember,
            member.IdBranch,
            member.BranchName,
            member.FirstName,
            member.LastName,
            member.Memberships
                .Where(membership => membership.IdMembership.HasValue && membership.IdMemberMembership.HasValue)
                .Select(membership => new EvoMembership(
                    membership.IdMembership!.Value,
                    membership.IdMemberMembership!.Value,
                    membership.Name ?? "Contrato nÃ£o informado",
                    membership.MembershipStatus,
                    membership.ValueNextMonth,
                    ReadDecimalOrNull(membership.NextCharge),
                    membership.StartDate,
                    membership.EndDate,
                    membership.NextDateSuspension))
                .ToArray())).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoMemberMembership>> ListMemberMembershipsAsync(
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync(
            $"api/v3/membermembership?take={take}&skip={skip}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"NÃ£o foi possÃ­vel consultar vÃ­nculos de matrÃ­cula no Evo (HTTP {(int)response.StatusCode}).");
        }

        var memberMemberships = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoMemberMembershipResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta invÃ¡lida ao consultar vÃ­nculos de matrÃ­cula.");

        return memberMemberships.Select(memberMembership => new EvoMemberMembership(
            memberMembership.IdMember,
            memberMembership.Name,
            memberMembership.IdMembership,
            memberMembership.IdMemberMembership,
            memberMembership.IdBranch,
            memberMembership.IdSale,
            memberMembership.SaleValue,
            memberMembership.NameMembership,
            memberMembership.StatusMemberMembership)).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoReceivable>> ListReceivablesAsync(
        DateOnly competenceDateStart,
        DateOnly competenceDateEnd,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/receivables?competenceDateStart={competenceDateStart:yyyy-MM-dd}" +
            $"&competenceDateEnd={competenceDateEnd:yyyy-MM-dd}&take={take}&skip={skip}");
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar recebíveis no Evo (HTTP {(int)response.StatusCode}).");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);
        var receivableElements = ReadReceivableElements(responseDocument.RootElement);

        return receivableElements
            .Select(receivableElement => receivableElement.Deserialize<EvoReceivableResponse>(
                WebJsonSerializerOptions))
            .Where(receivable => receivable is not null)
            .Select(receivable => new EvoReceivable(
                receivable!.IdReceivable,
                receivable.IdSale,
                receivable.IdMemberPayer,
                receivable.PayerName,
                ReadDateOnlyOrNull(receivable.CompetenceDate),
                ReadDateOnlyOrNull(receivable.DueDate),
                receivable.Ammount,
                receivable.Status?.Id,
                receivable.Status?.Name,
                receivable.PaymentType?.Id,
                receivable.PaymentType?.Name,
                receivable.CurrentInstallment,
                receivable.TotalInstallments))
            .ToArray();
    }

    public async Task<EvoSale> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<EvoSale>($"evo-sale-{saleId}", out var cachedSale)
            && cachedSale is not null)
        {
            return cachedSale;
        }

        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync($"api/v2/sales/{saleId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 403 or 404 or 429)
            {
                throw new EvoSaleLookupException(saleId, (int)response.StatusCode);
            }

            throw new ExternalOperationNotAllowedException(
                $"NÃ£o foi possÃ­vel consultar a venda {saleId} no Evo (HTTP {(int)response.StatusCode}).");
        }

        var sale = await response.Content.ReadFromJsonAsync<EvoSaleResponse>(cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                $"O Evo retornou uma resposta invÃ¡lida ao consultar a venda {saleId}.");

        var evoSale = new EvoSale(
            sale.IdSale,
            sale.IdMember,
            sale.MisspelledCorporatePartnershipId ?? sale.CorrectCorporatePartnershipId,
            sale.CorporatePartnershipName,
            sale.Removed == true,
            sale.SaleDate,
            sale.SaleItens?.Select(saleItem => new EvoSaleItem(
                saleItem.IdSaleItem,
                saleItem.IdMembership,
                saleItem.IdMemberMembership,
                saleItem.Item ?? saleItem.Description,
                saleItem.ItemValue,
                saleItem.SaleValue,
                saleItem.CorporateDiscount,
                saleItem.MisspelledCorporatePartnershipId ?? saleItem.CorrectCorporatePartnershipId,
                saleItem.CorporatePartnershipName)).ToArray() ?? []);

        memoryCache.Set($"evo-sale-{saleId}", evoSale, TimeSpan.FromMinutes(15));
        return evoSale;
    }

    public async Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync("api/v1/configuration/group-branches", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar grupos de filiais no Evo (HTTP {(int)response.StatusCode}).");
        }

        var branchGroups = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoBranchGroupResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar grupos de filiais.");

        return branchGroups.Select(branchGroup => new EvoBranchGroup(
            branchGroup.GroupId,
            branchGroup.GroupName,
            branchGroup.Branches.Select(branch => new EvoBranch(branch.BranchId, branch.BranchName)).ToArray())).ToArray();
    }

    private static void ConfigureHttpClient(HttpClient client, EvoOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ExternalOperationNotAllowedException("A URL da API Evo deve ser HTTPS e absoluta.");
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ExternalOperationNotAllowedException(
                "O usuário e o token da API Evo não estão configurados.");
        }

        // A consulta de vÃ­nculos corporativos executa mais de uma chamada no
        // mesmo gateway. HttpClient bloqueia a alteraÃ§Ã£o de BaseAddress e
        // headers depois da primeira requisiÃ§Ã£o, por isso a configuraÃ§Ã£o Ã©
        // aplicada uma Ãºnica vez por instÃ¢ncia.
        if (client.BaseAddress is not null)
        {
            return;
        }

        var credentialValue = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.ApiKey}"));
        client.BaseAddress = baseUri;
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentialValue);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvoqueBilling/0.1");
    }

    private static decimal? ReadDecimalOrNull(JsonElement? value)
    {
        if (value is not JsonElement jsonValue)
        {
            return null;
        }

        if (jsonValue.ValueKind == JsonValueKind.Number && jsonValue.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (jsonValue.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                jsonValue.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimalValue))
        {
            return decimalValue;
        }

        return null;
    }

    private static IReadOnlyCollection<JsonElement> ReadReceivableElements(JsonElement rootElement)
    {
        if (rootElement.ValueKind == JsonValueKind.Array)
        {
            return rootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
        }

        if (rootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "lista", "list", "ids" })
            {
                if (rootElement.TryGetProperty(propertyName, out var listElement)
                    && listElement.ValueKind == JsonValueKind.Array)
                {
                    return listElement.EnumerateArray().Select(element => element.Clone()).ToArray();
                }
            }
        }

        throw new ExternalOperationNotAllowedException(
            "O Evo retornou um formato inválido ao consultar recebíveis.");
    }

    private static DateOnly? ReadDateOnlyOrNull(DateTimeOffset? value)
    {
        return value is null ? null : DateOnly.FromDateTime(value.Value.Date);
    }

    private sealed record EvoEmployeeResponse(
        int IdEmployee,
        int IdBranch,
        string BranchName,
        string Name,
        string Status,
        string? CurrentEmail,
        string? JobPosition);

    private sealed record EvoMemberResponse(
        int IdMember,
        int IdBranch,
        string BranchName,
        string FirstName,
        string? LastName,
        IReadOnlyCollection<EvoMembershipResponse> Memberships);

    private sealed record EvoMembershipResponse(
        int? IdMembership,
        int? IdMemberMembership,
        string? Name,
        string? MembershipStatus,
        decimal? ValueNextMonth,
        JsonElement? NextCharge,
        DateTimeOffset? StartDate,
        DateTimeOffset? EndDate,
        DateTimeOffset? NextDateSuspension);

    private sealed record EvoMemberMembershipResponse(
        int IdMember,
        string Name,
        int IdMembership,
        int IdMemberMembership,
        int IdBranch,
        int? IdSale,
        decimal? SaleValue,
        string? NameMembership,
        int? StatusMemberMembership);

    private sealed record EvoSaleResponse(
        int IdSale,
        int IdMember,
        [property: JsonPropertyName("coporatePartnershipId")] int? MisspelledCorporatePartnershipId,
        [property: JsonPropertyName("corporatePartnershipId")] int? CorrectCorporatePartnershipId,
        string? CorporatePartnershipName,
        bool? Removed,
        DateTimeOffset? SaleDate,
        IReadOnlyCollection<EvoSaleItemResponse>? SaleItens);

    private sealed record EvoSaleItemResponse(
        int IdSaleItem,
        int? IdMembership,
        int? IdMemberMembership,
        string? Item,
        string? Description,
        decimal? ItemValue,
        decimal? SaleValue,
        decimal? CorporateDiscount,
        [property: JsonPropertyName("coporatePartnershipId")] int? MisspelledCorporatePartnershipId,
        [property: JsonPropertyName("corporatePartnershipId")] int? CorrectCorporatePartnershipId,
        string? CorporatePartnershipName);

    private sealed record EvoReceivableResponse(
        int IdReceivable,
        int? IdSale,
        int? IdMemberPayer,
        string? PayerName,
        DateTimeOffset? CompetenceDate,
        DateTimeOffset? DueDate,
        decimal Ammount,
        EvoReferenceResponse? Status,
        EvoReferenceResponse? PaymentType,
        int? CurrentInstallment,
        int? TotalInstallments);

    private sealed record EvoReferenceResponse(int? Id, string? Name);

    private sealed record EvoPartnershipResponse(
        int IdPartnership,
        string Description,
        bool IsBlockedFlag,
        bool IsInactiveFlag,
        EvoCompanyResponse? Company);

    private sealed record EvoCompanyResponse(
        int AgreementCompanyId,
        int BranchId,
        string CorporateName,
        string? TradeName,
        string? Cnpj,
        bool IsDeletedFlag);

    private sealed record EvoBranchGroupResponse(
        int GroupId,
        string GroupName,
        IReadOnlyCollection<EvoBranchResponse> Branches);

    private sealed record EvoBranchResponse(int BranchId, string BranchName);
}
