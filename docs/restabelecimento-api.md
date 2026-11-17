# Recuperação da API: incidente de 16/11/2026

## Diagnóstico e procedência

A correção `064e75248c633b65e13ced0e41d48918fd42cd54` já estava incorporada
ao branch do item 1 quando a implementação começou. Ela registra dois sintomas
distintos: o `POST /api/remoto/conversa/falas` falhava por ausência de
`FalasRemotas.Sessao`, e a instância nova não abria a porta porque aguardava uma
trava de migrations do SQLite deixada por um processo encerrado. O relato anterior
situa a base em WAL no armazenamento de rede `/home` do App Service.

Essa procedência é o código e o roteiro existentes, não um novo acesso aos logs
históricos ou à base de produção. A reprodução isolada confirma os mecanismos de
falha e recuperação; não estabelece, sozinha, qual binário está implantado ou se
houve outra causa em uma rota não informada. O erro de container citado fora
deste projeto não constitui evidência deste incidente.

O item 1 acrescenta cobertura HTTP ao reparo existente, sem alterar configurações,
migrations, DTOs, permissões ou código operacional da API. Não é necessário
reaplicar uma correção já recebida apenas para produzir outro diff.

## Como distinguir os estados

| Verificação | Resultado | Interpretação |
| --- | --- | --- |
| Nenhuma porta aberta, probe de startup expirada | Sem resposta HTTP da aplicação | Investigar inicialização e logs do host; não chamar isso de 500 da rota |
| `GET /health` | 200 `Healthy` | Checks atuais de conexão, esquema e licenças aprovados |
| `GET /health` | 200 `Degraded` | Ler o log de esquema/licenças; o status HTTP sozinho não comprova saúde |
| `GET /health` | 503 `Unhealthy` | Falha de um check, por exemplo abertura real do banco |
| Rota autenticada sem Bearer | 401 | Autenticação exigida; não indica indisponibilidade |
| Usuário sem autorização | 403 ou 404 conforme contrato | Preservar ACL e isolamento; não ampliar permissão para contornar a falha |
| Limite atingido | 429 | Preservar limitação e `Retry-After` |
| Exceção não tratada na rota | 500 ProblemDetails | Correlacionar método/rota e extensão `codigo` com o log |
| Fila/licença indisponível | 503 conforme contrato | Não confirmar entrega, execução ou licença inexistente |
| Browser bloqueia origem/header | Erro CORS/rede | Conferir preflight e URL de build antes de concluir que houve 500 |

`ErroUtilDaApi` inclui o mesmo código no `detail` e no log. O Board apresenta o
`detail`; a leitura de `traceId` como alternativa no cliente não prova a causa de
um 500. O consumidor `ApiDaEmpresa` do git.kit preserva `detail`, `codigo` e status.

## Verificações isoladas

Os caminhos abaixo identificam o clone da task:

- `dev.kit.page/api/tests/DevKitPage.Infrastructure.Tests/SubidaDoSqliteTests.cs` reproduz a
  espera da trava, libera somente a abandonada, preserva a recente, aplica a
  migration pendente e verifica inicialização repetida e detecção de esquema.
- `dev.kit.page/api/tests/DevKitPage.Api.Tests/RestabelecimentoApiTests.cs` usa a API real da
  fixture, com SQLite temporário. Remove a coluna somente nessa base e verifica
  500, rota, código no log e ocultação do detalhe interno para o desenvolvedor.
  Reaplica a migration para frente duas vezes e confere a fala anterior, o reenvio
  idempotente e a ausência de mensagens na fila. Também verifica 503 `Unhealthy`
  com conexão real a um arquivo inexistente e a recuperação ao restaurar a conexão.

Executar pelo projeto de teste com filtro dessas classes, sem rodar a solução
inteira. Os logs e resultados da execução pertencem à entrega do item, e não devem
ser substituídos por esta descrição dos cenários.

## Roteiro operacional e limites

1. Identificar URL, método/rota, horário UTC e `codigo` da falha. Colher pelo
   caminho oficial o log correspondente, sanitizando credenciais e dados pessoais.
   Para falha anterior ao HTTP, colher o log de startup do recurso identificado.
2. Conferir o SHA implantado e os nomes das opções: `Banco__Provider`,
   `ConnectionStrings__DevKitPage`, `Banco__TravaAbandonadaMinutos` e
   `Banco__JournalDoSqlite`. Conferir também os pré-requisitos de JWT, fila,
   KeyRing e cliente da máquina descritos em `dev.kit.page/docs/publicacao-azure.md`.
   Nunca anexar os valores secretos.
3. SQLite aplica migrations na subida. Não executar scripts de SQL Server nele.
   A correção já incorporada limita a espera, registra as etapas, trata a trava
   antiga e escolhe journal adequado ao armazenamento quando configurado `auto`.
   Uma trava recente não comprova abandono; não a remover durante investigação.
4. Se o histórico registra uma migration cujos efeitos faltam, comparar TODOS os
   seus efeitos, obter backup e ensaiar a recuperação em cópia isolada. A retirada
   da entrada do histórico feita pelo teste vale somente para sua fixture: não é
   autorização para alterar uma base compartilhada. Não executar `Down`, recriar
   tabelas ou apagar o arquivo para recuperar disponibilidade.
5. SQL Server existente requer os scripts pertinentes em
   `dev.kit.page/api/scripts/sqlserver`, conforme o roteiro de publicação. Não executar todos
   cegamente: scripts individualmente idempotentes podem ter efeitos diferentes
   em sequência. SQL Server não foi ensaiado nesta execução; não inferir seu
   resultado a partir do SQLite.
6. Configuração de produção, SQL compartilhado, publicação e reinício pertencem
   ao portão operacional do item 6. Este item não os executa nem os autoriza.
   Depois de uma operação autorizada, conferir logs, conteúdo de `/health`, rota
   atingida, autenticação, ACL e consumidores antes de declarar o serviço recuperado.

## Compatibilidade dos quatro braços

- Serviço/API: o reparo recebido atua antes de ouvir HTTP e mantém as rotas e os
  contratos. A nova regressão observa a pipeline real, sem substituir o banco.
- Board: `dev.kit.page/web/src/api/cliente.ts` usa `VITE_API_URL`, Bearer, `detail` e o header
  `X-DevKit-Matrix-Avaliacao`. Nenhuma tela é alterada; proposta visual não se aplica.
- Desktop: revisar no git.kit `ApiDaEmpresa`, `PublicacaoRemotaClient`,
  `SessaoDoDevKitClient` e `FilaRemotaDoAzure`. Como o contrato não muda, não há
  adaptação de DTO ou instalação no perfil compartilhado.
- devcli: reutiliza esses serviços. Não há nova ação para espelhar ou alteração de
  consulta/simulação, saída, permissões, aprovação por snapshot ou desfecho do host.

A entrega deve distinguir testes com handlers de contrato, integração HTTP real
em base isolada e observação pública remota. Nenhuma dessas evidências substitui
um login de produção, o log histórico ou a confirmação de integração pelo host.
