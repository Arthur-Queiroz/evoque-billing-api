# Fee administrativo por colaborador — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA — use
> `superpowers:subagent-driven-development` (recomendado) ou
> `superpowers:executing-plans` para executar tarefa a tarefa. Os passos usam
> checkbox (`- [ ]`) para acompanhamento.

**Objetivo:** cobrar a taxa administrativa por colaborador que 8 empresas do
catálogo pagam, hoje ausente do software.

**Arquitetura:** `Company.FeePerMember`, um `decimal?` ao lado de
`AmountPerMember`, com a mesma forma e o mesmo ciclo de vida. O gerador de
prévia acrescenta **um** `BillingDraftItem` de taxa depois dos colaboradores. O
total da prévia passa a incluí-la, e é dele que saem o valor do boleto e a base
da NFS-e.

**Stack:** ASP.NET Core net10.0, MySqlConnector, xUnit.

**Design:** `api/.agents/specs/design/2026-09-25-fee-administrativo-design.md`

---

## Contexto que você precisa ter antes de começar

Este é um sistema de faturamento que cria cobranças reais. Três regras do
projeto valem aqui:

1. **Camadas explícitas:** `Controller → Service → Repository → Banco`. Não
   introduza CQRS, MediatR, Result monads ou repositório genérico.
2. **Migrations já aplicadas nunca são editadas.** A `014` semeou os valores por
   colaborador e já rodou em produção. Toda correção vira migration nova.
3. **Nomes completos.** `feePerMember`, não `fee`; `companyTaxId`, não `id`.

O verificador é `dotnet test Evoque.Billing.slnx --no-restore`, rodado da pasta
`api/`. A suíte tem 248 testes hoje; ela precisa continuar verde a cada commit.

**Trabalhe na branch `feature/fee-administrativo`**, que já existe e já contém o
documento de design.

## Estrutura de arquivos

| Arquivo | Responsabilidade nesta mudança |
|---|---|
| `Evoque.Billing.Api/Domain/Company.cs` | a propriedade `FeePerMember`, o setter e a regra de valor |
| `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs` | migration `018`: coluna e seed das 8 empresas |
| `Evoque.Billing.Api/Repositories/MySqlCompanyRepository.cs` | ler e gravar a coluna |
| `Evoque.Billing.Api/Contracts/CompanyContracts.cs` | `FeePerMember` no request de atualização e no response |
| `Evoque.Billing.Api/Services/CompanyCatalogService.cs` | aplicar o valor recebido na atualização |
| `Evoque.Billing.Api/Services/CorporateBillingDraftService.cs` | o item de taxa na prévia |
| `Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs` | expor a taxa no resultado da geração |
| `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs` | testes do domínio e da atualização |
| `Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs` | testes da prévia com e sem taxa |
| `web/client/src/lib/api.ts` | tipos do cliente |
| `web/client/src/app/page.tsx` | exibir e editar a taxa |

A ordem das tarefas é deliberada: o domínio primeiro, porque tudo depende dele;
a persistência depois; a prévia em seguida; o portal por último.

---

## Tarefa 1: a propriedade `FeePerMember` no domínio

**Arquivos:**
- Modificar: `Evoque.Billing.Api/Domain/Company.cs`
- Testar: `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs`

- [ ] **Passo 1: escreva os testes que falham**

Acrescente ao final de `CompanyCatalogServiceTests.cs`, antes da chave que
fecha a classe. `Company.CreateManually` é como uma empresa nasce — o
construtor é privado.

