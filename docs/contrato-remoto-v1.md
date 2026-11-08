# Contrato v1 do controle remoto

O que o dev.kit publica e o site lê na fase 1 do controle remoto (projeto `controle-remoto-v2`): o **heartbeat** de cada
máquina e o **retrato** sanitizado dos projetos, fluxos e pendências — e, na fase 2, a **conversa e os comandos remotos**
(seção 4), no mesmo contrato 1. Os tipos estão em
`api/src/DevKitPage.Contracts/V1/RemotoV1.cs`; as regras, em `api/src/DevKitPage.Core/ServicosDoRemoto.cs`. JSON em
camelCase; datas em ISO 8601 (UTC).

**Regra de evolução:** campo novo é opcional e não muda a versão (a API ignora o campo que não conhece, e um dev.kit
antigo não o manda). Mudança incompatível é o **contrato 2**: o envio leva `contrato`, e a API recusa o que não fala com
**400** `codigo: contrato-desconhecido` e `contratoAceito: 1` — o dev.kit sabe que tem de se atualizar, em vez de gravar
um dado que a API leria errado.

## Autenticação e isolamento

- Todas as rotas exigem o **JWT do usuário** do dev.kit.page (o mesmo do login do dev.kit), sem troca de senha pendente.
  Sem token, **401**. A chave de máquina (`X-Machine-Key`) da telemetria **não** serve aqui.
- O dono é o usuário do token. A máquina é única por (usuário, `maquinaId`): o mesmo id anônimo enviado por outro
  usuário é outra máquina, e nenhum lê ou grava a do outro. A máquina de outro usuário é **404**, sem revelar que existe.
  Nem o admin lê a máquina alheia.
- Limite por usuário: `Remoto:EnviosPorMinuto` (padrão 60) envios por minuto, heartbeat e retrato somados; acima, **429**.
- **Token da máquina (client credentials, remote-control-v3 item 9).** Os envios do serviço — heartbeat, retrato, catálogo,
  SAS da fila, recebimento, respostas e falas — aceitam também o token que a máquina obtém sozinha, sem o usuário:
  1. No login do app, o dev.kit vincula a máquina: `POST /api/auth/cliente-da-maquina` com o JWT, a `X-Machine-Key` e
     `{ "chavePublica": "<SPKI ECDSA P-256 em base64>" }`. A resposta (`ClienteDaMaquinaRegistradoV1`) é o **anúncio**:
     `clientId` (o `maquinaId`), `escopo` (`remoto`), `rotaDoToken`, `audiencia`, `algoritmo` (`ES256`) e
     `validadeMaximaDaAssercaoSegundos`. Com **404** (a API antiga, ou `ClienteDaMaquina:Habilitado=false`), o dev.kit segue
     na renovação pela máquina (`sessao-da-maquina` / `renovar-pela-maquina`), que continua aceita.
  2. O serviço pede o token: `POST /api/auth/token` (`application/x-www-form-urlencoded`) com
     `grant_type=client_credentials`, `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` e
     `client_assertion` — um JWT ES256 assinado pela chave privada (que fica no DPAPI do usuário na máquina), com
     `iss` = `sub` = `clientId`, `aud` = `audiencia`, `iat`, `exp` (no máximo 300 s depois do `iat`) e um `jti` novo a cada
     pedido. A resposta é a da RFC 6749 (`access_token`, `token_type`, `expires_in`, `scope`); a recusa,
     `{ "error", "error_description" }`: **401** `invalid_client` (asserção vencida, repetida, longa, de outra chave, ou a
     máquina desvinculada), **400** `invalid_grant` (senha trocada, usuário bloqueado ou sem assento),
     `unsupported_grant_type` ou `invalid_request`.
  3. O token vale `ClienteDaMaquina:ValidadeDoTokenMinutos` (15) e **só** nestas rotas, e só para o `maquinaId` dele
     (**403** para outro). Em qualquer outra rota, **401**. Sem renovação: vencido, o serviço pede outro com uma asserção nova.
- **Revogação.** O "Desvincular" do Board (`DELETE /api/remoto/maquinas/{maquinaId}/vinculo`, só com o JWT do usuário:
  **204**, ou **404** sem vínculo dele) derruba o token já emitido na chamada seguinte — e a máquina desvinculada também não
  renova pela máquina (`sessao-da-maquina` e `renovar-pela-maquina` respondem **401**) até o próximo login no app, que a
  vincula de novo. A troca de senha e o bloqueio do
  usuário revogam todas as máquinas dele. A lista `GET /maquinas` diz `vinculada` em cada máquina.

