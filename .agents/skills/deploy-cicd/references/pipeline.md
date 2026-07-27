# Pipeline da API

O workflow `deploy.yml` é acionado por `main` ou manualmente e só executa quando
`DEPLOY_ENABLED=true`.

Ele cria a imagem da API no GHCR e envia a release versionada da infraestrutura
para a KVM2. O Compose puxa as imagens `api:main` e `web:main`, sobe API, web e
Nginx, e verifica `/health` em `127.0.0.1:8088`.

Use a conta limitada `agent` apenas para diagnóstico. Operações privilegiadas
ocorrem dentro do workflow usando a chave SSH exclusiva e Cloudflare Access.
