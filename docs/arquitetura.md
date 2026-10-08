# Arquitetura do dev.kit.page

## As peças

```
dev.kit (git.kit)  ──lote v1, X-Machine-Key──▶  API (ASP.NET Core)  ◀──JWT──  web (React)
   TelemetrySyncService                          │   ▲                          landing · login · dashboard
                                                 │   └── POST /api/demonstracoes (anônimo, limite por IP) ── formulário da landing
                                                 ▼
                                       SQLite embarcada (ou Azure SQL)
```

A API é uma solução .NET 10 em camadas, cada uma com a sua suíte de teste espelhada:

| Projeto | Papel | Referencia |
|---|---|---|
| `DevKitPage.Contracts` | O CONTRATO versionado (v1): o lote, o registro, os DTOs do dashboard e o pedido de demonstração. É dado, não referencia nada. | — |
| `DevKitPage.Core` | Entidades, opções e as regras puras: validação do lote e do pedido de demonstração, qualidade (divisor zero = nulo), chave da máquina, política de senha, período; as interfaces dos serviços. | Contracts |
| `DevKitPage.Infrastructure` | EF Core (SQLite / SQL Server), a ingestão, as consultas agregadas, os pedidos de demonstração, os expurgos, a semente do admin. | Core |
| `DevKitPage.Api` | Os endpoints, as duas autenticações, o limite de taxa do formulário (atrás do proxy), o expurgo diário, CORS, `/health`, OpenAPI. | Infrastructure, Contracts |

O SQL Server tem os seus scripts em `api/scripts/sqlserver` (ver *Base embarcada* abaixo).

## A landing (US #283)

A landing é a página de venda: hero com a proposta e o vídeo do fluxo → benefícios → como funciona
(o fluxo em sete etapas nomeadas) → recursos (um vídeo cada) → comparativo → integrações → segurança
→ para empresas → contato (pedido de demonstração) → FAQ → CTA final. A ordem é a lista `SECOES` de
`web/src/conteudo/landing.ts`, a mesma que o teste confere. Desde a US #381 a página **não explica a
área logada** (o teste recusa "dashboard" e "métricas" no conteúdo), e a tabela comparativa e a seção
"Para empresas" seguem a pesquisa de [landing-conteudo.md](landing-conteudo.md).

- **Conteúdo tipado, separado da apresentação.** `conteudo/landing.ts` tem os textos e as mídias
  (`Recurso`, `Beneficio`, `Passo`, `Integracao`, `PerguntaFrequente`, `Midia`) — evolução do antigo
  `RECURSOS` da `Landing.tsx`. Trocar um texto ou um vídeo não toca nos componentes.
- **Componentes reutilizáveis** em `web/src/componentes`: `Secao` (âncora + título por
  `aria-labelledby`), `Cta` (âncora da página ou rota do app), `Midia` e o hook
  `usePrefereMenosMovimento`, `FormularioDeDemonstracao` e `PedidosDeDemonstracao` (o dashboard).
- **Mídia sem pesar no carregamento.** `<video muted loop playsInline autoPlay preload="none">` com
  poster, largura e altura fixas (sem deslocar o layout); as `<source>` (WebM, depois MP4) só entram
  quando o vídeo se aproxima da tela (`IntersectionObserver`). Com `prefers-reduced-motion` fica só
  o poster; sem vídeo ou com falha, a captura estática.
- **SEO e compartilhamento** no `index.html`: título e descrição de venda, canonical, Open Graph e
  Twitter card (`compartilhar.jpg`, 1200×630), favicon e `theme-color`. A URL absoluta vem de
  `VITE_SITE_URL` no build (o plugin `url-do-site` do `vite.config.ts`).

### Mídias da landing

As mídias ficam **no repositório**, em `web/public/midia` (um arquivo por recurso e formato:
`agente.mp4`, `agente.webm`, `agente.jpg`), com orçamento de **1,5 MB por arquivo e 25 MB no total**
— o gravador (`npm run midia`) falha acima disso. **Sem Git LFS**: o build do Azure Static Web Apps e
o checkout do CI leem o repositório direto, e o LFS exigiria configurar os dois. Os formatos de
mídia estão como `binary` no `.gitattributes` (o `text=auto` não toca neles). Se uma mídia não couber
no orçamento, ela vai para um Blob Storage com CDN e a URL entra no `landing.ts`. O roteiro e o
caminho para regravar estão em [landing-conteudo.md](landing-conteudo.md).