## 1. Heartbeat — `POST /api/remoto/heartbeat`

O serviço do Windows (`GitKit.Scheduler`) envia a cada `IntervaloDoHeartbeatSegundos` (60 s), também com o app fechado.

```json
{ "contrato": 1, "maquinaId": "3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d", "nome": "Notebook do trabalho",
  "versaoDevKit": "75", "origem": "servico", "appAberto": true, "estadoDoAgente": "ativo",
  "turnoEmAndamento": false, "enviadoEm": "2026-11-03T12:00:00Z" }
```

- `maquinaId`: o id anônimo do dev.kit (o da telemetria) — letras, dígitos e hífen, até 64. Nunca o hostname.
- `origem`: `servico` ou `app`. `estadoDoAgente`: `ativo`, `pausado`, `limite` ou `parado` (vazio é `parado`).
- `turnoEmAndamento`: o site avisa antes de reiniciar o dev.kit, porque reiniciar interrompe o turno (o "reinício seguro"
  da proposta; no plano do projeto, o item 8, "Serviço liga e reinicia o dev.kit").
- Resposta **200** `{ "maquinaId", "recebidoEm", "intervaloSegundos": 60, "recursos": ["falas-do-desktop"] }`.
- `recursos` é o **sinal de suporte**: o que esta API aceita além da fase 1 (`RemotoV1.RecursosDaApi`; o novo entra no fim). O
  dev.kit lê a lista ANTES de usar a rota de um recurso e, sem ele — a API antiga não manda `recursos` —, não envia: hoje,
  `falas-do-desktop` libera `POST /api/remoto/conversa/falas` (seção 4.2).
- A máquina aparece `conectada` enquanto o último heartbeat tem até `SegundosAteDesconectar` (180 s) pelo relógio da API.

## 2. Retrato — `PUT /api/remoto/retrato`

Uma FOTO inteira do estado, enviada quando ele muda. Substitui a anterior; a mais velha que chegar depois (pelo
`geradoEm`) é aceita com `aplicado: false` e descartada.

```json
{
  "contrato": 1, "maquinaId": "3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d", "geradoEm": "2026-11-03T12:00:00Z",
  "projetos": [
    { "slug": "controle-remoto-v2", "nome": "controle-remoto v2", "situacao": "aprovado", "ondaAtual": 1,
      "itens": [ { "numero": 1, "titulo": "API recebe heartbeat e retrato", "situacao": "rodando", "onda": 1 } ] }
  ],
  "fluxos": [
    { "id": "exec-812", "nome": "Fluxo 6", "etapa": "Implementação", "situacao": "executando",
      "projeto": "controle-remoto-v2", "item": 1, "agente": "claude", "desdeEm": "2026-11-03T11:40:00Z" }
  ],
  "pendencias": [
    { "id": "pergunta-77", "tipo": "pergunta", "titulo": "Qual fila usar?", "pergunta": "Storage Queue ou Service Bus?",
      "opcoes": ["Storage Queue", "Service Bus"], "jaTentei": "Li a proposta.", "projeto": "controle-remoto-v2",
      "item": 4, "fluxo": "exec-813", "criadaEm": "2026-11-03T11:55:00Z" }
  ]
}
```

- Situação do projeto e do item: as de `ProjetosV1` (`rascunho`, `aguardando`, `aprovado`, `pausado`, `fechando`,
  `concluido`; `planejado`, `rodando`, `pergunta`, `integrado`, `falhou`).
- Pendência: `tipo` é `pergunta`, `aprovacao` ou `falha`. `pergunta`, `opcoes` e `jaTentei` são os campos do
  `[GITKIT:PERGUNTA]` — as decisões remotas (fase 3 da proposta, item 9 do plano) os mostram como botões. O `id` é estável: é por ele que a resposta volta.
- Tetos: 256 KB de corpo; 50 projetos de até 100 itens, 100 fluxos, 100 pendências de até 10 opções; nomes e títulos
  até 200 caracteres, `pergunta` e `jaTentei` até 2000.
- Resposta **200** `{ "maquinaId", "recebidoEm", "aplicado" }`.

