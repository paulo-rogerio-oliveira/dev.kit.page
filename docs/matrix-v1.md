# Matrix v1: modelo e operação

Os itens 1 e 2 entregam dados, persistência, operações e distribuição automática
aos agentes pelo host. O editor visual pertence aos próximos itens. Uma edição
da matriz nunca concede autorização de envio, PR, integração ou publicação.

## Identidade e revisões

`DocumentoMatrix` identifica `aplicacao`, `tipo`, `id` e `projeto` (slug opcional).
A aplicação é uma identidade estável escolhida para um conjunto de repositórios;
não é o nome da equipe. O mapa tem `id == aplicacao`. Duas aplicações da mesma
equipe têm streams independentes. Cada matriz tem seu próprio id de demanda.

Há quatro streams, versionados separadamente:

- `mapa`: repositórios, componentes com orientação reutilizável, layers livres e
  relações direcionadas com origem/destino, layers, gatilho, motivo, propagação e
  verificação. Relações são dados da aplicação e não criam Rules.
- `matriz`: revisão fixa do mapa, componentes/layers configuráveis e células com
  ids estáveis, obrigação, aplicabilidade/justificativa, origem, responsáveis,
  critério e evidência esperada. Uma célula aplicável precisa de dono. O editor
  não aceita reciclar um id em outro cruzamento ou renomear o id de um cruzamento
  já existente. Vários responsáveis são explícitos, inclusive por item do projeto.
- `vinculo`: associa a identidade do responsável à sessão após o disparo. A matriz
  pode ser atribuída antes de existir sessão. O vínculo é conferido contra a sessão
  local e branch do item; remotamente, contra máquina do usuário e retrato persistido.
  Uma matriz avulsa recusa sessão de projeto/item nos dois canais. Na leitura local,
  isso inclui branches secundários, branches customizados e vínculo não confirmado.
- `registro`: implementação ou avaliação, célula, responsável, revisão da definição,
  resultado, justificativa, evidências e SHAs completos por repositório. Não altera
  definição nem atribui nota aos avaliadores do fluxo.

Toda gravação recebe `EdicaoMatrix { versaoEsperada, documento }`. Zero cria;
qualquer versão diferente da atual é conflito, inclusive entre processos. Autor
e horário vêm do executor, não do arquivo. As revisões são imutáveis. A edição
retorna células e responsáveis afetados, incluindo antigos e novos donos. O helper
`RegistroVigente` compara a definição avaliada com a atual: alteração em outra
célula conserva a avaliação anterior. Mudanças de eixos afetam apenas as células
que os utilizam; eixo sem células e reordenação preservam os resultados. Trocar a
referência de origem do mapa, mantendo obrigações/eixos efetivos, também preserva
as avaliações. O registro histórico continua guardado em todos os casos.

## devcli

Use `devcli matrix --help` para as opções. A saída é JSON legível em todas as
operações. Cada escrita primeiro simula; repita com `--aplicar` após conferir.

```text
devcli matrix --aplicacao app --tipo mapa --arquivo mapa.json
devcli matrix --aplicacao app --tipo mapa --arquivo mapa.json --aplicar
devcli matrix --aplicacao app --tipo matriz --id demanda --gerar 1 --projeto projeto
devcli matrix --aplicacao app --tipo matriz --id demanda --arquivo matriz.json --aplicar
devcli matrix --aplicacao app --id demanda --atribuir celula-1 --responsavel task-1 --item 1 --versao 1
devcli matrix --aplicacao app --id demanda --versao 2 --recorte task-1
devcli matrix --aplicacao app --id demanda --historico
devcli matrix --aplicacao app --tipo mapa --versao 1 --sugerir chat --gatilho "alterar chat"
devcli matrix --evento gatilho:123
```

`--gerar` copia eixos e fixa a revisão, com células vazias para preenchimento
explícito. Não infere obrigações nem responsáveis silenciosamente. O arquivo da
matriz preenchida contém `matriz.revisaoDoMapa`, `componentes`, `layers` e `celulas`.
Uma task avulsa omite `projeto`; a identidade da demanda continua sendo `id`.
Para associar uma sessão, envie documento do tipo `vinculo`, com `responsavel`,
`sessao`, `item` opcional e `maquina` no remoto. Evidência/avaliação usa `registro`.

`--remoto` usa a API autenticada com o login do dev.kit. Streams recebidos da API
mantêm a origem remota também sem essa opção, inclusive novos registros da mesma
demanda. Falha remota não vira sucesso local. `impacto --sugerir` continua cuidando das sugestões de Rules, sem
interferência deste comando. A operação está disponível ao serviço e desktop
através de `ServicoMatrix`/`IArmazemMatrix`; nenhuma ação visual foi adicionada.

## API e compatibilidade

`GET /api/matrix/{aplicacao}/{tipo}/{id}` retorna histórico; `PUT` recebe a edição
e só grava com `?aplicar=true`. Ausente/falso simula as mesmas validações. O recurso
opcional `matrix-v1` é anunciado no heartbeat. `IMatrixRemota` não modifica os
contratos antigos de projetos e o cliente anterior não consegue apagar a matriz
ao reenviar um projeto sem campos novos.

