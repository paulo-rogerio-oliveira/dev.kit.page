# Arquitetura do dev.kit.page

## As peças

```
dev.kit (git.kit)  ──lote v1, X-Machine-Key──▶  API (ASP.NET Core)  ◀──JWT──  web (React)
   TelemetrySyncService                          │                              landing · login · dashboard
                                                 ▼
                                       SQLite embarcada (ou Azure SQL)
```

A API é uma solução .NET 10 em camadas, cada uma com a sua suíte de teste espelhada:

| Projeto | Papel | Referencia |
|---|---|---|
| `DevKitPage.Contracts` | O CONTRATO versionado (v1): o lote, o registro e os DTOs do dashboard. É dado, não referencia nada. | — |
| `DevKitPage.Core` | Entidades, opções e as regras puras: validação do lote, qualidade (divisor zero = nulo), chave da máquina, política de senha, período; as interfaces dos serviços. | Contracts |
| `DevKitPage.Infrastructure` | EF Core (SQLite / SQL Server), a ingestão, as consultas agregadas, o expurgo, a semente do admin. | Core |
| `DevKitPage.Api` | Os endpoints, as duas autenticações, o expurgo diário, CORS, `/health`, OpenAPI. | Infrastructure, Contracts |

As convenções vêm do git.kit: pacotes centralizados (`Directory.Packages.props`), aviso é erro e a
documentação XML gerada (`Directory.Build.props`), SDK fixado (`global.json`).

## Decisões (e por quê)

**Duas autenticações, separadas.** O usuário do dashboard entra por login e senha (hash do
`PasswordHasher` do Identity, JWT Bearer, bloqueio após falhas seguidas). A máquina entra pela chave
no cabeçalho `X-Machine-Key`, que a API devolve UMA vez no registro e guarda só como hash SHA-256. A
ingestão aceita só a chave (o JWT de usuário recebe 401) e o dashboard aceita só o JWT: vazar uma
não abre a outra.

**Troca de senha obrigatória.** O admin semeado tem `DeveTrocarSenha`; o token dele leva a claim
`troca_pendente`, e a política padrão de TODA rota a recusa (403) — só `/api/auth/trocar-senha`
aceita. A web repete a regra na rota protegida, mas quem garante é a API.

**Idempotência por `eventId`.** O dev.kit reenvia o lote que não teve resposta, e dois processos
dele (o app e o serviço agendador) podem enviar o mesmo evento. O `eventId` é único na base; o que
já existe vira "duplicado" e não conta de novo.

**Totais diários consolidados na ingestão.** Cada evento novo soma, na mesma transação, no total do
seu dia, tipo e recorte (`TotaisDiarios`). Os números do dashboard saem dali, agregados no banco
(`GroupBy` → SQL); o evento bruto serve ao log. Por isso o expurgo diário dos brutos além de
`Telemetria:RetencaoDias` (padrão 180) nunca muda o histórico.

**Qualidade de uso** — a mesma definição do `devcli telemetria` do dev.kit: taxa de falha de turno
(e as falhas por causa de `TurnFailures`), duração média do turno, nota média dos avaliadores,
objetivos cumpridos × recusados e turnos por objetivo cumprido (retrabalho). Sem denominador, a
razão é nula, e a web mostra um traço.

**Base embarcada, provider trocável.** SQLite agora, com as migrations versionadas
(`Infrastructure/Migrations`) aplicadas na subida. `Banco:Provider=SqlServer` troca para o Azure SQL
sem mudar código; nesse provider o esquema nasce do modelo (`EnsureCreated`), e as migrations
próprias do SQL Server entram num projeto de migrations separado quando a base for para lá.

**Privacidade.** A API só recebe o que o dev.kit manda, e o dev.kit não manda caminhos, nomes de
repositório ou cliente, argumentos de comando, prompt, resposta, código, e-mail nem usuário do
Windows. A máquina é um GUID anônimo; o "apelido" é derivado dele.

**Segredos fora do repositório.** `Jwt:Segredo`, `Telemetria:CodigoDeRegistro` e
`Seed:AdminPassword` vêm de User Secrets (desenvolvimento) ou de variável de ambiente / Key Vault
(Azure). Em Production a API não sobe sem o segredo JWT.

## Testes

| Suíte | O que prova |
|---|---|
| `api/tests/DevKitPage.Core.Tests` | validação do lote, qualidade com divisor zero, chave, senha, período |
| `api/tests/DevKitPage.Infrastructure.Tests` | SQLite de verdade: admin semeado uma vez, reenvio sem duplicar, eventId único, agregação e filtro, paginação, expurgo mantendo os totais, bloqueio do login |
| `api/tests/DevKitPage.Api.Tests` | WebApplicationFactory: login, 401/403, token vencido, troca obrigatória, chave inválida, JWT na ingestão, 413, registro, `/health`, Production sem segredo |
| `web/src/**/*.test.tsx` | Vitest + Testing Library + MSW: landing, login, expiração, troca, KPIs, filtros, divisor zero |
| `web/e2e` | Playwright: base nova → login do admin → troca → registro da máquina → lote (e reenvio) → números no dashboard |
