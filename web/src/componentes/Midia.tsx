import { useEffect, useRef, useState } from 'react';
import type { Midia as DadosDaMidia } from '../conteudo/landing';
import { usePrefereMenosMovimento } from './usePrefereMenosMovimento';

/**
 * A mídia animada de uma seção. O vídeo é muted, em loop, playsInline e autoPlay, com preload=none:
 * as fontes só entram quando ele chega perto da tela (IntersectionObserver), então nada pesa no
 * primeiro carregamento além do poster. Com prefers-reduced-motion fica o poster, parado; sem vídeo
 * (ou quando ele falha) entra a captura estática. Largura e altura fixas: a página não salta.
 */
export function Midia({ midia, className }: { midia: DadosDaMidia; className?: string }) {
  const menosMovimento = usePrefereMenosMovimento();
  const [falhou, setFalhou] = useState(false);
  const [visivel, setVisivel] = useState(false);
  const video = useRef<HTMLVideoElement>(null);
  const comVideo = Boolean(midia.video) && !falhou && !menosMovimento;

  useEffect(() => {
    const alvo = video.current;
    if (!comVideo || !alvo) return;
    if (typeof IntersectionObserver === 'undefined') {
      setVisivel(true);
      return;
    }
    const observador = new IntersectionObserver((entradas) => {
      if (entradas.some((e) => e.isIntersecting)) {
        setVisivel(true);
        observador.disconnect();
      }
    }, { rootMargin: '200px' });
    observador.observe(alvo);
    return () => observador.disconnect();
  }, [comVideo]);

  useEffect(() => {
    // As <source> entraram depois da montagem: o elemento só as lê num load() novo.
    if (visivel && video.current) video.current.load();
  }, [visivel]);

  const classe = `midia${className ? ` ${className}` : ''}`;

  if (!comVideo) {
    // Menos movimento: o poster (o primeiro quadro do vídeo). Sem vídeo ou com falha: a captura estática.
    const imagem = menosMovimento ? midia.poster : midia.captura ?? midia.poster;
    return (
      <img className={classe} src={imagem} alt={midia.alt} width={midia.largura} height={midia.altura} loading="lazy" decoding="async" />
    );
  }

  return (
    <video
      ref={video}
      className={classe}
      aria-label={midia.alt}
      poster={midia.poster}
      width={midia.largura}
      height={midia.altura}
      muted
      loop
      playsInline
      autoPlay
      preload="none"
      onError={() => setFalhou(true)}
    >
      {visivel && midia.video && (
        <>
          <source src={midia.video.webm} type="video/webm" />
          {/* O erro de uma <source> não chega ao <video>: a última a falhar é que aciona a captura. */}
          <source src={midia.video.mp4} type="video/mp4" onError={() => setFalhou(true)} />
        </>
      )}
    </video>
  );
}
