import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { TelaDeAcesso } from '../componentes/TelaDeAcesso';
import { Topo } from '../componentes/Topo';

/** Os planos da compra individual, na ordem do seletor. */
export const PLANOS_INDIVIDUAIS = [
  { valor: 'individual-mensal', rotulo: 'Individual mensal' },
  { valor: 'individual-anual', rotulo: 'Individual anual' },
] as const;

/**
 * A compra individual (US #425, /comprar): o e-mail e o plano vão ao checkout do gateway — a API devolve a URL dele
 * (ou a do checkout SIMULADO, sem gateway configurado, que vai direto à compra concluída). A conta nasce quando o
 * gateway avisa o pagamento: o link de definir a senha chega por e-mail. Na tela dividida da marca (versão 5).
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
    <TelaDeAcesso
      topo={<Topo secao="comprar" />}
      rotulo="Assinatura individual"
      titulo="Assine o dev.kit e leve o seu time de agentes."
      destaque="time de agentes"
      texto="Para uma pessoa: a licença se renova sozinha pelo login, e você cancela quando quiser."
      itens={['Renovação automática pelo login', 'Cancele quando quiser, sem fidelidade', 'Todas as skills e fluxos do catálogo']}
    >
      <form className="cartao formulario formulario-de-acesso" onSubmit={comprar} aria-labelledby="titulo-comprar">
        <div className="formulario-cabecalho">
          <h1 id="titulo-comprar">Assinar o dev.kit</h1>
          <p>Escolha o plano e siga para o pagamento.</p>
        </div>
        <label>
          Plano
          <select value={plano} onChange={(e) => setPlano(e.target.value)}>
            {PLANOS_INDIVIDUAIS.map((p) => <option key={p.valor} value={p.valor}>{p.rotulo}</option>)}
          </select>
        </label>
        <label>
          E-mail
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="nome@empresa.com.br" required autoComplete="email" />
        </label>
        <label>
          Nome
          <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} placeholder="Seu nome completo" autoComplete="name" />
        </label>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="botao botao-primario botao-grande" type="submit" disabled={enviando}>{enviando ? 'Abrindo o pagamento…' : 'Ir para o pagamento'}</button>
        <p className="nota-do-formulario centralizada">Pagamento seguro · cancele quando quiser</p>
      </form>
    </TelaDeAcesso>
  );
}