```csharp
    [Fact]
    public void NewCompany_HasNoFeePerMemberYet()
    {
        var company = Company.CreateManually("02346076000107", "Ciamon", OperatorId, DateTimeOffset.UtcNow);

        Assert.Null(company.FeePerMember);
    }

    [Fact]
    public void SetFeePerMember_StoresTheAgreedFee()
    {
        var company = Company.CreateManually("02346076000107", "Ciamon", OperatorId, DateTimeOffset.UtcNow);

        company.SetFeePerMember(8.00m, OperatorId, DateTimeOffset.UtcNow);

        Assert.Equal(8.00m, company.FeePerMember);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-8.00)]
    public void SetFeePerMember_RefusesSomethingThatIsNotAFee(decimal fee)
    {
        var company = Company.CreateManually("02346076000107", "Ciamon", OperatorId, DateTimeOffset.UtcNow);

        Assert.Throws<ValidationException>(
            () => company.SetFeePerMember(fee, OperatorId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetFeePerMember_AcceptsNullToClearIt()
    {
        var company = Company.CreateManually("02346076000107", "Ciamon", OperatorId, DateTimeOffset.UtcNow);
        company.SetFeePerMember(8.00m, OperatorId, DateTimeOffset.UtcNow);

        company.SetFeePerMember(null, OperatorId, DateTimeOffset.UtcNow);

        Assert.Null(company.FeePerMember);
    }
```

- [ ] **Passo 2: rode e confirme que falham**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: erro de compilação — `'Company' does not contain a definition for
'FeePerMember'`.

- [ ] **Passo 3: implemente**

Em `Company.cs`, logo **depois** da propriedade `AmountPerMember` e **antes** de
`CanBeBilled`:

```csharp
    /// <summary>
    /// Taxa administrativa que a empresa paga por colaborador, somada à
    /// mensalidade. Vem do acordo comercial, não do EVO.
    ///
    /// Nulo é o estado normal: das empresas do catálogo, só oito pagam taxa.
    /// </summary>
    public decimal? FeePerMember { get; private set; }
```

Ainda em `Company.cs`, logo **depois** do método `SetAmountPerMember`:

```csharp
    public void SetFeePerMember(decimal? feePerMember, string operatorId, DateTimeOffset updatedAt)
    {
        if (feePerMember is <= 0m)
        {
            throw new ValidationException(
                "A taxa por colaborador deve ser maior que zero. Para retirar a taxa, informe vazio.");
        }

        FeePerMember = feePerMember;
        RegisterUpdate(operatorId, updatedAt);
    }
```

A regra é a mesma de `SetAmountPerMember` de propósito: dois campos quase
idênticos com regras diferentes para valor vazio seria armadilha de leitura.

- [ ] **Passo 4: rode e confirme que passam**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: 252 testes, 0 falhas.

- [ ] **Passo 5: commit**

```bash
git add Evoque.Billing.Api/Domain/Company.cs Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs
git commit -m "Add the per-member fee to the company"
```

---

## Tarefa 2: persistir a taxa

`Company.Restore` é usado pelo repositório MySQL para reconstruir a entidade
lida do banco. Acrescentar um parâmetro a ele quebra a chamada em
`MySqlCompanyRepository.ReadCompany`, e é isso que queremos: o compilador
aponta todos os pontos que precisam mudar.

**Arquivos:**
- Modificar: `Evoque.Billing.Api/Domain/Company.cs`
- Modificar: `Evoque.Billing.Api/Repositories/MySqlCompanyRepository.cs`
- Modificar: `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs`

- [ ] **Passo 1: acrescente o parâmetro a `Company.Restore`**

Em `Company.cs`, no método `Restore`, acrescente o parâmetro logo **depois** de
`decimal? amountPerMember`:

```csharp
        decimal? feePerMember,
```

E no inicializador de objeto, logo **depois** da linha `AmountPerMember = amountPerMember,`:

```csharp
            FeePerMember = feePerMember,
```

- [ ] **Passo 2: acrescente a coluna ao SQL**

Em `MySqlCompanyRepository.cs` há quatro pontos. No `INSERT`, na lista de
colunas, depois de `amount_per_member,`:

```sql
             fee_per_member,
```

Na lista de `VALUES`, depois de `@amountPerMember,`:

```sql
             @feePerMember,
```

No bloco `ON DUPLICATE KEY UPDATE`, depois de
`amount_per_member = VALUES(amount_per_member),`:

