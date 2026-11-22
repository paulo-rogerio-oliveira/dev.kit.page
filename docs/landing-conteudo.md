# A landing de venda do dev.kit — mensagem e roteiro das mídias (US #283)

O que a página precisa responder ao visitante (dev, tech lead, gestor) em segundos: **o que o
dev.kit faz, como ele trabalha, por que confiar nele e como vê-lo funcionando**. O texto mora em
[`web/src/conteudo/landing.ts`](../web/src/conteudo/landing.ts) (dados tipados, separados dos
componentes) e as mídias em [`web/public/midia`](../web/public/midia).

## Desde o ajustes-2 (item 9): a hierarquia da proposta ajustes-2-landing v1

O pedido do projeto *ajustes_2* — "ajustar a landing page para refletir os recursos que estão no dev.kit.page" — trouxe a
proposta `ajustes-2-landing` v1 do projeto de design do dev.kit.page. Ela orienta o topo da página, e os mesmos tokens do
tema claro (item 7) se estendem ao resto, sem inventar disponibilidade:

- **Topo:** o menu da proposta tem quatro itens — Como funciona, Recursos, Catálogo e Planos —, com o Entrar e o Pedir
  demonstração ao lado. As seções que saíram do topo (Plataforma, Board web, Conceitos, Fábrica agêntica, Comparativo,
  Integrações, Segurança, Empresas e Perguntas) estão no rodapé: o `landing.test.ts` confere que nenhuma seção fica sem
  caminho (só o início, o por que, o contato e o CTA final, alcançados pela marca e pelos CTAs).
- **Hero:** o rótulo "Seu trabalho, do plano à entrega", o título *Planeje, acompanhe e entregue com agentes de código.*
  (o destaque em vermelho), o texto das quatro peças, os dois botões, o link "Baixar o dev.kit para Windows" (a versão, a
  data e o tamanho seguem na nota ao lado) e, no lugar da amostra do Board, o cartão "dev.kit · projetos" marcado como
  **Demonstração**: planejar e conferir propostas, implementar no branch do item, comparar proposta e recaptura.
- **Recursos do dev.kit.page** (`#plataforma`): os três cartões da proposta, cada um com o link para o detalhe —
  `#board-web`, `#recurso-design` e a vitrine pública `/catalogo` — e as **quatro peças**: o app desktop, o serviço do
  Windows, o devcli e o Board web.
- **Board web** (`#board-web`): os recursos remotos que existem hoje, cada um com a etiqueta do que ele exige — *Com
  login*, *Com a fila liberada* (a fila do controle remoto, criada quando o administrador do dev.kit libera a provisão) ou
  *Depende do navegador* —, o "O que é preciso" e as chamadas *Entrar no Board web* (`/board`, que cai no login sem
  sessão) e *Ver os planos*.
- **Catálogo e contexto:** o catálogo segue igual, e o contexto do time (três níveis, as sugestões dos agentes e os
  repositórios e convenções) entra dito para quem ele existe: a conta de uma empresa ou da assinatura individual.

**Decisão (e por quê):** desde a US #381 a página não explicava a área logada. O pedido do ajustes_2 é refletir os
recursos do dev.kit.page, então a landing passou a contar o Board web no nível do *que ele faz e do que exige*. Continua
sem explicar o painel do gestor e do admin: o teste segue recusando "dashboard" e "métricas" em todo o conteúdo.

### Promessas e onde elas moram (conferidas no código em 22/11/2026)

