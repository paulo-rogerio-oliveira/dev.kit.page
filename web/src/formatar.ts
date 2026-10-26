/** O traço de "sem denominador": nunca NaN, nunca 0% inventado. */
export const SEM_VALOR = '—';

const inteiro = new Intl.NumberFormat('pt-BR');
const decimal = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const percentual = new Intl.NumberFormat('pt-BR', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });

function valido(valor: number | null | undefined): valor is number {
  return valor !== null && valor !== undefined && Number.isFinite(valor);
}

export const formatar = {
  inteiro: (valor: number | null | undefined) => (valido(valor) ? inteiro.format(valor) : SEM_VALOR),
  decimal: (valor: number | null | undefined) => (valido(valor) ? decimal.format(valor) : SEM_VALOR),
  percentual: (valor: number | null | undefined) => (valido(valor) ? percentual.format(valor) : SEM_VALOR),
  segundos: (ms: number | null | undefined) => (valido(ms) ? `${decimal.format(ms / 1000)} s` : SEM_VALOR),
  /** Minutos com uma casa (o tempo parado de um impasse, US #405). */
  minutos: (valor: number | null | undefined) => (valido(valor) ? `${decimal.format(valor)} min` : SEM_VALOR),
  /**
   * O rótulo da versão do dev.kit, como o app o mostra na barra de título (US #467 · task #470): "dev.kit versão 136".
   * O formatador único da landing e do painel; sem versão, o "?" de quem ainda não a informou.
   */
  versaoDoDevKit: (versao: number | string | null | undefined) => `dev.kit versão ${versao === null || versao === undefined || versao === '' ? '?' : versao}`,
  /** Bytes em MB com uma casa (o tamanho do pacote do dev.kit). */
  megabytes: (bytes: number | null | undefined) => (valido(bytes) ? `${decimal.format(bytes / 1_048_576)} MB` : SEM_VALOR),
  /** A data por extenso curto (dd/mm/aaaa). */
  data: (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { timeZone: 'UTC' }) : SEM_VALOR),
  dataHora: (iso: string | null | undefined) =>
    iso ? new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : SEM_VALOR,
  /** Minutos como o painel de utilidade os escreve (US #687): "45 min", "6h40", "2d 3h"; o sinal quando pedido ("+1h50"). */
  duracao: (minutos: number | null | undefined, comSinal = false) => {
    if (!valido(minutos)) return SEM_VALOR;
    const sinal = comSinal ? (minutos < 0 ? '−' : '+') : minutos < 0 ? '−' : '';
    const total = Math.round(Math.abs(minutos));
    const texto = total < 60 ? `${total} min`
      : total < 24 * 60 ? `${Math.floor(total / 60)}h${String(total % 60).padStart(2, '0')}`
        : `${Math.floor(total / (24 * 60))}d ${Math.floor((total % (24 * 60)) / 60)}h`;
    return sinal + texto;
  },
  /** Há quanto tempo, em palavras curtas (US #687): "há 3 dias", "há 2 h", "há 15 min". */
  idade: (iso: string | null | undefined, agora = new Date()) => {
    if (!iso) return SEM_VALOR;
    const minutos = Math.max(0, Math.round((agora.getTime() - new Date(iso).getTime()) / 60_000));
    if (minutos < 60) return `há ${minutos} min`;
    if (minutos < 24 * 60) return `há ${Math.floor(minutos / 60)} h`;
    const dias = Math.floor(minutos / (24 * 60));
    return `há ${dias} ${dias === 1 ? 'dia' : 'dias'}`;
  },
  /** DD/MM de um AAAA-MM-DD (o rótulo do eixo da série diária). */
  dia: (iso: string) => {
    const [, mes, dia] = iso.split('-');
    return `${dia}/${mes}`;
  },
};

/** AAAA-MM-DD de uma data (no fuso UTC, como a API agrega). */
export function diaIso(data: Date): string {
  return data.toISOString().slice(0, 10);
}

/** O período dos últimos `dias` dias até hoje. */
export function ultimosDias(dias: number, hoje = new Date()): { de: string; ate: string } {
  const inicio = new Date(hoje);
  inicio.setUTCDate(inicio.getUTCDate() - (dias - 1));
  return { de: diaIso(inicio), ate: diaIso(hoje) };
}
