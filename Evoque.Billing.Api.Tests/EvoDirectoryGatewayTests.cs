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

    private static EvoDirectoryGateway CreateGateway(string responseJson)
    {
        var httpMessageHandler = new StaticResponseHttpMessageHandler(responseJson);
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }
}