| Promessa na landing | Onde mora | Quem acessa | Teste |
|---|---|---|---|
| O retrato de cada máquina (projetos, execuções, o que precisa de você), também com o app fechado | `/board` (`Board.tsx`); heartbeat do serviço e retrato ([contrato remoto](contrato-remoto-v1.md) §1–3) | qualquer papel logado; a máquina é só do dono | `Landing.test.tsx` (`podeAbrir` de cada rota) |
| Decisões com as evidências; sem o snapshot, a aprovação fica no dev.kit | `/board` (`DecisoesRemotas.tsx`, [decisões remotas](decisoes-remotas.md): snapshot, cinco minutos, auditoria) | o dono da máquina | idem |
| Projetos em ondas: plano, impactos, propostas visuais, entregas e histórico; o Sincronizar recupera | `/projetos` (`Projetos.tsx`) | logado, com o nível de compartilhamento do projeto | idem |
| Design compartilhado: lado a lado, uma a uma, sobreposto; o link estável da versão | `/design` e `/design/:projeto/:tela/:versao` (`Design.tsx`, `DesignCompartilhado.tsx`) | logado; o acesso do projeto (dono, leitor, editor) | idem |
| Conversa remota, Novo projeto/design e Gerar artefato (nunca produção) | `/board` pela fila do usuário ([contrato remoto](contrato-remoto-v1.md) §4–5.1, `ArtefatosDoProjeto.tsx`) | **com a fila liberada** pelo administrador (`FilaDoControleRemoto.tsx`) | `landing.test.ts` (nenhum texto oferece o remoto sem a fila) |
| Ligar e reiniciar o dev.kit, com o aviso do turno em andamento | `/board` → serviço do Windows (comandos `ligar`/`reiniciar`, `confirmarReinicio`) | com a fila liberada | idem |
| Avisos no dispositivo, sem o conteúdo da decisão | Web Push ([push do Board](push-board.md)) | depende do navegador e do sistema | `Landing.test.tsx` (etiqueta) |
| O contexto do time em três níveis e as sugestões dos agentes | `/conta` (Meu contexto) e `/empresa/conhecimento` | conta de empresa ou da assinatura individual (gestor e dev; o admin não é de empresa) | `Landing.test.tsx` (`podeAbrir`) |
| O catálogo básico, público | `/catalogo` (a vitrine; fora do ar, diz "indisponível no momento") | público | `Landing.test.tsx` (Explorar o catálogo) |
| O download e o teste grátis | `/api/versoes/ultima/download` (anônimo; no 503, "Download indisponível no momento.") | público | `Landing.test.tsx` (Baixar o dev.kit) |

O teste de links do `Landing.test.tsx` confere toda âncora (o alvo existe na página), toda rota (é uma das rotas do site, e
a que pede login diz "Entrar" no rótulo) e o único endereço externo (o download da API). Foi ele que achou o "Pular para
o conteúdo" quebrado: o `FocoDaRota` do App dá ao `<main>` o id `conteudo-principal`, e o `#conteudo` da landing sumia —
a landing usa agora a âncora do site inteiro.

## Referências de mercado

