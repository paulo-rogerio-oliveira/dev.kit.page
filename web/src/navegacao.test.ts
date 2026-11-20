import { comRetorno, destinoSeguro, listaDeProjetos, podeAbrir, retornoSeguro } from './navegacao';
import { destinoDa } from './sessao';
import { tokenDoDev, tokenDoGestor, tokenValido } from './testes/servidor';

describe('Destinos e retorno seguro', () => {
  it.each([tokenValido(), tokenDoGestor(), tokenDoDev()])('mantém a versão e o contexto do Design para $papel', sessao => {
    const origem = '/projetos?nivel=usuario&projeto=7&visao=propostas&filtro=teste#plano';
    const destino = comRetorno('/design/7/login/v2?hash=abc', origem);
    expect(destinoDa(sessao, destino)).toBe(destino);
    expect(retornoSeguro(sessao, origem, '/design/7/login/v2')).toBe(origem);
    expect(destinoDa(sessao, '/design?designTela=login&designModo=uma')).toBe('/design?designTela=login&designModo=uma');
  });
  it.each(['https://externo.test', '//externo.test', '/\\externo.test', '/%2fexterno.test', '/%5cexterno.test', '/%0a/conta', '/x/../conta', '/login', '/trocar-senha', '/desconhecido', '/%ZZ'])('recusa retorno inválido %s', valor => {
    expect(destinoSeguro(tokenDoDev(), valor)).toBeNull();
  });
  it('aplica ACL também ao destino pós-login', () => {
    expect(podeAbrir(tokenDoDev(), '/usuarios')).toBe(false);
    expect(podeAbrir(tokenDoGestor(), '/empresas')).toBe(false);
    expect(podeAbrir(tokenValido(), '/empresa/usuarios')).toBe(false);
    expect(destinoDa(tokenDoGestor(), '/empresas')).toBe('/dashboard');
    expect(destinoDa(tokenDoDev(), '/dashboard')).toBe('/conta');
  });
  it('evita ciclos diretos, indiretos e retornos proibidos', () => {
    expect(retornoSeguro(tokenDoDev(), '/design?designModo=uma', '/design')).toBeNull();
    expect(retornoSeguro(tokenDoDev(), comRetorno('/projetos', '/design'), '/design')).toBeNull();
    expect(retornoSeguro(tokenDoDev(), '/usuarios', '/design')).toBeNull();
    const design = comRetorno('/design?designTela=login', '/projetos?projeto=7');
    expect(retornoSeguro(tokenDoDev(), design, '/design/7/login/v2')).toBe(design);
  });
  it('volta à lista conservando filtros e parâmetros adicionais', () => {
    expect(listaDeProjetos(new URLSearchParams('nivel=usuario&projeto=7&visao=propostas&situacao=aprovado&extra=1')))
      .toBe('/projetos?nivel=usuario&situacao=aprovado&extra=1');
  });
});
