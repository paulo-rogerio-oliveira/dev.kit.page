# Matrix: publicação, ativação e recuperação

Este roteiro prepara o projeto matrix completo, incluindo editor, distribuição,
recibos e avaliação. Sua execução em um ambiente publicado cabe à etapa autorizada
de publicação. O item 6 valida a preparação em bancos temporários e não opera Azure,
não publica em produção e não cria uma execução. Preencher a matriz ou obter cobertura
completa não aprova projeto, envio, PR, merge, integração ou publicação.

## Pré-checagens e registro da revisão

1. Registrar os SHAs completos de `git.kit` e `dev.kit.page`, a versão do pacote do
   host e a revisão do catálogo. Usar o mesmo conjunto de revisão na homologação;
   número de versão do pacote, sozinho, não comprova capacidades.
2. Conferir `Banco__Provider`, `ConnectionStrings__DevKitPage` e a base efetivamente
   escolhida. SQLite precisa de pasta persistente gravável e instância única; SQL
   Server existente precisa dos scripts, pois `EnsureCreated` não evolui tabelas.
   Não trocar provider ou apontar a demonstração para a base do usuário.
3. Conferir as configurações obrigatórias pelo procedimento de
   [publicação Azure](publicacao-azure.md). O job `configuracao` deve passar antes de
   `sql` e `deploy`. Matrix não acrescenta segredo, fila, app setting ou chave de
   habilitação. Login válido, empresa/usuário correto e ACL do projeto continuam
   necessários. Registrar nomes e resultado das verificações, sem valores secretos.
4. Antes de migração real, conferir backup consistente e recuperação testada fora
   do ambiente original. Guardar o journal, histórico das migrations e dados locais
   do host; copiar SQLite aberto sem sua consistência/WAL não comprova backup.
5. Conferir URL da API usada pelo host e `VITE_API_URL` usada no build do Board.
   Conferir catálogo compatível com `matrix` e seus parâmetros usando
   `devcli matrix --contrato`, `devcli matrix --help` e `devcli paridade`.
   Não sincronizar catálogo antigo sobre customizações do desenvolvedor.

## Esquema e ordem de publicação

| Superfície | Preparação | Conferência |
| --- | --- | --- |
| API com SQLite | `20261110170000_Matrix`, depois de `20261108060000_DispositivoDaDecisao`, pelo inicializador habitual | Migration aplicada uma vez; nenhuma mudança pendente no modelo |
| API com SQL Server existente | `api/scripts/sqlserver/Matrix.sql` antes da API nova | Tabela, colunas e os dois índices presentes |
| Host/serviço/desktop/devcli | Inicialização aditiva de `TaskScores.RevisaoMatrix TEXT NOT NULL DEFAULT ''`; journal local `RevisoesMatrix` criado no primeiro acesso | Notas antigas e histórico preservados após reabrir |
| Recibos e avaliação | Streams opcionais no mesmo journal, sem nova tabela no Page | Clientes atuais recebem `evento` e `coberturas`; antigos recebem formato compatível |

