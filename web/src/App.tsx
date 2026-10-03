import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useSessao } from './sessao';
import { Landing } from './paginas/Landing';
import { Login } from './paginas/Login';
import { TrocarSenha } from './paginas/TrocarSenha';
import { Dashboard } from './paginas/Dashboard';

/**
 * A rota protegida: sem sessão (ou com ela vencida) volta ao login; com a troca de senha
 * pendente, só a troca é acessível — a mesma regra que a API aplica (403).
 */
export function RotaProtegida({ children, permiteTrocaPendente = false }: { children: ReactNode; permiteTrocaPendente?: boolean }) {
  const { sessao } = useSessao();
  const local = useLocation();

  if (!sessao) return <Navigate to="/login" replace state={{ de: local.pathname }} />;
  if (sessao.deveTrocarSenha && !permiteTrocaPendente) return <Navigate to="/trocar-senha" replace />;
  return <>{children}</>;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/login" element={<Login />} />
      <Route path="/trocar-senha" element={<RotaProtegida permiteTrocaPendente><TrocarSenha /></RotaProtegida>} />
      <Route path="/dashboard" element={<RotaProtegida><Dashboard /></RotaProtegida>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
