import { useEffect, useState } from 'react';

interface PedidoDeInstalacao extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

/** A instalação é local ao navegador e nunca envia um comando para a máquina. */
export function InstalarBoard() {
  const [pedido, setPedido] = useState<PedidoDeInstalacao | null>(null);
  const [ajuda, setAjuda] = useState(false);
  const [instalado, setInstalado] = useState(() => window.matchMedia('(display-mode: standalone)').matches);
  const [erro, setErro] = useState('');
  useEffect(() => {
    const disponivel = (e: Event) => { e.preventDefault(); setPedido(e as PedidoDeInstalacao); };
    const concluido = () => { setInstalado(true); setPedido(null); setAjuda(false); };
    window.addEventListener('beforeinstallprompt', disponivel);
    window.addEventListener('appinstalled', concluido);
    if ('serviceWorker' in navigator) {
      void navigator.serviceWorker.register('/board-sw.js', { scope: '/board' }).catch(() => {
        setErro('Instalação indisponível agora. Reabra o Board com internet e tente novamente.');
      });
    }
    return () => {
      window.removeEventListener('beforeinstallprompt', disponivel);
      window.removeEventListener('appinstalled', concluido);
    };
  }, []);
  async function instalar() {
    if (!pedido) { setAjuda(true); return; }
    try {
      await pedido.prompt();
      await pedido.userChoice;
      setPedido(null);
    } catch { setErro('Não foi possível instalar. Tente pelo menu do navegador.'); setAjuda(true); }
  }
  return <div className="board-instalacao">
    <small>PWA · instalação pelo navegador · consulta pela internet</small>
    <button className="board-btn" onClick={() => void instalar()} disabled={instalado}>{instalado ? 'App instalado' : 'Instalar app'}</button>
    {(ajuda || erro) && <div className="board-ajuda" role="dialog" aria-modal="false" aria-label="Instalar o Board">
      <strong>Instalar o Board</strong>
      <p>{erro || 'No Chrome ou Edge, abra o menu do navegador e escolha Instalar app. No iPhone ou iPad, use Compartilhar → Adicionar à Tela de Início.'}</p>
      <p>A instalação precisa de HTTPS (ou localhost). O Board requer internet e login; nenhum retrato é guardado para consulta offline.</p>
      <button className="board-btn" onClick={() => { setAjuda(false); setErro(''); }}>Fechar</button>
    </div>}
  </div>;
}
