# Publicação e recuperação dos artefatos do projeto

O POST `/api/projetos/{id}/artefatos` recebe multipart (`arquivo`, `repositorio`, `sha`, `hash`) para pacote ou JSON (`repositorio`, `sha`, `url`, `ambiente`) para preview. Usa a sessão do desenvolvedor: dono/editor gravam, leitor consulta, outro usuário/empresa sem visibilidade recebe 404. O projeto compartilhado existe independentemente de Azure Boards. Nenhuma destas rotas cria work item, concede acesso ou compartilha um projeto local.

O servidor calcula SHA-256 e tamanho no arquivo recebido. O campo `hash` opcional deve conferir quando informado. O tamanho declarado pelo chamador não determina nem o limite nem o tamanho persistido. O cliente só confirma publicação com ID positivo, hash/tamanho iguais ao zip e link válido ainda vigente. Erro de transporte, resposta inconsistente ou registro recusado preservam arquivo/preview e estado pendente; executar novamente o mesmo ID repete apenas upload/registro.

## Repetição e concorrência

A coluna opcional `Publicacao` contém SHA-256 de uma sequência JSON sem ambiguidade: tipo, repositório, SHA normalizado e hash do pacote; para preview, tipo, repositório, SHA, URL e ambiente. A identidade pertence ao projeto, com índice único `(ProjetoId, Publicacao)` filtrado para valores não nulos. Não contém sessão, SAS ou chave de storage. POSTs repetidos retornam o mesmo ID, mesmo se a resposta anterior se perder; concorrência é resolvida pelo índice, descartando o arquivo do perdedor. Registros históricos preservam a coluna nula e continuam legíveis, sem migração destrutiva de dados.

O retry não renova a retenção. Pacote vencido não é revivido por um registro ainda presente. Depois da remoção pela retenção, outro POST pode registrar novamente o conteúdo. O nome do download da primeira publicação é preservado. Falha depois do commit permite recuperar o registro; falha antes dele permite limpar o upload incompleto. Se banco/storage impedirem a limpeza, a API continua falhando e o log conserva a causa original e a da limpeza; não retorna sucesso nem remove arquivo cuja publicação esteja confirmada.

## Download privado e renovação

No Blob, o link usa SAS somente leitura, HTTPS, com prazo limitado pela retenção. A conexão precisa poder assinar (AccountKey); essa capacidade é conferida antes de gravar bytes/linha ou executar retenção. No disco de desenvolvimento, o token DataProtection cifrado permite o download sem sessão até vencer. Preserve as chaves DataProtection e a pasta ao reiniciar a API; perder as chaves invalida links antigos, que podem ser renovados pela leitura.

`GET /api/projetos/{id}/artefatos` e `GET /api/projetos/artefatos` exigem sessão e respeitam a ACL. Cada leitura emite links atuais sem mudar ID, hash, tamanho ou retenção. O cliente `ArtefatosDaEmpresaClient.ListarAsync` expõe essa leitura para desktop/devcli; seleção e interface visual pertencem ao item 5. Download vencido/adulterado/ausente retorna 404, nunca arquivo vazio com sucesso. O consumidor relê a lista autenticada, encontra o ID e usa seu novo link; se o ID não vier, o artefato saiu da retenção.

## Ordem de implantação e diagnóstico

1. No ambiente autorizado, conferir backup e esquema. SQL Server: aplicar `api/scripts/sqlserver/Us693.sql` se necessário e o script idempotente `api/scripts/sqlserver/ArtefatosDosProjetos.sql`, que também acrescenta `Publicacao` e seu índice numa tabela existente. SQLite: a inicialização aplica `20261119090000_PublicacaoDoArtefato`. Esta implementação só executa migrations em bases descartáveis de teste; não aplica SQL nem altera retenção em produção.
2. Conferir armazenamento privado e configuração: `Artefatos__Armazem` (`disco`/`blob`, vazio herda Design), `Artefatos__ConexaoDoBlob` (vazia herda Design), `Artefatos__Conteiner` (padrão `artefatos`), pasta e permissões quando disco. Preservar `RetencaoDias` (7), `MaximoPorProjeto` (10) e `LinkHoras` (24) existentes.
3. Conferir `Artefatos__TamanhoMaximoMb` (100 por padrão), espaço temporário e limite do proxy/ingress: permitir pelo menos o teto do zip mais 64 KiB de envelope multipart. Kestrel é ajustado por pedido antes da leitura; limites externos continuam independentes. Em IIS, conferir também `requestFiltering/requestLimits/maxAllowedContentLength` e o limite do servidor. Um 413 externo pode vir como HTML; o transporte preserva o status mesmo sem ProblemDetails.
4. Publicar API antes do host. JSON, multipart e `ArtefatoV1` permanecem compatíveis com clientes antigos. O esquema opcional é compatível com a API anterior; não fazer rollback removendo coluna/dados. Depois atualizar o host; o Board existente já lê os mesmos GETs e não exige mudança visual neste item. Não publicar produção por esta task.
5. Verificar `/health`, logs e a operação recusada: ausência de tabela/coluna aponta o script; 401 pede login, 403 indica ACL, 404 preserva projeto/registro inacessível ou rota ausente; 413 distingue limite API de limite proxy pelo corpo/log; hash divergente é 400; falha de banco, storage ou assinatura é 500 com código para o administrador. Não tratar nenhum desses estados como publicado. Tokens e connection strings nunca entram nos logs do cliente.

## Evidências e validação integrada

`ArtefatosApiTests` usa HTTP TestServer, SQLite e disco descartáveis: ACL, JSON/multipart, download do mesmo zip com hash/tamanho/manifesto, repetição e link vencido/renovado. `ArtefatosDosProjetosTests` cobre migration/modelo/SQL, tamanho real e falhas de assinatura/interrupção usando storage isolado. Os testes do Core e do cliente usam SQLite e transporte/processos controlados para conferir estados, timeout, erros HTTP e retomada sem rebuild.

Essas regressões não constituem upload real no ambiente compartilhado. Item 6 deve validar com usuário real autorizado: sessão expirada, proxy efetivo e seu limite, storage privado com assinatura, POST/GET/listagem e download cujo SHA-256, tamanho e manifesto coincidam com o zip gerado, incluindo interrupção/renovação. Não usar credenciais ou dados de produção nos fixtures.