A ordem explícita do job `sql` em `.github/workflows/main_devkit.yml` é:
`Remoto.sql`, `Provisao.sql`, `Conversa.sql`, `Decisoes.sql`, `Push.sql`,
`CatalogoRemoto.sql`, `ClienteDaMaquina.sql`, `BoardRemoto.sql`,
`ArtefatosDosProjetos.sql`, `Matrix.sql`. Os scripts históricos anteriores ao
controle remoto permanecem pré-requisitos da base existente; o job não os substitui
nem os reaplica indiscriminadamente. A lista central fica em
[publicacao-azure.md](publicacao-azure.md#scripts-sql-do-controle-remoto-na-ordem).

O job SQL continua opcional, `aplicar_sql=false` por padrão. Em uma publicação
autorizada que precise atualizar o esquema, habilitar explicitamente esse input
ou aplicar o script pelo procedimento aprovado, antes da API. Falha do SQL impede
o deploy; um push comum não aplica o script. Não usar o workflow real como teste
de idempotência: o mesmo workflow também publica a aplicação.

Publicar na ordem: esquema → API → Board → pacote compatível do host/serviço,
desktop e devcli → catálogo compatível, respeitando os portões de cada entrega.
Os workflows da API e da web são independentes: conferir ambos antes de ativar o
uso remoto. A matriz permanece em journal separado dos snapshots de projetos.
Um cliente antigo reenviando `/api/projetos` não remove a matriz.

Conferência SQL somente leitura, pelo operador autorizado, na base selecionada:

```sql
SELECT OBJECT_ID(N'dbo.RevisoesMatrix', N'U') AS Journal;
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.RevisoesMatrix') AND name IN
  (N'IX_RevisoesMatrix_Espaco_Aplicacao_Tipo_DocumentoId_Versao',
   N'IX_RevisoesMatrix_ProjetoId');
SELECT Tipo, COUNT_BIG(*) AS Revisoes, MAX(Versao) AS Ultima
FROM dbo.RevisoesMatrix GROUP BY Tipo;
```

Esperado: journal não nulo, índice de stream único e índice de projeto. As colunas
de identidade `Espaco`, `Aplicacao`, `Tipo`, `DocumentoId` têm collation
`Latin1_General_100_BIN2`, conforme o modelo SQL Server. Preservar esse significado
de identidade ao comparar dados. Contagens são diagnóstico; não comprovam conteúdo
sozinhas. Para ensaio de repetição, comparar também corpos/autoria/horários/versões
na base temporária, sem dados pessoais no relatório.

## Capacidades e convivência entre versões

| Combinação | Comportamento e condição de ativação |
| --- | --- |
| Cliente anterior sem Matrix + API nova | Projetos e tarefas continuam pelos contratos anteriores; não ativar obrigações para um agente incapaz de consumi-las |
| Cliente `matrix-v1` + API nova | Sem `X-DevKit-Matrix-Avaliacao: 1`, API omite stream `evento`, campos novos de registro e `matriz.coberturas` na resposta; omitir coberturas na edição conserva associações existentes |
| Cliente atual + API nova | Envia `X-DevKit-Matrix-Avaliacao: 1`; permite recibos/avaliação vinculados e cobertura por relação |
| Cliente atual + API anterior/indisponível | Falha não vira confirmação local; matriz remota vinculada permanece pendente. Atualizar API antes de habilitar uso remoto |

A resposta autenticada do heartbeat habitual `POST /api/remoto/heartbeat` anuncia
`recursos` com `matrix-v1` e `matrix-avaliacao-v1`. Conferir os dois no ambiente de
homologação; o anúncio da API não comprova atualização do host ou do Board. A
negociação da resposta de Matrix usa o cabeçalho, não uma app setting. Não enviar
documento futuro a um editor antigo: contrato/campo desconhecido é recusado.
Uma lista vazia de `coberturas` remove associações explicitamente; não usar como
substituto de campo omitido. Registros legados permanecem histórico e não ganham
validade de avaliação sem recibo do host.

## Ativação controlada em homologação

1. Com usuário e projeto fictícios, verificar `/health`, heartbeat e leitura de
   `GET /api/matrix`. Não avançar com erro de esquema, login, ACL ou capacidade.
2. Usar uma aplicação/demanda de teste: simular uma `EdicaoMatrix`, conferir células
   e responsáveis afetados, aplicar a mesma revisão e reler o histórico.
   Exemplo CLI: `devcli matrix --aplicacao demo --id demanda --arquivo edicao.json
   --remoto` e, após conferir, o mesmo comando com `--aplicar`.
3. Repetir versão esperada antiga: conflito 409 sem nova revisão. Cliente sem
   acesso recebe 404; leitor do projeto consulta e recebe 403 ao editar.
4. Conferir a mesma revisão em Impactos no desktop e no Board (inclusive celular)
   e por `devcli matrix --aplicacao demo --id demanda --historico --remoto --json`.
   IDs, aplicação e origem remota devem coincidir. Não usar matriz de produção no ensaio.
5. Em uma task de teste já autorizada pelo fluxo, conferir recorte, dependências e
   aviso da revisão pelo host. Após reabrir, a entrega pertinente não duplica;
   salvar a definição não cria/reinicia tasks. Este roteiro não autoriza dispará-las.
6. No evento de avaliação já existente, consultar `devcli matrix --evento
   gatilho:<execucao> --json`. Conferir sessão, avaliador, recorte, SHAs e manifesto
   recebido; registrar resultados por obrigação antes da nota global.
7. Mudar somente uma obrigação de teste por edição explícita: o resultado afetado
   fica obsoleto com motivo, os demais mantêm validade e o histórico continua legível.
   `devcli matrix --aplicacao demo --id demanda --cobertura --json` aponta pendências;
   cobertura incompleta não pode liberar o fechamento. Não fechar projeto neste ensaio.

Guardar revisão, comandos/resultados, logs e evidência da mesma entrega. Na preparação
do item 6, os passos que dependem de execução existente são cobertos pelos testes
focados de API/host dos itens anteriores, sem criar nova execução automaticamente.

## Diagnóstico e recuperação preservando histórico

| Sinal | Conferência | Recuperação |
| --- | --- | --- |
| Esquema incompleto / erro citando `RevisoesMatrix (Matrix.sql)` | Provider, base selecionada, log de inicialização, `/health`, tabela e índices | Aplicar script aditivo faltante na etapa autorizada; repetir em banco temporário antes de retomar |
| 401 / 403 / 404 | Login, papel de leitura/escrita, ACL, empresa, aplicação, id e URL | Corrigir configuração/acesso pelo canal existente; não copiar stream de outro usuário |
| 400 em `evento` ou campo desconhecido | Cabeçalho de avaliação, capacidades e versões de API/cliente | Atualizar o consumidor/produtor compatível; não apagar campos para forçar aceite |
| 409 / journal local e remoto divergentes | Histórico e origem da demanda nos dois lados | Preservar rascunho e ambas as revisões; comparar/reaplicar sobre a versão atual. Não trocar só `versaoEsperada` e sobrescrever |
| Recorte pendente / avaliação obsoleta | Vínculo da sessão, evento persistido, SHAs, manifesto, células e dependências alteradas | Restabelecer sincronia e usar o evento de reavaliação autorizado; não reutilizar recibo nem inventar nota |

Se for preciso recuar binários, suspender novas edições/avaliações pelo processo
operacional existente e voltar ao conjunto anterior compatível com o journal
preservado. Cliente antigo não deve editar dados de uma extensão que não conhece.
Não executar `Down` de `MatrixMigration` (ele remove a tabela), apagar journal,
limpar fila, recriar banco nem truncar revisões. Falha parcial pede correção para a
frente e repetição do script idempotente, conservando os registros já gravados.
Restaurar backup é último recurso explicitamente autorizado: ensaiar em base nova,
reconciliar revisões posteriores e conferir antes de qualquer troca de destino.
Restabelecida a versão compatível, reler histórico, refazer as prechecagens e
retomar a distribuição pela fila durável existente.

## Validação local verificável

- `MatrixPersistenciaTests`: base vazia, evolução desde a migration anterior,
  usuário antigo preservado, documentos legados/estendidos e repetição da inicialização.
- `EsquemaDoBancoTests`: modelo SQL Server e diagnóstico do script faltante.
- Testes de inventário de `PublicacaoDoControleRemotoTests`: ordem, presença,
  SQL optativo, falha impedindo deploy e ausência de operação destrutiva em Matrix.sql.
- No `git.kit`, `MatrixMigracaoTests`: nota anterior recebe contexto vazio,
  journal preservado nas reaberturas e nenhuma sessão criada pela migração.
- `MatrixApiTests`/`AvaliacaoMatrixApiTests` e testes Matrix do host verificam
  ACL, versão esperada, compatibilidade, recibos, distribuição e validade seletiva.
- `git.kit/build/evidenciar-matrix.mjs` demonstra API/CLI reais em SQLite novo,
  produzindo transcrição técnica para captura, sem tocar a instalação do usuário.

Os testes SQLite e a comparação com o modelo SQL Server não equivalem a executar
T-SQL em um servidor real. A repetição de `Matrix.sql` com histórico em SQL Server
temporário e a conferência pós-publicação permanecem checklist do operador antes
da ativação real. Nenhum resultado deste item representa publicação em produção.