```sql
            fee_per_member = VALUES(fee_per_member),
```

Em `SelectColumns`, depois de `amount_per_member,`:

```sql
               fee_per_member,
```

- [ ] **Passo 3: acrescente o parâmetro e a leitura**

No método que preenche os parâmetros, logo **depois** do bloco `@amountPerMember`:

```csharp
        command.Parameters.AddWithValue(
            "@feePerMember",
            company.FeePerMember.HasValue ? company.FeePerMember.Value : (object)DBNull.Value);
```

Em `ReadCompany`, logo **depois** do argumento de `amount_per_member`:

```csharp
            reader.IsDBNull(reader.GetOrdinal("fee_per_member"))
                ? null
                : reader.GetDecimal("fee_per_member"),
```

- [ ] **Passo 4: escreva a migration 018**

Em `DatabaseSchemaInitializer.cs`, acrescente a constante logo **depois** de
`SferaAmountPerMemberMigrationId`:

```csharp
    private const string CompanyFeePerMemberMigrationId = "018_add_company_fee_per_member";
```

Acrescente a chamada logo **depois** de `await CorrectSferaAmountPerMemberAsync(connection, cancellationToken);`:

```csharp
        await AddCompanyFeePerMemberAsync(connection, cancellationToken);
```

E o método, logo **depois** de `CorrectSferaAmountPerMemberAsync`:

```csharp
    /// <summary>
    /// A taxa administrativa por colaborador, cobrada por oito empresas do
    /// catálogo. Ela vinha da coluna VALOR FEE do controle operacional e nunca
    /// chegou ao software, porque o EVO descreve quem é colaborador e não o que
    /// foi combinado comercialmente.
    ///
    /// Os valores são semeados aqui, e não por endpoint depois, para ficarem
    /// versionados e revisáveis — e para o deploy não depender de alguém
    /// lembrar de um passo manual.
    ///
    /// A Ciasul entra por decisão explícita: a matriz não tem linha no controle,
    /// só as duas filiais, e as filiais foram tratadas como evidência do grupo.
    /// </summary>
    private static async Task AddCompanyFeePerMemberAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, CompanyFeePerMemberMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ColumnExistsAsync(connection, transaction, "companies", "fee_per_member", cancellationToken))
        {
            await ExecuteAsync(connection, """
                ALTER TABLE companies
                ADD COLUMN fee_per_member DECIMAL(18, 2) NULL AFTER amount_per_member;
                """, transaction, cancellationToken);
        }

        await ExecuteAsync(connection, """
            UPDATE companies SET fee_per_member = CASE tax_id
                WHEN '34818653000102' THEN 8.00
                WHEN '53164208000102' THEN 8.00
                WHEN '58515495000171' THEN 8.00
                WHEN '26626384000146' THEN 8.00
                WHEN '34480924000154' THEN 8.00
                WHEN '01330329000183' THEN 9.90
                WHEN '04026384000172' THEN 9.90
                WHEN '04967119000199' THEN 9.90
                ELSE fee_per_member
            END;
            """, transaction, cancellationToken);

        await InsertMigrationAsync(
            connection,
            transaction,
            CompanyFeePerMemberMigrationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
```

O `ELSE fee_per_member` é o que impede a migration de apagar a taxa das demais
empresas. O `ColumnExistsAsync` existe porque a `009` já foi editada depois de
aplicada neste projeto e a subida seguinte morreu com `duplicate column`.

- [ ] **Passo 5: rode os testes**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: 252 testes, 0 falhas. Se algo falhar aqui, é chamada de
`Company.Restore` que ficou sem o argumento novo — o compilador diz onde.

- [ ] **Passo 6: commit**

```bash
git add Evoque.Billing.Api/Domain/Company.cs Evoque.Billing.Api/Repositories/MySqlCompanyRepository.cs Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs
git commit -m "Persist the per-member fee and seed the eight companies"
```

---

## Tarefa 3: cadastrar a taxa pela API

