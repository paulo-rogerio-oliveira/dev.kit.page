# Board remoto · fase 1

`/board` é a leitura autenticada do contrato [remoto v1](contrato-remoto-v1.md). O link está na Conta e o login conserva o
retorno ao Board, inclusive para o papel dev. A troca de senha obrigatória e o vencimento da sessão seguem o restante do site.

O cliente `api/remoto.ts` descreve a leitura; `api/cliente.ts` reutiliza a autenticação e o tratamento de erros existentes.
`useBoardRemoto` consulta a lista de máquinas do dono e o retrato da escolhida a cada 15 segundos, sem sobrepor consultas.
Descarta respostas de uma seleção desmontada. O estado não é salvo no navegador e as consultas usam `cache: no-store`.
Erro de atualização e máquina desconectada identificam explicitamente dados antigos; máquina sem retrato e listas vazias têm
mensagem própria. Códigos futuros de execução aparecem neutros, sem quebrar a leitura.

O layout segue **app-remoto v5–v10**, derivados da v4: cartões à esquerda e Chat à direita no navegador; abas Chat/Board no
celular. Os tokens de claro e escuro pertencem somente ao Board, seguindo `prefers-color-scheme`. Não há navegação para
Projetos, Fluxos ou Infra dentro dele. Compositor, atalhos, modelo, decisões e reinício usam controles nativamente
desabilitados; o motivo da fase somente leitura permanece visível. Não existe chamada de escrita remota nesta página.

## Instalação

O manifesto `/board.webmanifest` declara entrada e escopo `/board`, exibição standalone e ícones PNG de 192 e 512 pixels.
Os ícones são reproduzíveis por `node web/scripts/gerar-icones-board.mjs`. A configuração do Static Web Apps preserva as rotas
do manifesto e do service worker e informa o MIME do manifesto.

`InstalarBoard` encaminha o evento nativo `beforeinstallprompt` e trata conclusão/cancelamento. Sem esse evento, mostra as
instruções do navegador, incluindo iOS. A instalação exige HTTPS ou localhost e não envia comando ao dev.kit.
`board-sw.js` controla somente `/board`, não cria cache e nunca intercepta a API. Sem internet, a navegação apresenta uma
mensagem de conexão necessária; nenhum retrato privado fica disponível offline. Ao abrir outra janela sem sessão válida,
o aplicativo volta ao login.

## Validação

- `npm --prefix web test -- src/paginas/Board.test.tsx`: contrato autenticado, ações desabilitadas, estados vazios,
  desconexão, código futuro, erro/nova tentativa, troca concorrente de máquina, sessão e instalação local.
- `npm --prefix web run build`: tipagem de produto/testes e build de produção.
- `web/e2e/remoto.spec.ts`: API real com SQLite descartável, login pelo navegador, seis variações visuais, atualização sem
  recarregar, somente GET no navegador, manifesto/ícones, service worker, ausência de cache privado, navegação sem internet e
  volta ao login. A instalação usa perfil persistente **novo e temporário**: o contexto anônimo do Playwright retorna
  `in-incognito`, que impede a instalação mesmo com o manifesto correto. `Page.getInstallabilityErrors` deve retornar vazio.

O cenário base-nova do repositório prepara a senha antes dos cenários. Para rodar o Board isolado em banco novo, seu helper
`garantirSenhaDefinitiva` também prepara a conta; nunca usar banco ou perfil de produção. A recaptura real do implementador é
registrada como nova versão no Design, com branch e SHA, separada das propostas.
