namespace Evoque.Billing.Api.Tests;

/// <summary>
/// Relógio fixo para os testes. Sem ele, os vencimentos fixos dos cenários
/// passam a ser "data no passado" com o correr do calendário e a suíte
/// quebra sozinha, sem nenhuma mudança de código.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedNow;
}