**Arquivos:**
- Modificar: `Evoque.Billing.Api/Contracts/CompanyContracts.cs`
- Modificar: `Evoque.Billing.Api/Services/CompanyCatalogService.cs`
- Testar: `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs`

- [ ] **Passo 1: escreva o teste que falha**

Acrescente a `CompanyCatalogServiceTests.cs`, logo depois de
`UpdateAsync_StoresAndReturnsTheAmountPerMember` (linha 337), que é o teste
irmão. `CreateCatalog()` é o helper que a classe já usa, e a leitura de volta é
`GetAsync`, não uma listagem.

`58515495000171` é o CNPJ real da Contract e tem dígitos verificadores válidos
— o domínio valida os dois dígitos e recusa sequências inventadas.

```csharp
    /// <summary>
    /// A taxa precisa atravessar o cadastro e voltar na leitura, pelo mesmo
    /// motivo do valor por colaborador: sem isto ela existiria no domínio e
    /// seria invisível para quem opera.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_StoresAndReturnsTheFeePerMember()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest("58515495000171", "Contract", 25),
            OperatorId,
            CancellationToken.None);

        var updated = await catalog.Service.UpdateAsync(
            "58515495000171",
            new UpdateCompanyRequest("Contract", 25, 59.90m, 8.00m),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(8.00m, updated.FeePerMember);

        var listed = await catalog.Service.GetAsync("58515495000171", CancellationToken.None);
        Assert.Equal(8.00m, listed.FeePerMember);
    }

- [ ] **Passo 2: rode e confirme que falha**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: erro de compilação — `UpdateCompanyRequest` não aceita quatro
argumentos.

- [ ] **Passo 3: implemente**

Em `CompanyContracts.cs`, em `UpdateCompanyRequest`, acrescente o parâmetro
depois de `decimal? AmountPerMember = null`:

```csharp
    decimal? FeePerMember = null);
```

Em `CompanyResponse`, acrescente depois de `decimal? AmountPerMember`:

```csharp
    decimal? FeePerMember)
```

Em `CompanyResponse.FromDomain`, acrescente depois de `company.AmountPerMember`:

```csharp
            company.FeePerMember);
```

Em `CompanyCatalogService.UpdateAsync`, logo **depois** da linha
`company.SetAmountPerMember(request.AmountPerMember, operatorId, updatedAt);`:

```csharp
        company.SetFeePerMember(request.FeePerMember, operatorId, updatedAt);
```

- [ ] **Passo 4: rode e confirme que passa**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: 253 testes, 0 falhas.

- [ ] **Passo 5: commit**

```bash
git add Evoque.Billing.Api/Contracts/CompanyContracts.cs Evoque.Billing.Api/Services/CompanyCatalogService.cs Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs
git commit -m "Accept the per-member fee when updating a company"
```

---

## Tarefa 4: o item de taxa na prévia

Esta é a tarefa que move dinheiro. O teste de regressão do primeiro passo é o
que protege as 30 empresas ativas que **não** pagam taxa.

**Arquivos:**
- Modificar: `Evoque.Billing.Api/Services/CorporateBillingDraftService.cs`
- Modificar: `Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs`
- Testar: `Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs`

- [ ] **Passo 1: estenda o helper de teste**

Em `CorporateBillingDraftServiceTests.cs`, na classe aninhada `TestScenario`,
troque a assinatura de `AddCompany` por esta — o parâmetro novo tem valor
padrão, então nenhuma chamada existente quebra:

```csharp
        public void AddCompany(
            string taxId,
            string name,
            decimal? amountPerMember,
            bool isActive = true,
            decimal? feePerMember = null)
        {
            var company = Company.CreateManually(taxId, name, OperatorId, DateTimeOffset.UtcNow);
            if (amountPerMember is not null)
            {
                company.SetAmountPerMember(amountPerMember, OperatorId, DateTimeOffset.UtcNow);
            }

            if (feePerMember is not null)
            {
                company.SetFeePerMember(feePerMember, OperatorId, DateTimeOffset.UtcNow);
            }

            if (!isActive)
            {
                company.Deactivate(OperatorId, DateTimeOffset.UtcNow);
            }

            DataStore.Companies[taxId] = company;
        }
