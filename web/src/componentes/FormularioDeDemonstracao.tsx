import { useState, type FormEvent } from 'react';
import { api, ErroDaApi } from '../api/cliente';
import type { PedidoDeDemonstracao } from '../api/tipos';

/** Os limites do pedido — os mesmos do ValidadorDeDemonstracao da API (Core/Regras.cs). */
export const LIMITES = { nome: 100, email: 254, empresa: 100, mensagem: 1000 } as const;

type Campo = keyof PedidoDeDemonstracao;
export type ErrosDoPedido = Partial<Record<Campo, string>>;

/** A validação do formulário ANTES do envio — a API repete a mesma e é ela que vale. */
export function validarPedido(pedido: PedidoDeDemonstracao): ErrosDoPedido {
  const erros: ErrosDoPedido = {};
  const nome = pedido.nome.trim();
  const email = pedido.email.trim();
  if (!nome) erros.nome = 'Informe o seu nome.';
  else if (nome.length > LIMITES.nome) erros.nome = `O nome tem até ${LIMITES.nome} caracteres.`;
  if (!email) erros.email = 'Informe o seu e-mail.';
  else if (email.length > LIMITES.email || !/^[^@\s]+@[^@\s]+\.[^@\s.]+$/.test(email)) erros.email = 'Informe um e-mail válido.';
  if (pedido.empresa.trim().length > LIMITES.empresa) erros.empresa = `A empresa tem até ${LIMITES.empresa} caracteres.`;
  if (pedido.mensagem.trim().length > LIMITES.mensagem) erros.mensagem = `A mensagem tem até ${LIMITES.mensagem} caracteres.`;
  if (!pedido.consentimento) erros.consentimento = 'É preciso concordar com o uso dos dados para o contato.';
  return erros;
}

const VAZIO: PedidoDeDemonstracao = { nome: '', email: '', empresa: '', mensagem: '', consentimento: false };

type Estado = 'editando' | 'enviando' | 'enviado';

/**
 * O formulário "Quero uma demonstração" da seção de contato: valida na hora do envio, grava pela
 * API e mostra o resultado — inclusive o 429 do limite por IP e os erros por campo do 400.
 */
export function FormularioDeDemonstracao() {
  const [pedido, setPedido] = useState<PedidoDeDemonstracao>(VAZIO);
  const [erros, setErros] = useState<ErrosDoPedido>({});
  const [falha, setFalha] = useState('');
  const [estado, setEstado] = useState<Estado>('editando');

  const mudar = <C extends Campo>(campo: C, valor: PedidoDeDemonstracao[C]) => setPedido((p) => ({ ...p, [campo]: valor }));

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setFalha('');
    const encontrados = validarPedido(pedido);
    setErros(encontrados);
    if (Object.keys(encontrados).length > 0) return;

    setEstado('enviando');
    try {
      await api.pedirDemonstracao({ ...pedido, nome: pedido.nome.trim(), email: pedido.email.trim() });
      setEstado('enviado');
    } catch (erro) {
      setEstado('editando');
      if (erro instanceof ErroDaApi && erro.status === 429) {
        setFalha('Recebemos muitos pedidos deste endereço agora. Tente de novo em um minuto.');
      } else if (erro instanceof ErroDaApi && erro.status === 400) {
        setErros(Object.fromEntries(Object.entries(erro.erros).map(([campo, mensagens]) => [campo, mensagens[0]])) as ErrosDoPedido);
        setFalha(erro.message);
      } else {
        setFalha('Não foi possível enviar agora. Tente de novo em instantes.');
      }
    }
  }

  if (estado === 'enviado') {
    return (
      <div className="cartao contato-sucesso" role="status">
        <h3>Pedido recebido!</h3>
        <p>Obrigado, {pedido.nome.trim()}. Vamos falar com você pelo e-mail {pedido.email.trim()} para combinar a demonstração.</p>
      </div>
    );
  }

  // O erro fica FORA do rótulo e ligado ao campo por aria-describedby: o nome do campo não muda.
  const atributos = (campo: Campo) => ({
    id: `demonstracao-${campo}`,
    'aria-invalid': Boolean(erros[campo]),
    'aria-describedby': erros[campo] ? `erro-${campo}` : undefined,
  });
  const erroDo = (campo: Campo) => erros[campo] && <span id={`erro-${campo}`} className="erro">{erros[campo]}</span>;

  return (
    <form className="cartao formulario formulario-contato" onSubmit={enviar} noValidate aria-label="Pedido de demonstração">
      <div className="campo">
        <label htmlFor="demonstracao-nome">Nome</label>
        <input {...atributos('nome')} value={pedido.nome} onChange={(e) => mudar('nome', e.target.value)} autoComplete="name" maxLength={LIMITES.nome} />
        {erroDo('nome')}
      </div>
      <div className="campo">
        <label htmlFor="demonstracao-email">E-mail</label>
        <input {...atributos('email')} type="email" value={pedido.email} onChange={(e) => mudar('email', e.target.value)} autoComplete="email" maxLength={LIMITES.email} />
        {erroDo('email')}
      </div>
      <div className="campo">
        <label htmlFor="demonstracao-empresa">Empresa (opcional)</label>
        <input {...atributos('empresa')} value={pedido.empresa} onChange={(e) => mudar('empresa', e.target.value)} autoComplete="organization" maxLength={LIMITES.empresa} />
        {erroDo('empresa')}
      </div>
      <div className="campo">
        <label htmlFor="demonstracao-mensagem">O que você quer ver na demonstração? (opcional)</label>
        <textarea {...atributos('mensagem')} rows={4} value={pedido.mensagem} onChange={(e) => mudar('mensagem', e.target.value)} maxLength={LIMITES.mensagem} />
        {erroDo('mensagem')}
      </div>
      <div className="campo consentimento">
        <label>
          <input {...atributos('consentimento')} type="checkbox" checked={pedido.consentimento} onChange={(e) => mudar('consentimento', e.target.checked)} />
          Concordo com o uso do meu nome e e-mail só para o contato sobre a demonstração. O pedido é apagado em até 365 dias, ou antes, se eu pedir.
        </label>
        {erroDo('consentimento')}
      </div>
      {falha && <p className="erro" role="alert">{falha}</p>}
      <button className="botao botao-primario" type="submit" disabled={estado === 'enviando'}>
        {estado === 'enviando' ? 'Enviando…' : 'Quero uma demonstração'}
      </button>
    </form>
  );
}
