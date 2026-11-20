# Validação do chat web na composição filtros — item 4

## Revisão conferida

Composição web: `1b15a49c9b05d343954754a012d5d4555f24b20d`.
Implementação de origem: `729ef7ba9f644e4603c2abd6cf78758e7eb1a1f5`, item 3.
Os três arquivos alterados e a árvore inteira de produto são idênticos entre
essas revisões. Branch de validação: `feature/dev/projeto-filtros-item-4`.
Este item acrescenta documentação; não altera a interface nem o transporte.

Matriz `devkit/filtros`, definição 2/mapa 2, revisão efetiva 1/contexto 2,
responsável `filtros-item-4`, recibo
`abertura:ab526740b1f44153acf8e5b00d7b5fc9`.
Célula atribuída: `validacao-filtros-board`. As alterações dos componentes
continuam sob responsabilidade do item 3.

## Critérios e evidência

| Critério | Resultado observado na evidência de origem preservada |
|---|---|
| ID/título a cada edição | Letras não são reduzidas a números. `login portal` encontra 812 e 836, incluindo espaços/caixa. Título sem escolha não vira ID no envio. |
| Nome/dono/origem | Termos de `DEV.AZURE.COM outra` retornam a origem completa; homônimos exibem donos distintos. |
| Identidade e contexto | Escolha envia ID/URL exatos. Pesquisa/rascunho não substituem contexto confirmado; reabrir conserva a seleção. |
| Teclado e cancelamento | Testes e roteiro verificam setas/Enter, Escape/Cancelar e foco. Toque não foca filtro nem branch automaticamente; tocar no filtro permite pesquisar. |
| Branch omitido | Selecionar repositório sem branch não exige confirmação; POST conserva omissão e mantém fluxo/modelo/parâmetros. |
| Catálogo e erros | Casos distintos para ausente, vazio, sem resultado e 401. ID explícito continua disponível; não há criação de work item nem contorno de ACL. |

As fontes relevantes são `dev.kit.page/web/src/componentes/ContextoDoChatRemoto.tsx`,
`dev.kit.page/web/src/componentes/ChatRemoto.css` e
`dev.kit.page/web/src/componentes/ChatRemoto.test.tsx`. Não há API nova,
dependência, lockfile, permissão ou endpoint de execução introduzido aqui.

## Comparação visual

Proposta fixada: Design
`github-com-paulo-rogerio-oliveira-dev-kit-page-1ac8edc17aa0853b`, tela
`filtros-chat-web`, v001, SHA-256
`4ED402E09F528FE167F04C32BC5F898029262C277E662AFDEF6A8178656C782B`.
Hash conferido contra o manifesto do projeto.

Foram abertas as 16 recapturas do item 3: larguras 390/1280, temas light/dark,
estados `titulo`, `origem`, `sem-resultados` e `selecionado-branch-vazio`.
O inventário `auditoria-filtros.json` da entrega do item 4 registra o caminho,
o SHA de origem e o SHA-256 de cada imagem.

Comparação com v001: painel Work item, rótulo de ID/título e dois resultados
legíveis no mobile; busca separada e Cancelar preservados. A implementação
mantém o cabeçalho e compositor reais e acrescenta seleção/aplicação explícita
para distinguir ID manual de pesquisa por título. No desktop o painel fica
ancorado ao compositor, e no mobile aparece centralizado. Não há corte de
resultados, botões ou campo de branch nas imagens; o painel aberto sobrepõe
parte do cabeçalho móvel como camada de interação. Não se afirma ausência
absoluta de sobreposição nem equivalência pixel a pixel à proposta.

## Logs e reuso

| Execução automática do item 3 | Resultado | Correlação |
|---|---|---|
| 89 — React ChatRemoto | 34/34 | `6515ea89b599410596dd5cdb93476926` |
| 90 — TypeScript/Vite | build aprovado | `e41fc319225745669b6bed7a098ceb1c` |
| 97 — Playwright com API/SQLite temporários | 1/1, quatro combinações e 16 recapturas finais | `31e01b0797414469bf391f0161672491` |

Os logs são os arquivos automáticos preservados na entrega do implementador;
o inventário referencia seus hashes, cabeçalhos e resultados sem os substituir.
As execuções anteriores, incluindo a falha corrigida do dublê `matchMedia`,
continuam no histórico. Nenhuma suíte web intacta foi repetida neste item.
A identidade Git justifica o reaproveitamento dos resultados para o mesmo
produto na composição, sem reatribuir os logs ao SHA de integração.

## Limites

Recapturas reais de navegador com dados fictícios/API temporária não comprovam
credencial/sessão de produção nem abertura do teclado virtual de um aparelho
físico. Os cenários de catálogo e 401 têm cobertura React controlada; não são
uma expiração humana cronometrada. A ausência de resultados não é tratada
como validação de acesso real.

Não foi produzida nova recaptura no SHA de integração: as imagens do SHA do
implementador são reutilizadas com fontes idênticos. Este item não altera tela,
portanto não cria proposta nem recaptura em nome do item 3. O upload remoto
das propostas foi recusado pelo teto de 500 versões; as imagens locais foram
preservadas. Não houve publicação, criação de WI nem envio de mensagem externa.

A avaliação independente desta revisão e a regressão/CI final pertencem aos
portões seguintes. O relatório não atribui nota nem declara integração do item.
