import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { Topo } from '../componentes/Topo';

/** Os planos da compra individual, na ordem do seletor. */
export const PLANOS_INDIVIDUAIS = [
  { valor: 'individual-mensal', rotulo: 'Individual mensal' },
  { valor: 'individual-anual', rotulo: 'Individual anual' },
] as const;

/**
 * A compra individual (US #425, /comprar): o e-mail e o plano vão ao checkout do gateway — a API devolve a URL dele
 * (ou a do checkout SIMULADO, sem gateway configurado, que vai direto à compra concluída). A conta nasce quando o
 * gateway avisa o pagamento: o link de definir a senha chega por e-mail.
 */
export function Comprar() {
  const [parametros] = useSearchParams();
  const navegar = useNavigate();
  const [email, setEmail] = useState('');
  const [nome, setNome] = useState('');
  const [plano, setPlano] = useState<string>(parametros.get('plano') ?? PLANOS_INDIVIDUAIS[0].valor);
  const [erro, setErro] = useState('');
  const [enviando, setEnviando] = useState(false);

  async function comprar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    setEnviando(true);
    try {
      const checkout = await api.checkout(email.trim(), nome.trim(), plano);
      if (checkout.simulado) navegar(checkout.url);
      else window.location.assign(checkout.url);
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API. Tente de novo.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="pagina">
      <Topo secao="comprar" />
      <main className="painel">
        <form className="cartao formulario" onSubmit={comprar} aria-labelledby="titulo-comprar">
          <h1 id="titulo-comprar">Assinar o dev.kit</h1>
          <p className="nota-da-secao">Para uma pessoa: a licença se renova sozinha pelo login, e você cancela quando quiser.</p>
          <label>
            Plano
            <select value={plano} onChange={(e) => setPlano(e.target.value)}>
              {PLANOS_INDIVIDUAIS.map((p) => <option key={p.valor} value={p.valor}>{p.rotulo}</option>)}
            </select>
          </label>
          <label>
            E-mail
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
          </label>
          <label>
            Nome
            <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} autoComplete="name" />
          </label>
          {erro && <p className="erro" role="alert">{erro}</p>}
          <button className="botao botao-primario" type="submit" disabled={enviando}>{enviando ? 'Abrindo o pagamento…' : 'Ir para o pagamento'}</button>
        </form>
      </main>
    </div>
  );
}
