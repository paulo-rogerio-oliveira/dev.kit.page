// O ROTEIRO das mídias da landing (US #283) — o mesmo de docs/landing-conteudo.md. Cada mídia é uma
// sequência de quadros ("slides"): uma TELA do dev.kit (as capturas de public/capturas, geradas pelo
// modo --screenshots do git.kit com dados de DEMONSTRAÇÃO) com os passos destacados, ou um TERMINAL
// com os comandos do devcli. As coordenadas dos destaques são em pixels da captura.

/** As capturas e a área útil de cada uma (sem a margem branca que o gerador deixa). */
export const TELAS = {
  agente: { arquivo: 'tela-agente.png', largura: 884, altura: 660 },
  atividades: { arquivo: 'tela-atividades.png', largura: 1024, altura: 680 },
  cherryPick: { arquivo: 'tela-cherry-pick.png', largura: 955, altura: 680 },
  agendamentos: { arquivo: 'tela-agendamentos.png', largura: 1024, altura: 680 },
  userStories: { arquivo: 'tela-user-stories.png', largura: 1024, altura: 680 },
};

const tela = (nome, duracao, passos) => ({ tipo: 'tela', tela: TELAS[nome], duracao, passos });
const terminal = (duracao, titulo, linhas, legenda) => ({ tipo: 'terminal', duracao, titulo, linhas, legenda });

// Linha de terminal: { cmd } é digitada; { out } aparece; { em } é o segundo (do slide) em que entra.
const DEVCLI = [
  { em: 0.2, cmd: 'devcli workitem --id 1234' },
  { em: 1.6, out: '#1234 · User Story · Active · Permitir login com SSO' },
  { em: 1.8, out: 'Responsável: Maria Souza' },
  { em: 2.0, out: '── Tasks filhas (2) ─────────────────────────' },
  { em: 2.2, out: '    task #1235 · Active · Provider SSO na API' },
  { em: 2.4, out: '    task #1236 · New · Tela de login com SSO' },
  { em: 3.6, cmd: 'devcli workitem-alterar --id 1236 --estado Active' },
  { em: 5.2, out: '{ "id": 1236, "aplicar": false,', classe: 'mudo' },
  { em: 5.4, out: '  "antes": { "estado": "New" }, "depois": { "estado": "Active" } }', classe: 'mudo' },
  { em: 5.6, out: 'Simulação: nada foi gravado. Use --aplicar para gravar.', classe: 'aviso' },
  { em: 7.0, cmd: 'devcli workitem-alterar --id 1236 --estado Active --aplicar' },
  { em: 8.8, out: '#1236 · Task · Active · Tela de login com SSO' },
  { em: 9.0, out: 'Gravado no board.', classe: 'ok' },
];

const DEPURADOR = [
  { em: 0.2, cmd: 'devcli depurar --exe Vendas.exe --parar src\\Vendas\\Pedido.cs:142 --valores' },
  { em: 2.4, out: 'Parada 1 de 3 · src\\Vendas\\Pedido.cs:142 · Pedido.CalcularTotal()', classe: 'ok' },
  { em: 3.0, out: 'Pilha' },
  { em: 3.2, out: '  #0  Pedido.CalcularTotal()          Pedido.cs:142' },
  { em: 3.4, out: '  #1  CarrinhoService.Fechar()        CarrinhoService.cs:58' },
  { em: 3.6, out: '  #2  Program.Main(string[] args)     Program.cs:21' },
  { em: 4.6, out: 'Valores do quadro #0' },
  { em: 4.8, out: '  itens.Count  = 3' },
  { em: 5.0, out: '  desconto     = 0.15' },
  { em: 5.2, out: '  subtotal     = 1250.00' },
  { em: 5.4, out: '  total        = 0          ← o desconto zerou o total', classe: 'aviso' },
  { em: 7.0, out: 'Captura gravada em debug\\2026-10-03_1412_pedido.md', classe: 'mudo' },
];

const PULL_REQUEST = [
  { em: 0.1, cmd: 'git commit -m "Ab#1234 permite login com SSO corporativo"' },
  { em: 0.9, out: '[feature/eq/1234 9f3c2a1] Ab#1234 permite login com SSO corporativo', classe: 'mudo' },
  { em: 1.1, cmd: 'git push origin feature/eq/1234' },
  { em: 1.7, out: 'feature/eq/1234 -> feature/eq/1234', classe: 'mudo' },
  { em: 2.0, out: 'Pull Request #42 aberta: Ab#1234 — Permitir login com SSO', classe: 'ok' },
];

