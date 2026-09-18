using Evoque.Billing.Api.Authentication;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Integrations.CompanyRegistry;
using Evoque.Billing.Api.Integrations.Evo;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// A Cloudflare termina o TLS na borda e entrega HTTP ao Nginx, então sem isto a
// aplicação enxerga o esquema e o IP do salto interno, não os do cliente.
//
// Isto NÃO afeta o cookie de sessão: `CookieSecurePolicy.Always` marca `Secure`
// incondicionalmente, sem consultar `Request.IsHttps` — comprovado emitindo o
// cookie por HTTP puro, com e sem `X-Forwarded-Proto`, e visível em
// `CookieBuilder.Build`, onde `IsHttps` só é lido no caminho `SameAsRequest`.
//
// O que isto corrige é o IP do cliente em `X-Forwarded-For`, que sem o
// middleware chega como o IP do Nginx em toda requisição — inútil para
// auditoria ou bloqueio por origem —, e o esquema percebido, para o dia em que
// algo além do cookie depender dele.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<OperatorAccountOptions>(
    builder.Configuration.GetSection(OperatorAccountOptions.SectionName));
builder.Services.AddScoped<OperatorAuthenticationService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "evoque.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Sem isto o ASP.NET Core responde 302 para uma tela de login que não
        // existe nesta API. O portal precisa de 401 para saber que deve pedir
        // credencial de novo.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// Exigir autenticação por padrão, e não por [Authorize] em cada controller.
// Assim um controller novo nasce protegido e liberar acesso vira ato explícito;
// o contrário falha no dia em que alguém esquecer um.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var allowedCorsOrigins = configuredCorsOrigins
    ?? (builder.Environment.IsDevelopment()
        ? ["http://localhost:3000", "http://127.0.0.1:3000"]
        : []);
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            // AllowCredentials é compatível com WithOrigins porque a lista é
            // sempre explícita, nunca curinga — o portal normalmente passa
            // pelo proxy same-origin do Next.js, mas em desenvolvimento
            // NEXT_PUBLIC_API_BASE_URL pode apontar direto para a API, e sem
            // isto o cookie de sessão não acompanha essa chamada cross-origin.
            policy.WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});
builder.Services.AddHttpClient<AsaasChargeGateway>();
builder.Services.AddHttpClient<AsaasCustomerNotificationGateway>();
builder.Services.AddHttpClient<AsaasCustomerGateway>();
builder.Services.AddHttpClient<AsaasInvoiceGateway>();
builder.Services.AddHttpClient<EvoDirectoryGateway>();
builder.Services.AddHttpClient<BrasilApiCompanyRegistryGateway>();
builder.Services.AddMemoryCache();
builder.Services.Configure<AsaasOptions>(builder.Configuration.GetSection(AsaasOptions.SectionName));
builder.Services.Configure<FiscalInvoiceOptions>(
    builder.Configuration.GetSection(FiscalInvoiceOptions.SectionName));
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
    builder.Services.AddScoped<IFiscalInvoiceRepository, InMemoryFiscalInvoiceRepository>();
    builder.Services.AddScoped<IChargeHistoryRepository, InMemoryChargeHistoryRepository>();
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
    builder.Services.AddScoped<IFiscalInvoiceRepository, MySqlFiscalInvoiceRepository>();
    builder.Services.AddScoped<IChargeHistoryRepository, MySqlChargeHistoryRepository>();
    builder.Services.AddScoped<ICompanyBillingScheduleRepository, MySqlCompanyBillingScheduleRepository>();
    builder.Services.AddScoped<ICompanyRepository, MySqlCompanyRepository>();
    builder.Services.AddScoped<ICompanyCatalogImportRepository, MySqlCompanyCatalogImportRepository>();
    builder.Services.AddScoped<ICorporateMemberRepository, MySqlCorporateMemberRepository>();
}

builder.Services.AddScoped<IAsaasChargeGateway, AsaasChargeGateway>();
builder.Services.AddScoped<IAsaasInvoiceGateway, AsaasInvoiceGateway>();
builder.Services.AddScoped<IAsaasCustomerNotificationGateway, AsaasCustomerNotificationGateway>();
builder.Services.AddScoped<IAsaasCustomerGateway, AsaasCustomerGateway>();
builder.Services.AddScoped<IEvoDirectoryGateway, EvoDirectoryGateway>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BillingPeriodService>();
builder.Services.AddScoped<BillingDraftService>();
builder.Services.AddScoped<ChargeCreationService>();
builder.Services.AddScoped<FiscalInvoiceService>();
builder.Services.AddScoped<ChargeHistoryService>();
builder.Services.AddScoped<ChargePaymentSynchronizationService>();
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

app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
