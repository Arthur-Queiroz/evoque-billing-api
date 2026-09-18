using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class BillableCorporateContractsTests
{
    [Theory]
    [InlineData("EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA")]
    [InlineData("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO")]
    public void Includes_TheTwoContractsTheCompanyPays(string contractName)
    {
        Assert.True(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// Este vem do EVO com valor preenchido: quem paga é a própria pessoa. Um
    /// `contains("CORPORATIVO")` o incluiria, e a empresa seria cobrada por
    /// alguém que já pagou.
    /// </summary>
    [Fact]
    public void Excludes_TheCorporateContractThatThePersonPays()
    {
        Assert.False(BillableCorporateContracts.Includes("EVOQUE CORPORATIVO RECORRENTE - 39,95"));
    }

    /// <summary>
    /// Cortesia encerrada. Não é cobrada de ninguém.
    /// </summary>
    [Fact]
    public void Excludes_TheDiscontinuedCourtesy()
    {
        Assert.False(BillableCorporateContracts.Includes("VIP (até 6 meses) - EVOQUE CORPORATIVO"));
    }

    [Theory]
    [InlineData("EVOPASS RECORRENTE 79,90")]
    [InlineData("Transferido da filial EVOQUE  RIBEIRÃO")]
    [InlineData("")]
    [InlineData(null)]
    public void Excludes_WhatIsNotCorporateBilling(string? contractName)
    {
        Assert.False(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// O EVO exporta o nome do contrato como foi digitado lá. Caixa e espaço
    /// sobrando não podem decidir se alguém é cobrado.
    /// </summary>
    [Theory]
    [InlineData("evoque corporativo - folha de pagamento")]
    [InlineData("  EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO  ")]
    public void Includes_IgnoringCaseAndSurroundingSpace(string contractName)
    {
        Assert.True(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// A lista é o dado que diz quem é cobrado. Se alguém acrescentar um
    /// contrato sem pensar, este teste falha e obriga a decisão a ser explícita.
    /// </summary>
    [Fact]
    public void HasExactlyTheTwoContractsWeKnowAbout()
    {
        Assert.Equal(2, BillableCorporateContracts.All.Count);
    }
}