### Pedido de demonstração

- **Rota pública, validada antes do banco.** `POST /api/demonstracoes` é a única escrita anônima da
  API. O `ValidadorDeDemonstracao` (Core) exige nome, e-mail válido e o consentimento, e limita os
  tamanhos (empresa e mensagem são opcionais); o 400 traz os erros por campo, e a web repete a mesma
  validação no formulário.
- **Limite de taxa por IP do cliente, certo atrás do proxy.** No Azure Container Apps todo visitante
  chega com o IP do proxy — dividiriam uma cota só. Por isso `UseForwardedHeaders`
  (`X-Forwarded-For`/`Proto`) roda ANTES do `UseRateLimiter`, e só aceita o cabeçalho de quem está em
  `Proxy:RedesConfiaveis` (de qualquer outro ele é ignorado: o visitante não escolhe o próprio IP para
  fugir da cota). O limitador é de janela fixa de um minuto, particionado pelo IP, com
  `Demonstracoes:LimitePorMinuto` (padrão 5); acima disso, 429 com `Retry-After`. Só a rota do
  formulário tem a política.
- **Privacidade dos contatos (LGPD).** Grava só o que o visitante informou (nome, e-mail, empresa,
  mensagem) e o instante do consentimento — nem IP, nem navegador. Ler (`GET
  /api/dashboard/demonstracoes`, paginado) e excluir (`DELETE /api/dashboard/demonstracoes/{id}`, o
  atendimento ao pedido de eliminação do titular) só no grupo `/api/dashboard`, sob a política JWT
  padrão. A retenção é `Demonstracoes:RetencaoDias` (padrão 365): o `ExpurgoDiario` apaga os pedidos
  vencidos na MESMA volta de 24 h do expurgo dos eventos (`IExpurgoDePedidos` ao lado do
  `IExpurgoDeEventos`), cada um com o seu tratamento de falha — um que falha não impede o outro.
- **Sem tocar na telemetria.** A tabela `PedidosDeDemonstracao` não tem relação com máquinas nem
  eventos.

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

**Máquinas nas métricas (US #381).** A `QuantidadeResposta` traz `MaquinasAtivas` (máquinas
distintas com total no período — a mesma fonte dos outros números, então o expurgo não as apaga) e
`MaquinasRegistradas` (as que já existiam no fim do período). Nenhuma tabela nova: a contagem sai de
`TotaisDiarios` e de `Maquinas`, e a lista `/api/dashboard/maquinas` continua alimentando o filtro.

