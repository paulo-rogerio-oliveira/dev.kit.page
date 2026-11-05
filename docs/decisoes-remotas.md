# Decisões remotas — fase 3, item 9

O Board e o Chat apresentam os campos `pergunta`, `opcoes` e `jaTentei` recebidos no retrato. Mensagens da conversa continuam sendo texto e nunca são interpretadas como autorização. Os endpoints exigem a política autenticada existente, inclusive troca de senha concluída, e isolam pelo usuário do JWT.

## Contrato aditivo

O heartbeat anuncia `decisoes-com-snapshot`. A pendência de aprovação passa a aceitar `snapshot` opcional: origem, destino, SHA completo, notas por avaliador com mínima, testes aprovados/falhos, conclusão da regressão, contagem de arquivos/commits, alterações e capturas com título/URL. O retrato sem esse campo continua aceito, mas não permite aprovação remota. Capturas aceitam HTTPS ou `/api/design/`; nenhum token é incluído na URL. Todos os textos novos passam pela sanitização existente.

- `GET /api/remoto/maquinas/{maquina}/decisoes`: pendências, hash do snapshot, bloqueio e resposta registrada.
- `POST /api/remoto/maquinas/{maquina}/decisoes/{pendencia}`: `{snapshotId, acao, opcao?, codigo?}`. `responder` exige uma opção exata da pergunta; `ajuste` devolve sem autorizar; `aprovar` exige todas as notas suficientes, regressão concluída sem falhas e TOTP válido. Snapshot alterado ou já respondido retorna 409. Segundo fator ausente/incorreto/expirado/reutilizado retorna 403.
- `GET /api/remoto/maquinas/{maquina}/decisoes/respostas`: outbox auditada do dono, até 100 respostas registradas.
- `POST /api/remoto/maquinas/{maquina}/decisoes/respostas/{id}/recebida`: primeira retirada revalida snapshot, evidências e prazo de cinco minutos, marca recebida e devolve a autorização estruturada. Repetição ou autorização vencida retorna 409. Recebida não significa executada. A máquina deve persistir o recebimento e conferir também o snapshot local antes de chamar seu portão de integração; nunca reconstruir autorização a partir do chat. O adaptador desktop pertence ao repositório `git.kit`.

O hash inclui toda a pendência e suas evidências; trocar destino, SHA, nota ou captura exige nova revisão. Escrita da resposta e consumo usam transação serializável com o retrato. Índice único filtrado impede duas decisões não vencidas para o mesmo snapshot. O SQL guarda autor (usuário autenticado), momento, máquina, escolha, origem/destino/SHA e segundo fator confirmado; código e chave não entram na auditoria.

Após cinco minutos sem retirada, a consulta apresenta a última resposta como `vencida` e reabre os botões para nova revisão. A nova tentativa relê o snapshot, exige um código TOTP novo para aprovar e recebe outro ID; a tentativa anterior é preservada como vencida na mesma transação. A outbox não oferece respostas fora do prazo. Uma tentativa vencida nunca pode ser retirada e uma já recebida continua impedindo outra decisão para o mesmo snapshot. Isso também permite renovar uma resposta comum vencida, sem segundo fator.

## Autenticador

`GET /api/remoto/segundo-fator` informa ativação. `POST .../preparar` exige a senha atual e devolve uma chave temporária para cadastro manual no aplicativo autenticador, com `Cache-Control: no-store`. `POST .../confirmar` exige o primeiro código para ativar. Não há substituição de autenticador ativo por esse caminho.

TOTP segue a RFC 6238 (SHA-1, seis dígitos, 30 segundos, tolerância de um passo). Cada código pode ser consumido uma vez, inclusive entre decisões distintas. Cinco tentativas em cinco minutos, além do rate limiter de usuário existente. Preparação vence em dez minutos. A chave é protegida por ASP.NET Data Protection. No ambiente publicado, preservar e compartilhar o key ring entre instâncias antes de habilitar o recurso; perder essas chaves impede a verificação dos autenticadores existentes. Não registrar chave, senha ou código em logs.

## Banco e validação

SQLite: migrations `20261105024000_DecisoesESegundoFator` e `20261105143000_NovaTentativaDaDecisao`; a segunda substitui somente o índice, preservando o histórico. Azure SQL existente: executar o script idempotente `api/scripts/sqlserver/Decisoes.sql` pelo processo de publicação autorizado antes de publicar a API. Esta task não publica nem altera produção.

Validar com `DecisoesTests` (vetores oficiais TOTP e reuso), `DecisoesApiTests` (três níveis, isolamento, snapshot e consumo) e `Board.test.tsx` (pergunta, revisão e confirmação). Comparar os dois temas e viewports com app-remoto v228/v230 e v231/v233. Dados de notas e testes usados na demonstração são fictícios e não aprovam este item.
