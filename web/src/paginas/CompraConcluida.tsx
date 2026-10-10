import { Link, useSearchParams } from 'react-router-dom';
import { Topo } from '../componentes/Topo';

/**
 * A volta do checkout (US #425, /compra-concluida): o pagamento confirmado pelo gateway cria a conta e manda o link
 * de definir a senha — a página diz isso e leva ao download. `?simulado=1` é o checkout sem gateway (desenvolvimento
 * e e2e): nada foi cobrado.
 */
export function CompraConcluida() {
  const [parametros] = useSearchParams();
  const simulado = parametros.get('simulado') === '1';

  return (
    <div className="pagina">
      <Topo secao="compra" />
      <main className="painel">
        <section className="cartao" aria-labelledby="titulo-compra">
          <h1 id="titulo-compra">Compra concluída</h1>
          {simulado && <p className="aviso" role="status">Checkout simulado: nenhum pagamento foi cobrado.</p>}
          <p>Assim que o pagamento for confirmado, enviamos ao seu e-mail o link para definir a senha (vale 24 horas).</p>
          <p>Enquanto isso, baixe o dev.kit: os 14 dias de teste valem até você entrar com a sua conta.</p>
          <div className="ctas">
            <Link className="botao botao-primario" to="/#planos">Baixar o dev.kit</Link>
            <Link className="botao botao-fantasma" to="/definir-senha">Não recebi o e-mail</Link>
          </div>
        </section>
      </main>
    </div>
  );
}
