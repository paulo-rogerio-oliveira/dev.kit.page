import { useState, type FormEvent } from 'react';
import { api, mensagemDaFalha } from '../api/cliente';
import type { EmpresaEditada, EmpresaResumo } from '../api/tipos';
import { formatar } from '../formatar';
import { PainelLateral } from './PainelLateral';

/** Os segmentos do seletor (os mesmos da API: o texto livre de antes continua aceito). */
export const SEGMENTOS = ['Software / ERP', 'Construção civil', 'Financeiro', 'Indústria', 'Varejo', 'Saúde', 'Educação', 'Governo', 'Serviços', 'Outro'];

/** As 27 UFs. */
export const UFS = [
  'AC', 'AL', 'AM', 'AP', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MG', 'MS', 'MT', 'PA', 'PB', 'PE', 'PI', 'PR', 'RJ', 'RN', 'RO', 'RR', 'RS', 'SC', 'SE', 'SP', 'TO',
];

/** O CNPJ na máscara da tela (12.345.678/0001-90); o que não tem 14 dígitos volta como está. */
export function formatarCnpj(digitos: string): string {
  const d = digitos.replace(/\D/g, '');
  return d.length === 14 ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}` : digitos;
}

/** O CNPJ pelos dígitos verificadores — a mesma regra da API (RegrasDaEmpresa.CnpjValido). */
export function cnpjValido(texto: string): boolean {
  const d = texto.replace(/\D/g, '');
  if (d.length !== 14 || /^(\d)\1+$/.test(d)) return false;
  const verificador = (n: number) => {
    let soma = 0;
    for (let i = 0; i < n; i++) soma += Number(d[i]) * (2 + ((n - 1 - i) % 8));
    const resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  return verificador(12) === Number(d[12]) && verificador(13) === Number(d[13]);
}

/** A prévia da diferença de assentos ("+5 assentos (25 → 30)"); vazia sem mudança. */
export function previaDosAssentos(antes: number, depois: number): string {
  const diferenca = depois - antes;
  if (diferenca === 0) return '';
  return `${diferenca > 0 ? '+' : '−'}${Math.abs(diferenca)} ${Math.abs(diferenca) === 1 ? 'assento' : 'assentos'} (${antes} → ${depois}).`;
}

/** A edição a partir do resumo da empresa (os dados que ainda não existem, vazios). */
function edicaoDe(e: EmpresaResumo): EmpresaEditada {
  const d = e.dados;
  return {
    nome: e.nome, plano: e.plano, assentos: e.assentos, cnpj: d?.cnpj ? formatarCnpj(d.cnpj) : '', segmento: d?.segmento ?? '', cidade: d?.cidade ?? '',
    uf: d?.uf ?? '', contato: d?.contato ?? '', telefone: d?.telefone ?? '', site: d?.site ?? '', contratoAte: d?.contratoAte ?? null,
  };
}

/**
 * O painel "Editar &lt;empresa&gt;" (US #687, proposta admin-empresas v009): os dados básicos (nome, CNPJ, segmento,
 * cidade/UF, contato, telefone, site), o plano, o contrato até e os ASSENTOS editáveis (− / +), que não descem abaixo
 * dos em uso, com a prévia da diferença. O contrato é a validade da licença dos usuários dela.
 */
export function PainelDaEmpresa({ token, empresa, planos, aoSalvar, aoFechar }: {
  token: string;
  empresa: EmpresaResumo;
  planos: string[];
  aoSalvar: (editada: EmpresaResumo) => void;
  aoFechar: () => void;
}) {
  const [edicao, setEdicao] = useState<EmpresaEditada>(() => edicaoDe(empresa));
  const [erro, setErro] = useState('');
  const [salvando, setSalvando] = useState(false);
  const minimo = Math.max(1, empresa.assentosEmUso);
  const mudar = (parcial: Partial<EmpresaEditada>) => { setEdicao({ ...edicao, ...parcial }); setErro(''); };

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    if (edicao.cnpj.trim() && !cnpjValido(edicao.cnpj)) {
      setErro('CNPJ inválido: confira os 14 dígitos.');
      return;
    }
    setSalvando(true);
    try {
      aoSalvar(await api.editarEmpresa(token, empresa.id, { ...edicao, nome: edicao.nome.trim(), cnpj: edicao.cnpj.replace(/\D/g, '') }));
    } catch (falha) {
      setErro(mensagemDaFalha(falha));
    } finally {
      setSalvando(false);
    }
  }

  const livres = Math.max(0, edicao.assentos - empresa.assentosEmUso);
  const previa = previaDosAssentos(empresa.assentos, edicao.assentos);
  const opcoesDePlano = planos.includes(edicao.plano) ? planos : [edicao.plano, ...planos];

  return (
    <PainelLateral id="titulo-editar-empresa" titulo={`Editar ${empresa.nome}`}>
      <button className="botao-icone fechar-painel" type="button" aria-label="Fechar a edição" onClick={aoFechar}>×</button>
      <form className="formulario-lateral formulario-da-empresa" onSubmit={salvar} aria-label={`Editar ${empresa.nome}`} noValidate>
        <p className="rotulo-do-grupo">Dados da empresa</p>
        <label>
          Nome
          <input value={edicao.nome} onChange={(e) => mudar({ nome: e.target.value })} maxLength={100} required />
        </label>
        <div className="campos-lado-a-lado">
          <label>
            CNPJ
            <input value={edicao.cnpj} onChange={(e) => mudar({ cnpj: e.target.value })} onBlur={() => mudar({ cnpj: formatarCnpj(edicao.cnpj) })}
              placeholder="00.000.000/0000-00" maxLength={18} inputMode="numeric" />
          </label>
          <label>
            Segmento
            <select value={edicao.segmento} onChange={(e) => mudar({ segmento: e.target.value })}>
              <option value="">Escolha</option>
              {(SEGMENTOS.includes(edicao.segmento) || !edicao.segmento ? SEGMENTOS : [edicao.segmento, ...SEGMENTOS]).map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
        </div>
        <div className="campos-lado-a-lado campos-cidade">
          <label>
            Cidade
            <input value={edicao.cidade} onChange={(e) => mudar({ cidade: e.target.value })} maxLength={80} />
          </label>
          <label>
            UF
            <select value={edicao.uf} onChange={(e) => mudar({ uf: e.target.value })}>
              <option value="">—</option>
              {UFS.map((uf) => <option key={uf} value={uf}>{uf}</option>)}
            </select>
          </label>
        </div>
        <div className="campos-lado-a-lado">
          <label>
            Contato responsável
            <input value={edicao.contato} onChange={(e) => mudar({ contato: e.target.value })} maxLength={120} />
          </label>
          <label>
            Telefone
            <input value={edicao.telefone} onChange={(e) => mudar({ telefone: e.target.value })} maxLength={30} placeholder="(11) 0000-0000" />
          </label>
        </div>
        <label>
          Site
          <input value={edicao.site} onChange={(e) => mudar({ site: e.target.value })} maxLength={200} placeholder="https://" />
        </label>

        <p className="rotulo-do-grupo">Plano e assentos</p>
        <div className="campos-lado-a-lado">
          <label>
            Plano
            <select value={edicao.plano} onChange={(e) => mudar({ plano: e.target.value })}>
              {opcoesDePlano.map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </label>
          <label>
            Contrato até
            <input type="date" value={edicao.contratoAte ?? ''} onChange={(e) => mudar({ contratoAte: e.target.value || null })} />
          </label>
        </div>

        <div className="assentos-editaveis">
          <div className="contador" role="group" aria-label="Assentos contratados">
            <button className="botao botao-fantasma" type="button" aria-label="Menos um assento" disabled={edicao.assentos <= minimo}
              onClick={() => mudar({ assentos: Math.max(minimo, edicao.assentos - 1) })}>−</button>
            <input aria-label="Assentos" type="number" min={minimo} value={edicao.assentos}
              onChange={(e) => mudar({ assentos: Math.max(minimo, Number(e.target.value) || minimo) })} />
            <button className="botao botao-fantasma" type="button" aria-label="Mais um assento" onClick={() => mudar({ assentos: edicao.assentos + 1 })}>+</button>
          </div>
          <span className="assentos-texto">
            assentos contratados
            <strong>{empresa.assentosEmUso} em uso</strong> <span className="texto-mudo">· {livres} {livres === 1 ? 'livre' : 'livres'}</span>
          </span>
          <span className="trilho trilho-de-assentos" aria-hidden="true">
            <span className="barra barra-em-uso" style={{ width: `${Math.min(100, (empresa.assentosEmUso / Math.max(1, edicao.assentos)) * 100)}%` }} />
            <span className="barra barra-nova" style={{ width: `${Math.max(0, Math.min(100, ((edicao.assentos - empresa.assentos) / Math.max(1, edicao.assentos)) * 100))}%` }} />
          </span>
          <p className={previa ? 'previa-dos-assentos' : 'texto-mudo previa-dos-assentos'} role="status">
            {previa} Mínimo permitido: {minimo}, {minimo === 1 && empresa.assentosEmUso < 1 ? 'pelo menos um assento' : 'os que estão em uso'}.
          </p>
        </div>

        {erro && <p className="erro" role="alert">{erro}</p>}
        <div className="acoes acoes-da-licenca">
          <button className="botao botao-fantasma" type="button" onClick={aoFechar}>Cancelar</button>
          <button className="botao botao-primario" type="submit" disabled={salvando}>Salvar alterações</button>
        </div>
        {empresa.dados?.alteradaEm && (
          <p className="nota-do-formulario">Última alteração: {formatar.data(empresa.dados.alteradaEm)} por {empresa.dados.alteradaPor || 'admin'}.</p>
        )}
      </form>
    </PainelLateral>
  );
}
