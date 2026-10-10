# Contrato v1 da telemetria

O que o dev.kit (`git.kit`, `TelemetrySyncService`) envia e a API aceita. Os tipos estão em
`api/src/DevKitPage.Contracts/V1`. JSON em camelCase; datas em ISO 8601 (UTC).

**Regra de evolução:** campo novo é opcional e não quebra ninguém; mudança incompatível é `v2`, com
rota própria — há máquinas instaladas falando v1.

## 1. Registrar a máquina — `POST /api/maquinas/registrar`

```json
{ "maquinaId": "3f2b…(GUID anônimo, sem hífens)", "versaoDevKit": "1.4.0", "codigoRegistro": "…" }
```

- Aceita quem apresenta o `Telemetria:CodigoDeRegistro` configurado, **ou** um JWT de admin (sem
  troca de senha pendente). Sem nenhum dos dois: **401**.
- Resposta **200** `{ "chave": "…" }` — a chave é devolvida UMA vez; a API guarda só o hash.
- Registrar de novo a mesma máquina **gira** a chave — e só vale com a **chave atual** no cabeçalho
  `X-Machine-Key` (ou com o JWT do admin). Sem ela, **409**: o código de registro é público, e sem
  isto qualquer um giraria a chave de outra máquina ou mexeria na adesão dela (US #381). O dev.kit que
  perdeu a chave recebe o 409, gera outro id anônimo e se registra como máquina nova.
- **Adesão a uma empresa (US #381, campos opcionais):** `codigoEmpresa` e `colaborador`, que o
  dev.kit só manda depois de o colaborador aceitar o aviso de coleta. Ausentes (o dev.kit antigo), o
  vínculo fica como está; `codigoEmpresa` vazio o desfaz. A resposta ganha `empresa` (a empresa
  vinculada, ou nulo) e `adesao` (o que aconteceu com o código: vinculada, desconhecido, sem assento).

```json
{ "maquinaId": "3f2b…", "versaoDevKit": "1.5.0", "codigoRegistro": "…", "codigoEmpresa": "DK-7QH4-M2XA", "colaborador": "Ana Souza" }
```

## 2. Enviar um lote — `POST /api/telemetria/lote`

Cabeçalho obrigatório: `X-Machine-Key: <chave>`. O JWT de usuário **não** serve aqui.

```json
{
  "versao": "v1",
  "maquinaId": "3f2b…",
  "versaoDevKit": "1.4.0",
  "eventos": [
    { "eventId": "9c1e…", "tipo": "TurnoExecutado", "sessaoId": "a7d0…", "quantidade": 1,
      "valor": 42500, "detalhe": "claude", "em": "2026-10-03T13:01:07Z" }
  ]
}
```

| Resposta | Quando |
|---|---|
| **202** `{ recebidos, novos, duplicados, ignorados }` | Aceito. Reenviar o mesmo lote dá `novos: 0` e não muda nenhum total. |
| **400** | Versão diferente de `v1`, `maquinaId` diferente da máquina da chave, evento sem `eventId`, quantidade negativa, JSON inválido. |
| **401** | Sem chave, ou chave desconhecida (o dev.kit esquece a dele e se registra de novo). |
| **413** | Mais de `Telemetria:MaxEventosPorLote` eventos ou corpo acima de `MaxBytesPorLote`. O dev.kit envia 200 por lote. |

Um `tipo` que a API não conhece é **ignorado** (contado em `ignorados`), não recusado: um dev.kit mais
novo não quebra uma API mais velha.

## Os tipos de evento

| Tipo | `detalhe` | `valor` | Mede |
|---|---|---|---|
| `SessaoIniciada` | versão do dev.kit | — | sessões (tasks) |
| `TurnoExecutado` | CLI (claude, kiro, kimi, glm) | duração em ms | turnos e duração |
| `TurnoFalhou` | causa (`prompt-perdido`, `sessao-ausente`, `aborto-de-encoding`, `sem-autenticacao`, `limite-de-uso`, `nao-classificada`) | — | taxa de falha por causa |
| `FluxoExecutado` | id do fluxo | — | fluxos executados |
| `FerramentaAcionada` | nome da ferramenta | — | ferramentas (quantidade = chamadas) |
| `ComandoDelegado` | executável (sem argumentos) | — | comandos delegados |
| `ArquivoAlterado` | — | — | arquivos (quantidade = arquivos) |
| `ObjetivoAvaliado` | — | nota 0–100 | nota média dos avaliadores |
| `ObjetivoCumprido` / `ObjetivoRecusado` | — | — | objetivos aceitos × recusados |
| `ExcecaoNaoClassificada` (US #381) | a assinatura (a API a usa como recorte) | — | exceções sem causa conhecida, agrupadas por assinatura |
| `RoiCalculado` (US #387) | o id do work item (texto) | turnos do agente | quantos ROIs foram calculados; a foto do ROI vai no campo opcional `roi` |
| `EntregaAvaliada` (US #399) | o motivo, da lista fechada (`nao-atendeu-o-pedido`, `quebrou-build-ou-teste`, `fora-do-padrao`, `arquitetura`, `seguranca`, `escopo-alem-do-pedido`, `inventou-api-ou-arquivo`, `retrabalho-manual-alto`, `outro`; vazio na boa sem motivo) | 1 = boa, 0 = ruim | aprovação humana e motivos de reprovação; desde a US #417 o joinha do turno vai no campo opcional `avaliacao` |
| `EntregaPronta` (US #399) | a origem (`objetivo`, `resumo`, `turno`) | — | entregas declaradas prontas (o começo do tempo de revisão) |
| `RevisaoHumana` (US #399) | a decisão (`aprovada`, `commit`, `devolvida`) | ms desde a entrega pronta | tempo médio de revisão humana e rodadas de devolução |
| `TokensConsumidos` (US #399) | CLI (claude, glm…) | tokens de entrada + saída | consumo de tokens (só o CLI que mede envia) |
| `CustoEstimado` (US #399) | CLI | micro-dólares (custo EQUIVALENTE de API) | custo dos turnos |
| `AgenteTrocado` (US #399) | `de→para` (ex.: `claude→glm`) | — | trocas automáticas de agente no limite de uso |
| `ComandoNegado` (US #399) | a classe (`proibido`, `nao-aprovado`) | — | comandos negados ao agente (a trilha de auditoria) |

| `ImpasseDetectado` (US #405) | a origem (`mensagem-parada`, `objetivo-parado`) | minutos parado até a detecção | impasses do fluxo e o tempo parado |
| `ImpasseResolvido` (US #405) | como destravou (`nota`, `mensagem-entregue`, `objetivo-cumprido`, `dev-falou`, `agente-pediu-avaliacao`, `cancelado`, `arbitro-reagiu`) | minutos entre a detecção e o desfecho | como e em quanto tempo os impasses destravam |
| `ArbitroAgiu` (US #405) | `<acao>` ou `<acao>\|<seção da regra>` (ação: `cobrou`, `escalou-ao-dev`, `aprovou`, `corrigido`, `teto-de-rodadas`, `teto-de-custo`, `falha-de-comunicacao`, `reencaminhou-resposta`, `pediu-avaliacao`, `turno-no-dono`, `turno-no-leitor`) | a rodada (ou o nº da reação) | cobranças por regra, taxa de correção e escaladas ao dev |

### O impasse e o árbitro (US #405)

Os três tipos não levam campo novo: o recorte vai no `detalhe` e a medida no `valor`, e entram nos
totais diários como os outros. No `ArbitroAgiu`, a seção é o **título** da seção do CLAUDE.md (ex.:
`Arquivos alterados no turno`) — nunca texto de conversa —, e o painel separa a ação da seção pelo
**primeiro** `|` (a seção pode conter outros). O painel de qualidade (`GET /api/dashboard/qualidade`,
campos opcionais `impasses` e `arbitro` da `QualidadeResposta`) agrega:

- **Impasses**: detectados (e por origem), a média dos minutos parado na detecção, os destravados, a média
  dos minutos até destravar e como destravaram;
- **Árbitro**: as ações, as cobranças (`cobrou`) por seção da regra, as correções (`corrigido`), a **taxa de
  correção** = corrigidas ÷ cobranças (**nula sem cobrança** — a tela mostra um traço) e as escaladas ao dev
  (`escalou-ao-dev`).

Tudo no escopo de quem consulta (o gestor, só a empresa dele) e no período do filtro. Um dev.kit antigo não
manda os tipos, e uma API antiga os ignora (`ignorados`).

Nenhum tipo da US #399 leva campo novo: o texto livre da avaliação, o comando negado, o modelo efetivo e o
detalhamento dos tokens (cache) ficam na máquina do dev. Os indicadores desses tipos no painel são a fase 2;
os totais diários já acumulam desde a primeira versão que os envia.

### Os campos opcionais da exceção (US #381)

O `ExcecaoNaoClassificada` leva, além dos campos de todo evento, dois campos **opcionais** — o v1 não
muda de versão, porque acrescentar campo opcional não quebra ninguém:

```json
{ "eventId": "…", "tipo": "ExcecaoNaoClassificada", "sessaoId": "a7d0…", "quantidade": 1,
  "valor": null, "detalhe": "9f2c41d0a7b3e816", "em": "2026-10-07T13:01:07Z",
  "trace": "System.InvalidOperationException: Sequence contains no elements\n   at GitKit.Core.Services.X.Y() linha 42",
  "assinatura": "9f2c41d0a7b3e816" }
```

- `trace`: o tipo da exceção (ou o texto da falha do turno) e os quadros `Namespace.Tipo.Metodo` com a
  linha, **já sanitizado no dev.kit** (`TraceSanitizado`): sem caminhos, e-mails, URLs nem GUIDs. Até
  8 KB — o que passar é cortado. A API mascara de novo antes de gravar (defesa em profundidade).
- `assinatura`: a impressão digital (o tipo e os primeiros quadros do dev.kit, sem a linha) que agrupa
  as ocorrências. Ausente, a API deriva uma do trace.
- Um dev.kit antigo não manda os campos (nem o tipo), e uma API antiga ignora o tipo desconhecido.

São enviados só com o MESMO opt-in de *Configurações → Telemetria de uso*: não há chave nova.

### Os campos opcionais do ROI (US #387)

O `RoiCalculado` (o `devcli roi --id N` do dev.kit) leva, além dos campos de todo evento, o objeto
**opcional** `roi` — de novo sem mudar o v1:

```json
{ "eventId": "…", "tipo": "RoiCalculado", "sessaoId": "", "quantidade": 1, "valor": 42,
  "detalhe": "387", "em": "2026-10-07T13:01:07Z",
  "roi": { "workItem": 387, "tipo": "User Story", "estado": "Closed", "de": "2026-10-01", "ate": "2026-10-04",
           "turnosDoAgente": 42, "sessoes": 3, "horas": 31.5, "horasNoBoard": 30, "horasNoTimesheet": 31.5,
           "leadTimeDias": 3.5, "aberto": false, "pullRequests": 2, "pullRequestsMergeadas": 1 } }
```

- `detalhe` é o id do work item e `valor`, os turnos do agente: no total diário o evento conta "quantos
  ROIs foram calculados", como qualquer outro tipo.
- `de`/`ate` são datas (`AAAA-MM-DD`); as horas são decimais; `horasNoTimesheet` (sem timesheet
  configurado) e `leadTimeDias` (item ainda aberto) podem vir `null`.
- **O ROI é uma FOTO, não uma soma.** A mesma máquina pode mandar o mesmo work item várias vezes; a
  API guarda UMA foto por (máquina, work item) e só a substitui por um evento **mais recente** (pelo
  `em`). Um evento mais antigo que chegue depois conta no total diário, mas não volta a foto para
  trás; o reenvio (o mesmo `eventId`) é descartado como os demais.
- Sem `roi` (um dev.kit com defeito) ou com `workItem` inválido, o evento é aceito e contado, mas não
  cria foto. Textos (`tipo`, `estado`) são cortados em 50 caracteres e números negativos viram zero —
  o lote não é recusado por isso.
- O painel lê as fotos em `GET /api/dashboard/roi?de=&ate=&maquina=` (as calculadas no período, as mais
  recentes primeiro, com os totais), com o mesmo escopo das outras consultas: o gestor vê só as
  máquinas da empresa que consentiram, a partir do consentimento.
- Um dev.kit antigo não manda o tipo nem o campo, e uma API antiga ignora o tipo desconhecido.

### O campo opcional da avaliação de entrega (US #417)

O `EntregaAvaliada` (o joinha da janela da task) leva, além dos campos de todo evento, o objeto
**opcional** `avaliacao` — de novo sem mudar o v1:

```json
{ "eventId": "…", "tipo": "EntregaAvaliada", "sessaoId": "a7d0…", "quantidade": 1, "valor": 0,
  "detalhe": "quebrou-build-ou-teste", "em": "2026-10-10T13:01:07Z",
  "avaliacao": { "boa": false, "motivo": "quebrou-build-ou-teste", "agente": "claude", "modelo": "opus-5",
                 "fluxo": "revisao", "workItem": 417, "turno": 3 } }
```

- `turno` é o número do turno do agente avaliado na sessão; `workItem` pode vir `null`; `agente`, `modelo`
  e `fluxo` podem vir vazios (a task sem fluxo).
- **Uma avaliação por (máquina, `sessaoId`, `turno`).** Avaliar de novo o mesmo turno SUBSTITUI a anterior
  — vale a mais recente pelo `em` —, nunca soma. Um evento mais antigo que chegue depois conta no total
  diário, mas não volta a avaliação para trás; o reenvio (o mesmo `eventId`) é descartado como os demais.
- Sem `avaliacao` (o dev.kit anterior), sem `sessaoId` ou com `turno` negativo, o evento é aceito e contado,
  mas não entra no feedback. Textos são cortados no tamanho das colunas.
- A leitura (para a ferramenta de análise do dev.kit, que nunca lê o banco) fica em
  `GET /api/dashboard/feedback?de=&ate=&maquina=&pagina=&tamanho=` (as avaliações, uma `Pagina<FeedbackV1>`:
  `itens`, `total`, `numeroDaPagina`, `tamanho`) e `GET /api/dashboard/feedback/metricas?de=&ate=&maquina=`
  (`MetricasDeFeedbackV1`: total, positivos e negativos, `porFluxo`, `porAgente` e `porDia`). O fluxo e o
  agente vazios agregam sob `(sem fluxo)` e `(sem agente)`. Mesmo escopo e mesma autorização das outras
  consultas do painel; `de` depois de `ate` é **400** (ProblemDetails), e a base vazia devolve tudo zerado.

## O que NÃO é coletado

Caminhos de pasta, nomes de repositório ou de cliente, argumentos de comando, o texto do prompt e
das respostas, código, e-mail e o usuário do Windows. O trace da exceção não classificada vai sem
caminhos, e-mails, URLs nem GUIDs — só o tipo, a mensagem mascarada e os quadros com a linha. A máquina é um GUID gerado no dev.kit, nunca o
hostname. A sessão é o id interno da task no dev.kit.

## No dev.kit

- **Ligado por padrão** (opt-out): a URL da API e o código de registro vêm preenchidos no build do
  dev.kit, e o usuário desliga em *Configurações → Telemetria de uso*. Só o que for registrado com o
  envio ligado é enviado.
- O código de registro embutido no instalador é, na prática, **público** (dá para extraí-lo do
  binário). Ele separa as máquinas do dev.kit de chamadas anônimas, mas não autentica ninguém. Se
  vazar ou for abusado, troque o `Telemetria__CodigoDeRegistro` na API e publique um dev.kit novo:
  as máquinas já registradas continuam enviando com a chave delas.
- **Fila local**: a própria tabela de telemetria; o que não foi aceito espera o próximo envio.
- **Espera exponencial** depois de falha (1, 2, 4… minutos, até 6 h); uma falha nunca afeta o turno.
- **Enviar agora**: o botão das Configurações ou `devcli telemetria --enviar`.
