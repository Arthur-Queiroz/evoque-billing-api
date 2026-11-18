namespace Evoque.Billing.Api.Domain;

public enum EntryProrationKind
{
    /// <summary>O colaborador já estava no convênio no início do período.</summary>
    FullPeriod,

    /// <summary>Entrou no meio do período: paga só os dias usados.</summary>
    Prorated,

    /// <summary>Entrou no dia do fechamento ou depois: não pertence a este período.</summary>
    OutsidePeriod,
}

public sealed record EntryProrationResult(
    EntryProrationKind Kind,
    int BilledDays,
    decimal Amount);

/// <summary>
/// Proporcional de entrada, combinado com a Evoque em 11/2026.
/// </summary>
/// <remarks>
/// <para>
/// Só o primeiro mês do colaborador no convênio é proporcional. A data de
/// entrada é o início do primeiro contrato corporativo dele no EVO, não o
/// cadastro na academia: quem já era aluno e passou para o plano da empresa
/// entra no convênio naquele dia. Quem sai e volta depois não é entrada nova.
/// </para>
/// <para>
/// A base é sempre 30 dias, mesmo em período de 31 ou 28, e os dias contados são
/// <c>fechamento − entrada</c>: entrada em 14/06 com fechamento em 25/06 são 11
/// dias. Quem entra no primeiro dia do período usou o período inteiro e paga o
/// valor cheio; o valor nunca passa do cheio. A saída no meio do período não é
/// proporcional: paga o valor cheio.
/// </para>
/// </remarks>
public static class EntryProration
{
    public const int BaseDays = 30;

    public static EntryProrationResult Calculate(
        decimal amountPerMember,
        DateOnly entryDate,
        DateOnly closingDate)
    {
        var periodStart = closingDate.AddMonths(-1).AddDays(1);
        if (entryDate <= periodStart)
        {
            return new EntryProrationResult(EntryProrationKind.FullPeriod, BaseDays, amountPerMember);
        }

        var billedDays = closingDate.DayNumber - entryDate.DayNumber;
        if (billedDays <= 0)
        {
            return new EntryProrationResult(EntryProrationKind.OutsidePeriod, 0, 0m);
        }

        if (billedDays >= BaseDays)
        {
            return new EntryProrationResult(EntryProrationKind.FullPeriod, BaseDays, amountPerMember);
        }

        var amount = decimal.Round(
            amountPerMember * billedDays / BaseDays,
            2,
            MidpointRounding.AwayFromZero);
        return new EntryProrationResult(EntryProrationKind.Prorated, billedDays, amount);
    }
}