## Recusas dos envios

| Resposta | Quando |
|---|---|
| **400** | JSON inválido ou vazio, forma fora do contrato (com a mensagem do campo), ou `contrato` diferente de 1 (`codigo: contrato-desconhecido`, `contratoAceito`). |
| **401** | Sem JWT, ou JWT vencido. |
| **413** | Corpo acima do teto (4 KB o heartbeat, 256 KB o retrato), com ou sem `Content-Length`. |
| **415** | O corpo não é `application/json`, ou o `charset` é desconhecido. |
| **422** | Token ou dado de cliente em algum texto: `codigo: dado-sensivel` e `campos: [{ "campo": "pendencias[0].titulo", "tipo": "cnpj" }]` — o valor nunca volta. |
| **429** | Acima do limite por usuário. |

O 415 é só do formato do corpo: a leitura (`CorpoJson`) não captura exceção genérica, então a recusa do conteúdo (400 e
422) nunca vira 415, e uma falha interna é 500 com o código do log.

### O que conta como dado sensível

Todo texto do envio é conferido, inclusive ids e slugs. Tipos: `segredo`, `email`, `cpf`, `cnpj`, `telefone`.

- **Segredo:** os mesmos padrões da máscara dos relatos (token do GitHub e do Azure DevOps, `Bearer`, JWT, `senha=`,
  connection string, chave privada).
- **CPF e CNPJ:** só com os dígitos verificadores certos. O CNPJ numérico vale formatado ou com os 14 dígitos; o
  alfanumérico (de 2026), só formatado (`AB.CDE.FGH/IJKL-DV`). O carimbo `20261102-095333` tem 14 dígitos, mas o hífen fora
  da posição do DV o tira do padrão: **não** é CNPJ.
- **Telefone:** com `+55`, com o DDD entre parênteses, ou com o DDD solto e um separador (o celular de 11 dígitos também
  colado); o DDD tem de existir, e o assinante segue a Anatel (celular com 9 na frente, fixo de 2 a 5). O celular sem DDD
  conta com o separador (`98765-4321`). **Não** é telefone: o número colado por hífen a uma palavra — o slug
  `sprint-11-2026-2027` —, a faixa de anos (`11 2026-2027`) e o fixo sem DDD, indistinguível de uma faixa de anos.

## 3. Leitura do site

- `GET /api/remoto/maquinas` → `MaquinaRemotaV1[]`: `maquinaId`, `nome`, `versaoDevKit`, `origem`, `appAberto`,
  `estadoDoAgente`, `turnoEmAndamento`, `conectada`, `ultimoHeartbeatEm`, `retratoGeradoEm`. As conectadas primeiro.
- `GET /api/remoto/maquinas/{maquinaId}/retrato` → `RetratoDaMaquinaV1`: `maquina`, `retrato` (o último aplicado) e
  `recebidoEm`. **404** para a máquina sem retrato ou de outro usuário.

## 4. Conversa e comandos remotos (fase 2)

O site manda um **comando** a UMA máquina do usuário; a API grava no SQL e põe na **fila do usuário** (a Storage Queue
criada pela provisão, item 4 do plano) com id, destino e TTL. O serviço da máquina lê a fila com uma **SAS curta** pedida à
API, confirma o recebimento e o dev.kit manda as **respostas** por HTTPS; o site as recebe pelo **SSE**. A fila é só
transporte (a mensagem lida some): o histórico fica no SQL. Tipos em `api/src/DevKitPage.Contracts/V1/ConversaV1.cs`;
regras em `api/src/DevKitPage.Core/ServicosDaConversa.cs`. Todas as rotas exigem o JWT do usuário, e o comando, a máquina
e a fila de outro usuário são **404**.

### 4.1 Do site

**`POST /api/remoto/comandos`**

```json
{ "id": "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d", "maquinaId": "3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d", "tipo": "mensagem",
  "texto": "Como está o projeto controle-remoto-v2?", "ttlSegundos": 900, "dispositivo": "celular" }
```

- `tipo`: `mensagem` (ao Orquestrador; `texto` obrigatório, até 4000), `ligar` ou `reiniciar` (para o serviço; sem texto)
  e `catalogo` (o handshake do chat, seção 5; sem texto).
