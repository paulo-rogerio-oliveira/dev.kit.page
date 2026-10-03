import { Link } from 'react-router-dom';

/** Um recurso do dev.kit: o texto vem do README e do manual do git.kit; a imagem, das capturas do --screenshots. */
interface Recurso {
  id: string;
  titulo: string;
  texto: string;
  captura?: string;
}

export const RECURSOS: Recurso[] = [
  {
    id: 'agente',
    titulo: 'O agente sobre a sua task',
    texto:
      'Escolha a US ou o Bug da iteração (ou uma tarefa avulsa), marque os repositórios e descreva o que precisa ser feito. O dev.kit clona cada repositório lado a lado, cria o branch, grava as regras, as skills e o conhecimento comum na pasta da task e roda a CLI do agente — claude, kiro, kimi ou glm — turno a turno, até o commit, o push e a Pull Request.',
    captura: '/capturas/tela-agente.png',
  },
  {
    id: 'fluxos',
    titulo: 'Fluxos com avaliadores e objetivo',
    texto:
      'Encadeie agentes num fluxo: um planeja, outro implementa e um avaliador dá nota ao trabalho. O objetivo só é aceito com a nota mínima de cada avaliador, dada depois da última alteração — e o fluxo segue sozinho para a aprovação.',
    captura: '/capturas/tela-atividades.png',
  },
  {
    id: 'replicacao',
    titulo: 'Cherry-pick e replicação de branch',
    texto:
      'Replique commits entre branches numa cópia temporária, sem trocar o branch do seu projeto: por cherry-pick ou por integração de diff. Conflito que o git não resolve abre o TortoiseGitMerge arquivo por arquivo.',
    captura: '/capturas/tela-cherry-pick.png',
  },
  {
    id: 'agendamento',
    titulo: 'Agendamento sem ninguém na frente da tela',
    texto:
      'Agende uma task por cron: o serviço do Windows roda o mesmo pipeline às três da manhã, com as permissões do agente liberadas e os comandos pedidos executados automaticamente.',
    captura: '/capturas/tela-agendamentos.png',
  },
  {
    id: 'depurador',
    titulo: 'Depurador sem tela',
    texto:
      'O agente colhe evidência antes de teorizar: roda o alvo sob o depurador do dev.kit, para no ponto combinado e devolve a pilha e os valores de cada quadro — ou você depura na aba Depuração, com os mesmos pontos de parada.',
  },
  {
    id: 'devcli',
    titulo: 'devcli: o dev.kit nas mãos do agente',
    texto:
      'O lado do board voltado ao agente: consulta e altera work items, comenta, anexa, planeja as tasks, lança as horas e mede o próprio uso. Usa o mesmo banco e as mesmas credenciais do app — o agente nunca vê o token — e o que grava simula por padrão.',
    captura: '/capturas/tela-principal.png',
  },
  {
    id: 'board',
    titulo: 'Board e horas',
    texto:
      'Os work items da iteração em cartões, o plano técnico, os repositórios e o branch da task num lugar só — e as horas do dia lançadas nos itens trabalhados, rateadas pelos turnos do agente.',
    captura: '/capturas/tela-user-stories.png',
  },
];

/** A landing pública: os recursos do dev.kit e o caminho para o login. */
export function Landing() {
  return (
    <div className="pagina">
      <header className="topo">
        <span className="marca">dev<span className="marca-ponto">.</span>kit</span>
        <nav>
          <a href="#recursos">Recursos</a>
          <Link className="botao botao-primario" to="/login">Entrar</Link>
        </nav>
      </header>

      <section className="hero">
        <h1>O agente de IA trabalhando sobre a sua task</h1>
        <p>
          O dev.kit põe um agente para trabalhar nas tasks do Azure DevOps: clona os repositórios do work item,
          escreve as regras e as skills na pasta da task e leva o resultado até o commit, o push e a Pull Request.
        </p>
        <Link className="botao botao-primario" to="/login">Acessar o dashboard de uso</Link>
      </section>

      <main id="recursos" className="recursos">
        {RECURSOS.map((recurso) => (
          <section key={recurso.id} className="recurso" aria-labelledby={`recurso-${recurso.id}`}>
            <div>
              <h2 id={`recurso-${recurso.id}`}>{recurso.titulo}</h2>
              <p>{recurso.texto}</p>
            </div>
            {recurso.captura && <img src={recurso.captura} alt={`Tela do dev.kit: ${recurso.titulo}`} loading="lazy" />}
          </section>
        ))}
      </main>

      <footer className="rodape">
        <p>O dashboard mostra, por máquina, a quantidade e a qualidade de uso do dev.kit — com o envio ligado (opt-in) em Configurações → Telemetria de uso.</p>
        <Link to="/login">Entrar no dashboard</Link>
      </footer>
    </div>
  );
}
