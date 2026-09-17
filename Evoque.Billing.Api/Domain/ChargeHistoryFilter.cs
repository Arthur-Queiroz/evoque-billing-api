namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Filtros da consulta ao histórico. Todos opcionais: sem nenhum, devolve tudo
/// que o sistema emitiu, do mais recente para o mais antigo.
/// </summary>
public sealed record ChargeHistoryFilter
{
    /// <summary>Nome da empresa ou CNPJ, parcial. Compara sem acento e sem caixa.</summary>
    public string? CompanySearch { get; init; }

    public AsaasEnvironment? AsaasEnvironment { get; init; }

    public BillingPeriodReference? BillingPeriodReference { get; init; }
}
