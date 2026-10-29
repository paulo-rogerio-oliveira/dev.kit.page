# Arquitetura do dev.kit.page

## Importação de catálogos (US 738)

`POST /api/catalogos/importar` exige administrador e o limite de taxa da publicação. O navegador envia somente
JSON da pasta escolhida com caminhos relativos; a API nunca lê um caminho local do servidor. O formato é
`manifesto.json` com itens em `skills/`, `plans/`, `fluxos/`, também aceitando essas pastas sob `base/`.

`ImportacaoDeCatalogos` reutiliza `ManifestoJson`, `RegrasDosCatalogos.Conferir`, a conferência de hashes existente
e as tabelas de catálogo/itens. Catálogo, origem e rascunhos entram numa transação, sem importação parcial.
Não se importa o histórico de releases da origem: o novo catálogo começa em v1 ao publicar pelo diálogo existente.
Colisões com o básico são recusadas; dependências passam pela conferência existente antes da publicação.

GitHub aceita repositório público, branch/tag (padrão `main`) e pasta opcional. Cliente HTTP separado, sem
`REPO_KEY`, acessa somente `codeload.github.com`, sem redirecionamentos. Não há clone, extração no disco, execução
ou hooks. Caminhos absolutos, segmentos `..`, duplicatas e links simbólicos são recusados. Limites: 2000 JSON,
1 MB por arquivo, 16 MB no catálogo/download, 32 MB descompactados, 10000 entradas no repositório e timeout de 45s.
A origem fica em `Catalogos.Origem`: migration SQLite e script `api/scripts/sqlserver/us738-origem-catalogo.sql`
para bases Azure SQL existentes. Repositórios privados exigem outra definição de acesso e ficam fora deste fluxo.
`MinDevKitDaOrigem` preserva o maior requisito de versão mínima do manifesto e dos seus itens, inclusive quando
o usuário publicar sem informá-lo ou tentar um mínimo menor. A numeração do novo catálogo é independente da origem.

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

