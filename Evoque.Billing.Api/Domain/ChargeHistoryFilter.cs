namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Filtros da consulta ao histórico. Todos opcionais: sem nenhum, devolve tudo
/// que o sistema emitiu, do mais recente para o mais antigo.
/// </summary>
public sealed record ChargeHistoryFilter
{
    /// <summary>
    /// Nome da empresa ou CNPJ, parcial. Compara sem acento e sem caixa: buscar
    /// "acucar" encontra "Açúcar". Só os dígitos contam na comparação por CNPJ,
    /// então "02.346.076/0001-07" e "02346076" acham a mesma empresa.
    /// </summary>
    /// <remarks>
    /// No MySQL isso sai de graça da collation do servidor,
    /// <c>utf8mb4_0900_ai_ci</c> — o <c>ai</c> ignora acento e o <c>ci</c>,
    /// caixa. A implementação em memória precisa dobrar acento e caixa por conta
    /// própria para chegar ao mesmo resultado, e é por isso que ela normaliza o
    /// texto antes de comparar em vez de usar um <c>Contains</c> qualquer.
    /// </remarks>
    public string? CompanySearch { get; init; }

    public AsaasEnvironment? AsaasEnvironment { get; init; }

    public BillingPeriodReference? BillingPeriodReference { get; init; }
}