| Produto | O que a landing dele faz bem | O que trouxemos |
|---|---|---|
| Cursor | Hero com o produto em movimento logo abaixo da promessa | Hero com vídeo em loop do fluxo completo |
| Devin (Cognition) | A tarefa de ponta a ponta contada em etapas nomeadas | "Como funciona" em 7 etapas nomeadas, do work item à PR (US #381) |
| GitHub Copilot | Um recurso por bloco, cada um com a sua demonstração | Um vídeo curto por recurso |
| Linear | Texto curto, muito espaço, CTA repetido | CTA no topo, no hero, no contato e no fechamento |
| Warp | Terminal como cenário de demonstração | As cenas do `devcli` e do depurador em terminal |

Planos, preços e checkout estão **fora** do escopo: o CTA é o pedido de demonstração (B2B).

## Pesquisa comparativa e a estratégia (US #381)

Levantada em **07/10/2026** nas páginas públicas de cada produto — revise a cada versão da landing.
A tabela da página (`COMPARATIVO` em `landing.ts`) só afirma o que essas páginas mostram, e não cita
preço de ninguém. "Em parte" é o recurso que existe com integração ou configuração extra.

| Produto | Fonte | O que a página faz bem | O que falta e o dev.kit tem |
|---|---|---|---|
| Devin | devin.ai | O fluxo em etapas nomeadas (planejar → codar e testar → revisar → automatizar); a revisão como produto | Nota numérica e nota mínima por avaliador; horas no work item; rodar na máquina do dev |
| GitHub Copilot coding agent + Azure Boards | github.com/features/copilot/agents | O botão no work item do Azure Boards; o trabalho assíncrono | Exige o código no GitHub; sem avaliadores com nota, agendamento local nem horas |
| Cursor | cursor.com/pricing | Prova social; o uso por time no plano Teams; convite por domínio | Sem fluxo com avaliadores; sem Azure Boards |
| Factory | factory.com | Separa as "missões" (várias etapas) das "automações" (evento ou agendamento) | Sem Azure DevOps nem horas |
| Sentry | docs.sentry.io/concepts/data-management/event-grouping | Agrupamento por impressão digital, estados (novo, resolvido, regressão) e o trace com contexto | Referência do painel de exceções, não concorrente |
| Copilot Business / Tabnine (admin) | docs.github.com/en/copilot/concepts/copilot-usage-metrics, docs.tabnine.com | O painel do gestor (ativos por período, uso por pessoa) e a exportação em CSV | Nenhum fala em consentimento do colaborador — o argumento de venda do plano empresarial |

**A estratégia aplicada na página:**
- **Tabela "dev.kit × Copilot + Azure Boards × Devin × Cursor"**, porque nenhum concorrente tem a sua
  e nenhum mostra nota mínima. As linhas são o que pesa para quem está no Azure DevOps: Azure Repos sem
  GitHub, o work item como ponto de partida, avaliadores com nota e nota mínima, gatilhos entre agentes,
  agendamento local, horas no work item, cherry-pick, depurador para o agente e rodar na máquina do dev.
- **O fluxo contado em etapas nomeadas**, como a Devin faz: work item → executor → avaliadores com nota
  mínima → gatilhos entre agentes → objetivo cumprido (com teto de rodadas e impasse) → sua aprovação →
  fechamento com a PR.
- **"Para empresas"**: o acompanhamento do time pelo gestor, com o **consentimento do colaborador** e a
  exportação auditada como diferenciais; o contato é o mesmo formulário de demonstração.
- **A página não explica o painel do gestor e do admin** (o que eles veem e como): ela vende o produto,
  e quem tem acesso entra pelo link "Entrar". O teste `landing.test.ts` recusa a palavra "dashboard"
  e "métricas" em todo o conteúdo. Desde o ajustes-2 (item 9), o Board web — o que ele faz e o que cada
  recurso exige — é contado na página (ver a seção do ajustes-2 acima).

## A mensagem

- **Proposta de valor (hero):** desde o ajustes-2, *Planeje, acompanhe e entregue com agentes de código.* — o desktop, o
  serviço, o devcli e o Board web conectados, com projetos, Design e entregas num só lugar. Até então era *O agente de IA
  que leva a sua task do work item à Pull Request*, que segue contada no "Como funciona".
- **Benefícios (verificáveis no produto, sem número inventado):**
  1. Da task à PR sem trocar de janela.
  2. Qualidade com nota mínima — o objetivo só é aceito com a nota de cada avaliador.
  3. Horas lançadas sozinhas, rateadas pelos turnos do agente.
- **Como funciona (o fluxo, em sete etapas nomeadas):** work item → executor → avaliadores com nota
  mínima → gatilhos entre agentes (ao enviar, ao revisar, ao cumprir o objetivo, ao aprovar) → objetivo
  cumprido → sua aprovação → fechamento com a PR.
- **Conceitos (trabalhar com agentes):** as três peças, cada uma com um exemplo no formato do arquivo em
  que mora — o **agente** (o `pedido.md` com o planejamento técnico), a **skill** (um `SKILL.md`, com o nome, quando
  usar e os passos) e a **rule** (o `CLAUDE.md` com as Rules do time; as do catálogo somam os padrões) — e um exemplo de ponta a ponta
  em quatro passos (o pedido, as rules, as skills, a entrega).
- **Fábrica agêntica e time agêntico:** a fábrica é a linha de produção de agentes — as estações Planejar,
  Implementar, Verificar, Avaliar, Arbitrar e Entregar, ligadas por gatilhos, com a nota mínima como critério
  de passagem (no produto, um fluxo salvo; com Best-of-N e a medida de falhas, notas, impasses e custo). O time
  agêntico são os papéis: desenvolvedor, agente executor, agente avaliador, árbitro, agentes entre tasks e gestor.
- **Comparativo:** a tabela acima.
- **Para empresas:** o gestor acompanha o time, o colaborador consente no próprio dev.kit, a exportação
  é auditada e a adesão é por código, dentro dos assentos do plano.
- **Provedor cognitivo:** o usuário escolhe o melhor provedor e modelo para cada papel (o agente que implementa,
  o avaliador, o Orquestrador) — Claude Code, Codex (OpenAI), Kiro, Kimi e GLM. O Codex é um dos provedores, e
  não "o" agente; a página só cita provedor que o produto suporta.
- **Nomes do produto:** Rules (o antigo Tech Plan; o tipo `plan` do contrato não muda), skill, fluxo, gatilho,
  avaliador e nota mínima, Orquestrador, catálogo (básico e da empresa), Design, entrega.
- **Integrações:** Azure DevOps, GitHub, os provedores cognitivos (Claude Code, Codex, Kiro, Kimi e GLM),
  TortoiseGit, o serviço do Windows.
- **Segurança e privacidade:** o agente nunca vê o token; gravar é explícito (simula por padrão);
  o código fica na máquina; a telemetria é anônima, vem ligada e o usuário a desliga — o trace de erro
  vai sem caminhos, e-mails nem URLs, e o nome só é compartilhado com o gestor com o consentimento.
- **Contato:** o formulário *Quero uma demonstração* (nome, e-mail, empresa e mensagem opcionais,
  consentimento LGPD obrigatório) — ver [arquitetura](arquitetura.md#pedido-de-demonstração).
- **FAQ:** processo no Azure DevOps, provedores suportados, commit e push sozinho, onde o código roda,
  como funciona o plano para empresas, o que acontece com os dados do pedido.

## Roteiro das mídias

Todas em 1280×720, 24 fps, muted e em loop, com o poster no quadro zero (que já mostra o primeiro
destaque — é o que aparece com *prefers-reduced-motion*). As telas são as capturas do modo
`--screenshots` do git.kit, com **dados de demonstração** (nenhum dado de cliente). O roteiro executável
é [`web/midia/roteiro.mjs`](../web/midia/roteiro.mjs).

**Desde a US #467 (a proposta da home)** a home tem só os QUATRO recursos da proposta, cada um com a
mídia numa moldura de janela do app ("dev.kit - Agente") e a **legenda abaixo dela, na página** — o
vídeo não tem mais legenda sobreposta: fica só a tela do app, o destaque e o cursor. O vídeo do fluxo
(`hero`) saiu do "Como funciona", e `agendamento`, `depurador` e `devcli` saíram da home (os recursos
continuam no comparativo e nas integrações); as mídias e as capturas que deixaram de ser usadas
saíram de `public/`, e o `landing.test.ts` confere que o roteiro e a pasta só têm as que a home usa.

| Mídia | Duração | Cenário e o que aparece | Legenda na página |
|---|---|---|---|
| `agente` | 12 s | Pedido de comando (Permitir sempre) → suíte verde → mensagem de commit para aprovar | Propõe o commit com Ab#1234 — e você aprova |
| `fluxos` | 12 s | Abas de estado → task em execução → task pronta para enviar | Com a nota mínima de cada avaliador, o trabalho segue para o push |
| `replicacao` | 12 s | Branches de origem/destino → commits marcados → Replicar selecionados | Marque os commits a replicar |
| `board` | 12 s | Board: horas de hoje → hoje/ontem/semanal/mensal → os work items do "Precisa de você" (captura `tela-board.png`, a `tela-board-precisa-de-voce` do gerador) | Horas de hoje, já rateadas |
| `codex` | 12 s | Pré-execução de uma tarefa avulsa: o seletor do Modelo (o provedor da task) → o modelo no Resumo → Executar com o agente (captura `tela-codex.png`); é a mídia do recurso "Provedor cognitivo" | O provedor e o modelo da task, no resumo antes de executar |
| `design` | 12 s | Painel Design do Orquestrador: as telas (captura e proposta) → a galeria lado a lado → o agente avisando que gravou as propostas (captura `tela-design.png`) | A proposta aparece como versão nova, ao lado da captura |

**Desde a versão 5 do dev.kit** a home tem SETE recursos: o Orquestrador, o Provedor cognitivo (a escolha do
provedor de cada papel; o Codex entrou como um deles) e o Design entraram (marcados "novo" na aba). O **Orquestrador** não é gravado pelo roteiro: é o GIF das Novidades da versão 5
(`src/GitKit.Core/Updates/Novidades/versao-5.gif` do git.kit, em `public/midia/orquestrador.gif`), com o
primeiro quadro como poster (`orquestrador.jpg`, o que fica parado com *prefers-reduced-motion*). As capturas
do `codex` e do `design` são as **escuras** (v001) do projeto de design do git.kit @ `feature/dev/semana4`,
recortadas sem a margem branca — nunca a v002 do login do app, que mostra um usuário real. O hero ganhou a
fala do Orquestrador no topo da amostra do Board.

O texto alternativo completo de cada uma está no campo `alt` da mídia em `landing.ts`, e a legenda no
campo `legenda` do recurso.

## Regravar as mídias (a cada versão do produto)

1. **Telas novas** (quando a interface mudar): no git.kit, gere as capturas com um banco de
   DEMONSTRAÇÃO — nunca o `gitkit.db` real, que o modo de capturas leria (configurações, usuário e
   ferramentas da máquina): `GitKit.exe --screenshots <pasta>`, com o `devcli --db <arquivo
   temporário>` para preparar os dados. Copie as telas para `web/public/capturas` e ajuste as
   coordenadas dos destaques e o `largura`/`altura` úteis em `web/midia/roteiro.mjs`.
2. **Gerar:** com o Chromium do Playwright (`npx playwright install chromium`) e um ffmpeg com
   libx264 e libvpx-vp9 (no PATH ou em `FFMPEG`):

   ```bash
   cd web
   npm run midia                  # todas as mídias e a imagem de compartilhamento
   npm run midia -- agente board  # só estas
   ```

   Com `--gif=<pasta>` (ex.: `npm run midia -- codex design --gif=../saida/gifs`), ele grava também um
   GIF curto de cada mídia (12 fps, 800px), fora de `public` — para as notas da versão e a entrega.

   O gravador pausa as animações CSS de cada cena e fotografa quadro a quadro (o resultado não
   depende da velocidade da máquina). Os comandos de conversão que ele roda, por mídia:

   ```bash
   ffmpeg -framerate 24 -i %04d.png -c:v libx264 -preset slow -crf 27 -pix_fmt yuv420p -movflags +faststart -an NOME.mp4
   ffmpeg -framerate 24 -i %04d.png -c:v libvpx-vp9 -b:v 0 -crf 40 -row-mt 1 -deadline good -cpu-used 2 -pix_fmt yuv420p -an NOME.webm
   ffmpeg -i 0000.png -q:v 4 NOME.jpg
   ```

3. **Orçamento:** o gravador falha se um arquivo passar de **1,5 MB** ou a pasta de **25 MB**. Acima
   disso, encurte a cena ou suba o `-crf`; se ainda assim não couber, a mídia vai para um Blob
   Storage com CDN e a URL entra no `landing.ts` (ver [arquitetura](arquitetura.md#mídias-da-landing)).
4. **Gravação de tela de verdade** (opcional): uma gravação do app rodando (por exemplo
   `ffmpeg -f gdigrab -framerate 24 -i title="dev.kit" bruto.mp4`, só com o banco de demonstração na
   tela) passa pelos mesmos dois comandos de conversão, com `-i bruto.mp4` no lugar dos quadros.
