# Login temporário

Data: 18/09/2026
Status: aprovado para implementação

## Objetivo

Exigir identificação para usar o Evoque Cobranças, e fazer a auditoria passar a
responder "quem autorizou?".

É explicitamente temporário: a Evoque vai autenticar pelo Azure da empresa. O
desenho existe para que esse dia troque a origem da identidade, não o resto.

## O problema

Não existe autenticação nenhuma. Verificado em 18/09/2026, da internet aberta:

```text
GET https://evoque.devarthur.com.br/api/charge-history  ->  200
```

Qualquer pessoa com a URL lê o histórico de faturamento. Os endpoints que
aprovam prévia, aprovam lote e executam cobrança estão igualmente expostos. O
`app.UseAuthorization()` no `Program.cs` não barra nada, porque não há
autenticação configurada por trás dele.

O segundo problema é a assinatura de quem age. `operatorId` está fixo no portal:

```ts
const operatorId = "operador-web";
```

Aprovar prévia, aprovar lote e digitar `CONFIRMAR` registram todos o mesmo nome.
Num sistema que cria cobrança real, a pergunta "quem autorizou?" não tem
resposta. É o item **2.5** de `PENDENCIAS.md`.

## O que já é verdade, e por isso não está no escopo

A aplicação é servida por Cloudflare Tunnel em `evoque.devarthur.com.br`, com
TLS válido na borda. **A senha não trafega em claro.** Uma versão anterior deste
desenho supunha o contrário, olhando o `listen 80` do Nginx sem perceber que ele
é o lado interno do túnel.

Fica registrado como configuração a fazer, fora de código: `http://` responde
`200` sem redirecionar, e o painel da Cloudflare tem o "Always Use HTTPS" para
isso.

## Decisões

1. **Uma identidade por pessoa**, não senha compartilhada. Uma senha única
   protegeria o acesso e deixaria a auditoria tão anônima quanto hoje — metade
   do problema, resolvida.
2. **Cookie de sessão do ASP.NET Core**, não token próprio. É o esquema nativo:
   no dia do Azure troca-se o registro e os controllers não mudam.
3. **O servidor decide quem é o operador.** `OperatorId` sai dos contratos HTTP.
   Enquanto o cliente informar quem ele é, qualquer um manda
   `OperatorId: "geovanna"` e o sistema registra como se fosse ela — uma
   auditoria decorativa.
4. **Exigir autenticação por política padrão**, não por `[Authorize]` em cada
   controller. Assim um controller novo nasce protegido e liberar acesso é um
   ato explícito. O contrário falha no dia em que alguém esquecer um.
5. **Sem tabela, sem cadastro, sem tela de gestão de usuários.** São três ou
   quatro pessoas e a coisa é temporária.

## Onde moram as identidades

Em `production.env`, junto dos segredos que já vivem lá:

```text
AUTH__USERS__0__USERNAME=geovanna
AUTH__USERS__0__PASSWORDHASH=100000.<salt em base64>.<hash em base64>
```

Senha com hash, não em texto puro. O argumento para texto puro seria "o arquivo
já guarda a chave de produção do Asaas, que é pior" — e é verdade, mas pessoas
reusam senha entre sistemas, então um vazamento passaria do estrago deste
sistema.

**PBKDF2 pelo `Rfc2898DeriveBytes.Pbkdf2` da própria plataforma**, com salt por
usuário, 100.000 iterações, SHA-256, e comparação em tempo fixo por
`CryptographicOperations.FixedTimeEquals`. Não é inventar criptografia: é o uso
documentado do KDF da plataforma, em vinte e poucas linhas, sem dependência
nova. A alternativa era trazer `Microsoft.Extensions.Identity.Core` pelo
`PasswordHasher<T>`; o projeto hoje referencia um único pacote, e o arquivo
inteiro será apagado no dia do Azure.

Gerar o hash precisa de um caminho documentado. O binário da API aceita
`hash-password <senha>` como argumento e imprime a linha pronta para colar no
env, saindo antes de subir a aplicação. É pequeno o bastante para não virar um
modo escondido, e evita o operador procurar um gerador na internet e colar a
senha num site qualquer.

## A API

Um recurso, três operações:

| Operação | O quê |
|---|---|
| `POST /api/session` | entra; devolve o cookie |
| `DELETE /api/session` | sai |
| `GET /api/session` | quem sou eu, ou `401` |

Cookie `httpOnly`, invisível ao JavaScript, então não vaza por XSS. `SameSite`
estrito — portal e API compartilham origem atrás do mesmo Nginx. Sessão de **8
horas deslizantes**: cobre um dia de trabalho sem re-login e expira sozinha num
navegador esquecido.

Ficam abertos apenas `POST /api/session` e `/health`, este último porque o
Compose o usa para saber se o contêiner subiu. Todo o resto exige autenticação
pela política padrão.

### Um detalhe que só aparece no deploy