**Exceções não classificadas (US #381).** O dev.kit manda o evento `ExcecaoNaoClassificada` com o
`trace` JÁ sanitizado e a `assinatura` (campos opcionais do v1). Na ingestão, na MESMA transação:
- o recorte do evento é a assinatura, então o total diário por (dia, tipo, assinatura) dá as
  ocorrências, a linha do tempo e as máquinas de cada grupo — sem tabela de contagem, e sobrevivendo
  ao expurgo como o resto do histórico;
- o `GrupoDeErro` (um por assinatura, nunca expurgado) guarda o tipo, o estado da reação, a primeira
  e a última versão e quando foi visto; a `OcorrenciaDeErro` guarda o trace das últimas 20 de cada
  grupo, no máximo `Telemetria:MaxOcorrenciasPorEmpresa` por empresa (padrão 2.000; as anônimas são um
  grupo só — quem gera exceção em laço descarta as próprias, nunca as de outra empresa) e, somando
  todas, no máximo `Telemetria:MaxOcorrenciasGuardadas` (padrão 10.000 — saem as mais antigas),
  expurgadas além de `Telemetria:RetencaoDias` pelo `ExpurgoDiario`;
- o trace é mascarado DE NOVO (`RegrasDeErro.Mascarar`: caminhos Windows, UNC e Unix, e-mails, URLs e
  GUIDs) e cortado em 8 KB — defesa em profundidade, caso um dev.kit com defeito escape do sanitizador.
  O `Detalhe` continua com 200 caracteres: é chave do total diário, e texto livre ali explodiria a
  cardinalidade.

A reação segue o Sentry: `Novo` → `Visto` → `Resolvido` (com a versão da correção) ou `Ignorado`; o
grupo resolvido que volta numa versão igual ou maior que a da correção passa a `Regrediu` (só o
servidor põe esse estado). Para coletar, `GET /api/dashboard/erros/{id}/exportar` devolve o detalhe em
JSON (trace, ocorrências, linha do tempo, versões e máquinas). O ciclo fecha no dev.kit: um detector
novo em `TurnFailures.Padrao` tira a falha de "não classificada", e o grupo para de crescer.

**Venda empresarial (US #381).** O gestor de uma empresa vê e coleta o uso dos colaboradores dela:
- **Empresa e gestor.** O admin cria a `Empresa` (nome, plano, assentos e um código de adesão
  aleatório, `DK-XXXX-XXXX`) e convida o gestor (`POST /api/empresas/{id}/gestores`). O gestor é um
  `Usuario` com `Papel = gestor` e `EmpresaId` — o MESMO login, JWT e troca obrigatória de senha do
  admin; o token leva as claims `role` e `empresa`.
- **Adesão no dev.kit, com consentimento.** O colaborador informa o código e o próprio nome em
  *Configurações → Telemetria de uso* e aceita o aviso do que o gestor passa a ver; só então o dev.kit
  manda `codigoEmpresa` e `colaborador` no registro (`MachineRegistrationV1`, campos opcionais). A
  máquina ganha `EmpresaId`, `Colaborador` e `ConsentiuEmUtc`. Código desconhecido ou empresa sem
  assento livre deixam a máquina anônima, e a resposta (`adesao`) diz por quê; código vazio desfaz o
  vínculo; o dev.kit antigo (sem o campo) não mexe nele.
- **Isolamento num ponto só.** Toda rota do painel lê o `EscopoDoPainel` das claims
  (`Seguranca.Escopo`) e o passa às consultas, que o aplicam em `ConsultasDoPainel` — e só lá. O
  gestor vê só as máquinas da empresa que CONSENTIRAM, e delas só o uso a partir do consentimento: os
  totais diários desde o dia dele (`Maquinas.DadosDesde`), o log bruto e as ocorrências de erro desde o
  instante (`ConsentiuEmUtc`). Sem consentimento, nada da máquina vai para a empresa, nem nos totais; a
  exportação dos colaboradores corta no consentimento também para o admin. O filtro de uma máquina
  fora do escopo é 403, não uma lista vazia; um token sem escopo válido nunca vira "ver tudo".
- **Assentos.** A adesão é contada antes de gravar e conferida DEPOIS: na corrida pelo último
  assento, fica quem consentiu primeiro (e o id, no empate), e a excedente desfaz o próprio vínculo.
- **Só a própria máquina muda a adesão dela.** Registrar de novo uma máquina que já existe exige a chave
  atual (`X-Machine-Key`) ou o admin — senão 409. O código de registro vai no instalador (é público),
  e sem isto qualquer um tiraria a máquina de uma empresa ou a poria em outra.
- **Coleta auditada.** `GET /api/dashboard/exportar?formato=csv|json` devolve o uso por colaborador e
  dia; cada exportação — e a do grupo de erro em JSON, que leva os nomes das máquinas — grava uma linha em `AcessosAosDados` (quem, quando, o quê e quantas linhas),
  expurgada com `Telemetria:RetencaoDias`. O CSV usa `;` e neutraliza a célula que começa com
  `= + - @` (o nome é texto do colaborador). A exportação e os cadastros têm limite por usuário
  (`Painel:ExportacoesPorMinuto`, padrão 10).
- **O que é só do admin** (`Seguranca.PoliticaAdmin`): as empresas, os pedidos de demonstração e a
  reação aos erros.

**Base embarcada, provider trocável.** SQLite agora, com as migrations versionadas
(`Infrastructure/Migrations`) aplicadas na subida — as novas são ADITIVAS (a dos pedidos de
demonstração só cria a tabela). `Banco:Provider=SqlServer` troca para o Azure SQL sem mudar código;
nesse provider o esquema nasce do modelo (`EnsureCreated`), e as migrations próprias do SQL Server
entram num projeto de migrations separado quando a base for para lá.

**A restrição do `EnsureCreated` (Azure SQL).** Ele só cria o esquema numa base VAZIA: numa base que
já existe, uma tabela nova do modelo NÃO é criada. Por isso cada tabela nova vem com um script
idempotente em `api/scripts/sqlserver` (`IF OBJECT_ID(...) IS NULL CREATE TABLE`), que se roda
antes de publicar a versão que a usa — a da US #283 é `PedidosDeDemonstracao.sql`, e as da US #381,
`GruposDeErro.sql` e `Empresas.sql` (este também acrescenta colunas a `Usuarios` e `Maquinas`). Os testes `Script_do_azure_sql_*` comparam cada script com o `CREATE TABLE` que o
EF gera para o SQL Server a partir do mesmo modelo (`BaseDeTeste.ColunasNoSqlServer`).

**Privacidade.** A API só recebe o que o dev.kit manda, e o dev.kit não manda caminhos, nomes de
repositório ou cliente, argumentos de comando, prompt, resposta, código, e-mail nem usuário do
Windows. A máquina é um GUID anônimo; o "apelido" é derivado dele.

**Segredos fora do repositório.** `Jwt:Segredo`, `Telemetria:CodigoDeRegistro` e
`Seed:AdminPassword` vêm de User Secrets (desenvolvimento) ou de variável de ambiente / Key Vault
(Azure). Em Production a API não sobe sem o segredo JWT.

## Testes

| Suíte | O que prova |
|---|---|
| `api/tests/DevKitPage.Core.Tests` | validação do lote (inclusive o v1 antigo, sem os campos novos) e do pedido de demonstração (obrigatórios, e-mail, limites, consentimento), qualidade com divisor zero, chave, senha, período; a máscara e o limite do trace, as transições e a regressão por versão |
| `api/tests/DevKitPage.Infrastructure.Tests` | SQLite de verdade: admin semeado uma vez, reenvio sem duplicar, eventId único, agregação e filtro, paginação, expurgo mantendo os totais, bloqueio do login, máquinas ativas e registradas; grupos de exceção (mesma assinatura = um grupo com as ocorrências e as máquinas, reenvio sem duplicar, trace mascarado na gravação, regressão, só as últimas ocorrências, expurgo mantendo o grupo); pedidos de demonstração (gravação, paginação, exclusão, expurgo além da retenção mantendo os recentes) e os scripts do Azure SQL contra o modelo |
| `api/tests/DevKitPage.Api.Tests` | WebApplicationFactory: login, 401/403, token vencido, troca obrigatória, chave inválida, JWT na ingestão, 413, registro, `/health`, Production sem segredo; lote antigo aceito, chave da máquina sem acesso aos erros, reação pelo JWT, exportação do grupo; `EmpresasApiTests`: o gestor da empresa A não lê máquinas, erros nem a exportação da B (403 pelo id), a máquina sem consentimento só nos totais, o CSV auditado e sem fórmula, o 429 da exportação em laço, a adesão recusada sem assento ou com código desconhecido, o admin vê tudo e o gestor não administra; demonstração 201/400/401/404, 429 com `Retry-After`, `X-Forwarded-For` do proxy confiável (IPs diferentes não dividem a cota, o mesmo divide) e do não confiável (ignorado), o expurgo diário |
| `web/src/**/*.test.tsx` | Vitest + Testing Library + MSW: landing (ordem das seções, uma mídia por recurso, CTA, Entrar, sem explicar o dashboard, as sete etapas, a tabela comparativa acessível e completa, Para empresas), Midia (atributos, carregamento sob demanda, reduced-motion, fallback), conteúdo, formulário (validação, envio, 400, 429, falha), pedidos no dashboard (lista, exclusão, paginação), login (inclusive o gestor), expiração, troca, KPIs (inclusive as máquinas), filtros, divisor zero; exceções não classificadas (lista vazia, trace, PUT com a versão, exportação, cópia para o Bug, o gestor sem reação); o painel por papel (Colaborador e Exportar CSV do gestor, Empresas do admin) |
| `web/e2e` | Playwright: base nova → login do admin → troca → registro da máquina → lote (e reenvio) → números no dashboard → a exceção não classificada aparece com o trace sem caminhos e é resolvida; landing → seções e vídeo do hero → comparativo e empresas pelo menu → pedido de demonstração → o admin vê e exclui no dashboard |