- `id`: escolhido pelo site (um GUID). O reenvio com o MESMO id e o mesmo conteúdo devolve o comando que já existe (**200**),
  sem outra mensagem na fila e sem contar no limite — o duplo toque no celular não duplica. O mesmo id com outro conteúdo é
  **409** `id-em-uso`. Sem id, a API sorteia.
- `ttlSegundos`: de `Remoto:TtlMinimoSegundos` (60) a `TtlMaximoSegundos` (86400); vazio é `TtlPadraoSegundos` (900).
- `reiniciar` com `turnoEmAndamento` no último heartbeat exige `"confirmarReinicio": true` — sem ela, **409**
  `turno-em-andamento` (o reinício seguro).
- Resposta **202** com `Location: /api/remoto/comandos/{id}` e o `ComandoRemotoV1`:
  `id`, `maquinaId`, `tipo`, `texto`, `dispositivo`, `estado`, `criadoEm`, `expiraEm`, `entregueEm`, `concluidoEm`, `erro`.

| Estado | Quando |
|---|---|
| `pendente` | Gravado no SQL, entrando na fila. |
| `na-fila` | Na fila do usuário, esperando o serviço da máquina. |
| `entregue` | O dev.kit confirmou o recebimento. |
| `respondido` · `concluido` · `recusado` | O dev.kit respondeu, executou (o ligar) ou recusou (o chat não aprova nada). |
| `expirado` | O TTL venceu ANTES do recebimento (a máquina desligada): volta assim, e o dev.kit não o executa. |
| `falhou` | A fila não aceitou (**503** `fila-indisponivel`, com o `comando` no corpo); o reenvio com o mesmo id tenta de novo. |

**`GET /api/remoto/comandos/{id}`** → o comando. **`GET /api/remoto/maquinas/{maquinaId}/conversa`** →
`{ "comandos": [...], "respostas": [...], "ultimoEvento": 42, "falas": [...] }`, os mais antigos primeiro (até 200 de cada).
As `falas` (campo novo; vazias com o dev.kit antigo) são a conversa do chat do dev.kit — `FalaRemotaV1`: `id`, `maquinaId`,
`autor`, `origem`, `texto`, `comandoId`, `ditaEm`, `recebidaEm` —, na ordem em que foram **ditas** (`ditaEm`): a que chega
atrasada entra no lugar dela. O chat do site mostra só a **conversa** (remote-control-v3, item 3): a fala do Orquestrador com
id `fl-…` é a resposta de um turno de **lote** (avisos, retratos e gatilhos da varredura, marcada na origem pelo dev.kit) e
não aparece; a fala espontânea do Orquestrador repetida com o mesmo texto, sem fala do dev nem comando no meio (o aviso
empilhado), aparece uma vez, a mais recente — a que repete depois de outra pergunta é conversa e fica; e as decisões ficam
só no painel de decisões, nunca no chat.

**`GET /api/remoto/conversa/eventos[?maquinaId=...]`** → `text/event-stream`. Cada evento:

```
id: 43
event: comando
data: {"id":43,"tipo":"comando","maquinaId":"3f2b...","comando":{"id":"9b1d...","estado":"entregue",...},"resposta":null,"em":"..."}
```

- `event` é `comando` (o comando mudou de estado), `resposta` (chegou uma resposta) ou `fala` (chegou uma fala do chat do
  dev.kit, no campo novo `fala`, com `comando` e `resposta` nulos); `ping`, sem `id` e com `data: null`, mantém a conexão viva.
  O site que não conhece um `event` o ignora e só avança o cursor.
- O site abre o SSE com `Last-Event-ID: <ultimoEvento>` do histórico (ou `?desde=`) e não perde nada entre as duas chamadas;
  a reconexão retoma do último `id` recebido. O fluxo fecha sozinho depois de `Remoto:DuracaoDoSseSegundos` (300).
- O JWT vai no cabeçalho `Authorization`: o site usa `fetch` com o corpo em fluxo (o `EventSource` do navegador não manda
  cabeçalho).
- Mais de uma instância da API: o evento gravado por outra chega na próxima releitura do SQL
  (`Remoto:IntervaloDoSseSegundos`, 3 s); o da mesma instância, na hora.

### 4.2 Do dev.kit

**`POST /api/remoto/fila/sas`** `{ "contrato": 1, "maquinaId": "..." }` → **200**
`{ "endpoint": "https://dkq...queue.core.windows.net/comandos", "sas": "sv=...&st=...&se=...&sp=p&spr=https&sig=...", "permissoes": "p", "expiraEm": "..." }`.

