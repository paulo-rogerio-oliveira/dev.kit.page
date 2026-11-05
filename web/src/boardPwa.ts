/** Manifesto e worker pertencem ao Board; outras páginas mantêm seu comportamento. */
export function prepararBoardPwa() {
  if (window.location.pathname !== '/board') return;
  if (!document.querySelector('link[rel="manifest"]')) {
    const manifesto = document.createElement('link');
    manifesto.rel = 'manifest';
    manifesto.href = '/board.webmanifest';
    document.head.appendChild(manifesto);
  }
  if ('serviceWorker' in navigator) {
    void navigator.serviceWorker.register('/board-sw.js', { scope: '/board' }).catch(() => {
      // A leitura online continua disponível quando o navegador recusa a instalação.
    });
  }
}
