using System.Text;
using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

/// <summary>
/// Junta lote, item, prévia e nota numa consulta só. A nota entra pela mais
/// recente de cada prévia: uma reemissão cria a sequência seguinte, e é ela que
/// vale.
/// </summary>
public sealed class MySqlChargeHistoryRepository(MySqlConnectionFactory connectionFactory)
    : IChargeHistoryRepository
{
    private const string BaseQuery = """
        SELECT
            cb.id                AS charge_batch_id,
            bd.id                AS billing_draft_id,
            bp.reference_year    AS reference_year,
            bp.reference_month   AS reference_month,
            cb.asaas_environment AS asaas_environment,
            bd.company_name      AS company_name,
            bd.company_tax_id    AS company_tax_id,
            cb.due_date          AS due_date,
            cb.created_at        AS issued_at,
            cbi.status           AS item_status,
            cbi.asaas_payment_id AS asaas_payment_id,
            cbi.bank_slip_url    AS bank_slip_url,
            cbi.error_message    AS item_error_message,
            fi.status            AS invoice_status,
            fi.pdf_url           AS invoice_pdf_url,
            fi.error_message     AS invoice_error_message,
            (SELECT COALESCE(SUM(ROUND(bdi.quantity * bdi.unit_amount, 2)), 0)
               FROM billing_draft_items bdi
              WHERE bdi.billing_draft_id = bd.id) AS total_amount,
            (SELECT COUNT(*)
               FROM billing_draft_items bdi
              WHERE bdi.billing_draft_id = bd.id) AS member_count
        FROM charge_batch_items cbi
        JOIN charge_batches cb ON cb.id = cbi.charge_batch_id
        JOIN billing_drafts bd ON bd.id = cbi.billing_draft_id
        JOIN billing_periods bp ON bp.id = cb.billing_period_id
        LEFT JOIN fiscal_invoices fi
               ON fi.billing_draft_id = bd.id
              AND fi.sequence_number = (
                    SELECT MAX(fi2.sequence_number)
                      FROM fiscal_invoices fi2
                     WHERE fi2.billing_draft_id = bd.id)
        """;

    public async Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var commandText = new StringBuilder(BaseQuery);
        var conditions = new List<string>();

        if (filter.AsaasEnvironment is not null)
        {
            conditions.Add("cb.asaas_environment = @asaasEnvironment");
        }

        if (filter.BillingPeriodReference is not null)
        {
            conditions.Add("bp.reference_year = @referenceYear AND bp.reference_month = @referenceMonth");
        }

        // Nome e CNPJ são buscas mutuamente exclusivas, decididas por
        // ChargeHistoryFilter.SearchesByTaxId — a mesma regra que a
        // implementação em memória usa, para que as duas nunca divirjam sobre
        // o que é CNPJ e o que é nome. Um OR entre as duas colunas faria
        // "Farmava 2" virar também uma busca por "2" dentro do CNPJ, que casa
        // com quase toda empresa.
        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            conditions.Add(filter.SearchesByTaxId
                ? "bd.company_tax_id LIKE @companyTaxIdSearch"
                : "bd.company_name LIKE @companySearch");
        }

        if (conditions.Count > 0)
        {
            commandText.Append("\nWHERE ").Append(string.Join("\n  AND ", conditions));
        }

        commandText.Append("\nORDER BY cb.created_at DESC;");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText.ToString(), connection);

        if (filter.AsaasEnvironment is not null)
        {
            command.Parameters.AddWithValue("@asaasEnvironment", filter.AsaasEnvironment.ToString());
        }

        if (filter.BillingPeriodReference is not null)
        {
            command.Parameters.AddWithValue("@referenceYear", filter.BillingPeriodReference.Year);
            command.Parameters.AddWithValue("@referenceMonth", filter.BillingPeriodReference.Month);
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            if (filter.SearchesByTaxId)
            {
                var digitsOnly = new string(filter.CompanySearch.Where(char.IsAsciiDigit).ToArray());
                command.Parameters.AddWithValue("@companyTaxIdSearch", $"%{digitsOnly}%");
            }
            else
            {
                // `%`, `_` e `\` são curinga dentro de um LIKE. Sem escapar o que
                // foi digitado, buscar "100%" casaria com qualquer nome e "_" com
                // qualquer caractere único — comportamento que a implementação em
                // memória, que compara com `Contains` literal, nunca teve.
                // Escapar aqui é o que mantém as duas implementações concordando
                // sobre o mesmo termo digitado.
                var escapedSearchTerm = EscapeLikeWildcards(filter.CompanySearch.Trim());
                command.Parameters.AddWithValue("@companySearch", $"%{escapedSearchTerm}%");
            }
        }

        var entries = new List<ChargeHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    /// <summary>
    /// A barra invertida precisa ser escapada primeiro: escapando `%` e `_`
    /// antes dela, as barras que essas duas substituições introduzem seriam
    /// escapadas de novo e o termo pesquisado sairia errado.
    /// </summary>
    private static string EscapeLikeWildcards(string term)
    {
        return term
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static ChargeHistoryEntry ReadEntry(MySqlDataReader reader)
    {
        var invoiceStatus = ReadNullableString(reader, "invoice_status");
        return new ChargeHistoryEntry(
            reader.GetGuid("charge_batch_id"),
            reader.GetGuid("billing_draft_id"),
            new BillingPeriodReference(reader.GetInt32("reference_year"), reader.GetInt32("reference_month")),
            Enum.Parse<AsaasEnvironment>(reader.GetString("asaas_environment")),
            reader.GetString("company_name"),
            reader.GetString("company_tax_id"),
            reader.GetDecimal("total_amount"),
            reader.GetInt32("member_count"),
            DateOnly.FromDateTime(reader.GetDateTime("due_date")),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("issued_at"), DateTimeKind.Utc)),
            Enum.Parse<ChargeBatchItemStatus>(reader.GetString("item_status")),
            ReadNullableString(reader, "asaas_payment_id"),
            ReadNullableString(reader, "bank_slip_url"),
            ReadNullableString(reader, "item_error_message"),
            invoiceStatus is null ? null : Enum.Parse<FiscalInvoiceStatus>(invoiceStatus),
            ReadNullableString(reader, "invoice_pdf_url"),
            ReadNullableString(reader, "invoice_error_message"));
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var columnOrdinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(columnOrdinal) ? null : reader.GetString(columnOrdinal);
    }
}
