using System.Net;
using Evoque.Billing.Api.Integrations.Asaas;

namespace Evoque.Billing.Api.Tests;

/// <summary>
/// O motivo da recusa vem no corpo da resposta do Asaas. Descartá-lo fazia um
/// lote falhar mostrando apenas "HTTP 400" ao operador.
/// </summary>
public sealed class AsaasErrorMessageTests
{
    [Fact]
    public void Describe_ReportsTheReasonAsaasReturned()
    {
        const string responseBody = """
            {"errors":[{"code":"invalid_dueDate","description":"A data de vencimento deve ser maior ou igual a data de hoje."}]}
            """;

        var description = AsaasErrorMessage.Describe(HttpStatusCode.BadRequest, responseBody);

        Assert.Equal(
            "A data de vencimento deve ser maior ou igual a data de hoje. (HTTP 400)",
            description);
    }

    [Fact]
    public void Describe_JoinsEveryReportedError()
    {
        const string responseBody = """
            {"errors":[
                {"code":"invalid_customer","description":"Cliente não encontrado."},
                {"code":"invalid_value","description":"O valor deve ser maior que zero."}
            ]}
            """;

        var description = AsaasErrorMessage.Describe(HttpStatusCode.BadRequest, responseBody);

        Assert.Equal(
            "Cliente não encontrado. O valor deve ser maior que zero. (HTTP 400)",
            description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{\"errors\":[]}")]
    [InlineData("{\"errors\":[{\"code\":\"x\",\"description\":\"\"}]}")]
    [InlineData("{\"mensagem\":\"formato inesperado\"}")]
    public void Describe_FallsBackToTheStatusCodeWhenTheBodyExplainsNothing(string? responseBody)
    {
        var description = AsaasErrorMessage.Describe(HttpStatusCode.BadGateway, responseBody);

        Assert.Equal("HTTP 502", description);
    }

    [Fact]
    public void Describe_KeepsALongReasonReadable()
    {
        var longDescription = new string('a', 900);
        var responseBody = $$"""
            {"errors":[{"code":"invalid_value","description":"{{longDescription}}"}]}
            """;

        var description = AsaasErrorMessage.Describe(HttpStatusCode.BadRequest, responseBody);

        Assert.StartsWith(new string('a', 400), description);
        Assert.EndsWith("… (HTTP 400)", description);
        Assert.True(description.Length < 450);
    }

    [Fact]
    public void Describe_CollapsesLineBreaksIntoASingleLine()
    {
        const string responseBody = """
            {"errors":[{"code":"invalid_value","description":"Primeira linha.\nSegunda linha."}]}
            """;

        var description = AsaasErrorMessage.Describe(HttpStatusCode.BadRequest, responseBody);

        Assert.Equal("Primeira linha. Segunda linha. (HTTP 400)", description);
        Assert.DoesNotContain('\n', description);
    }
}
