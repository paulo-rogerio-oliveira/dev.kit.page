# Matrix: retomada no Board e no celular

O item 7 preserva o editor e o contrato existentes. Acrescenta o cenário
`dev.kit.page/web/e2e/matrix-retomada.spec.ts` ao runner focado
`dev.kit.page/web/playwright.matrix.config.ts`.

## Requisito, célula e evidência

| Requisito | Célula e responsável fictícios | Evidência produzida | Resultado exigido |
|---|---|---|---|
| Falha de rede durante revisão | `imp-008`, item 4 | `rede-1440.png`, `rede-390.png` e histórico consultado pela API | erro explícito; nenhum incremento de revisão; edição preservada |
| Retomada após recarregar a página | mesma célula | campo relido do sessionStorage após reload | critério digitado mantido nas duas larguras |
| Credencial recusada ao aplicar | mesma célula | `autenticacao-1440.png`, `autenticacao-390.png` e HTTP 401 da API real | rascunho preservado; nenhuma gravação |
| Repetição com a credencial válida | mesma célula | `retomada-1440.png`, `retomada-390.png` e histórico relido | uma nova revisão; critério correto; r4 anterior preservada |

As capturas vão para a pasta de apoio da task, fora dos clones. Devem ser abertas,
conferidas e registradas com `devcli entrega --captura` após a execução. A presença
do roteiro no repositório não comprova que ele passou.

Em 12/11/2026, o comando delegado 374 executou os três cenários focados com um
worker: 3/3 passaram, incluindo este roteiro nas duas larguras. As seis imagens
de rede, autenticação e retomada foram abertas e registradas na entrega. Mostram
dados fictícios; a recusa não incrementou revisão e a repetição válida gravou r5
e r6. O runner compilou Board e API com banco temporário. Isso não elimina os
limites de autenticação e de host/avaliador descritos a seguir.

O comando delegado 386 compilou os projetos da API e aprovou o caso
`MatrixApiTests.Projeto_usa_mesmo_acl_e_cliente_anterior_nao_apaga_a_matrix`
(1/1): o cliente anterior atualiza o projeto sem apagar sua matriz, e o mesmo
controle de acesso continua sendo aplicado. Esse caso não usa a interface móvel.

## Limites

O runner sobe Board compilado e API reais com SQLite e usuários fictícios. A
interrupção usa o modo offline do navegador. A recusa de autenticação substitui o
cabeçalho somente no PUT de aplicação; a API verifica a credencial e devolve o
401. Isso cobre o tratamento da falha e a retomada da escrita; não comprova login
humano, expiração por tempo, host reiniciado nem execução de agente avaliador.

As fixtures anteriores de avaliação têm eventos e SHAs sintéticos. As imagens
mostram o recurso renderizado, mas não devem ser apresentadas como recibos
originados num host real. O percurso separado da tarefa avulsa foi validado pelo
roteiro `git.kit/build/evidenciar-matrix-host.mjs`, no comando delegado 384:
o mesmo avaliador real recebeu r1/`gatilho:1` e r2/`gatilho:2`, com SHAs distintos,
após reinício completo do host e sem mensagem do Orquestrador. Os dois recibos,
as mensagens e os logs próprios desse cenário são a prova da avaliação; os PNGs
do E2E continuam representando dados sintéticos. O percurso completo do fluxo
Tela com agentes reais ainda exige comprovação separada, conforme o relatório
`git.kit/docs/matrix-validacao-integrada.md`.

## Rules 28 e 29

- Board: novo teste de transporte e persistência do rascunho em 1440 e 390 px.
- API: autenticação e journal existentes são exercitados sem alterar contratos.
- Desktop/serviço: nenhuma operação mudou; o cenário de runner, herança e
  recortes está em `git.kit/tests/GitKit.Presentation.Processes.Tests/Jobs/AgentHookRunnerMatrixJornadaTests.cs`.
- devcli: o exemplo dos chats, atribuições e isolamento é exercitado em
  `git.kit/tests/GitKit.Core.Tests/Projetos/MatrixJornadaChatsTests.cs`.

Sem mudança visual, dispensa-se proposta nova. Permanecem as propostas D1/D2/D3 e
as recapturas dos itens de tela como referências de comparação. A nova prova de
falhas/retomada é adicional e não substitui essas recapturas nem concede portões.
