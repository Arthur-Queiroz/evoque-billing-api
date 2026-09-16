using System.Net;
using System.Text;
using Evoque.Billing.Api.Integrations.CompanyRegistry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Tests;

public sealed class BrasilApiCompanyRegistryGatewayTests
{
    private const string ValidTaxId = "56087276000103";

    [Fact]
    public async Task FindByTaxIdAsync_SendsARequestWithANonEmptyUserAgent()
    {
        // A BrasilAPI recusa requisições sem User-Agent com HTTP 429. Sem esta
        // asserção, o cabeçalho poderia ser removido de novo e o catálogo
        // inteiro voltaria a ficar sem endereço em silêncio.
        var recordingHandler = new RecordingHttpMessageHandler("""{}""");
        var httpClient = new HttpClient(recordingHandler);
        var options = Options.Create(new CompanyRegistryOptions
        {
            BaseUrl = "https://brasilapi.example/api/",
        });
        var gateway = new BrasilApiCompanyRegistryGateway(
            httpClient,
            options,
            NullLogger<BrasilApiCompanyRegistryGateway>.Instance);

        await gateway.FindByTaxIdAsync(ValidTaxId, CancellationToken.None);

        var sentRequest = Assert.Single(recordingHandler.SentRequests);
        Assert.True(sentRequest.Headers.UserAgent.Count > 0);
        Assert.False(string.IsNullOrWhiteSpace(sentRequest.Headers.UserAgent.ToString()));
    }

    private sealed class RecordingHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        public List<HttpRequestMessage> SentRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }
}