```

- [ ] **Passo 2: escreva os testes que falham**

Acrescente junto dos demais `[Fact]` da classe. `ContractTaxId` é uma constante
nova — declare-a ao lado de `OpenSportsTaxId`, no topo da classe:

```csharp
    private const string ContractTaxId = "58515495000171";
```

```csharp
    [Fact]
    public async Task GenerateAsync_AddsNoFeeItemToACompanyWithoutAFee()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);

        var result = await scenario.GenerateAsync();

        Assert.Equal(2157.60m, result.Created.Single().TotalAmount);
        Assert.Equal(24, scenario.DataStore.BillingDrafts.Single().Value.Items.Count);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFeeOncePerMember()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(ContractTaxId, "Contract", 59.90m, feePerMember: 8.00m);
        scenario.AddMembers(ContractTaxId, 6);

        var result = await scenario.GenerateAsync();

        // 6 x 59,90 = 359,40 de mensalidade, mais 6 x 8,00 = 48,00 de taxa.
        Assert.Equal(407.40m, result.Created.Single().TotalAmount);
        Assert.Equal(8.00m, result.Created.Single().FeePerMember);
    }

    [Fact]
    public async Task GenerateAsync_PutsTheFeeInASingleNamedItem()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(ContractTaxId, "Contract", 59.90m, feePerMember: 8.00m);
        scenario.AddMembers(ContractTaxId, 6);

        await scenario.GenerateAsync();

        var items = scenario.DataStore.BillingDrafts.Single().Value.Items;
        Assert.Equal(7, items.Count);
        var feeItem = items.Single(item => item.ExternalMemberId is null);
        Assert.Equal("Taxa administrativa", feeItem.Description);
        Assert.Equal(6, feeItem.Quantity);
        Assert.Equal(8.00m, feeItem.UnitAmount);
        Assert.Equal(48.00m, feeItem.TotalAmount);
    }
```

- [ ] **Passo 3: rode e confirme que falham**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: `GenerateAsync_ChargesTheFeeOncePerMember` falha porque
`GeneratedBillingDraftResponse` não tem `FeePerMember`. Os outros dois falham
na asserção de total ou de contagem de itens.

- [ ] **Passo 4: implemente**

Em `CorporateBillingDraftContracts.cs`, em `GeneratedBillingDraftResponse`,
acrescente depois de `decimal AmountPerMember,`:

```csharp
    decimal? FeePerMember,
```

Em `CorporateBillingDraftService.GenerateAsync`, substitua o bloco que monta
`billingDraft` — hoje ele começa em `var amountPerMember = company.AmountPerMember!.Value;`
— por este:

```csharp
            var amountPerMember = company.AmountPerMember!.Value;
            var feePerMember = company.FeePerMember;
            var version = ResolveNextVersion(company.TaxId, existingDraftsByCompany);
            var billingDraft = new BillingDraft(
                billingPeriod.Id,
                company.TaxId,
                company.DisplayName,
                company.TaxId,
                null,
                BuildItems(companyMembers, amountPerMember, feePerMember),
                version,
                generatedAt);
```

E na chamada a `created.Add`, acrescente `feePerMember` depois de
`amountPerMember`:

```csharp
            created.Add(new GeneratedBillingDraftResponse(
                billingDraft.Id,
                company.TaxId,
                company.DisplayName,
                companyMembers.Length,
                amountPerMember,
                feePerMember,
                billingDraft.TotalAmount));