- A SAS é de SERVIÇO, só da fila do usuário, só HTTPS e só `p` (process: ler e apagar mensagens — não espia, não escreve).
  Vale `Remoto:SasMinutos` (15): o serviço pede outra antes de `expiraEm`.
- A máquina precisa ter mandado o heartbeat (**404** sem ele); sem a fila pronta, **409** `sem-fila`.
- A mensagem da fila (`MessageText`) é o JSON do `MensagemDaFilaV1` em **Base64** (UTF-8):
  `{ "contrato": 1, "id", "maquinaId", "tipo", "texto", "origem": "remoto", "dispositivo", "criadoEm", "expiraEm", "confirmarReinicio" }`.
  A fila descarta a mensagem no fim do TTL, lida ou não.
- `confirmarReinicio` (campo novo, item 8): `true` só no `reiniciar` que o usuário confirmou com o turno em andamento. O
  serviço confere o turno DE NOVO na hora de executar — o heartbeat que o site leu pode ser de antes do turno — e, sem a
  confirmação, recusa (`recusado`, com o motivo) em vez de interromper. O dev.kit antigo o ignora.
- `ligar` e `reiniciar` quem executa é o SERVIÇO da máquina (o `GitKit.Scheduler`, item 8): ele abre o dev.kit SEM JANELA (o
  serviço não abre janela na área de trabalho), que hospeda o Orquestrador e restaura as sessões. A resposta é `concluido`
  (ligado ou reiniciado; o ligar com o dev.kit já aberto também) ou `recusado` com o motivo (o caminho do dev.kit ausente, a
  licença vencida, o dev.kit que não fechou nem à força, o turno sem confirmação).

**`POST /api/remoto/comandos/{id}/recebido`** `{ "contrato": 1, "maquinaId": "..." }` → **200**
`{ "id", "estado", "executar", "repetido" }`.

- A fila pode entregar de novo e fora de ordem. O serviço confirma ANTES de executar e só executa com `executar: true` — o
  PRIMEIRO recebimento dentro do TTL. O repetido (`repetido: true`, também o que chega ao mesmo tempo) e o vencido
  (`estado: expirado`) voltam com `executar: false`: apague a mensagem e não execute.
- O comando de outra máquina do usuário (a fila é uma por usuário) é **409** `outra-maquina`: não o execute nem o apague — ele
  volta visível para a máquina certa.

**`POST /api/remoto/respostas`**

```json
{ "contrato": 1, "maquinaId": "...", "id": "r-0001", "comandoId": "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d", "tipo": "mensagem",
  "texto": "O item 5 está em implementação.", "geradaEm": "2026-11-03T12:01:00Z" }
```

- `tipo`: `mensagem` (o comando vai a `respondido`; sem `comandoId` é a mensagem espontânea do Orquestrador), `concluido` ou
  `recusado` (estes dizem o `comandoId`).
- **201** a nova; **200** com `repetida: true` o reenvio do mesmo `id` (idempotente). O `texto` passa pelas mesmas regras do
  dado sensível (**422**): o dev.kit manda já sanitizado (`TraceSanitizado`).

**`POST /api/remoto/conversa/falas`** — só com `falas-do-desktop` nos `recursos` do heartbeat (seção 1)

```json
{ "contrato": 1, "maquinaId": "...", "id": "f-0001", "autor": "dev", "origem": "desktop",
  "texto": "Como está o projeto controle-remoto-v2?", "ditaEm": "2026-11-03T12:00:30Z" }
```

O espelho do chat do Orquestrador no dev.kit, para o chat do Board mostrar a MESMA conversa.

- `autor`: `orquestrador` ou `dev` — quem falou. `origem`: `desktop` (escrita no dev.kit) ou `remoto` (veio do site por um
  comando; diz o `comandoId`, para o site juntar a fala ao comando e não mostrá-la duas vezes). Sem diferença de caixa.
- `texto` obrigatório, até 4000 (o dev.kit corta a fala maior); `ditaEm` é o relógio da máquina e dá a ordem do histórico.
- **201** a nova; **200** com `repetida: true` o reenvio do mesmo `id` com o mesmo conteúdo (idempotente: o 429 e a queda não
  perdem a fala, o dev.kit reenvia com o mesmo `id`); o mesmo `id` com outro `autor`, `origem`, `texto` ou `comandoId` é **409**
  `id-em-uso`. O `id` é único por máquina.
