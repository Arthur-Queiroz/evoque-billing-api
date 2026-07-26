# Contexto do projeto

O Evoque Cobranças é um sistema interno para preparar, aprovar e emitir
cobranças corporativas no Asaas. A aplicação não substitui o Evo: pessoas,
contratos, matrículas e valores correntes vêm exclusivamente da API do Evo.

## Fontes de dados

- **Evo API:** fonte corrente de membros, contratos e valores; nenhuma chamada
  mutável é permitida neste projeto.
- **Planilha histórica exportada do Evo:** referência para entender o formato
  de empresa/CNPJ e contratos antigos; não é fonte corrente.
- **Banco legado da Evoque:** não é fonte do produto.
- **Banco próprio do faturamento:** persiste competência, prévia, agenda,
  vínculo configurado, lote, auditoria e resultados.

## Repositórios

- `evoque-billing-api`: API ASP.NET Core, infraestrutura de produção e Compose.
- `evoque-billing-web`: Next.js/Tailwind, publicado como imagem separada.

Ambos publicam imagens no GHCR. A API sobe a stack inicial da KVM2; alterações
web posteriores atualizam apenas a imagem do client pelo GitHub Actions.

## Estado do MVP

O fluxo de prévia, aprovação, confirmação e lote está implementado. A emissão
de teste é permitida apenas no Asaas Sandbox. Falta confirmar, com a Evo, uma
fonte automática e confiável para o vínculo empresa pagadora por matrícula;
até lá, o vínculo deve ser configurado e auditado no produto.