A Cloudflare termina o TLS na borda e entrega HTTP ao Nginx. Sem
`UseForwardedHeaders`, a aplicação acredita que a requisição chegou por HTTP e
se recusa a emitir um cookie `Secure` — o login funcionaria localmente e
falharia em produção, com sintoma confuso. O middleware entra junto, lendo
`X-Forwarded-Proto`.

## Camadas

```text
SessionController ──→ OperatorAuthenticationService ──→ AuthenticationOptions
                                                        (usuários do ambiente)
```

`OperatorAuthenticationService` responde uma pergunta: este usuário e esta senha
conferem? Não conhece HTTP, cookie nem `HttpContext`, e por isso é testável
direto, sem subir aplicação.

Uma senha errada e um usuário inexistente devolvem a mesma resposta, e o serviço
calcula o hash mesmo quando o usuário não existe. Responder mais rápido para
usuário inexistente conta ao atacante quais nomes valem a pena atacar.

## O operador

Os services **não mudam**. Eles já recebem `operatorId` como parâmetro, que é o
desenho certo. Muda quem preenche esse parâmetro: hoje é o corpo da requisição,
passa a ser o usuário autenticado, lido pelo controller.

O alcance real, medido e não estimado:

| O quê | Quanto |
|---|---|
| Contratos de **requisição** que perdem `OperatorId` | 21 |
| Pontos de teste que constroem esses contratos | 53 |
| Chamadas do portal que param de enviar o campo | 37 |

**Os testes quebram.** Uma versão anterior deste desenho afirmava que os 191
seguiriam válidos sem tocar em nenhum, olhando apenas as chamadas a services.
Está errado: os testes também constroem os contratos HTTP diretamente, como em
`new CreateCompanyRequest(taxId, nome, dia, OperatorId)`. São 53 pontos, todos
mecânicos, mas precisam entrar na conta do trabalho.

**`OperatorId` em resposta permanece.** `AuditLogResponse` e
`CompanyCatalogImportResponse` mostram quem fez o quê — é dado gravado, não
alegação de quem chama. Só o lado de requisição some.

**Quatro contratos ficam vazios.** `ApproveBillingDraftRequest`,
`CreateBillingPeriodRequest`, `SynchronizeChargeHistoryRequest` e
`CompanyOperatorRequest` só carregam `OperatorId`. Sem ele não sobra campo, e os
endpoints correspondentes passam a não receber corpo nenhum. Melhor apagar o
record do que manter um tipo vazio que sugere que um dia haverá algo ali.

## O portal

Ao abrir, pergunta `GET /api/session`. `401` mostra a tela de login no lugar da
aplicação; `200` segue com o nome de quem entrou visível e um botão de sair.
Qualquer chamada que devolva `401` volta para o login, o que cobre a sessão
expirada no meio do uso.

A tela é um formulário: usuário, senha, botão. O erro não diz qual dos dois
estava errado — dizer "usuário não existe" entrega a lista de usuários válidos a
quem estiver tentando.

## Erros

| Situação | Resposta |
|---|---|
| usuário ou senha errados | `401`, mensagem única |
| campos vazios | `400` |
| sessão expirada | `401`, portal volta ao login |
| nenhum usuário configurado | a aplicação **não sobe** |

A última é deliberada. Uma lista vazia de usuários poderia ser lida como "sem
restrição", e o modo de falha seria um deploy que remove a proteção em silêncio.
Falhar na subida é ruidoso e reversível; abrir o sistema não é.

## Testes

- senha correta autentica; senha errada não;
- usuário inexistente devolve o mesmo resultado que senha errada;
- o hash gerado para a mesma senha é diferente a cada vez, pelo salt, e ainda
  assim confere;
- ambiente sem usuário configurado impede a subida;
- um controller sem `[AllowAnonymous]` exige autenticação — o teste que protege
  a política padrão, porque o defeito que ela evita é silencioso.

Os testes atuais não passam por HTTP, então a política padrão não quebra nenhum
deles — e é exatamente por isso que ela precisa do teste acima. Uma proteção que
nenhum teste exercita por acidente é uma proteção que ninguém percebe ter
perdido.

O que quebra são os 53 pontos que constroem contratos de requisição com
`OperatorId`. É trabalho mecânico de acompanhar a mudança, não regressão.

## O dia do Azure

Troca-se o registro do esquema no `Program.cs`. A política padrão, os
controllers, `User.Identity` e a leitura do operador seguem idênticos.

Some: a tela de login, os três endpoints de sessão, a lista de usuários no
ambiente e o gerador de hash — exatamente as partes que só existem porque o
Azure ainda não está lá.

## Fora de escopo

Cadastro e gestão de usuários pela interface, recuperação de senha, papéis e
permissões, "lembrar de mim", bloqueio por tentativas, e autenticação de dois
fatores. Nada disso se paga numa camada que será removida.

O redirecionamento de `http://` para `https://` é configuração no painel da
Cloudflare, não código.

## Relação com as pendências

Fecha o item **2.5** de `PENDENCIAS.md`. Com ele, o item **2.1** passa a valer a
pena: uma tela de auditoria deixaria de mostrar "operador-web" em toda linha.
