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

    /// <summary>
    /// Diz se <see cref="CompanySearch"/> deve ser tratado como busca por CNPJ
    /// em vez de nome. Nome e CNPJ são buscas mutuamente exclusivas pela forma
    /// do termo: um termo só é CNPJ se for feito inteiramente de dígitos e da
    /// pontuação usual do CNPJ, e qualquer letra no meio já indica busca por
    /// nome.
    /// </summary>
    /// <remarks>
    /// Vive aqui, e não em cada repositório, porque "isto é uma busca por
    /// CNPJ?" é uma pergunta sobre o filtro, não sobre como um repositório
    /// específico consulta os dados. As duas implementações — memória e MySQL —
    /// leem esta propriedade em vez de repetir a regra, e é assim que ficam
    /// impedidas de divergir sobre o que é CNPJ e o que é nome. Sem essa
    /// separação, um dígito solto dentro de um nome (ex.: "Farmava 2") viraria
    /// candidato a CNPJ e casaria com qualquer empresa cujo CNPJ contivesse
    /// aquele dígito — na prática, quase toda empresa do banco.
    /// </remarks>
    public bool SearchesByTaxId =>
        !string.IsNullOrWhiteSpace(CompanySearch) &&
        CompanySearch.Trim().All(character =>
            char.IsAsciiDigit(character) || character is '.' or '/' or '-' or ' ');
}
