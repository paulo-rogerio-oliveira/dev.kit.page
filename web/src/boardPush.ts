import { api, ErroDaApi } from './api/cliente';

export const CHAVE_DO_PUSH = 'devkitpage.push';
export const suportaPush = () => window.isSecureContext && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
export const idDoPush = () => localStorage.getItem(CHAVE_DO_PUSH);

export function chaveDoPush(texto: string): Uint8Array<ArrayBuffer> {
  const base = texto.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(texto.length / 4) * 4, '=');
  return Uint8Array.from(atob(base), c => c.charCodeAt(0));
}

async function registro() {
  return await navigator.serviceWorker.getRegistration('/board')
    ?? await navigator.serviceWorker.register('/board-sw.js', { scope: '/board' });
}

export async function conferirPush(token: string): Promise<boolean> {
  const id = idDoPush();
  if (!id) return false;
  const sub = await (await registro()).pushManager.getSubscription();
  if (!sub || Notification.permission !== 'granted') {
    await api.cancelarPush(token, id); localStorage.removeItem(CHAVE_DO_PUSH); return false;
  }
  try { await api.assinaturaDoPush(token, id); return true; }
  catch (e) {
    if (!(e instanceof ErroDaApi && e.status === 404)) throw e;
    // Outra conta, rotação de chave ou assinatura expirada: o endpoint antigo não continua recebendo.
    await sub.unsubscribe(); localStorage.removeItem(CHAVE_DO_PUSH); return false;
  }
}

export async function ativarPush(token: string, publica: string) {
  // Primeiro await é a permissão: conserva a interação direta exigida pelo iOS.
  if (await Notification.requestPermission() !== 'granted') throw new Error('Permissão não concedida. Você pode alterá-la nas configurações do navegador ou do sistema.');
  const reg = await registro();
  if (!reg.active) {
    await new Promise<void>((resolve, reject) => {
      const worker = reg.installing ?? reg.waiting;
      if (!worker) { reject(new Error('A instalação do app não foi concluída. Reabra o Board.')); return; }
      const timer = window.setTimeout(() => { worker.removeEventListener('statechange', mudou); reject(new Error('A instalação do app demorou. Tente novamente.')); }, 10_000);
      function mudou() {
        if (worker!.state === 'activated') { window.clearTimeout(timer); worker!.removeEventListener('statechange', mudou); resolve(); }
      }
      worker.addEventListener('statechange', mudou); mudou();
    });
  }
  let sub = await reg.pushManager.getSubscription();
  const chave = chaveDoPush(publica);
  if (sub && (!sub.options.applicationServerKey || String(new Uint8Array(sub.options.applicationServerKey)) !== String(chave))) {
    await sub.unsubscribe(); sub = null;
  }
  sub ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: chave });
  let id: string | undefined;
  try {
    id = (await api.assinarPush(token, sub.toJSON())).id;
    localStorage.setItem(CHAVE_DO_PUSH, id);
  } catch (e) {
    if (id) await api.cancelarPush(token, id).catch(() => {});
    await sub.unsubscribe().catch(() => {});
    throw e;
  }
}

export async function desligarPush(token: string) {
  const id = idDoPush();
  // A API confirma o cancelamento antes de a interface declarar que desligou.
  if (id) await api.cancelarPush(token, id);
  localStorage.removeItem(CHAVE_DO_PUSH);
  const reg = await navigator.serviceWorker.getRegistration('/board');
  const sub = await reg?.pushManager.getSubscription();
  await sub?.unsubscribe().catch(() => {});
  for (const aviso of await reg?.getNotifications() ?? []) aviso.close();
}

/** Sair explicitamente também cancela o push deste dispositivo; expirar o JWT pede login ao abrir o aviso. */
export async function cancelarPushAoSair(token: string) {
  if (!suportaPush()) return;
  try { await desligarPush(token); }
  catch {
    // Mesmo sem rede, o navegador deixa de receber; a API remove o endpoint em 404/410 ou na retenção.
    localStorage.removeItem(CHAVE_DO_PUSH);
    const reg = await navigator.serviceWorker.getRegistration('/board');
    await (await reg?.pushManager.getSubscription())?.unsubscribe();
  }
}