- A fala não é resposta: não muda o estado de comando nenhum. A resposta a um comando do site continua em `/respostas`.
- **422** `dado-sensivel` com o mesmo critério da resposta; **404** a máquina sem heartbeat, de outro usuário, ou o `comandoId`
  que não existe; **409** `outra-maquina` o `comandoId` de outra máquina do usuário.
- O dev.kit antigo não conhece a rota e não manda nada: o histórico e o SSE seguem como antes, com `falas` vazias.

### 4.3 Recusas e limites

As da seção "Recusas dos envios" (400, 401, 413 acima de 32 KB, 415, 422) e:

| Resposta | Quando |
|---|---|
| **404** | O comando, a máquina ou a fila não é do usuário (ou não existe). |
| **409** | `sem-fila`, `turno-em-andamento`, `id-em-uso`, `outra-maquina`. |
| **429** | `limite-de-envio`: mais de `Remoto:ComandosPorHora` (30) comandos na última hora, contados no SQL (vale entre instâncias), com `Retry-After`. Cada mensagem pode abrir um turno do agente. As escritas da conversa também têm `Remoto:EnviosDaConversaPorMinuto` (60), separado do heartbeat. As falas têm o seu, `Remoto:FalasPorMinuto` (60), com `Retry-After`: o espelho do chat não tira a vez do comando nem da resposta. |
| **503** | `fila-indisponivel`: a fila não aceitou, ou a SAS não pôde ser assinada. |

## 5. Catálogo do chat — o handshake (remote-control-v3)

Os atalhos do chat do Board (`/` fluxo, projeto ou design · `#` work item · `@` repositório) mostram o que o dev.kit da
máquina tem. O site pede esse catálogo por um **comando** da fila e o dev.kit o publica; a API guarda o último por máquina,
com a data, e o site o mostra também com o dev.kit fechado. Tipos em `api/src/DevKitPage.Contracts/V1/CatalogoRemotoV1.cs`;
regras em `api/src/DevKitPage.Core/CatalogoDoRemoto.cs`.

- **Sinal de suporte:** a API anuncia `catalogo-remoto` nos `recursos` do heartbeat (seção 1). O dev.kit só publica com ele.
- **O pedido (handshake):** `POST /api/remoto/comandos` com `"tipo": "catalogo"` e sem texto — o site manda ao conectar
  (com o dev.kit aberto e o catálogo ausente ou velho) e no botão **Atualizar**. Ele **não conta** no
  `Remoto:ComandosPorHora` (não abre turno do agente) e não empilha: enquanto um pedido da mesma máquina está `pendente` ou
  `na-fila`, o novo devolve esse mesmo (**200**). O dev.kit confirma o recebimento como qualquer comando (seção 4.2); o
  serviço não avisa nada na conversa quando o app está fechado — o pedido espera o app abrir.
- **A resposta:** o app monta o catálogo, sanitizado, e faz `PUT /api/remoto/catalogo` com o `comandoId` do pedido; depois
  fecha o comando com a resposta `concluido` (ou `recusado`, com o motivo). O site não mostra essas respostas no chat: o
  `concluido` só o faz reler o catálogo.

```json
{ "contrato": 1, "maquinaId": "3f2b9c0e5d7a", "geradoEm": "2026-11-06T20:00:00Z", "comandoId": "9b1deb4d3b7d",
  "workItems": [ { "id": 812, "titulo": "Handshake do chat", "tipo": "User Story", "estado": "Active" } ],
  "repositorios": [ { "nome": "github.com/empresa/dev.kit.page", "branchPadrao": "main" } ],
  "fluxos": [ { "nome": "Tarefa avulsa", "descricao": "Implementação avaliada" } ],
  "projetos": [ { "slug": "remote-control-v3", "nome": "remote-control-v3", "situacao": "aprovado" } ],
  "designs": [ { "nome": "dev.kit.page · board", "telas": 3, "repositorio": "github.com/empresa/dev.kit.page" } ] }
```

- Resposta **200** `{ "maquinaId", "recebidoEm", "aplicado" }`; como no retrato, o catálogo gerado antes do gravado é aceito
  com `aplicado: false`. **404** sem o heartbeat da máquina (o catálogo não cria máquina) ou de outro usuário.