```

Acrescente o método privado, logo **depois** de `GenerateAsync`:

```csharp
    /// <summary>
    /// Uma linha por colaborador e, quando a empresa paga taxa administrativa,
    /// uma única linha de taxa no fim.
    ///
    /// A taxa é um item próprio, e não um acréscimo ao valor unitário de cada
    /// colaborador, porque o boleto e a nota levam só o total: se o valor for
    /// embutido, a distinção entre mensalidade e taxa desaparece do sistema e
    /// a conferência contra o controle operacional para de bater.
    /// </summary>
    private static IReadOnlyCollection<BillingDraftItem> BuildItems(
        IReadOnlyCollection<CorporateMember> companyMembers,
        decimal amountPerMember,
        decimal? feePerMember)
    {
        var items = companyMembers
            .Select(member => new BillingDraftItem(
                member.MemberName,
                1,
                amountPerMember,
                member.EvoMemberId.ToString()))
            .ToList();

        if (feePerMember is > 0m)
        {
            items.Add(new BillingDraftItem(
                "Taxa administrativa",
                companyMembers.Count,
                feePerMember.Value,
                null));
        }

        return items;
    }
```

Ajuste também a mensagem de auditoria para registrar a taxa. Substitua a
interpolação existente por:

```csharp
                    feePerMember is > 0m
                        ? $"{companyMembers.Length} colaborador(es) x {amountPerMember:F2} mais taxa de {feePerMember.Value:F2} por colaborador para {company.DisplayName}."
                        : $"{companyMembers.Length} colaborador(es) x {amountPerMember:F2} para {company.DisplayName}."),
```

- [ ] **Passo 5: rode e confirme que passam**

```bash
cd api && dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: 256 testes, 0 falhas.

- [ ] **Passo 6: commit**

```bash
git add Evoque.Billing.Api/Services/CorporateBillingDraftService.cs Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs
git commit -m "Charge the administrative fee as an item of the billing draft"
```

---

## Tarefa 5: a taxa no portal

**Arquivos:**
- Modificar: `web/client/src/lib/api.ts`
- Modificar: `web/client/src/app/page.tsx`

Não há teste automatizado no client; a verificação é `npm run build` mais a
conferência visual descrita no passo 4.

- [ ] **Passo 1: os tipos**

Em `web/client/src/lib/api.ts` há três lugares com `amountPerMember`.
Acrescente `feePerMember` logo depois de cada um, com o mesmo tipo:

- linha ~259, no tipo da empresa: `feePerMember: number | null;`
- linha ~379, no request de atualização: `feePerMember?: number | null;`
- linha ~387, no resultado da geração: `feePerMember: number | null;`

- [ ] **Passo 2: exibir na lista e no detalhe**

Em `web/client/src/app/page.tsx`, na interface da empresa (linha ~102),
acrescente depois de `amountPerMember`:

```ts
  feePerMember?: number | null;
```

Na célula que mostra o valor (linha ~1440), acrescente **depois** dela uma
linha com a taxa, para a coluna dizer o que compõe a cobrança:

```tsx
{company.feePerMember ? (
  <div className="text-xs text-slate-500">
    + {money(company.feePerMember)} de taxa
  </div>
) : null}
```

Na linha que resume a prévia gerada (linha ~1914), troque por:

```tsx
{billingDraft.memberCount} &times; {money(billingDraft.amountPerMember)}
{billingDraft.feePerMember ? <> + {money(billingDraft.feePerMember)} de taxa</> : null}
{" = "}<strong>{money(billingDraft.totalAmount)}</strong>
```

- [ ] **Passo 3: editar no formulário**

No formulário de empresa (a partir da linha ~2171), o campo de valor já existe.
Replique-o para a taxa: um `useState` inicializado do mesmo jeito, o mesmo
`replace(".", ",")` na leitura e o mesmo `replace(",", ".")` no envio.

O estado, junto do `amountPerMember` existente:

```tsx
  const [feePerMember, setFeePerMember] = useState(
    company.feePerMember === null || company.feePerMember === undefined
      ? ""
      : String(company.feePerMember).replace(".", ","),
  );
```

No efeito que ressincroniza o formulário quando a empresa muda (linha ~2185),
acrescente a mesma chamada para `setFeePerMember`.

