using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class DatabaseSchemaInitializer(MySqlConnectionFactory connectionFactory)
{
    private const string InitialSchemaMigrationId = "001_initial_billing_schema";
    private const string BankSlipUrlMigrationId = "002_add_bank_slip_url";
    private const string ChargeBatchMigrationId = "003_add_charge_batches";
    private const string ChargeBatchApprovalMigrationId = "004_add_charge_batch_approval";
    private const string CompanyBillingScheduleMigrationId = "005_add_company_billing_schedules";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id VARCHAR(128) NOT NULL PRIMARY KEY,
                applied_at DATETIME(6) NOT NULL
            );
            """, null, cancellationToken);

        if (!await IsAppliedAsync(connection, InitialSchemaMigrationId, cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS billing_periods (
                id CHAR(36) NOT NULL PRIMARY KEY,
                reference_year SMALLINT NOT NULL,
                reference_month TINYINT NOT NULL,
                status VARCHAR(32) NOT NULL,
                created_at DATETIME(6) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                CONSTRAINT uq_billing_periods_reference UNIQUE (reference_year, reference_month)
            );
            """, transaction, cancellationToken);

            await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS billing_drafts (
                id CHAR(36) NOT NULL PRIMARY KEY,
                billing_period_id CHAR(36) NOT NULL,
                external_company_id VARCHAR(128) NOT NULL,
                company_name VARCHAR(255) NOT NULL,
                company_tax_id VARCHAR(32) NOT NULL,
                asaas_customer_id VARCHAR(128) NULL,
                status VARCHAR(32) NOT NULL,
                version INT NOT NULL,
                approved_by VARCHAR(255) NULL,
                approved_at DATETIME(6) NULL,
                asaas_payment_id VARCHAR(128) NULL,
                created_at DATETIME(6) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                CONSTRAINT fk_billing_drafts_period
                    FOREIGN KEY (billing_period_id) REFERENCES billing_periods (id),
                CONSTRAINT uq_billing_drafts_company_period
                    UNIQUE (billing_period_id, external_company_id, version)
            );
            """, transaction, cancellationToken);

            await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS billing_draft_items (
                id CHAR(36) NOT NULL PRIMARY KEY,
                billing_draft_id CHAR(36) NOT NULL,
                item_order INT NOT NULL,
                description VARCHAR(500) NOT NULL,
                quantity DECIMAL(18, 4) NOT NULL,
                unit_amount DECIMAL(18, 2) NOT NULL,
                external_member_id VARCHAR(128) NULL,
                CONSTRAINT fk_billing_draft_items_draft
                    FOREIGN KEY (billing_draft_id) REFERENCES billing_drafts (id) ON DELETE CASCADE,
                CONSTRAINT uq_billing_draft_items_order UNIQUE (billing_draft_id, item_order)
            );
            """, transaction, cancellationToken);

            await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS audit_logs (
                id CHAR(36) NOT NULL PRIMARY KEY,
                action VARCHAR(128) NOT NULL,
                operator_id VARCHAR(255) NOT NULL,
                occurred_at DATETIME(6) NOT NULL,
                billing_period_id CHAR(36) NULL,
                billing_draft_id CHAR(36) NULL,
                details TEXT NOT NULL,
                INDEX ix_audit_logs_billing_draft (billing_draft_id, occurred_at)
            );
            """, transaction, cancellationToken);

            await using (var command = new MySqlCommand(
                "INSERT INTO schema_migrations (id, applied_at) VALUES (@id, @appliedAt);",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@id", InitialSchemaMigrationId);
                command.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, BankSlipUrlMigrationId, cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(connection, """
                ALTER TABLE billing_drafts
                ADD COLUMN bank_slip_url TEXT NULL AFTER asaas_payment_id;
                """, transaction, cancellationToken);

            await InsertMigrationAsync(connection, transaction, BankSlipUrlMigrationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, ChargeBatchMigrationId, cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, """
                CREATE TABLE charge_batches (
                    id CHAR(36) NOT NULL PRIMARY KEY,
                    billing_period_id CHAR(36) NOT NULL,
                    due_date DATE NOT NULL,
                    operator_id VARCHAR(255) NOT NULL,
                    retry_of_charge_batch_id CHAR(36) NULL,
                    status VARCHAR(32) NOT NULL,
                    created_at DATETIME(6) NOT NULL,
                    updated_at DATETIME(6) NOT NULL,
                    CONSTRAINT fk_charge_batches_period
                        FOREIGN KEY (billing_period_id) REFERENCES billing_periods (id),
                    CONSTRAINT fk_charge_batches_retry
                        FOREIGN KEY (retry_of_charge_batch_id) REFERENCES charge_batches (id),
                    INDEX ix_charge_batches_period_created (billing_period_id, created_at)
                );
                """, transaction, cancellationToken);

            await ExecuteAsync(connection, """
                CREATE TABLE charge_batch_items (
                    charge_batch_id CHAR(36) NOT NULL,
                    billing_draft_id CHAR(36) NOT NULL,
                    status VARCHAR(32) NOT NULL,
                    asaas_payment_id VARCHAR(128) NULL,
                    bank_slip_url TEXT NULL,
                    error_message TEXT NULL,
                    updated_at DATETIME(6) NOT NULL,
                    PRIMARY KEY (charge_batch_id, billing_draft_id),
                    CONSTRAINT fk_charge_batch_items_batch
                        FOREIGN KEY (charge_batch_id) REFERENCES charge_batches (id) ON DELETE CASCADE,
                    CONSTRAINT fk_charge_batch_items_draft
                        FOREIGN KEY (billing_draft_id) REFERENCES billing_drafts (id),
                    INDEX ix_charge_batch_items_draft (billing_draft_id)
                );
                """, transaction, cancellationToken);

            await InsertMigrationAsync(connection, transaction, ChargeBatchMigrationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (await IsAppliedAsync(connection, ChargeBatchApprovalMigrationId, cancellationToken))
        {
            await CreateCompanyBillingScheduleTableAsync(connection, cancellationToken);
            return;
        }

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await ExecuteAsync(connection, """
                ALTER TABLE charge_batches
                ADD COLUMN asaas_environment VARCHAR(16) NOT NULL DEFAULT 'Sandbox' AFTER retry_of_charge_batch_id,
                ADD COLUMN approved_by VARCHAR(255) NULL AFTER status,
                ADD COLUMN approved_at DATETIME(6) NULL AFTER approved_by;
                """, transaction, cancellationToken);

            await InsertMigrationAsync(connection, transaction, ChargeBatchApprovalMigrationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await CreateCompanyBillingScheduleTableAsync(connection, cancellationToken);
    }

    private static async Task CreateCompanyBillingScheduleTableAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, CompanyBillingScheduleMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE company_billing_schedules (
                external_company_id VARCHAR(128) NOT NULL PRIMARY KEY,
                billing_day TINYINT NOT NULL,
                is_active BOOLEAN NOT NULL,
                updated_by VARCHAR(255) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                INDEX ix_company_billing_schedules_day_active (billing_day, is_active)
            );
            """, transaction, cancellationToken);
        await InsertMigrationAsync(connection, transaction, CompanyBillingScheduleMigrationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsAppliedAsync(
        MySqlConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM schema_migrations WHERE id = @id;",
            connection);
        command.Parameters.AddWithValue("@id", migrationId);
        var migrationCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return migrationCount > 0;
    }

    private static async Task InsertMigrationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "INSERT INTO schema_migrations (id, applied_at) VALUES (@id, @appliedAt);",
            connection,
            transaction);
        command.Parameters.AddWithValue("@id", migrationId);
        command.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string commandText,
        MySqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(commandText, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
