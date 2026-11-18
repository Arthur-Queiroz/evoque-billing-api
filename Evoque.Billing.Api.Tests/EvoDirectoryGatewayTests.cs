using System.Net;
using System.Text;
using Evoque.Billing.Api.Integrations.Evo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Tests;

public sealed class EvoDirectoryGatewayTests
{
    [Fact]
    public async Task ListReceivablesAsync_ReadsTheDirectArrayReturnedByEvo()
    {
        const string responseJson = """
            [
              {
                "idReceivable": 987654,
                "idSale": 123456,
                "idMemberPayer": 78910,
                "payerName": "Pessoa de Teste",
                "competenceDate": "2026-07-01T00:00:00-03:00",
                "dueDate": "2026-07-20T00:00:00-03:00",
                "ammount": 79.90,
                "status": { "id": 1, "name": "Open" },
                "paymentType": { "id": 5, "name": "Bank slip" },
                "currentInstallment": 1,
                "totalInstallments": 1
              }
            ]
            """;
        var gateway = CreateGateway(responseJson);

        var receivables = await gateway.ListReceivablesAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            50,
            0,
            CancellationToken.None);

        var receivable = Assert.Single(receivables);
        Assert.Equal(987654, receivable.Id);
        Assert.Equal(123456, receivable.SaleId);
        Assert.Equal(79.90m, receivable.Amount);
        Assert.Equal(new DateOnly(2026, 7, 1), receivable.CompetenceDate);
    }

    [Fact]
    public async Task ListMembersByIdsAsync_RequestsTheFullContractHistoryOfTheGivenMembers()
    {
        const string responseJson = """
            [
              {
                "idMember": 45010,
                "idBranch": 1,
                "branchName": "Filial",
                "firstName": "Pessoa",
                "lastName": "de Teste",
                "memberships": [
                  {
                    "idMembership": 1361,
                    "idMemberMembership": 2249671,
                    "name": "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO",
                    "membershipStatus": "expired",
                    "startDate": "2025-11-14T00:00:00",
                    "endDate": "2025-12-13T00:00:00"
                  }
                ]
              }
            ]
            """;
        var httpMessageHandler = new StaticResponseHttpMessageHandler(responseJson);
        var gateway = CreateGateway(httpMessageHandler);

        var members = await gateway.ListMembersByIdsAsync([45010, 30002], CancellationToken.None);

        Assert.Equal(
            "https://evo.example/api/v2/members?idsMembers=45010,30002&showMemberships=true&take=50",
            httpMessageHandler.LastRequestUri?.ToString());
        var membership = Assert.Single(Assert.Single(members).Memberships);
        Assert.Equal(new DateTime(2025, 11, 14), membership.StartDate!.Value.DateTime);
    }

    private static EvoDirectoryGateway CreateGateway(string responseJson)
    {
        return CreateGateway(new StaticResponseHttpMessageHandler(responseJson));
    }

    private static EvoDirectoryGateway CreateGateway(StaticResponseHttpMessageHandler httpMessageHandler)
    {
        var httpClient = new HttpClient(httpMessageHandler);
        var options = Options.Create(new EvoOptions
        {
            BaseUrl = "https://evo.example/",
            Username = "test-user",
            ApiKey = "test-key",
        });
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new EvoDirectoryGateway(httpClient, options, memoryCache);
    }

    private sealed class StaticResponseHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }
}
