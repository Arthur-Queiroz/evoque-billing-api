using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

/// <summary>
/// Configuração fiscal única da conta. Os valores padrão reproduzem as 264 notas
/// lidas da produção em 07/09/2026: serviço municipal 82367, ISS de 5%, demais
/// tributos zerados e descrição no formato "Serviços prestados em MM/AAAA".
/// A retenção de ISS não fica aqui: ela varia por tomador e mora no catálogo de
/// empresas.
/// </summary>
public sealed class FiscalInvoiceOptions
{
    public const string SectionName = "FiscalInvoice";

    private const string BillingPeriodPlaceholder = "{competencia}";

    public string MunicipalServiceId { get; init; } = string.Empty;

    public decimal IssTaxRate { get; init; }

    /// <summary>
    /// Termina com ponto porque é assim que as 264 notas reais da produção
    /// trazem <c>serviceDescription</c>: "Serviços prestados em 08/2026.".
    /// Não uniformizar com <see cref="MunicipalServiceNameTemplate"/>: a
    /// pontuação diferente entre os dois campos é o que a prefeitura já
    /// recebeu, não um descuido de digitação.
    /// </summary>
    public string ServiceDescriptionTemplate { get; init; } =
        $"Serviços prestados em {BillingPeriodPlaceholder}.";

    /// <summary>
    /// Sem ponto final, ao contrário de <see cref="ServiceDescriptionTemplate"/>:
    /// é assim que <c>municipalServiceName</c> aparece nas notas reais da
    /// produção, "Serviços prestados em 08/2026". A diferença é intencional.
    /// </summary>
    public string MunicipalServiceNameTemplate { get; init; } =
        $"Serviços prestados em {BillingPeriodPlaceholder}";

    /// <summary>
    /// Além dos campos obrigatórios, garante que os dois templates ainda
    /// contenham o placeholder da competência. Um template reescrito sem ele
    /// formataria em silêncio: o <see cref="Format"/> não lançaria erro, e o
    /// texto quebrado iria direto para uma nota fiscal real.
    /// </summary>
    public bool IsComplete()
    {
        return !string.IsNullOrWhiteSpace(MunicipalServiceId)
            && IssTaxRate is > 0 and <= 100
            && ServiceDescriptionTemplate.Contains(BillingPeriodPlaceholder, StringComparison.Ordinal)
            && MunicipalServiceNameTemplate.Contains(BillingPeriodPlaceholder, StringComparison.Ordinal);
    }

    public string BuildServiceDescription(BillingPeriodReference billingPeriodReference)
    {
        return Format(ServiceDescriptionTemplate, billingPeriodReference);
    }

    public string BuildMunicipalServiceName(BillingPeriodReference billingPeriodReference)
    {
        return Format(MunicipalServiceNameTemplate, billingPeriodReference);
    }

    private static string Format(string template, BillingPeriodReference billingPeriodReference)
    {
        return template.Replace(
            BillingPeriodPlaceholder,
            $"{billingPeriodReference.Month:00}/{billingPeriodReference.Year:0000}",
            StringComparison.Ordinal);
    }
}