- Tetos: 256 KB de corpo; até 200 entradas em cada lista; nomes e títulos até 200 caracteres, a descrição do fluxo até
  2000. As recusas são as dos envios (400, 413, 415, 422 com os campos, como `workItems[0].titulo`, 429).
- **`GET /api/remoto/maquinas/{maquinaId}/catalogo`** → `{ "maquinaId", "recebidoEm", "catalogo" }`: o último recebido.
  **404** sem catálogo ou de outro usuário.
- **Compatibilidade:** o dev.kit antigo não conhece o tipo `catalogo` e o recusa (`recusado`, "não é conhecido por esta
  versão"); o site guarda o último catálogo que tiver e deixa o texto livre nos atalhos. A API antiga recusa o tipo com
  **400** e não anuncia o recurso: o dev.kit novo não publica nela.
- O `repositorio` do design (campo novo, opcional) é o mesmo nome dos `repositorios`: o site abre por ele o projeto de design
  compartilhado de mesma chave. O dev.kit antigo não o manda.

### 5.1 Criar projeto e projeto de design pelo site (remote-control-v3, item 4)

O Board do site cria um projeto do Orquestrador e um projeto de design na máquina por dois comandos da mesma fila. O
**formulário vai em JSON no `texto`**, conferido pela API antes da fila (400 com o campo); o dev.kit o executa pelo mesmo
caminho do `devcli` e responde `concluido` (com o slug ou o projeto de design) ou `recusado` (com o motivo). Tipos em
`api/src/DevKitPage.Contracts/V1/CriacaoRemotaV1.cs`; regras em `api/src/DevKitPage.Core/CriacaoRemota.cs`.

| Tipo | Formulário (`texto`) | No dev.kit |
|---|---|---|
| `projeto` | `{ "nome", "repositorios": [nomes do catálogo], "definicao"?, "nivel"? }` | `devcli projeto --criar`: nasce em **rascunho**; o plano, a aprovação e o disparo seguem no dev.kit |
| `design` | `{ "repositorio": nome do catálogo }` | `devcli design --repo`: abre (ou cria) o projeto do repositório, sem gravar nem enviar versão |

- Os repositórios são **nomes do catálogo** (`repositorios[].nome`): o dev.kit só aceita os que conhece e recusa o resto.
- Tetos: nome e repositório até 200 caracteres; até 20 repositórios; definição até 3000; `nivel` é `usuario`, `time` ou
  `empresa` (vazio é o padrão do `devcli`). Contam no `Remoto:ComandosPorHora`, como a mensagem.
- Depois de criar, o dev.kit republica o catálogo com o `comandoId` da criação: o site relê a lista sem outro handshake.
- **O site não aprova nem dispara projeto**: não há comando para isso.
- **Teto de versões do design:** a criação não grava versão, então não consome o teto do plano (`GET /api/design/uso`); o
  formulário do site mostra o uso e avisa quando o teto está cheio, porque as versões seguintes do projeto serão recusadas
  no envio (413).
- **Compatibilidade:** o dev.kit antigo não conhece `projeto` nem `design` e os recusa (`recusado`, "não é conhecido por esta
  versão do dev.kit"); a API antiga recusa o tipo com **400**. O site mostra as duas recusas no formulário.

### 5.2 Correções do board remoto (07/11/2026)

Campos novos, **todos opcionais**: o dev.kit e o site antigos seguem como antes (regra de impacto #2).

| Onde | Campo | O que é |
|---|---|---|
| `ComandoNovoV1` e `MensagemDaFilaV1` | `fluxo` (id), `agente` (`cli` ou `cli:modelo`), `parametros` (`{ NOME: valor }`) | O fluxo escolhido no `/` do chat, com o chip de modelo e os de parâmetro. Só na `mensagem`; `agente` e `parametros` só com `fluxo`. O texto continua com o contexto escrito (`Modelo: …`, `Parâmetro NOME: …`). O dev.kit monta `devcli processo-iniciar --fluxo <id> --param NOME=valor --agente cli:modelo`, validado pelo próprio `processo-iniciar` (simulação). A mensagem sem fluxo sai sem esses campos. |
| `CatalogoRemotoV1.fluxos[]` | `id`, `parametros` (`nome`, `rotulo`, `tipo` = `texto`/`lista`/`sim-nao`, `opcoes`, `usuario`, `obrigatorio`, `padrao`), `agentePadrao` | O que os chips do fluxo pedem. |
| `CatalogoRemotoV1` | `agentes`, `membros` | Os `cli:modelo` da máquina e os membros do time (o parâmetro de usuário). Até 200 cada. |
| `DesignNoCatalogoV1` | `miniatura` | O link da última versão enviada (`/api/design/...`): a miniatura do card de Design do painel. |
| `FalaDoDevKitV1` / `FalaRemotaV1` | `anexos` (`titulo`, `url`?, `aviso`?) | As imagens da fala: o dev.kit traduz o `[GITKIT:DESIGN]`/`[GITKIT:IMAGEM]` no link estável da versão ANTES de sanitizar. Sem link (o teto do plano, o envio pendente), vai o `aviso`. Até 10. O site mostra a miniatura e abre o modal. |
| `DecisaoPendenteV1` | `dispensada` | O card fechado no site. |

- **Dispensar** (`POST .../decisoes/{pendencia}` com `{ "acao": "dispensar" }` ou `"restaurar"`): só no site, por usuário,
  máquina e pendência; não vai à máquina e não decide nada. `GET .../decisoes` deixa as dispensadas de fora (o push também);
  `?dispensadas=true` as traz marcadas. A dispensa de uma pendência que saiu do retrato é apagada na próxima dispensa.
- **Projetos (ajuste 8):** o dev.kit publica `fechando` quando todos os itens do projeto estão integrados (a situação derivada);
  `concluido` continua exigindo o fechamento confirmado. O painel do Board lista só os em andamento, e o "Mostrar concluídos"
  traz os `fechando` e os `concluido`.
- **Execuções (ajuste 7):** a sessão de gatilho (avaliador, fechamento) ociosa — sem turno e sem pendência real — não entra em
  `fluxos`; em turno, entra agrupada sob a task que a disparou. O Fechamento que integrou com sucesso conclui a própria task, os
  avaliadores da mesma dona e a dona (nada é apagado).
- **Autorização (ajuste 9):** com o recurso `decisoes-autorizar` no heartbeat, o dev.kit publica a pendência `tipo: "autorizacao"`
  com o `escopo` exato (`ato` = `concluir-projeto`, `integrar-item`, `arquivar-tasks` ou `executar-comando`; `alvo`, `sha`?,
  `itens`?, `comando`?). O site responde `acao: "autorizar"` com o código TOTP — o mesmo segundo fator e a mesma auditoria do
  `aprovar` —, ou `ajuste` para recusar. O `escopo` entra no id do snapshot: o escopo alterado depois da revisão é 409. O texto do
  chat continua sem aprovar nada. Nesta versão o dev.kit oferece e executa `executar-comando` (o comando bloqueado, uma vez) e
  `concluir-projeto` (em fechamento, sem o fechamento confirmado); `integrar-item` segue pelo `aprovar` do snapshot.
- **Azure SQL:** a tabela `PendenciasDispensadas` e as colunas `ComandosRemotos.Contexto` e `FalasRemotas.Anexos` vêm de
  `api/scripts/sqlserver/BoardRemoto.sql` (idempotente). Sem configuração nova.

## Retenção

A máquina sem heartbeat nem retrato há mais de `Remoto:RetencaoDias` (padrão 30) sai do banco com o retrato, no
expurgo diário; o catálogo sai pelo mesmo prazo, contado do último recebimento. O retrato guardado é só o último: não há histórico. A conversa (comandos, respostas, falas e eventos) sai
depois de `Remoto:RetencaoDaConversaDias` (padrão 30), pela data de cada linha.

## No Azure SQL

A tabela `MaquinasRemotas` vem de `api/scripts/sqlserver/Remoto.sql`, e as da conversa (`ComandosRemotos`,
`RespostasRemotas`, `EventosDaConversa`, `FalasRemotas`), de `api/scripts/sqlserver/Conversa.sql` — idempotentes; rode-os antes
de publicar a API. Numa base que já tem a conversa do item 5, rodar de novo o `Conversa.sql` só cria `FalasRemotas`. O
catálogo (seção 5) vem de `api/scripts/sqlserver/CatalogoRemoto.sql` (`CatalogosRemotos`), também idempotente.
