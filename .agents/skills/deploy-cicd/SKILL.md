---
name: deploy-cicd
description: Publicar ou diagnosticar o CI/CD da API Evoque na KVM2 via GitHub Actions, GHCR e Cloudflare Access. Use ao alterar workflows, imagens Docker, Compose, variáveis de deploy, falhas de Actions ou releases na VPS; nunca para orientar deploy manual por SSH.
---

# Deploy CI/CD da API

1. Leia `references/pipeline.md` e `.agents/specs/DEPLOYMENT.md` antes de
   alterar infraestrutura.
2. Mantenha a publicação no GitHub Actions: build da imagem, push para GHCR e
   deploy remoto via Cloudflare Access. Não substitua esse fluxo por comandos
   manuais de Docker ou Git na VPS.
3. Nunca escreva segredos em workflow, imagem, logs ou repositório. Use somente
   GitHub Secrets e `/opt/evoque/production.env`.
4. Antes de mudar o workflow, valide que Sandbox continua sendo o único Asaas
   mutável autorizado no MVP.
5. Após mudanças de infraestrutura, valide `docker compose config` usando o
   arquivo de exemplo e execute os testes da API.

