using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Asaas;

public sealed class AsaasInvoiceGateway(
    HttpClient httpClient,
    IHostEnvironment hostEnvironment,
    IOptions<AsaasOptions> asaasOptions) : IAsaasInvoiceGateway
{
    public async Task<AsaasInvoiceCreation> ScheduleInvoiceAsync(
        AsaasEnvironment asaasEnvironment,
        AsaasInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateInvoiceIssuance(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        // Deduções e os tributos federais vão zerados porque é assim que as notas
        // da conta são emitidas hoje; só ISS e a retenção variam.
        var requestBody = new
        {
            payment = request.AsaasPaymentId,
            value = request.Value,
            deductions = 0m,
            effectiveDate = request.EffectiveDate.ToString("yyyy-MM-dd"),
            serviceDescription = request.ServiceDescription,
            observations = string.Empty,
            externalReference = request.ExternalReference,
            municipalServiceId = request.MunicipalServiceId,
            municipalServiceName = request.MunicipalServiceName,
            taxes = new
            {
                retainIss = request.RetainsIss,
                iss = request.IssTaxRate,
                cofins = 0m,
                csll = 0m,
                inss = 0m,
                ir = 0m,
                pis = 0m,
            },
        };

        using var response = await httpClient.PostAsJsonAsync("invoices", requestBody, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var refusalReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                $"O Asaas recusou a emissão da nota fiscal: {refusalReason}");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasInvoiceResponse>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id) || string.IsNullOrWhiteSpace(responseData.Status))
        {
            throw new ExternalOperationNotAllowedException(
                "O Asaas não retornou o identificador e o status da nota fiscal emitida.");
        }

        return new AsaasInvoiceCreation(responseData.Id, responseData.Status);
    }

    public async Task<AsaasInvoiceState> GetInvoiceAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasInvoiceId,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateReadOperation(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        using var response = await httpClient.GetAsync(
            $"invoices/{Uri.EscapeDataString(asaasInvoiceId)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failureReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar a nota fiscal no Asaas: {failureReason}");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasInvoiceResponse>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id) || string.IsNullOrWhiteSpace(responseData.Status))
        {
            throw new ExternalOperationNotAllowedException(
                "O Asaas retornou uma resposta inválida ao consultar a nota fiscal.");
        }

        return new AsaasInvoiceState(responseData.Id, responseData.Status, responseData.StatusDescription);
    }

    private sealed record AsaasInvoiceResponse(string? Id, string? Status, string? StatusDescription);
}
