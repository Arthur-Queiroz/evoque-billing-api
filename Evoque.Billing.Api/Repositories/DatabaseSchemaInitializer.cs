using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class DatabaseSchemaInitializer(MySqlConnectionFactory connectionFactory)
{
    private const string InitialSchemaMigrationId = "001_initial_billing_schema";
    private const string BankSlipUrlMigrationId = "002_add_bank_slip_url";
    private const string ChargeBatchMigrationId = "003_add_charge_batches";
    private const string ChargeBatchApprovalMigrationId = "004_add_charge_batch_approval";
    private const string CompanyBillingScheduleMigrationId = "005_add_company_billing_schedules";
    private const string CompanyCatalogMigrationId = "006_add_company_catalog";
    private const string CorporateMemberMigrationId = "007_add_corporate_member_crm";
    private const string ClosingDayMigrationId = "008_rename_billing_day_to_closing_day";

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
            await ApplyLatestMigrationsAsync(connection, cancellationToken);
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

        await ApplyLatestMigrationsAsync(connection, cancellationToken);
    }

    private static async Task ApplyLatestMigrationsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await CreateCompanyBillingScheduleTableAsync(connection, cancellationToken);
        await CreateCompanyCatalogTablesAsync(connection, cancellationToken);
        await CreateCorporateMemberTablesAsync(connection, cancellationToken);
        await RenameBillingDayToClosingDayAsync(connection, cancellationToken);
    }

    /// <summary>
    /// O dia guardado nesta coluna sempre foi o fechamento do período de serviço
    /// (02, 18, 20 ou 25), nunca o vencimento do boleto — que no Asaas cai em
    /// outros dias e normalmente no mês seguinte. O nome antigo levou o lote
    /// agendado a filtrar empresas pelo dia do vencimento e a não achar nenhuma.
    /// O valor não muda; só o nome passa a dizer o que o dado é.
    /// </summary>
    private static async Task RenameBillingDayToClosingDayAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, ClosingDayMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, """
            ALTER TABLE company_billing_schedules
                RENAME COLUMN billing_day TO closing_day;
            """, transaction, cancellationToken);
        await InsertMigrationAsync(connection, transaction, ClosingDayMigrationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task CreateCorporateMemberTablesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, CorporateMemberMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE corporate_members (
                evo_member_id BIGINT NOT NULL PRIMARY KEY,
                member_name VARCHAR(255) NOT NULL,
                company_tax_id CHAR(14) NOT NULL,
                is_active BOOLEAN NOT NULL,
                first_seen_at DATETIME(6) NOT NULL,
                last_seen_at DATETIME(6) NOT NULL,
                deactivated_at DATETIME(6) NULL,
                last_import_id CHAR(36) NOT NULL,
                updated_by VARCHAR(255) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                CONSTRAINT fk_corporate_members_company
                    FOREIGN KEY (company_tax_id) REFERENCES companies (tax_id),
                INDEX ix_corporate_members_company_active
                    (company_tax_id, is_active, member_name),
                INDEX ix_corporate_members_last_import (last_import_id)
            );
            """, transaction, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE corporate_member_contracts (
                evo_member_id BIGINT NOT NULL,
                contract_key VARCHAR(128) NOT NULL,
                evo_contract_id VARCHAR(64) NULL,
                contract_name VARCHAR(255) NULL,
                PRIMARY KEY (evo_member_id, contract_key),
                CONSTRAINT fk_corporate_member_contracts_member
                    FOREIGN KEY (evo_member_id) REFERENCES corporate_members (evo_member_id)
                    ON DELETE CASCADE
            );
            """, transaction, cancellationToken);

        await InsertMigrationAsync(connection, transaction, CorporateMemberMigrationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task CreateCompanyCatalogTablesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, CompanyCatalogMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE companies (
                tax_id CHAR(14) NOT NULL PRIMARY KEY,
                display_name VARCHAR(255) NOT NULL,
                evo_name VARCHAR(255) NULL,
                legal_name VARCHAR(255) NULL,
                trade_name VARCHAR(255) NULL,
                registration_status VARCHAR(64) NULL,
                registry_street VARCHAR(255) NULL,
                registry_number VARCHAR(32) NULL,
                registry_complement VARCHAR(255) NULL,
                registry_neighborhood VARCHAR(255) NULL,
                registry_city VARCHAR(255) NULL,
                registry_state VARCHAR(8) NULL,
                registry_postal_code VARCHAR(16) NULL,
                registry_lookup_status VARCHAR(32) NOT NULL,
                registry_last_checked_at DATETIME(6) NULL,
                is_active BOOLEAN NOT NULL,
                source VARCHAR(32) NOT NULL,
                last_imported_member_count INT NOT NULL,
                first_seen_at DATETIME(6) NULL,
                last_seen_at DATETIME(6) NULL,
                last_import_id CHAR(36) NULL,
                requires_review_after_reappearing BOOLEAN NOT NULL,
                asaas_sandbox_customer_id VARCHAR(128) NULL,
                asaas_production_customer_id VARCHAR(128) NULL,
                created_by VARCHAR(255) NOT NULL,
                created_at DATETIME(6) NOT NULL,
                updated_by VARCHAR(255) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                INDEX ix_companies_active_display_name (is_active, display_name),
                INDEX ix_companies_last_import (last_import_id)
            );
            """, transaction, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE company_catalog_imports (
                id CHAR(36) NOT NULL PRIMARY KEY,
                file_name VARCHAR(255) NOT NULL,
                file_hash CHAR(64) NOT NULL,
                operator_id VARCHAR(255) NOT NULL,
                synchronized_at DATETIME(6) NOT NULL,
                analyzed_row_count INT NOT NULL,
                discovered_company_count INT NOT NULL,
                created_company_count INT NOT NULL,
                updated_company_count INT NOT NULL,
                unseen_company_count INT NOT NULL,
                warning_count INT NOT NULL,
                INDEX ix_company_catalog_imports_synchronized_at (synchronized_at)
            );
            """, transaction, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE company_catalog_import_members (
                import_id CHAR(36) NOT NULL,
                company_tax_id CHAR(14) NOT NULL,
                source_row_number INT NOT NULL,
                member_name VARCHAR(255) NOT NULL,
                contract_name VARCHAR(255) NULL,
                PRIMARY KEY (import_id, company_tax_id, source_row_number),
                CONSTRAINT fk_company_catalog_import_members_import
                    FOREIGN KEY (import_id) REFERENCES company_catalog_imports (id) ON DELETE CASCADE,
                INDEX ix_company_catalog_import_members_company (company_tax_id)
            );
            """, transaction, cancellationToken);

        await InsertMigrationAsync(connection, transaction, CompanyCatalogMigrationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