A landing é a página de venda, na disposição da proposta da home (US #467, que reorganizou a da US #405):
hero (selo "Para times no Azure DevOps", o título com "Pull Request" em destaque, os dois botões, o link
discreto "Baixar o dev.kit versão N" e a amostra do Board feita em HTML) → como funciona (sete etapas, de
Clona a Pull Request) → recursos (as abas 01–07 — Orquestrador, Provedor cognitivo e Design são os da versão 5 — e os blocos alternados: o texto com os marcadores e a
mídia numa moldura de janela do app — o componente `Janela`, o mesmo da amostra do Board —, com a legenda
abaixo) → por que o dev.kit (três cartões) → catálogo → comparativo → integrações → segurança → para
empresas → planos → contato (pedido de demonstração) → FAQ → CTA escuro → rodapé. O "Baixar o dev.kit"
(`BaixarDevKit`) lê `/api/versoes/ultima` e, desde a US #467, é um **link de verdade** (`<a href>`) para o
alias permanente `/api/versoes/ultima/download`, com o endereço absoluto escrito abaixo dele: o
navegador mostra a URL, o "Copiar endereço do link" funciona e ela cola em outra aba — o `fetch` + blob
anterior só deixava um `blob:` passageiro. A ordem é a lista `SECOES` de
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

## A área logada da web (US #461)

O visual de Empresas, Usuários e Catálogo segue os prints anexados à US #461, sobre os MESMOS tokens de
`web/src/estilo.css` (as variantes de cor novas — sucesso, alerta, info, papel — entram ali, com o tema escuro).

**A identidade de marca da versão 5** (as propostas do projeto de design do site): o site é só ESCURO — o
grafite `#0d0d0e` com as superfícies `#151517`–`#1d1d20`, o brilho vermelho e a grade sutil no fundo (o
`.pagina::before`) — e o vermelho da marca `#e2261c` preenche o botão, o selo "dK" e o indicador do item ativo
(o texto vermelho usa `#ff4a3d`, que dá AA sobre o grafite). O `Topo` traz a `Marca` (selo, ponto vermelho e a
seção numa pílula mono) e agrupa os links no menu (`MenuDoAdmin`, `MenuDaEmpresa`); a `Pagina` aceita o `rotulo`
(o eyebrow em mono vermelho). Os formulários são MAIORES: campos de 52px, rótulos de 14px semibold, foco com a
borda e o anel vermelhos e botões de 52px; o login, a adesão e a compra usam a `TelaDeAcesso` (o painel da marca à
esquerda e o formulário num cartão à direita; no celular, um embaixo do outro). Os gráficos seguem na série azul.
Nada de biblioteca de componentes: os compartilhados novos ficam em `web/src/componentes`:

| Componente | Papel |
|---|---|
| `Pagina` | o corpo da página logada: o título, o subtítulo (ao lado, `emLinha`, ou embaixo) e a ação/pílula à direita |
| `PainelLateral` | o cartão à direita da lista ("Nova empresa", "Novo usuário", "Novo catálogo"), dentro de um `.com-lateral` que vira uma coluna na tela estreita |
| `Abas` | o `tablist` do WAI-ARIA (setas, Home/End); a aba ativa é decidida pela página |
| `Chips` | os filtros de um valor só com a contagem (`aria-pressed`) |
| `Selo` | o badge (neutro, sucesso, aviso, info, papel), com o ponto opcional |
| `BotaoCopiar` | o copiar ao lado de um valor, com o retorno num `aria-live` sem papel de status; o `copiarTexto` (`web/src/areaDeTransferencia.ts`) é o mesmo do "Copiar para Bug" das exceções |
| `BarraDeUso` | "N de M em uso" com o `.trilho`/`.barra` de sempre (`role="progressbar"`) |
| `Avatar` | as iniciais com a cor derivada do nome (decorativo: `aria-hidden`) |

O `Topo` é o cabeçalho de TODA página logada (Empresas e Usuários deixaram de montar o próprio), com o avatar de
quem está logado; o `MenuDoAdmin` (Uso, Empresas, Usuários, Catálogo) marca a página atual.

- **Empresas**: a tabela traz o plano (selo), os assentos em uso (`assentosEmUso` do `EmpresaResumo` — os devs
  ativos, a mesma conta do login), o código de adesão com o copiar e o gestor (`gestor`, ou o selo "Sem gestor") com
  o Convidar, que abre o login na própria linha. O cadastro fica no painel lateral.
- **Usuários**: a busca por nome ou login e os filtros de papel e empresa são do navegador (a lista já vem inteira);
  cada linha tem o menu ⋯ (editar, bloquear/desbloquear, redefinir senha). A API não mudou.
- **Catálogo** (`/catalogo`): a aba e o catálogo escolhido ficam na URL — `?aba=releases&catalogo={slug}`,
  `?aba=catalogos&catalogo={slug}` —, então o link volta ao mesmo lugar e a página continua sendo `/catalogo` para a
  regra das páginas do dev (`sessao.tsx`). A rota antiga **`/catalogo-base` redireciona para `/catalogo?aba=releases`**
  (só para o admin, como antes). Quem não é admin vê só a **Vitrine**, qualquer que seja a aba da URL.
  - **Vitrine** (pública): o básico de `GET /api/catalogo/base/vitrine`, com a busca, os chips de tipo (a contagem
    respeita a busca e a categoria) e a barra lateral de categorias; a pílula do cabeçalho conta os itens dela.
  - **Releases** (admin): o seletor de catálogo (o básico por padrão), o cartão da release atual (tag, Atual, data,
    itens, retirados, tamanho, SHA-256 abreviado com o copiar), **Ver manifesto** (o JSON numa área sobre a página) e
    **Baixar zip** (por `fetch` com o Bearer e blob, como as exportações) e o histórico — de
    `GET /api/catalogos/{slug}/releases`, `.../{versao}/manifesto` e `.../{versao}/download`.
  - **Catálogos** (admin): a lista e o painel "Novo catálogo" (o slug é conferido antes da API: minúsculas, números e
    hífen, 2–40); o detalhe tem as empresas relacionadas (o básico "vale para todas"; o 409 da colisão mostra o
    `detail`), os itens com a situação em relação à última release e os chips de tipo, o "Importar do GitHub" no
    básico vazio, e o **Publicar**: a conferência (`GET .../publicacao`, com o dev.kit mínimo), a confirmação e, no
    409, o relatório que vem no corpo (`ErroDaApi.corpo` — não é ProblemDetails) com os problemas; publicada, a
    página vai à aba Releases daquele catálogo.
  - **O editor** grava o MESMO JSON do dev.kit desktop (`web/src/paginas/catalogo/conteudo.ts`): a skill como o
    `SkillJson` e a Rule (o tipo `plan`, o antigo Tech Plan) como o `TechPlanJson` — mesmos campos, mesma ordem, dois
    espaços —, com as mensagens de erro do app (com o nome Rule); a tela mostra "Rule"/"Rules" e a categoria "Tech
    Plans" do manifesto como "Rules", sem mudar o contrato (`plan`, `techPlanId`, o `esperado.json`); o fluxo é o envelope `devkit-fluxo` como texto (colado ou carregado do `devcli fluxos --exportar`),
    conferido (JSON, `formato`, `versao` 1, `nome`, `desenho`) e com a prévia dos passos (nó → tipo → skill). O
    desenho gráfico continua no desktop. Salvar grava rascunho; só o Publicar leva às máquinas.

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

**Recorte do log e filtro por harness.** A contagem das falhas por causa sai dos totais diários, que
não têm sessão; para listar UMA A UMA as falhas que ela conta, o `GET /api/dashboard/eventos` aceita
`tipo` e `detalhe` (exatos) e `harness` — o CLI de agente da SESSÃO: o evento entra quando a sessão
dele, na mesma máquina, tem um `TurnoExecutado` com aquele CLI no recorte. O `GET /api/dashboard/erros`
aceita o mesmo `harness`, que deixa só os grupos `Falha de turno do <cli>` (a primeira linha do trace
que o `TraceSanitizado.DoTexto` do dev.kit escreve); as exceções do app não têm harness e ficam de fora.
Os harnesses aceitos são `claude`, `kiro` (também `kiro-cli`), `glm` e `codex` (`Harnesses`) — o CLI do
provedor cognitivo que rodou o turno; o codex entrou como provedor na versão 5 do dev.kit —, e o desconhecido é 400. O harness NÃO
filtra a quantidade nem a qualidade: os totais não guardam a sessão. Toda falha `nao-classificada` tem
uma `ExcecaoNaoClassificada` gêmea na mesma sessão (o `JobTelemetria.Turno` do dev.kit manda as duas):
`tipo=TurnoFalhou&detalhe=nao-classificada&harness=claude` dá as falhas, e o grupo dá o trace.

**ROI por work item (US #387).** O dev.kit manda, na mesma ingestão do v1, o evento `RoiCalculado`
(o `devcli roi --id N`) com o campo opcional `roi`: horas (do dev.kit, do board e do timesheet), turnos
do agente, sessões, lead time e pull requests de um work item. Na ingestão, na MESMA transação:
- o evento vai para `EventosDeUso` e `TotaisDiarios` como os outros (o recorte é o id do item) — conta
  "quantos ROIs foram calculados";
- a foto vai para `RoisDeWorkItem`, UMA por (máquina, work item) pelo índice único, num upsert em que
  **o mais recente vence** (pelo `em` do evento): o evento antigo que chega atrasado não volta a foto
  para trás, e o reenvio nem chega ao upsert (é duplicado pelo `eventId`). Textos cortados e números
  trazidos para a faixa da coluna (`RegrasDeRoi.Foto`); sem `roi`, não há foto.
- a foto não é expurgada com os brutos: é o estado atual do item, e não um histórico.

`GET /api/dashboard/roi?de=&ate=&maquina=` devolve as fotos calculadas no período, das mais recentes
para as mais antigas (no máximo 200 na lista), com horas por turno (nula sem turno) e os totais de todas
— itens, turnos, horas, lead time médio dos ENCERRADOS e PRs. O escopo é o das outras consultas
(`ConsultasDoPainel.Rois`): o gestor vê só as máquinas da empresa que consentiram, e delas só as fotos
calculadas depois do instante do consentimento. Na web, a seção "ROI por work item" do dashboard mostra
os KPIs, o gráfico de horas lançadas × turnos do agente (pequenos múltiplos, uma escala por medida — sem
eixo duplo) e a tabela.

**Feedback das entregas (US #417).** O joinha que o desenvolvedor dá na janela da task segue no
`EntregaAvaliada` com o campo opcional `avaliacao` (agente, modelo, fluxo, work item e turno). Na ingestão,
na MESMA transação, o evento vai aos totais diários como os outros, e a avaliação vai para
`AvaliacoesDeEntrega`, UMA por (máquina, sessão, turno) pelo índice único — o upsert do ROI: o mais recente
(pelo `em`) vence, o atrasado não volta para trás e o reenvio nem chega (`RegrasDeAvaliacao.Avaliacao` corta
os textos e descarta a avaliação sem sessão). `GET /api/dashboard/feedback` lista as avaliações do período
(paginadas, `Pagina<FeedbackV1>`) e `GET /api/dashboard/feedback/metricas` agrega total, positivos e
negativos por fluxo, por agente e por dia, com o vazio sob `(sem fluxo)`/`(sem agente)` — no escopo das outras
consultas (`ConsultasDoPainel.Avaliacoes`: o gestor, só as máquinas da empresa a partir do consentimento). As
duas rotas recusam `de` depois de `ate` com 400, em vez de inverter como as da tela: quem as lê é a ferramenta
de análise do dev.kit, e o período invertido é defeito dela.

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

**Versões do dev.kit pelas GitHub Releases (US #405).** O app (git.kit) e a landing perguntam pela última
versão e baixam o pacote PELA API — nunca pelo GitHub —, porque o token do repositório não pode ir para o
cliente:
- `GET /api/versoes/ultima` (anônima) devolve a `VersaoV1`: o número do dev.kit, a tag, a data, até 5
  destaques (os itens de lista do corpo da release, sem markdown), o tamanho e o SHA-256 do zip (o
  `digest` do asset ou o `<zip>.sha256` publicado ao lado), a `urlDownload` e o `loginObrigatorio`.
- `GET /api/versoes/{versao}/download` exigia o usuário na US #405; **desde a US #425 é ANÔNIMO** (a
  reversão: quem baixa começa o trial de 14 dias na primeira abertura), limitado por IP pela
  `PoliticaDoDownload` (`Atualizacao:DownloadsPorHora`, padrão 20, 429 acima). Segue um **proxy em streaming**: o `ReleasesDoDevKit` pede
  `releases/assets/{id}` com `Accept: application/octet-stream` e `HttpCompletionOption.ResponseHeadersRead`,
  e a rota copia o stream para a resposta — o zip nunca é bufferizado. O HttpClient nasce **sem
  redirecionamento automático**: o 302 do GitHub para o armazenamento (outro host, URL assinada) é seguido à
  mão, SEM o `Authorization`, para o token não sair do api.github.com. Os cabeçalhos da resposta são da API
  (`application/zip`, `attachment; filename=devkit-136.zip`, o `Content-Length`).
- `GET /api/versoes/ultima/download` (US #467) é o **alias permanente** da última estável: o mesmo handler,
  o mesmo streaming e a mesma cota por IP (503 sem versão estável). É o `href` do "Baixar o dev.kit" da
  landing — a URL que o visitante copia continua valendo na próxima versão, o que o `/{versao}/download`
  não faz.
- **A regra de versão é pura, no Core** (`RegrasDeVersao`): a tag `vNNN` é o número; a legada `vX.Y.Z`
  vale X; prerelease, rascunho e tag ilegível ficam de fora; a ordem é numérica (v136 antes de v99), e duas
  tags da mesma versão ficam uma só (a publicada por último).
- **O token** (`OpcoesDeAtualizacao`) vem SÓ da variável `REPO_KEY` (o `PostConfigure` sobrescreve qualquer
  valor do appsettings); o repositório (`Atualizacao:Repositorio`) e o cache (`Atualizacao:CacheMinutos`,
  padrão 5) são configuração. A lista de releases fica no `IMemoryCache` (o GitHub limita as chamadas por
  token), uma consulta por vez quando ele vence; só o sucesso entra no cache.
- **Falha não vaza**: sem `REPO_KEY`, com o GitHub fora do ar ou sem release estável com zip, a resposta é
  um 503 genérico — o log leva o status e o tipo da falha, nunca o corpo do GitHub nem o token. Versão
  inexistente é 404.

**Gestão de usuários e papel dev (US #405).** O admin administra os usuários em `/api/usuarios`
(`Seguranca.PoliticaAdmin`): listar, criar, editar nome/papel/empresa, bloquear/desbloquear e redefinir a
senha. A senha é **temporária**, gerada pelo `GeradorDeSenha` (Core) — o MESMO do admin semeado e do gestor
convidado, que agora é uma criação da `IGestaoDeUsuarios` com o papel `gestor` —, devolvida **uma vez** (na
criação e na redefinição) e com a troca obrigatória no primeiro acesso. O papel `dev` (o usuário do dev.kit)
entra no app e baixa a versão, mas o grupo `/api/dashboard` exige a `Seguranca.PoliticaPainel`
(`Papeis.EntraNoPainel`: admin e gestor) — e o `EscopoDoPainel` do dev também é nulo (403 em dobro). O
bloqueio pelo admin (`BloqueadoPeloAdmin`) é diferente do temporário por falhas (`BloqueadoAteUtc`): só
aparece para quem acertou a senha (sem ela, a resposta é a de sempre) e só sai ao desbloquear. O 423 do
bloqueio temporário leva a extensão `bloqueadoAte` (ISO UTC) para o app dizer quando tentar de novo; o do
admin, não. O admin não muda o próprio papel nem se bloqueia. A política de login do app
(`OpcoesDeLogin`, variável `DEVKIT_LOGIN_OBRIGATORIO`) sai em `GET /api/auth/politica` — que não depende do
GitHub — e no `loginObrigatorio` da última versão. Colunas novas: migration `GestaoDeUsuarios` e
`api/scripts/sqlserver/Usuarios.sql`.

**Impasses e árbitro na qualidade (US #405).** Os tipos `ImpasseDetectado`, `ImpasseResolvido` e
`ArbitroAgiu` (ver o [contrato](contrato-telemetria-v1.md#o-impasse-e-o-árbitro-us-405)) entram nos totais
diários como os outros; o `CalculoDeQualidade` monta os cartões Impasses e Árbitro a partir das MESMAS
somas por tipo e recorte que o `ConsultasDoPainel.QualidadeAsync` já tira do banco no escopo e no período —
nenhuma consulta a mais. A taxa de correção sem cobrança é nula. Na web, os cartões ficam na seção
"Qualidade de uso" do dashboard.

**Catálogo, conta do dev e licença assinada (US #425).** Cinco grupos novos, em `EndpointsDaUs425.cs`:
- **Catálogo básico** (`/api/catalogo/base`): as releases `catalogo-vN` do git.kit, lidas na MESMA consulta das
  versões (`ReleasesDoDevKit`; a `RegrasDeVersao` recusa as tags `catalogo-`), com o manifesto em cache. O app
  pede a DELTA desde a versão dele (`RegrasDoCatalogoBase.Delta`: os itens com `catalogo > desde`, mais os
  retirados) e recebe 304 sem mudança; `/download` serve o zip; `/vitrine` é a página pública `/catalogo`,
  agrupada por tecnologia; `/releases` é do admin.
- **Catálogo da empresa** (`/api/catalogo/empresa`): o gestor e o dev publicam, listam, assinam e retiram
  itens da empresa DELES — o isolamento entre empresas fica num ponto só (`CatalogoDaEmpresa.DaEmpresaAsync`).
- **Conta** (`/api/conta`, `/api/empresa`): a adesão por código e e-mail cria o dev e manda o link de definir
  a senha (uso único, 24 h, só o hash no banco) pelo `IEnvioDeEmail` (SMTP; sem SMTP, o log); o assento
  conta por USUÁRIO (`Assentos.DentroAsync`: os primeiros devs ativos da empresa) e o login além dele é 403
  `sem-assento`; o gestor bloqueia (libera o assento), gera código novo e define a política de login da
  empresa, que sobrepõe o `DEVKIT_LOGIN_OBRIGATORIO` (agora só o padrão). Cada recusa do login leva o seu
  `codigo` (`CodigosDeRecusa`), e `POST /api/auth/renovar` troca o token válido por um novo com as MESMAS
  recusas — o app renova sozinho, e o corte do gestor vale também ali.
- **Licenças** (`/api/licencas/renovar`): exige o JWT E o `X-Machine-Key`; emite a licença `DKL2` (Ed25519,
  `LicencaAssinada`, chave privada `Licencas:ChavePrivada` do Key Vault) de 30 dias, a partir do 7º dia da
  atual; bloqueado, sem assento ou assinatura cancelada não renovam (`RecusaDaRenovacao`). O registro da
  máquina com a impressão (`/api/maquinas/registrar`) devolve o TRIAL assinado, contado do primeiro registro
  daquela impressão.
- **Pagamentos** (`/api/pagamentos`): o checkout da compra individual (simulado sem gateway configurado) e o
  webhook assinado (`X-Gateway-Assinatura: t=..,v1=hmac`, `RegrasDoGateway`), que cria a empresa individual
  ou marca a assinatura cancelada.

Tabelas novas: migration `Us425CatalogoContaELicenca` e `api/scripts/sqlserver/Us425.sql`.

**Catálogos nomeados: o site publica (US #461).** O site passa a ser a FONTE de publicação dos catálogos,
inclusive do básico — é a única forma de "editar pelo site, gerar o manifesto e republicar" sem duas fontes de
`catalogo-vN`. Em `EndpointsDeCatalogos.cs`, no padrão do `EndpointsDaUs425`:
- **Modelo** (`EntidadesDaUs461.cs`): `Catalogo` (slug imutável — é o que as máquinas guardam —, nome, `Basico`),
  `VinculoDeCatalogo` (N:N com `Empresa`; o nome não é `CatalogoDaEmpresa` porque esse já é o serviço da US #425),
  `ItemDoCatalogoEditavel` (o RASCUNHO de skill, plan ou fluxo; distinto do `ItemDeCatalogo` dos devs),
  `VersaoDoCatalogo` (a release: tag `catalogo-vN` no básico e `{slug}-vN` nos demais, o manifesto, o SHA-256) e
  `PacoteDoCatalogo` (o zip, à parte, atrás do `IArmazemDePacotes` — no banco hoje, Blob depois sem mudar regra).
  O básico é semeado pelo `InicializadorDaBase` e não se vincula nem se exclui.
- **O formato é o do app** (`FormatosDoCatalogo`): o porte do `SkillJson`, do `TechPlanJson` (com a chave fixa e o
  `Slugify`) e do envelope `devkit-fluxo`, com as MESMAS mensagens de erro. O editor grava exatamente esse JSON; a
  importação na máquina continua sendo a do dev.kit. O nome segue a regra do APP (o básico real tem a skill
  `resolver conflitos`, com espaço), e não a do catálogo da empresa.
- **O manifesto é o do git.kit** (`GeradorDoManifesto` e `ManifestoJson`, portados): hash novo é versão + 1, o que
  sai vai para os retirados, nada mudou não publica; `requer`, `tecnologia` e `dependencias` pelas mesmas regras. São
  dois repositórios sem pacote comum, então a igualdade é TRAVADA por teste: a paridade com o `catalogo/base` v2 do
  git.kit (os 58 itens dão o mesmo hash, e o manifesto regravado sai com o mesmo texto) e o teste de contrato
  `ContratoDoManifesto`, o MESMO conjunto de fixtures nos dois repositórios — mudar o gerador de um lado obriga a
  mudar o outro. A importação da release do GitHub também confere o hash de cada item e para se divergir.
- **Publicar** (`PublicacaoDeCatalogos`): o zip leva TODOS os itens ativos (a dependência resolvida no próprio
  catálogo vai no mesmo pacote) e o `manifesto.json`. O FECHAMENTO das dependências (`RegrasDosCatalogos.Dependencias`)
  aceita o que está no próprio catálogo, o plan embutido no `planos[]` do fluxo (o caso real `plan:avaliar-arquitetura`)
  e o básico (que toda máquina sincroniza); a de outro catálogo nomeado, ou inexistente, RECUSA com o relatório (409
  com o `RelatorioDePublicacaoV1`, e não um ProblemDetails, para a tela listar o que corrigir). O `minDevKit` dos
  itens que mudaram é o informado, ou o do manifesto anterior.
- **Colisão de nomes**: na máquina o item é tipo + nome. Recusa-se (409) o item de um catálogo nomeado com o nome de
  um item do básico, e relacionar a uma empresa um catálogo que colide com outro já relacionado a ela.
- **Distribuição**: `GET /api/catalogos/meus` (o básico para todos; com o JWT, os relacionados à empresa; o admin vê
  todos), `GET /api/catalogos/{slug}?desde=N` (a delta, 304 sem mudança), `/download` (o zip) e
  `/releases/{versao}/manifesto`. O isolamento é UM ponto (`PublicacaoDeCatalogos.VisivelAsync`, pelo
  `EscopoDaEmpresa`): o catálogo que quem pede não pode ver responde 404, e não 403 — não se descobre que ele existe.
- **Compatibilidade**: o formato `ManifestoDoCatalogoV1` NÃO muda (o slug e o nome vão só no `/meus`), e as rotas
  `/api/catalogo/base*` seguem para o dev.kit antigo, agora servidas pela última release do básico NO SITE — com
  fallback para as releases do GitHub enquanto não houver uma. A importação (`POST /api/catalogos/basico/importar`)
  copia UMA vez a última `catalogo-vN` do GitHub (o mesmo zip e o mesmo manifesto) para o site, e a numeração continua.

Tabelas novas: migration `Us461CatalogosNomeados` e `api/scripts/sqlserver/Us461.sql`.

**O que a empresa compartilha com o dev.kit: o catálogo de repositórios e as convenções (US #491).** Duas coisas que
uma task aprende valem para a próxima — inclusive a do colega: QUAL repositório é qual, e COMO se nomeia o branch.
As rotas ficam sob `/api/empresa`, no `EscopoDaEmpresa` do token, mas abertas ao DEV (as outras dali são do gestor):
- `GET /api/empresa/repositorios?filtro=` e `POST /api/empresa/repositorios` (`GravacaoDeRepositorioV1 {origem,
  descricao}`): o agente do dev.kit grava o caminho e a descrição de cada repositório que usou ao terminar a task, e
  todos da empresa consultam para resolver o repositório de um pedido. Único por (empresa, caminho normalizado —
  minúsculas, sem barra nem `.git` no fim, `RegrasDoConhecimentoDaEmpresa.ChaveDaOrigem`): gravar de novo troca a
  descrição. O filtro (caminho ou descrição, sem caixa) é em memória. `DELETE /api/empresa/repositorios?origem=`
  tira um repositório do catálogo: do gestor, ou de quem o descreveu por último (os outros recebem 403; 404 se não
  existe).
- `GET /api/empresa/convencoes?time=` e `PUT /api/empresa/convencoes` (`ConvencaoV1 {nivel, chave, valor, time}`): as
  convenções da EMPRESA, as do TIME e as do USUÁRIO — hoje o modelo do nome do branch, uma por TIPO DE BRANCH
  (`branch.feature`, `branch.bug`, `branch.hotfix` ou `branch.<tipo do work item>`, chave em minúsculas sem acento; quem
  escolhe qual vale é o dev.kit, pelo tipo do item ou pelo `tipo=` do comentário).
  O time é um NOME, o que o dev declara na Equipe das Configurações do dev.kit, e não um cadastro desta API: a empresa
  não precisa montar times aqui para compartilhar a convenção de um. A da empresa é do gestor (o dev recebe 403); a de
  um time, de qualquer um da empresa; a do usuário, só do próprio (gravada no `UsuarioId` do token — não há como
  gravar a de outro); valor vazio apaga a do nível. O GET devolve a da empresa, a do time pedido e as do próprio
  usuário. A precedência (usuário &gt; time &gt; empresa) é do dev.kit, que lê estas na abertura do `devcli` — a do
  usuário vale na máquina mesmo sem a API, e sobe para cá quando dá.
- `GET /api/empresa/convencoes/todas` é a AUDITORIA: cada convenção com o nível, o time, de quem é (a do usuário), o
  autor da última gravação e quando. O gestor vê todas, de todos; o dev vê as da empresa, as de todos os times e as
  dele — as de outro usuário são pessoais.
- A página `/empresa/conhecimento` do site (`EmpresaConhecimento.tsx`, do dev e do gestor — o admin vai para o
  dashboard) é onde isso se audita e se edita: a aba Repositórios lista o catálogo com o autor e a data, edita a
  descrição e (gestor) remove; a aba Convenções filtra por nível, grava e apaga, com o formulário oferecendo só os
  níveis que o papel pode gravar (empresa, só o gestor).
- O admin, sem empresa no token, recebe 403, como nas outras rotas da empresa (o contexto, US #550, ganhou rotas
  próprias para ele, abaixo). Serviço: `IConhecimentoDaEmpresa`
  (Core, `ServicosDaUs491.cs`) e `ConhecimentoDaEmpresa` (Infrastructure); modelo em `EntidadesDaUs491.cs`.

Tabelas novas: migrations `Us491ConhecimentoDaEmpresa` e `Us491ConvencaoDoUsuario` (a coluna `UsuarioId` da
convenção do usuário, no índice único) e `api/scripts/sqlserver/Us491.sql`, que também acrescenta a coluna numa base
que rodou a primeira versão dele.

**O contexto do dev.kit, gerenciável por nível (US #550).** O que os agentes do dev.kit devem saber da empresa, da
equipe e do usuário em toda task. Não há tabela nova: o contexto é a família de chaves `contexto.<tema>` das MESMAS
convenções da #491 — a tabela `ConvencoesDaEmpresa`, o contrato `ConvencaoV1` e a rota que o dev.kit já lê
(`GET /api/empresa/convencoes?time=`, que não muda: o dev.kit antigo ignora as chaves que não conhece).
- **Teto próprio.** `RegrasDoConhecimentoDaEmpresa.EhContexto`/`TetoDoValor`: o texto do contexto vai até 4000
  caracteres (`TamanhoMaximoDoValorDeContexto`); as convenções de branch continuam com 200. A coluna `Valor` passou a
  `nvarchar(4000)` — migration `Us550ContextoPorNivel` (sem DDL no SQLite, cujo `TEXT` não tem tamanho) e
  `api/scripts/sqlserver/Us550.sql` (idempotente: altera só enquanto a coluna for menor).
- **Quem grava, numa regra pura** (`RegrasDoConhecimentoDaEmpresa.PodeGravar`, no Core, testada sem banco): a da
  EMPRESA é do gestor; num TIME, o CONTEXTO da equipe é do gestor e o branch (#491) continua de qualquer um; a do
  USUÁRIO é do dono — o gestor a vê na auditoria, mas não a edita; o admin geral grava tudo.
- **O admin geral** não tem empresa no token e continua recebendo 403 em `/api/empresa/*`. Em vez de afrouxar aquelas
  rotas, as dele dizem a empresa no caminho, sob a `PoliticaAdmin` (`EndpointsDaUs550.cs`): `GET
  /api/empresas/{id}/convencoes/todas`, `PUT /api/empresas/{id}/convencoes` e `GET /api/empresas/{id}/equipes`. Elas
  montam o `EscopoDaEmpresa.DoAdmin` (gestor naquela empresa, e mais) e usam o MESMO `ConhecimentoDaEmpresa`. O nível
  usuário de outro vai pelo login em `ConvencaoV1.Usuario` — só o admin escolhe o dono; para o dev e o gestor o dono
  continua sendo o do token. Empresa inexistente é 404; login de outra empresa também (sem revelar que existe).
- **As equipes conhecidas** (`GET /api/empresa/equipes`, e a rota do admin): os times distintos, normalizados, já
  gravados nas convenções da empresa. A equipe continua um nome livre — a lista só ajuda a escolher.
- **As telas** usam um editor só (`componentes/EditorDeContexto.tsx`: temas, texto com o contador do teto, remover =
  gravar vazio) e, para os três níveis, `componentes/ContextoPorNivel.tsx`: a seção **Meu contexto** em `/conta` (o
  do próprio usuário; sem empresa, o aviso), a aba **Contexto** de `/empresa/conhecimento` (o gestor edita empresa e
  equipes e lê os usuários; o dev só lê) e o painel **Contexto** de cada empresa em `/empresas` (o admin, os três
  níveis, inclusive escolher um usuário e editar o dele). A aba Convenções passou a mostrar só as de branch.
- No dev.kit, a composição é a do `ContextoDoDevKit` (empresa → equipe → usuário; num tema em conflito, vale o mais
  específico), que entra no arquivo de regras de toda task.

**Regra do usuário × Sugestão do agente (US #663).** O que a PESSOA escreve no contexto é REGRA para o agente; o que o
agente do dev.kit registra ao concluir uma task (`devcli contexto --sugerir`) é SUGESTÃO — consulta, nunca obrigação —
até alguém com o papel da camada promovê-la.
- **A origem na mesma tabela.** `ConvencaoDaEmpresa` ganhou `Origem` (`usuario` | `agente`) e `Versao` (sobe a cada
  gravação), e o índice único passou a incluir a origem: a regra e a sugestão do mesmo tema coexistem. Migration
  `Us663OrigemDoContexto` (só acrescenta; o que existia virou regra na versão 1) e `api/scripts/sqlserver/Us663.sql`.
- **Contrato compatível.** `ConvencaoV1` ganhou, no fim e opcionais, `Id`, `Origem` e `Versao`. `GET
  /api/empresa/convencoes` continua devolvendo SÓ as regras (o dev.kit instalado leria a sugestão como regra); com
  `?origem=todas`, as duas. A auditoria (`/todas`) traz as duas.
- **As rotas novas** (`EndpointsDaUs663.cs`): `POST /api/empresa/contexto/sugestoes` (`SugestaoDeContextoV1`; a origem
  é SEMPRE agente, o corpo não a escolhe; idempotente pelo autor+tema+texto — o reenvio da fila offline do dev.kit não
  sobe a versão), `POST /api/empresa/contexto/{id}/promover` (`PromocaoDeContextoV1.VersaoEsperada`; outra → 409) e
  `DELETE /api/empresa/contexto/{id}`; as mesmas do admin em `/api/empresas/{id}/contexto/*`, no mesmo serviço.
- **Quem pode, em regras puras** (`RegrasDoConhecimentoDaEmpresa`): `PodeSugerir` (toda camada que o usuário vê; no
  nível usuário, só o próprio), `PodePromover` (= `PodeGravar` da camada) e `PodeDescartar` (quem grava a camada, ou
  o autor da sugestão). Só o contexto aceita sugestão: `branch.*` continua só regra. Promover grava (ou sobrescreve) a
  regra do tema na camada e remove a sugestão num `SaveChanges` só.
- **Segredo barrado.** `RegrasDosRelatos.ContemSegredo` usa os MESMOS padrões da máscara dos relatos (que ganhou a
  chave privada): toda gravação de contexto, regra ou sugestão, com segredo é 422, sem ecoar o valor.
- **As telas**: o `EditorDeContexto` mostra, em cada nível, a parte **Regra (do usuário)** e a **Sugestão (do
  agente)** — só leitura, com autor e data, os botões **Promover a regra** (de quem edita o nível) e **Descartar** (a
  API aceita de quem edita ou do autor) e o aviso quando o tema já tem regra (a regra prevalece).

**Relatos dos usuários (US #602).** O defeito, a sugestão ou a dúvida que o usuário manda pelo dev.kit (a ação Relatar
e o `devcli relatar`) chega em `POST /api/relatos` (qualquer usuário logado; o usuário e a empresa vêm das claims do
JWT, nunca do corpo) e devolve 201 com `{id, estado, recebidoEm}`. O envio tem limite POR USUÁRIO
(`Relatos:EnviosPorHora`, padrão 20, janela de uma hora; acima dele, 429) e é idempotente pela `chaveDoCliente` — o
reenvio da fila offline devolve o MESMO relato. O contexto consentido e o texto passam pela máscara de segredos e pela
`RegrasDeErro.Mascarar` (caminhos, e-mails, URLs) antes de gravar, mesmo já sanitizados no dev.kit; a captura é PNG ou
JPEG em base64 (até 2 MB). O autor acompanha os seus em `GET /api/relatos/meus` (estado e resposta). A gestão — `GET
/api/relatos?estado=&categoria=&empresa=&de=&ate=&pagina=&tamanho=` (paginada), `GET /api/relatos/{id}`, `PATCH
/api/relatos/{id}/estado` e `POST /api/relatos/{id}/resposta` (responder um `novo` o leva a `em-analise`) — fica sob a
`PoliticaPainel` com o `EscopoDoPainel`: o admin geral vê todos (e só ele usa o filtro `empresa`), o gestor só os da
empresa dele (o relato de outra empresa é 404, sem revelar que existe) e o dev recebe 403. Categorias `defeito`,
`sugestao`, `duvida`; estados `novo`, `em-analise`, `resolvido`. Tabela nova pela migration `Us602Relatos` e, no Azure
SQL, pelo script `api/scripts/sqlserver/Us602.sql`. Na web, a tela `/relatos` (admin e gestor; item Relatos no menu do
admin e na navegação do gestor) lista com os selos de categoria e estado, autor, empresa, data e os indicadores de
captura e de resposta, filtra por estado, categoria, empresa (só o admin) e período, e o detalhe mostra o texto, o
contexto (em `<pre>`), a captura, a troca de estado e a caixa de resposta com quem respondeu
(`paginas/Relatos.tsx`, `componentes/ListaDeRelatos.tsx`).

**Design compartilhado (US #669).** O projeto de design do dev.kit (as capturas de tela e as propostas visuais) sai
da máquina do dev e passa a morar aqui — e os PNG nunca entram no git: a PR da semana cita o LINK ESTÁVEL da versão.
- **Modelo:** um `ProjetoDeDesign` por REPOSITÓRIO (a URL normalizada por `DesignV1.ChaveDoRepositorio` — https, ssh,
  `.git` e maiúsculas são o mesmo; o dev.kit usa a MESMA regra) num nível, no padrão da `ConvencaoDaEmpresa`; `TelaDeDesign`
  (id do dev.kit, nome, estado); `VersaoDeDesign` (número, tipo captura|proposta, base, tema, dimensões lidas do PNG,
  origem web|desktop|proposta, alvo, BRANCH e COMMIT de onde saiu — o branch é atributo, não projeto —, descrição, autor
  e se foi o usuário ou o agente, a data original e a de envio, o SHA-256 e a chave da imagem).
- **Escopo:** o `EscopoDoDesign` existe SEM empresa (ao contrário do `EscopoDaEmpresa`), por isso as rotas ficam em
  `/api/design`, e não em `/api/empresa`: quem não tem empresa guarda tudo no nível `usuario`. Com empresa, o nível
  `empresa` é de todos dela, o `time` de quem declara o time (`?time=`; sem ele, o site vê todos) e o `usuario` só do
  dono (`RegrasDoDesign.Visivel`). Projeto que não é visível é 404, sem revelar se existe.
- **Imagens fora do banco:** `IArmazemDeImagens` (Core) com o Azure Blob Storage em produção (`ArmazemDeImagensNoBlob`,
  `Design:Armazem=blob` e a conexão `Design:ConexaoDoBlob` do Key Vault) e uma pasta em desenvolvimento e nos testes
  (`ArmazemDeImagensEmDisco`, `Design:Pasta`). A versão só fica no banco com a imagem gravada: a falha do armazém desfaz
  a versão, e o reenvio da fila a grava de novo.
- **Teto por plano:** `Design:Tetos:<plano>` (versões e MB; o Empresarial vem com 500 versões e 2 GB) e
  `Design:TetoPadrao` (100 e 256 MB) para o plano fora da lista e para quem não tem empresa. O espaço que consome o teto é
  a empresa (ou o usuário sem ela). Estourou: 413 com a mensagem.
- **Rotas:** `GET /api/design/projetos?time=&desde=` (o `desde` é a sincronização incremental da abertura do dev.kit:
  só os projetos alterados depois — e o projeto vem inteiro, então a versão apagada também se reconcilia),
  `GET /api/design/projetos/{id}`, `GET /api/design/uso`, `POST /api/design/versoes` (multipart: os metadados e o PNG em
  `imagem`, até 16 MB; limite por usuário em `Design:EnviosPorMinuto`), `GET /api/design/{projeto}/{tela}/v{n}` (o PNG,
  com ETag pelo hash) e `DELETE` no mesmo caminho (só o autor da versão ou o gestor da empresa; a tela sem versão sai).
- **Idempotência:** a chave é (projeto, tela, número) + o hash. O reenvio do mesmo PNG é 200 com a mesma versão; outro PNG
  no mesmo número é 409 — o dev.kit não sobrescreve. Sem número, a próxima da tela.
- **Web:** a aba **Design** de `/empresa/conhecimento` e a página `/design` (quem não tem empresa; o link fica na `/conta`)
  usam o mesmo `componentes/DesignCompartilhado.tsx` (a proposta `empresa-conhecimento` v002): o repositório, o branch
  (filtro) e o escopo Empresa / Equipe / Minhas, o uso do teto, as telas com o selo da última versão e a comparação
  captura | proposta | recaptura (a última proposta, a captura que é a base dela e a última captura depois dela) em Uma,
  Lado a lado ou Sobrepor, com Copiar link e Apagar (só de quem pode) e a faixa do link estável. A imagem vem por fetch
  com o token e um `blob:` (a rota é autenticada). O link estável é a página `/design/{projeto}/{tela}/v{n}`.
- Tabelas novas pela migration `Us669DesignCompartilhado` e, no Azure SQL, pelo script `api/scripts/sqlserver/Us669.sql`.

### O admin útil: painel de utilidade, licença, empresa e editor de artefato (US #687)

- **Painel de utilidade** (`/dashboard`, `GET /api/dashboard/utilidade`, `ConsultasDoPainelDeUtilidade`): a "Saúde agora",
  o resultado do uso, os turnos por dia pelo desfecho e "onde as tasks travam" substituem os números de quantidade. Lê as
  FOTOS de estado (`FotosDeEstado`, o evento mais recente por máquina/tipo/chave, gravado na ingestão) e os totais
  diários; as regras puras ficam em `RegrasDoPainelDeUtilidade` (Core). O contrato dos seis tipos novos está em
  `docs/contrato-telemetria-v1.md`.
- **Licença por usuário** (`/usuarios`, `GET|PUT /api/usuarios/{id}/licenca`, `POST .../licenca/previa`,
  `POST .../licenca/revogar`, só admin): somar dias, até uma data ou sem prazo, com o tipo e o motivo no histórico
  (`HistoricosDeLicenca`). A renovação do dev.kit (`POST /api/licencas/renovar`) emite `min(agora + 30 dias, validade do
  admin)` e recusa a licença vencida ou revogada (403). A janela de renovação é a MESMA regra nos dois lados — `agora >=
  emitida + 7 dias` ou `agora >= validade − 7 dias`, e a alteração do admin depois da emissão abre a janela —,
  conferida pelo mesmo arquivo de casos (`ContratoDaRenovacao/casos.json`) nos testes da API e do dev.kit. Sem licença
  definida pelo admin vale a da empresa (o contrato até) ou, sem ela, os 30 dias corridos de antes.
- **Edição da empresa** (`/empresas`, `PUT /api/empresas/{id}`): os dados (CNPJ com dígitos verificadores), o plano, o
  contrato e os assentos — nunca abaixo dos que estão em uso (`RegrasDaEmpresa`).
- **Editor de artefato** (`/catalogo`, aba Itens; `GET /api/catalogos/itens` e `/api/catalogos/{slug}/itens/{id}/`
  `diferencas|historico|descartar|anexos`): a lista de todos os catálogos com busca no conteúdo e o escopo base/empresa/
  projeto, e o editor com o SKILL.md (salvo sozinho), as diferenças com a versão publicada, o histórico e os anexos (até
  1 MB, 10 por item; ficam no site, não vão para o pacote). A Rule de IMPACTO nunca entra no catálogo básico: o site
  recusa ao salvar e ao publicar, e o dev.kit a ignora na sincronia.
- Tabelas e colunas novas pela migration `Us687AdminDeUtilidade` e, no Azure SQL, pelo script `api/scripts/sqlserver/Us687.sql`.

**Base embarcada, provider trocável.** SQLite agora, com as migrations versionadas
(`Infrastructure/Migrations`) aplicadas na subida — as novas são ADITIVAS (a dos pedidos de
demonstração só cria a tabela). `Banco:Provider=SqlServer` troca para o Azure SQL sem mudar código;
nesse provider o esquema nasce do modelo (`EnsureCreated`), e as migrations próprias do SQL Server
entram num projeto de migrations separado quando a base for para lá.

**A restrição do `EnsureCreated` (Azure SQL).** Ele só cria o esquema numa base VAZIA: numa base que
já existe, uma tabela nova do modelo NÃO é criada. Por isso cada tabela nova vem com um script
idempotente em `api/scripts/sqlserver` (`IF OBJECT_ID(...) IS NULL CREATE TABLE`), que se roda
antes de publicar a versão que a usa — a da US #283 é `PedidosDeDemonstracao.sql`, e as da US #381,
`GruposDeErro.sql` e `Empresas.sql` (este também acrescenta colunas a `Usuarios` e `Maquinas`), e a da US #387, `RoiDeWorkItem.sql`, e a da US #417, `AvaliacoesDeEntrega.sql`, e a da US #425, `Us425.sql`, e a da US #461, `Us461.sql`, e a da US #491, `Us491.sql`, e a da US #550, `Us550.sql` (que só alarga uma coluna), e a da US #602, `Us602.sql`, e a da US #663, `Us663.sql` (a origem e a versão do contexto), e a da US #669, `Us669.sql`, e a da US #687, `Us687.sql`. Os testes `Script_do_azure_sql_*` comparam cada script com o `CREATE TABLE` que o
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
| `api/tests/DevKitPage.Infrastructure.Tests` | SQLite de verdade: admin semeado uma vez, reenvio sem duplicar, eventId único, agregação e filtro, paginação, expurgo mantendo os totais, bloqueio do login, máquinas ativas e registradas; grupos de exceção (mesma assinatura = um grupo com as ocorrências e as máquinas, reenvio sem duplicar, trace mascarado na gravação, regressão, só as últimas ocorrências, expurgo mantendo o grupo); pedidos de demonstração (gravação, paginação, exclusão, expurgo além da retenção mantendo os recentes) e os scripts do Azure SQL contra o modelo; `RoiTests`: a foto do ROI (o mais recente vence, o atrasado e o reenvio não mudam nada, sem `roi` não há foto, corte dos textos e números), horas por turno, totais, filtro e o escopo do gestor |
| `api/tests/DevKitPage.Api.Tests` | WebApplicationFactory: login, 401/403, token vencido, troca obrigatória, chave inválida, JWT na ingestão, 413, registro, `/health`, Production sem segredo; lote antigo aceito, chave da máquina sem acesso aos erros, reação pelo JWT, exportação do grupo; `EmpresasApiTests`: o gestor da empresa A não lê máquinas, erros nem a exportação da B (403 pelo id), a máquina sem consentimento só nos totais, o CSV auditado e sem fórmula, o 429 da exportação em laço, a adesão recusada sem assento ou com código desconhecido, o admin vê tudo e o gestor não administra; demonstração 201/400/401/404, 429 com `Retry-After`, `X-Forwarded-For` do proxy confiável (IPs diferentes não dividem a cota, o mesmo divide) e do não confiável (ignorado), o expurgo diário; `RoiApiTests`: o `RoiCalculado` no JSON exato do dev.kit vira a foto no painel, o envio mais novo substitui e o mais antigo não, o evento sem `roi` não cria foto, o gestor não lê o ROI de outra empresa (403 pelo id) |
| `web/src/**/*.test.tsx` | Vitest + Testing Library + MSW: landing (ordem das seções, uma mídia por recurso, CTA, Entrar, sem explicar o dashboard, as sete etapas, a tabela comparativa acessível e completa, Para empresas), Midia (atributos, carregamento sob demanda, reduced-motion, fallback), conteúdo, formulário (validação, envio, 400, 429, falha), pedidos no dashboard (lista, exclusão, paginação), login (inclusive o gestor), expiração, troca, KPIs (inclusive as máquinas), filtros, divisor zero; exceções não classificadas (lista vazia, trace, PUT com a versão, exportação, cópia para o Bug, o gestor sem reação); o painel por papel (Colaborador e Exportar CSV do gestor, Empresas do admin); o ROI por work item (KPIs, gráfico com a dica do lead time, tabela, traço sem turno, estado vazio com o `devcli roi`, os filtros) |
| US #405 | Core: `RegrasTests` (tag → versão, prerelease/rascunho/ilegível, ordem numérica, destaques, SHA-256, senha temporária, validação do usuário, papel dev sem escopo, os tipos novos e os cartões Impasses/Árbitro inclusive sem denominador); Infrastructure: `UsuariosTests` (senha temporária, redefinição, bloqueio pelo admin × temporário, dev sem painel, script do Azure SQL) e a qualidade com os eventos novos no escopo; Api: `VersoesApiTests` (GitHub de mentira: 200, cache, 401 anônimo, streaming do zip, o token só no api.github.com e nunca no armazenamento nem na resposta, 404, 503 sem REPO_KEY e com o GitHub fora do ar), `UsuariosApiTests` (401/403, senha só na criação/redefinição, `bloqueadoAte` no 423, dev 403 no painel) e `QualidadeDoFluxoApiTests`; web: `Landing.test.tsx` (proposta e download), `Usuarios.test.tsx`, os cartões em `Dashboard.test.tsx`; e2e: `usuarios.spec.ts` e `us405.spec.ts` |
| US #425 | Core: `Ed25519Tests` (vetores do RFC 8032, adulterada, chave errada), `CatalogoTests` (tag, delta, vitrine, validação da publicação), `LicencasTests` (emitir/ler, janela do 7º dia, recusas, link, gateway); Infrastructure: `Us425Tests` (isolamento do catálogo por empresa, adesão, assento por usuário, link de uso único, trial pela impressão, licenças, webhook, script `Us425.sql`); Api: `CatalogoApiTests` (delta e 304, download, vitrine, admin), `ContaApiTests` (adesão, link, recusa com `codigo`, renovação do token cortada pelo bloqueio, política por empresa, gestor 403 na outra empresa), `LicencasApiTests` (renovação com JWT e chave, cada recusa, a janela) e `VersoesApiTests` (download anônimo com limite por IP); web: `Catalogo`, `Conta`, `Comprar` e `EmpresaDoGestor`; e2e: `us425.spec.ts` (as telas novas com as capturas) |
| US #461 (API) | Core: `GeradorDoManifestoTests` (paridade com o `catalogo/base` v2 do git.kit em `Fixtures/catalogo-v2`: mesmos hashes, dependências, `requer` e manifesto regravado igual; versão + 1, retirados, o que volta, sem mudança), `FormatosDoCatalogoTests` (os 58 arquivos do git.kit regravados no formato canônico com o mesmo hash, a ordem do `SkillJson`, as mensagens do app), `RegrasDosCatalogosTests` (slug, nome do app, situação do rascunho, dependências, colisões) e `ContratoDoManifestoTests` (o conjunto de fixtures compartilhado com o git.kit); Infrastructure: `Us461Tests` (básico semeado, slug único, rascunho → release → versão nova só do alterado → retirada, o zip e o manifesto, a dependência que recusa, as colisões, o isolamento da distribuição, o gestor e os assentos de Empresas, script `Us461.sql`); Api: `CatalogosApiTests` (só o admin gere; a empresa vinculada vê, baixa e recebe 304, a outra e o anônimo recebem 404; o SHA-256 do zip; o 409 com o relatório; a importação do GitHub e as rotas antigas servindo o básico do site na `catalogo-v3`; o histórico 3, 2, 1) |
| US #461 (web) | Vitest: `componentes/Compartilhados.test.tsx` (iniciais, selo, barra de uso, copiar, abas pelo teclado, chips, página e painel lateral), `Empresas.test.tsx` (assentos em uso, gestor, copiar o código, painel lateral, convite na linha), `Usuarios.test.tsx` (busca e filtros, menu ⋯, painel lateral), `Catalogo.test.tsx` (vitrine com busca, chips e categorias; a rota antiga na aba Releases; o cartão da release, o copiar do SHA-256, o manifesto e o zip; o seletor de catálogo), `catalogo/conteudo.test.ts` (o JSON da skill e do plan na ordem do dev.kit, as mensagens do app, o envelope do fluxo e a prévia) e `catalogo/Catalogos.test.tsx` (cadastro, slug, empresas e a colisão, itens, editor, retirar, publicação com o 409 da dependência e o sucesso indo para Releases); e2e: `us461.spec.ts` (catálogo → empresa → skill e fluxo → publicar → release e manifesto → `/api/catalogos/meus` só para a empresa relacionada; o nome por US faz ele rodar depois do `fluxo.spec.ts`, que pressupõe a base nova com a senha inicial do admin) e `usuarios.spec.ts` no layout novo |
| US #491 | Infrastructure: `Us491Tests` (o mesmo caminho escrito de dois jeitos é um repositório e gravar de novo troca a descrição, o filtro, o isolamento entre empresas, a convenção da empresa só do gestor e a do time de qualquer um, cada time vê só a dele, vazio apaga, script `Us491.sql`); Api: `ConhecimentoApiTests` (o dev grava e consulta o catálogo, 400 sem descrição, a outra empresa não vê, admin 403 e anônimo 401; a convenção da empresa 403 ao dev e 200 ao gestor, a do time do dev, o nível do usuário 400, o `?time=` trazendo a da empresa e a do time) |
| US #602 | Core: `RegrasDosRelatosTests` (categoria e texto, a captura PNG/JPEG no teto, os segredos mascarados antes do caminho e do e-mail, estado e resposta validados, o resumo da lista); Infrastructure: `Us602Tests` (gravação nova, mascarada e ligada ao usuário e à empresa, a chave do cliente idempotente, filtros e paginação com o gestor só na empresa dele, responder um novo o leva a em análise, `/meus` só do autor, script `Us602.sql`); Api: `RelatosApiTests` (401 sem login e o dev fora da gestão, o gestor só na empresa dele, o admin vê todos, filtra, muda o estado e responde, o usuário e a empresa do token, o reenvio sem duplicar, o segredo mascarado no servidor, a captura acima do teto recusada, o 429 com `Retry-After`); web: `Relatos.test.tsx` (a lista com os selos e os indicadores, a query dos filtros, o filtro de empresa só do admin, o detalhe com o contexto em `<pre>` e a captura, trocar o estado e responder atualizando lista e detalhe, a mensagem do 403); e2e: `us602.spec.ts` (dois devs de duas empresas, por adesão, relatam pela API; o admin vê, filtra, leva a em análise e a resolvido e responde; o gestor vê só o da empresa dele; o dev lê a resposta em `/api/relatos/meus`; capturas `80`–`84`) |
| US #669 | Core: `RegrasDoDesignTests` (a URL normalizada em https/ssh/.git/maiúsculas/pasta, o nível usuário de quem não tem empresa, o escopo das claims, a visibilidade da regra do contexto, quem apaga, o teto do plano e a validação do envio); Infrastructure: `Us669Tests` (script `Us669.sql` contra o modelo, o armazém em disco preso à pasta, o envio cuja imagem falha sem deixar versão); Api: `DesignApiTests` (o envio com o branch como atributo e o mesmo repositório escrito de dois jeitos num projeto, o link estável devolvendo o PNG, a visibilidade usuário × equipe × empresa × outra empresa, quem não tem empresa no nível usuário, o reenvio idempotente e o 409, apagar autor/gestor/terceiro, a sincronização com `?desde=`, o 413 do teto); web: `componentes/DesignCompartilhado.test.tsx` (a comparação, a aba com o uso do teto e as colunas, o link copiado, o Apagar só de quem pode, o filtro de branch e o modo Uma, o escopo Minhas e o vazio, `/design` sem empresa, a página do link estável e a versão que não existe mais); e2e: `us669.spec.ts` (o envio multipart com o PNG gerado no teste, a comparação lado a lado, o branch, o Sobrepor e o link estável; capturas `90`–`92`) |
| `web/e2e` | Playwright: base nova → login do admin → troca → registro da máquina → lote (e reenvio) → números no dashboard → a exceção não classificada aparece com o trace sem caminhos e é resolvida; landing → seções e recursos da proposta (abas, moldura, legenda) → comparativo e empresas pelo menu → pedido de demonstração → o admin vê e exclui no dashboard; a home em desktop e celular, claro e escuro (US #467) |
