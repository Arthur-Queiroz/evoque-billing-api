# Regras de negócio

## Ambientes

- **Sandbox:** pode criar cobranças de teste após aprovação e confirmação.
- **Produção:** permanece bloqueada até autorização operacional explícita e
  configuração independente do Asaas.
- O seletor visual nunca é autorização suficiente para criar cobranças reais.

## Fluxo de faturamento

```text
Dados correntes do Evo
→ prévia por empresa
→ aprovação da prévia/competência
→ prévia de lote sem chamada ao Asaas
→ aprovação do lote
→ confirmação textual CONFIRMAR
→ criação no Asaas
→ auditoria e resultado por item
```

Uma prévia Sandbox não consolida uma empresa como cobrada. Apenas uma execução
de Produção bem-sucedida consolida a cobrança definitiva.

## Empresas e ciclos

- Dias de cobrança permitidos: `02`, `18`, `20` e `25`.
- Uma execução recorrente seleciona somente empresas com agenda ativa naquele
  dia e prévias aprovadas na competência.
- O vínculo necessário é explícito e auditável:

```text
contrato ou matrícula Evo → empresa pagadora → CNPJ → cliente Asaas → dia
```

- Não inferir empresa pagadora a partir do nome de um colaborador.
- O Asaas envia o boleto/e-mail ao cliente cadastrado; o MVP não depende de
  Gmail ou OAuth do GCP.

## Segurança e idempotência

- Cada operação registra operador, data, ambiente e resultado.
- Não criar duas cobranças para a mesma empresa, competência e versão aprovada.
- Erros do Asaas ficam no item do lote e podem ser repetidos somente pelo fluxo
  de retry controlado.