`GET /api/matrix` acrescenta descoberta dos cabeçalhos (aplicação, tipo, id,
projeto e última versão). O catálogo aplica o mesmo ACL antes de retornar qualquer
identidade; não revela documentos de outro usuário/projeto. Não altera as rotas
existentes nem exige campos novos de clientes anteriores. API anterior pode
responder 404: task sem origem remota continua como antes; matriz já vinculada à
API registra pendência em vez de tratar a avaliação como completa.

## Contexto e distribuição pelo host

O Orquestrador consulta pedido, Rules do escopo e mapa da aplicação pelos comandos
existentes. `--sugerir` seleciona relações por componente e gatilho explícitos e
devolve aplicação, revisão do mapa, ID da relação, motivo e obrigações candidatas.
Não grava células. Cabe ao Orquestrador revisar a pertinência e editar a matriz
concreta, com responsáveis e versão esperada; novas descobertas exigem proposta
explícita, preservando as Rules reutilizáveis e os demais projetos.

O host resolve atribuições de item pelas sessões e branches cadastrados, incluindo
branches customizados. Avulsa usa vínculo explícito. Continuidade de fluxo
(`OpensTask`, incluindo Implementador da tela) persiste `MATRIX_ORIGEM` nos
parâmetros da sessão; a cadeia é relida após reinício ou troca de agente.

`ContextoDaMatrix` atende a composição inicial da task e os dois caminhos dos
gatilhos: `AbrirTaskAsync` e `AvisarTaskAbertaAsync`. Roteiro padrão e prompt
próprio recebem células atribuídas, dependências com seus donos, IDs e revisões.
Cada evento distingue a sessão efetivamente avaliada da origem do vínculo da matriz
e fixa um documento imutável `gatilho:<execução>` no journal do host,
consultável por `devcli matrix --evento`. A referência acompanha a entrega e a
correlação já existentes no evento, inclusive os SHAs e a evidência da entrega
conferidos pelo host, que ficam também no documento imutável. Reavaliar fixa um
novo documento sem substituir o anterior. Falha de leitura impede compor o evento
como avaliação completa; a execução falha com pendência explícita.

A revisão efetiva muda quando obrigações/eixos utilizados do responsável mudam.
A revisão do contexto considera também as dependências; `celulasAlteradas`
destaca a mudança. Obrigações alheias não são cobradas como parte da entrega.
A matriz completa permanece consultável na revisão indicada.

`SincronizacaoMatrix` consulta a API autenticada na abertura/evento e a cada
30 segundos no host, sem depender de turno do Orquestrador. Importa o histórico
imutável e recusa definições locais/remotas divergentes sem sobrescrever nenhuma.
A reserva de entrega e o aviso entram na mesma transação SQLite em `TaskMessages`;
processos concorrentes, repetição, reinício e troca de agente reutilizam a mesma
entrega. Cada revisão pertinente recebe uma identidade própria. Alteração apenas
local também usa essa fila, inclusive sem login no site. Salvar uma definição
não reinicia uma execução nem concede PR, merge ou publicação.

API armazena streams por empresa (ou usuário sem empresa), aplicação, tipo, id e
versão. Aplicação/demanda sem projeto pertence somente ao criador. Com projeto,
reutiliza o ACL explícito: leitor consulta, editor/dono grava; sem acesso retorna
404, sem revelar conteúdo. Slug ambíguo é recusado. O escopo do stream é imutável.
Os contratos são espelhados nos dois repositórios para publicação independente;
uma evolução deve atualizar ambos. Contrato futuro/campo desconhecido é recusado
para evitar sobrescrita por um editor incapaz de preservar o novo dado.

## Ativação e impacto nos quatro braços

- Serviço/local: `AppDatabaseMatrix` cria journal SQLite aditivo/idempotente;
  `ServicoMatrix` valida referências e sessão sem dependência da UI.
- devcli: `MatrixTool`, contrato de ajuda e comandos opcionais remotos; a paridade
  das ações visuais existentes continua válida, pois não há nova ação de view model.
- API: `MatrizesNoBanco`, ACL de `ConsultaDosProjetos`/`RegrasDeAcesso`, migração
  `20261110170000_Matrix` e script SQL Server `Matrix.sql`.
- Desktop/Board: telas não alteradas neste item sem editor visual. Consumidores
  antigos continuam usando `ProjetosDoDevKit` e `/api/projetos`; o armazenamento
  da matriz fica independente dos snapshots antigos. Layout e recapturas de tela
  dispensados; a demonstração do recurso usa dados fictícios e banco isolado.

Publicação: aplicar o script idempotente `api/scripts/sqlserver/Matrix.sql` antes
da API nova (passo Matrix.sql no job SQL, desligado por padrão), conferir a tabela
e índice único e só então disponibilizar o cliente novo. SQLite recebe a migration
na inicialização habitual. Validar simulação, aplicação, releitura e conflito em
ambiente temporário, além de 404 sem acesso e 403 como leitor. Repetir a migração
preserva revisões. Nenhuma publicação de produção é executada por este item.

Após compilar os projetos afetados, `node build/evidenciar-matrix.mjs` no git.kit
executa API e CLI reais em bancos novos fora dos clones. Requer os dois repositórios
lado a lado, Node e .NET. Copia os binários da API para não travar o build e encerra
somente o processo criado pela demonstração. A saída indica a transcrição JSON e
HTML, sem credenciais ou autoria pessoal, para captura da evidência técnica.
