# Publicacao do basico pelo Git

O CD publica exatamente o zip validado por `dev.kit.catalogo`. O site continua sendo a fonte de distribuicao e de historico. A automacao nao importa para o editor nem publica o seu rascunho: os rascunhos existentes permanecem intactos. O manifesto do Git e a fonte do conteudo e da numeracao dos itens: ele nao precisa ser a evolucao de um passo da release publicada. A gestao manual usa as mesmas releases e a mesma numeracao do catalogo; se ela chegar a versao do Git enquanto ele era validado, o CD recusa a versao obsoleta (409 com a versao publicada e a recebida). Aprovar outra release com versao maior que a publicada (o salto e aceito); se a gestao manual retirou ou avancou um item, refletir isso no manifesto do Git antes, pela regra de sequencia abaixo. Nunca renumerar bytes ja aprovados durante o upload.

## Contrato

Somente a politica `PublicarBasicoGit` aceita as rotas sob `/api/catalogos/basico/automacao`:

- `POST /conferir`: `PublicacaoGitV1`, sem escrita. Confere a mesma regra da publicacao.
- `POST /publicar`: o mesmo envelope, com zip em base64. Devolve `ConferenciaGitV1` com recibo.
- `GET /recibos/{sha}`: recibo persistido, ou 404.

Envelope: `sha` (40 hex minusculos), `referencia` (`refs/tags/catalogo-vN`), `motor` (SHA completo do gerador), `sha256` (64 hex), `versao` e `pacote`. Limites: 16 MiB compactados e descompactados, 2000 itens, 1 MiB por arquivo, sem arquivos extras, links, caminhos externos ou repetidos. O pacote confere com o manifesto: hashes normalizados pelo motor, nomes, dependencias e MinDevKit de cada item ate o do manifesto. Uma origem historica indisponivel recusa o CD; nao e prova de catalogo vazio.

Sequencia diante da release publicada (`RegrasDoCatalogoBase.ProblemaDaSequencia`): o manifesto nao e regenerado a partir da release anterior. A versao recebida deve ser maior que a publicada; o salto e aceito, e versao igual ou menor responde 409. Item presente nas duas releases nao pode ter versao menor; item novo entra livremente com a versao do Git. Os retirados seguem a regra do gerador: o item que sai vai para os retirados, e o retirado continua listado ou volta ao catalogo, nenhum com versao menor nem ativo e retirado ao mesmo tempo. Cada recusa diz a versao publicada e a recebida, ou o item e as duas versoes. A API nao reescreve o campo `catalogo` dos itens, e a delta das maquinas filtra por ele: item novo ou alterado com `catalogo` igual ou menor que a versao publicada nao chega a quem ja tem essa release.

A transacao serializavel inclui release, pacote e recibo. O armazem e relido e seu hash conferido antes do commit. Indices unicos de SHA e de versao impedem duplicacao entre instancias. Mesmo SHA/ref/motor/versao/hash devolve o recibo anterior inclusive depois de outras releases. Divergencia ou versao ocupada responde 409, sem sobrescrever. Uma corrida com a publicacao manual pode devolver 409: consultar o recibo e repetir somente o mesmo artefato. Falha de armazenamento reverte tudo. O download publico continua nas rotas existentes da release; o painel consulta a mesma tabela e apresenta a mesma versao/hash.

O CLI `catalogo-publicacao` faz a conferência ou a publicacao sem usar a sessao pessoal. `--publicar` sem `--aplicar` chama apenas `/conferir`; `--conferir` nunca grava. O sucesso de `--publicar --aplicar` exige GET do manifesto, zip e recibo persistido, com identidade/versao/hash iguais aos do artefato. Uma falha de GET retorna exit 1 mesmo se o POST ja persistiu; consultar o recibo e repetir o artefato imutavel para recuperar. Uma recusa imprime o HTTP e o motivo devolvido pela API (o `detail` do ProblemDetails), numa linha e sem token; corpo que nao e JSON nao e impresso. `skills --publicar` permanece exclusivo da empresa.

## Identidade restrita e configuracao pendente

O pacote pode conter `gatilhos/` do catalogo atual, alem de skills, plans e fluxos. O publicador le o envelope portatil e confere o hash pela regra do motor, incluindo referencias de Rules e gatilhos adotados pelos fluxos. Isso apenas distribui conteudo: nao cria editor, nao executa gatilho no site e nao amplia os tipos aceitos pelas rotas atuais de edicao/empresa.

Nao ha segredo de publicacao permanente. O esquema JWT adicional valida assinatura RS256, issuer `https://token.actions.githubusercontent.com`, audiencia e validade pelos metadados oficiais do GitHub. Depois exige `repository`, `repository_id`, `environment`, `event_name=release`, ref `refs/tags/catalogo-vN` e `workflow_ref` de `.github/workflows/catalogo.yml` nessa ref. SHA e ref do envelope devem coincidir com as claims assinadas. Nao depende do formato mutavel do `sub`. JWT pessoal/admin e chave/token de maquina nao abrem estas rotas; OIDC nao abre a administracao de outros catalogos.

Antes de habilitar producao, o desenvolvedor deve publicar a API e aprovar a configuracao:

1. SQLite: migration `20261119000000_RecibosDoCatalogoGit` pelo inicializador habitual. SQL Server existente: aplicar `api/scripts/sqlserver/CatalogoGit.sql` pelo provisionamento autorizado. Base vazia nasce pelo modelo habitual.
2. Configurar `CatalogoAutomacao__Repositorio`, `CatalogoAutomacao__RepositorioId` (ID numerico imutavel), `CatalogoAutomacao__Ambiente=catalogo-producao` e `CatalogoAutomacao__Audiencia` (audiencia exclusiva acordada). Vazios recusam a identidade. Permitir saida HTTPS para discovery/JWKS do GitHub; sem isso a autenticacao falha, sem fallback pessoal.
3. No repositorio do catalogo, definir `CATALOGO_BRANCH_INTEGRADA` para o branch de release integrado e protegido. Proteger as tags `catalogo-v*` e o workflow; nao permitir que branch de item seja definido como integrado.
4. Criar o ambiente `catalogo-producao` com aprovadores obrigatorios e regras de tags de release. Definir nele `CATALOGO_API_URL` (raiz HTTPS da API) e `CATALOGO_OIDC_AUDIENCE` (mesmo valor da API). A aprovacao ocorre depois do CI, antes da emissao de OIDC.
5. O acesso privado aos clones do motor/CLI continua por `REPO_KEY` ja suportado, com leitura de conteudo apenas; nao e credencial de publicacao. Nao imprimir tokens ou colocar valores em arquivos versionados.

Referencia das claims e descoberta: [GitHub OIDC](https://docs.github.com/en/actions/reference/security/oidc).

Esta entrega nao configura ambiente/segredos, nao cria tags/releases e nao publica producao. Testes usam assinatura local de teste, TestServer e SQLite temporario, com GitHub e armazenamento isolados. O diagnostico remoto 503 e o consumo/setup pertencem ao item 15.
