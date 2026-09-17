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
    /// pontuação usual do CNPJ, **com pelo menos um dígito**, e qualquer letra
    /// no meio já indica busca por nome.
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
    ///
    /// A exigência de pelo menos um dígito não é cosmética: sem ela, um termo
    /// feito só de pontuação (ex.: "-") também seria CNPJ-shaped, mas com zero
    /// dígitos para comparar. As duas implementações discordam sobre o que
    /// fazer com uma extração vazia — um <c>LIKE '%%'</c> no MySQL casa com
    /// toda linha, enquanto um <c>Contains</c> vazio em memória não casa com
    /// nenhuma — e essa é exatamente a divergência que esta propriedade existe
    /// para impedir. Exigindo um dígito, o caso de extração vazia nunca chega a
    /// acontecer: um termo sem dígito é sempre busca por nome.
    /// </remarks>
    public bool SearchesByTaxId =>
        !string.IsNullOrWhiteSpace(CompanySearch) &&
        CompanySearch.Trim().Any(char.IsAsciiDigit) &&
        CompanySearch.Trim().All(character =>
            char.IsAsciiDigit(character) || character is '.' or '/' or '-' or ' ');

    /// <summary>
    /// Só os dígitos de <see cref="CompanySearch"/>, para comparar com
    /// <see cref="ChargeHistoryEntry.CompanyTaxId"/>. Só faz sentido chamar
    /// quando <see cref="SearchesByTaxId"/> é verdadeiro; nesse caso, o
    /// resultado nunca é vazio, porque <see cref="SearchesByTaxId"/> já exige
    /// pelo menos um dígito.
    /// </summary>
    /// <remarks>
    /// Vive ao lado de <see cref="SearchesByTaxId"/> pelo mesmo motivo: as duas
    /// implementações liam <c>CompanySearch.Where(char.IsAsciiDigit)</c> cada
    /// uma por conta própria, e foi exatamente essa duplicação, um nível
    /// abaixo da forma do termo, que permitiu a extração vazia divergir entre
    /// as duas.
    /// </remarks>
    public string CompanyTaxIdDigits =>
        new(CompanySearch?.Where(char.IsAsciiDigit).ToArray() ?? []);
}
