# A landing de venda do dev.kit — mensagem e roteiro das mídias (US #283)

O que a página precisa responder ao visitante (dev, tech lead, gestor) em segundos: **o que o
dev.kit faz, como ele trabalha, por que confiar nele e como vê-lo funcionando**. O texto mora em
[`web/src/conteudo/landing.ts`](../web/src/conteudo/landing.ts) (dados tipados, separados dos
componentes) e as mídias em [`web/public/midia`](../web/public/midia).

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
- **A página não explica a área logada** (o que o gestor ou o admin veem e como): ela vende o produto,
  e quem tem acesso entra pelo link "Entrar". O teste `landing.test.ts` recusa a palavra "dashboard"
  e "métricas" em todo o conteúdo.

## A mensagem

- **Proposta de valor (hero):** *O agente de IA que leva a sua task do work item à Pull Request* —
  com as regras do time, avaliadores com nota mínima e você aprovando.
- **Benefícios (verificáveis no produto, sem número inventado):**
  1. Da task à PR sem trocar de janela.
  2. Qualidade com nota mínima — o objetivo só é aceito com a nota de cada avaliador.
  3. Horas lançadas sozinhas, rateadas pelos turnos do agente.
- **Como funciona (o fluxo, em sete etapas nomeadas):** work item → executor → avaliadores com nota
  mínima → gatilhos entre agentes (ao enviar, ao revisar, ao cumprir o objetivo, ao aprovar) → objetivo
  cumprido → sua aprovação → fechamento com a PR.
- **Comparativo:** a tabela acima.
- **Para empresas:** o gestor acompanha o time, o colaborador consente no próprio dev.kit, a exportação
  é auditada e a adesão é por código, dentro dos assentos do plano.
- **Integrações:** Azure DevOps, GitHub, as CLIs claude/kiro/kimi/glm, TortoiseGit, o serviço do Windows.
- **Segurança e privacidade:** o agente nunca vê o token; gravar é explícito (simula por padrão);
  o código fica na máquina; a telemetria é anônima, vem ligada e o usuário a desliga — o trace de erro
  vai sem caminhos, e-mails nem URLs, e o nome só é compartilhado com o gestor com o consentimento.
- **Contato:** o formulário *Quero uma demonstração* (nome, e-mail, empresa e mensagem opcionais,
  consentimento LGPD obrigatório) — ver [arquitetura](arquitetura.md#pedido-de-demonstração).
- **FAQ:** processo no Azure DevOps, agentes suportados, commit e push sozinho, onde o código roda,
  como funciona o plano para empresas, o que acontece com os dados do pedido.

## Roteiro das mídias

Todas em 1280×720, 24 fps, muted e em loop, com o poster no quadro zero (que já mostra a primeira
legenda e o primeiro destaque — é o que aparece com *prefers-reduced-motion*). As telas são as
capturas do modo `--screenshots` do git.kit, com **dados de demonstração** (nenhum dado de cliente);
as cenas de terminal reproduzem o formato da saída do `devcli` sobre o mesmo work item fictício
(#1234). O roteiro executável é [`web/midia/roteiro.mjs`](../web/midia/roteiro.mjs).

| Mídia | Duração | Cenário e o que aparece | Texto alternativo (resumo) |
|---|---|---|---|
| `hero` | 15 s | Board (work item) → agente (suíte verde) → Atividades (pronto para enviar) → terminal com commit, push e PR #42 | O work item vira task, o agente trabalha, o avaliador dá a nota e o trabalho segue para a PR |
| `agente` | 12 s | Pedido de comando (Permitir sempre) → suíte verde → mensagem de commit para aprovar | O agente pede a compilação, roda os testes e propõe o commit |
| `fluxos` | 12 s | Abas de estado → task em execução → task pronta para enviar | As tasks do fluxo em execução, aguardando e prontas para enviar |
| `replicacao` | 12 s | Branches de origem/destino → commits marcados → Replicar selecionados | A tela de cherry-pick com os commits marcados |
| `agendamento` | 12 s | Expressão cron → Executar agora → atividades recentes | Os agendamentos com cron, próxima execução e Executar agora |
| `depurador` | 12 s | Terminal: `devcli depurar --parar Pedido.cs:142 --valores` → pilha → valores | O alvo para no ponto e a saída mostra a pilha e os valores |
| `devcli` | 12 s | Terminal: `devcli workitem` → `workitem-alterar` simulado → `--aplicar` | Consulta o work item e simula antes de gravar |
| `board` | 12 s | Work item → repositórios e branches → horas de hoje | O work item com os repositórios, o branch e as horas |

O texto alternativo completo de cada uma está no campo `alt` da mídia em `landing.ts`.

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
