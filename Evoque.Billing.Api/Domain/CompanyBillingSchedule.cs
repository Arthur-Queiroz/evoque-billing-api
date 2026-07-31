namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Agenda de faturamento da empresa.
///
/// <see cref="ClosingDay"/> é o dia em que o período de serviço fecha, não o dia
/// do vencimento. O histórico real do Asaas mostra períodos como
/// "do dia 26/05 ao dia 25/06" com boleto vencendo em 06/07: o fechamento é o
/// dia 25, o vencimento é negociado à parte e cai em outro dia, geralmente no
/// mês seguinte. Tratar os dois como o mesmo número fazia o lote agendado nunca
/// encontrar empresa nenhuma.
/// </summary>
public sealed record CompanyBillingSchedule(
    string ExternalCompanyId,
    int ClosingDay,
    bool IsActive,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    private static readonly int[] SupportedClosingDays = [2, 18, 20, 25];

    public static CompanyBillingSchedule Create(
        string externalCompanyId,
        int closingDay,
        bool isActive,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(externalCompanyId))
        {
            throw new ValidationException("O identificador externo da empresa é obrigatório.");
        }

        ValidateClosingDay(closingDay);

        if (string.IsNullOrWhiteSpace(updatedBy))
        {
            throw new ValidationException("O responsável pela atualização da agenda é obrigatório.");
        }

        return new CompanyBillingSchedule(externalCompanyId.Trim(), closingDay, isActive, updatedBy.Trim(), updatedAt);
    }

    public static void ValidateClosingDay(int closingDay)
    {
        if (!SupportedClosingDays.Contains(closingDay))
        {
            throw new ValidationException("O dia de fechamento deve ser 02, 18, 20 ou 25.");
        }
    }
}
