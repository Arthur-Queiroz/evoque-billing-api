using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Integrations.CompanyRegistry;
using Evoque.Billing.Api.Integrations.Evo;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://127.0.0.1:3000"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddHttpClient<AsaasChargeGateway>();
builder.Services.AddHttpClient<AsaasCustomerNotificationGateway>();
builder.Services.AddHttpClient<AsaasCustomerGateway>();
builder.Services.AddHttpClient<EvoDirectoryGateway>();
builder.Services.AddHttpClient<BrasilApiCompanyRegistryGateway>();
builder.Services.AddMemoryCache();
builder.Services.Configure<AsaasOptions>(builder.Configuration.GetSection(AsaasOptions.SectionName));
builder.Services.Configure<EvoOptions>(builder.Configuration.GetSection(EvoOptions.SectionName));
builder.Services.Configure<CompanyRegistryOptions>(
    builder.Configuration.GetSection(CompanyRegistryOptions.SectionName));
builder.Services.AddSingleton<StartupConfigurationValidator>();
builder.Services.AddHealthChecks().AddCheck<BillingDatabaseHealthCheck>("billing_database");

var billingDatabaseConnectionString = builder.Configuration.GetConnectionString("BillingDatabase");
if (string.IsNullOrWhiteSpace(billingDatabaseConnectionString))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "A connection string BillingDatabase é obrigatória em produção.");
    }

    builder.Services.AddSingleton<InMemoryBillingDataStore>();
    builder.Services.AddScoped<IBillingPeriodRepository, InMemoryBillingPeriodRepository>();
    builder.Services.AddScoped<IBillingDraftRepository, InMemoryBillingDraftRepository>();
    builder.Services.AddScoped<IAuditLogRepository, InMemoryAuditLogRepository>();
    builder.Services.AddScoped<IChargeBatchRepository, InMemoryChargeBatchRepository>();
    builder.Services.AddScoped<ICompanyBillingScheduleRepository, InMemoryCompanyBillingScheduleRepository>();
    builder.Services.AddScoped<ICompanyRepository, InMemoryCompanyRepository>();
    builder.Services.AddScoped<ICompanyCatalogImportRepository, InMemoryCompanyCatalogImportRepository>();
    builder.Services.AddScoped<ICorporateMemberRepository, InMemoryCorporateMemberRepository>();
}
else
{
    builder.Services.AddSingleton(new MySqlConnectionFactory(billingDatabaseConnectionString));
    builder.Services.AddScoped<DatabaseSchemaInitializer>();
    builder.Services.AddScoped<IBillingPeriodRepository, MySqlBillingPeriodRepository>();
    builder.Services.AddScoped<IBillingDraftRepository, MySqlBillingDraftRepository>();
    builder.Services.AddScoped<IAuditLogRepository, MySqlAuditLogRepository>();
    builder.Services.AddScoped<IChargeBatchRepository, MySqlChargeBatchRepository>();
    builder.Services.AddScoped<ICompanyBillingScheduleRepository, MySqlCompanyBillingScheduleRepository>();
    builder.Services.AddScoped<ICompanyRepository, MySqlCompanyRepository>();
    builder.Services.AddScoped<ICompanyCatalogImportRepository, MySqlCompanyCatalogImportRepository>();
    builder.Services.AddScoped<ICorporateMemberRepository, MySqlCorporateMemberRepository>();
}

builder.Services.AddScoped<IAsaasChargeGateway, AsaasChargeGateway>();
builder.Services.AddScoped<IAsaasCustomerNotificationGateway, AsaasCustomerNotificationGateway>();
builder.Services.AddScoped<IAsaasCustomerGateway, AsaasCustomerGateway>();
builder.Services.AddScoped<IEvoDirectoryGateway, EvoDirectoryGateway>();
builder.Services.AddScoped<BillingPeriodService>();
builder.Services.AddScoped<BillingDraftService>();
builder.Services.AddScoped<ChargeCreationService>();
builder.Services.AddScoped<ChargeBatchService>();
builder.Services.AddScoped<CompanyBillingScheduleService>();
builder.Services.AddScoped<ScheduledChargeBatchService>();
builder.Services.AddScoped<AsaasCustomerService>();
builder.Services.AddScoped<EvoDirectoryService>();
builder.Services.AddScoped<EvoCorporatePartnershipResolver>();
builder.Services.AddScoped<CorporateBillingPreviewService>();
builder.Services.AddScoped<ICompanyRegistryGateway, BrasilApiCompanyRegistryGateway>();
builder.Services.AddScoped<CompanyRegistryEnrichmentService>();
builder.Services.AddScoped<CompanyCatalogService>();
builder.Services.AddScoped<CompanyAsaasSynchronizationService>();
builder.Services.AddScoped<CompanyCatalogSpreadsheetReader>();
builder.Services.AddScoped<CompanyCatalogImportService>();
builder.Services.AddScoped<CorporateMemberService>();
builder.Services.AddScoped<SpreadsheetWorkbookReader>();
builder.Services.AddScoped<BillingSpreadsheetReader>();
builder.Services.AddScoped<BillingSpreadsheetImportService>();
builder.Services.AddScoped<IntegrationStatusService>();
builder.Services.AddScoped<MonthlyComparisonService>();

var app = builder.Build();

app.Services.GetRequiredService<StartupConfigurationValidator>().Validate();

if (!string.IsNullOrWhiteSpace(billingDatabaseConnectionString))
{
    await using var serviceScope = app.Services.CreateAsyncScope();
    var databaseSchemaInitializer = serviceScope.ServiceProvider.GetRequiredService<DatabaseSchemaInitializer>();
    await databaseSchemaInitializer.InitializeAsync(CancellationToken.None);
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("WebClient");
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
