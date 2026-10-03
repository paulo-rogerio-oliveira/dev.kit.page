import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { requisicoes, servidor } from './servidor';

beforeAll(() => servidor.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  cleanup();
  servidor.resetHandlers();
  sessionStorage.clear();
  requisicoes.length = 0;
});
afterAll(() => servidor.close());
