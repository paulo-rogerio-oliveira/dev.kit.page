import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { App } from '../App';
import type { LoginResponse } from '../api/tipos';
import { CHAVE_DA_SESSAO, SessaoProvider } from '../sessao';

/** Abre o app numa rota, com (ou sem) uma sessão já gravada — como o navegador depois de um login. */
export function renderizar(rota: string, sessao?: LoginResponse) {
  if (sessao) sessionStorage.setItem(CHAVE_DA_SESSAO, JSON.stringify(sessao));
  return render(
    <MemoryRouter initialEntries={[rota]}>
      <SessaoProvider>
        <App />
      </SessaoProvider>
    </MemoryRouter>,
  );
}