/**
 * As mídias: nome do arquivo (public/midia/NOME.mp4|webm|jpg) e os slides. O poster é o quadro do
 * instante zero — por isso todo slide inicial já começa com a legenda e o destaque visíveis.
 */
export const MIDIAS = [
  {
    nome: 'hero',
    slides: [
      tela('userStories', 3.6, [{ em: 0, legenda: '1 · Escolha o work item e os repositórios', destaque: [54, 55, 622, 160], zoom: 1.25 }]),
      tela('agente', 4, [{ em: 0, legenda: '2 · O agente implementa, compila e testa', destaque: [68, 407, 480, 50], zoom: 1.45 }]),
      tela('atividades', 3.6, [{ em: 0, legenda: '3 · Avaliadores com nota mínima; o fluxo segue sozinho', destaque: [60, 344, 940, 126], zoom: 1.35 }]),
      terminal(3.8, 'Windows PowerShell', PULL_REQUEST, '4 · Commit, push e Pull Request'),
    ],
  },
  {
    nome: 'agente',
    slides: [tela('agente', 12, [
      { em: 0, legenda: 'O agente pede para rodar a compilação — você permite uma vez ou sempre', destaque: [508, 99, 126, 34], zoom: 1.5 },
      { em: 4, legenda: 'Roda a suíte e confere: 34 testes, nenhuma falha', destaque: [68, 407, 480, 50], zoom: 1.5 },
      { em: 8, legenda: 'Propõe o commit com Ab#1234 — e você aprova', destaque: [72, 233, 666, 52], zoom: 1.4 },
    ])],
  },
  {
    nome: 'fluxos',
    slides: [tela('atividades', 12, [
      { em: 0, legenda: 'As tasks do fluxo na aba Atividades, cada uma no seu estado', destaque: [241, 10, 604, 30], zoom: 1 },
      { em: 4, legenda: 'O agente implementa — turno em andamento', destaque: [60, 190, 940, 142], zoom: 1.35 },
      { em: 8, legenda: 'Com a nota mínima de cada avaliador, o trabalho segue para o push', destaque: [60, 344, 940, 126], zoom: 1.35 },
    ])],
  },
  {
    nome: 'replicacao',
    slides: [tela('cherryPick', 12, [
      { em: 0, legenda: 'Escolha o repositório e os branches de origem e destino', destaque: [26, 213, 912, 58], zoom: 1.25 },
      { em: 4, legenda: 'Marque os commits a replicar', destaque: [26, 452, 912, 70], zoom: 1.45 },
      { em: 8, legenda: 'Replica numa cópia temporária — o branch do seu projeto não muda', destaque: [757, 638, 198, 34], zoom: 1.6 },
    ])],
  },
  {
    nome: 'agendamento',
    slides: [tela('agendamentos', 12, [
      { em: 0, legenda: 'Agende uma task por cron', destaque: [70, 240, 600, 82], zoom: 1.45 },
      { em: 4, legenda: 'O serviço do Windows dispara no horário — ou agora', destaque: [524, 244, 111, 26], zoom: 1.7 },
      { em: 8, legenda: 'E o resultado aparece nas atividades recentes', destaque: [701, 178, 315, 236], zoom: 1.4 },
    ])],
  },
  {
    nome: 'depurador',
    slides: [terminal(12, 'Agente · devcli depurar', DEPURADOR, 'O agente para no ponto combinado e lê a pilha e os valores antes de mexer no código')],
  },
  {
    nome: 'devcli',
    slides: [terminal(12, 'Agente · devcli', DEVCLI, 'Consulta o work item e simula antes de gravar no board')],
  },
  {
    nome: 'board',
    slides: [tela('userStories', 12, [
      { em: 0, legenda: 'O work item, os repositórios e o branch da task num lugar só', destaque: [54, 55, 622, 160], zoom: 1.25 },
      { em: 4, legenda: 'Cada repositório no seu branch', destaque: [70, 290, 590, 110], zoom: 1.5 },
      { em: 8, legenda: 'E as horas do dia lançadas nos itens trabalhados', destaque: [701, 55, 315, 108], zoom: 1.6 },
    ])],
  },
];
