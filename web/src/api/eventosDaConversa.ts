import type { EventoDaConversa } from './remoto';

/** Lê SSE por fetch para manter o JWT no cabeçalho. Frames podem chegar em vários chunks. */
export async function lerEventosDaConversa(resposta: Response, receber: (evento: EventoDaConversa) => void) {
  if (!resposta.headers.get('content-type')?.includes('text/event-stream') || !resposta.body)
    throw new Error('A API não abriu o canal de atualizações da conversa.');
  const leitor = resposta.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let dados: string[] = [];
  function linha(texto: string) {
    if (!texto) {
      if (dados.length) {
        const evento = JSON.parse(dados.join('\n')) as EventoDaConversa | null;
        if (evento && Number.isSafeInteger(evento.id)) receber(evento);
      }
      dados = [];
    } else if (texto.startsWith('data:')) dados.push(texto.slice(5).replace(/^ /, ''));
  }
  try {
    while (true) {
      const { value, done } = await leitor.read();
      buffer += decoder.decode(value, { stream: !done });
      let fim: number;
      while ((fim = buffer.indexOf('\n')) !== -1) {
        linha(buffer.slice(0, fim).replace(/\r$/, ''));
        buffer = buffer.slice(fim + 1);
      }
      if (done) break;
    }
  } finally { await leitor.cancel(); leitor.releaseLock(); }
}
