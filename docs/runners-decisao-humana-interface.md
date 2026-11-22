# Decisão humana de item — desktop, Board e CLI

Concluir manualmente e Cancelar item retiram o trabalho restante do curso das ondas. A decisão não cria integração, não desfaz commits integrados e não exclui o item, a entrega ou o histórico. A parada física das reservas continua dependendo de recibo autenticado do host.

O desktop usa `DecisaoDoItemPelaInterface` e a política `CicloDoItem` do git.kit. A CLI usa a mesma política/persistência. Quando remoto, o desktop usa o cliente oficial e o Board usa o transporte autenticado existente para `POST /api/projetos/{id}/itens/{numero}/decisao`. A API anuncia `ciclo-humano-item-v1`; o cliente correspondente envia `X-DevKit-Ciclo-Item: 1`, inclusive na leitura da lista para reabrir um projeto que já tem decisões.

| Etapa | Desktop e Board | CLI correspondente |
| --- | --- | --- |
| Consultar | Selecionar o item e conferir trabalho, recibo e papel | `projeto --id <slug> --item <n> --decisao-item --json` |
| Simular conclusão | Abrir Concluir manualmente; apresentar efeitos calculados pela política | `projeto --id <slug> --item <n> --concluir-manualmente --json` |
| Simular cancelamento | Abrir Cancelar item; apresentar dependentes e preservação | `projeto --id <slug> --item <n> --cancelar-item --json` |
| Confirmar | Motivo obrigatório de até2.000 caracteres e clique humano | Mesma ação com `--pedido-id <guid> --versao <versao-retornada> --motivo <texto> --confirmar --aplicar`; confirmação interativa humana real |
| Desistir | Voltar/Escape fecha sem aplicar a decisão | Não aplicar a simulação; recusar a confirmação interativa |
| Conflito/erro | Preservar motivo, invalidar simulação antiga, reler e revisar antes de outro clique | Reler estado/recibo e simular novamente com a versão atual; não repetir escrita automaticamente |

Os comandos acima descrevem o contrato dos fontes integrados e da CLI compilada isoladamente. Não presumem que a instalação que conduz a task já tenha essas opções. Não copiar binários para `C:/dk`, sincronizar catálogo ou orientar uso de flags ausentes por inferência; a atualização publicável da skill depende da disponibilização oficial do contrato.

## Efeito e recibo equivalentes

A simulação não persiste decisão nem histórico. Aplicar exige pedido único, ação, versão esperada, motivo, identidade autenticada e origem da confirmação. Replay exato preserva o recibo; outro pedido, versão ou motivo é confrontado com o estado atual. Os valores de autoria, pedido e data pertencem à execução que realmente decidiu; comparar o significado e os campos, não exigir valores artificiais iguais entre usuários ou bases diferentes.

O recibo portátil conserva pedido, ação, autor, confirmação, motivo, data, versão esperada, estado anterior, snapshot e indicação de parada. Aceite de fila sem recibo correspondente não comprova aplicação. A tela conserva os itens integrados na contagem de integração e apresenta estados humanos separadamente. O navegador não confirma parada por PID, não envia credencial de máquina e não recalcula o grafo ou as ondas para aplicar uma decisão.

A gravação das operações humanas mantém o ID do projeto que foi lido/autorizado. Editor de projeto pessoal compartilhado não se torna dono dele, e um projeto homônimo do editor não pode ser escolhido como destino. Visibilidade, identidade, permissão de editar e versão são conferidas antes de persistir. A confirmação de parada usa o mesmo alvo e ainda exige a credencial da máquina e o recibo correspondente.

## Evidências e validação

- `GitKit.Core.Tests.CicloDoItemCliTests`: ambas as ações, simulação sem gravação, recusa sem confirmação humana, replay e consulta do mesmo recibo, em SQLite descartável.
- `GitKit.Core.Tests.DecisaoDoItemPelaInterfaceTests` e `PlanoDoProjetoDecisaoHumanaTests`: adaptador/cliente oficial, autorização, versão/correlação, recibo e recuperação sem integração otimista; UI local preserva motivo e trabalho anterior.
- `web/src/api/cicloDosItens.test.ts` e `web/src/paginas/ProjetosDecisaoDoItem.test.tsx`: transporte e cliques humanos, motivo, ACL/API anterior, falhas, resposta sem recibo e recuperação sem aplicação automática.
- `CicloDosItensApiTests` e `ImportacaoDasDecisoesApiTests`: efeito/recibo persistidos, projeto pessoal/empresa, homônimo preservado, replay/revogação e parada autenticada, sem banco compartilhado.
- `playwright.ciclo-item.config.ts`: percurso real de API/SQLite/navegador em1280 e390, incluindo conflito concorrente e credencial recusada. Os logs da entrega registram o resultado efetivo de cada execução; a existência do roteiro não afirma que passou.

As referências aprovadas são desktop v15/v16 e Board v009/v010. No Board desta base, tema escuro e quatro abas; as versões v007/v008 pertencem ao histórico superado. As recapturas do SHA final e o confronto com as propostas ficam na entrega. Não gerar/publicar artefato real para provar abertura, retorno ou cancelamento de um diálogo humano.