No envio (linha ~2305), ao lado do `normalizedAmount`:

```tsx
              const normalizedFee = feePerMember.trim().replace(",", ".");
              const parsedFee = normalizedFee === "" ? null : Number(normalizedFee);
```

E no corpo enviado, ao lado de `amountPerMember: parsedAmount`:

```tsx
                feePerMember: parsedFee,
```

O campo entra **entre** o `<label>` de "Valor por colaborador" e o `<p>` que o
explica. As classes são as mesmas do vizinho — `field mt-1.5 w-full` no input e
`block text-xs font-extrabold uppercase tracking-wide text-slate-500` no label.
São classes do design system do projeto; não invente outras.

```tsx
            <label className="block text-xs font-extrabold uppercase tracking-wide text-slate-500">Taxa administrativa por colaborador
              <input
                className="field mt-1.5 w-full"
                inputMode="decimal"
                placeholder="Sem taxa"
                value={feePerMember}
                onChange={(event) => setFeePerMember(event.target.value)}
              />
            </label>
```

E troque o texto explicativo (o `<p>` logo abaixo, hoje falando só do valor)
por um que cubra os dois campos:

```tsx
            <p className="text-xs leading-5 text-slate-500">O valor multiplica a quantidade de colaboradores eleg&iacute;veis ao gerar a pr&eacute;via mensal. A taxa administrativa, quando houver, &eacute; cobrada por colaborador e somada ao total.</p>
```

- [ ] **Passo 4: verifique**

```bash
cd web/client && npm run build
```

Esperado: build sem erro de tipo.

Depois suba a aplicação e confira três coisas na tela de empresas: a Contract
mostra `+ R$ 8,00 de taxa`, a Open Sports não mostra taxa nenhuma, e salvar a
Contract com o campo vazio remove a taxa.

- [ ] **Passo 5: commit**

```bash
git add web/client/src/lib/api.ts web/client/src/app/page.tsx
git commit -m "Show and edit the administrative fee in the portal"
```

---

## Tarefa 6: registrar a pendência do fee fixo

A MFA paga `500 reais fixo` e vai continuar sem taxa nenhuma no software. Sem
registro, isso vira perda de receita silenciosa quando ela começar a faturar.

**Arquivos:**
- Modificar: `api/.agents/PENDENCIAS.md`

- [ ] **Passo 1: leia o arquivo e siga o formato existente**

```bash
cat api/.agents/PENDENCIAS.md
```

- [ ] **Passo 2: acrescente a pendência**

Use o formato das demais entradas do arquivo. O conteúdo:

> **Fee fixo por empresa não implementado.** A MFA Pasta e Alimentos
> (07.618.678/0001-81) paga `500 reais fixo` segundo a coluna `VALOR FEE` do
> controle operacional. O software só cobra taxa por colaborador, então a MFA
> vai gerar prévia sem taxa nenhuma e nada vai sinalizar a falta. Hoje ela não
> fatura — está sem valor por colaborador —, mas isso precisa ser resolvido
> antes de ela entrar em lote.

- [ ] **Passo 3: commit**

```bash
git add api/.agents/PENDENCIAS.md
git commit -m "Record the missing fixed-fee support for MFA"
```

---

## Ao terminar

Confira o efeito no ciclo do dia 25, que o design prevê como R$ 195,00 de
acréscimo:

| Empresa | Pessoas | Taxa | Acréscimo |
|---|--:|--:|--:|
| Ciamon | 5 | 8,00 | 40,00 |
| Ciasul | 1 | 8,00 | 8,00 |
| Contract | 6 | 8,00 | 48,00 |
| Plastvidro | 6 | 9,90 | 59,40 |
| Vitória | 4 | 9,90 | 39,60 |

**Não gere prévias nem lotes como parte desta implementação.** As prévias da
competência 2026/09 já existem e já foram faturadas em Sandbox; regerá-las com
a taxa é uma decisão operacional de quem conduz o faturamento, não um passo do
plano.
