// Ícones determinísticos sem dependência gráfica: marca dK branca sobre o vermelho do dev.kit.
import { writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
const letras = ['01100100010', '00000100100', '01110111000', '10010100100', '10010100010', '01110100010'];
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const b of bytes) {
    crc ^= b;
    for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function bloco(tipo, dados) {
  const tamanho = Buffer.alloc(4); tamanho.writeUInt32BE(dados.length);
  const conteudo = Buffer.concat([Buffer.from(tipo), dados]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(conteudo));
  return Buffer.concat([tamanho, conteudo, crc]);
}
for (const lado of [192, 512]) {
  const pixels = Buffer.alloc((lado * 3 + 1) * lado);
  const escala = Math.floor(lado / 18);
  const inicioX = Math.floor((lado - 11 * escala) / 2), inicioY = Math.floor((lado - 6 * escala) / 2);
  for (let y = 0; y < lado; y++) for (let x = 0; x < lado; x++) {
    const lx = Math.floor((x - inicioX) / escala), ly = Math.floor((y - inicioY) / escala);
    const branco = letras[ly]?.[lx] === '1';
    const pos = y * (lado * 3 + 1) + 1 + x * 3;
    pixels.set(branco ? [255, 255, 255] : [199, 54, 43], pos);
  }
  const cabecalho = Buffer.alloc(13);
  cabecalho.writeUInt32BE(lado); cabecalho.writeUInt32BE(lado, 4); cabecalho[8] = 8; cabecalho[9] = 2;
  writeFileSync(new URL(`../public/board-${lado}.png`, import.meta.url), Buffer.concat([
    Buffer.from([137,80,78,71,13,10,26,10]), bloco('IHDR', cabecalho), bloco('IDAT', deflateSync(pixels)), bloco('IEND', Buffer.alloc(0)),
  ]));
}
