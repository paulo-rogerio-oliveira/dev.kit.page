import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { papelDa, useSessao } from './sessao';
import { Landing } from './paginas/Landing';
import { Login } from './paginas/Login';
import { TrocarSenha } from './paginas/TrocarSenha';
import { Dashboard } from './paginas/Dashboard';
import { Empresas } from './paginas/Empresas';

/**
 * A rota protegida: sem sessão (ou com ela vencida) volta ao login; com a troca de senha
 * pendente, só a troca é acessível — a mesma regra que a API aplica (403). `soAdmin` (US #381)
 * leva o gestor de volta ao dashboard da empresa dele.
 */
export function RotaProtegida({ children, permiteTrocaPendente = false, soAdmin = false }: {
  children: ReactNode;
  permiteTrocaPendente?: boolean;
  soAdmin?: boolean;
}) {
  const { sessao } = useSessao();
  const local = useLocation();

  if (!sessao) return <Navigate to="/login" replace state={{ de: local.pathname }} />;
  if (sessao.deveTrocarSenha && !permiteTrocaPendente) return <Navigate to="/trocar-senha" replace />;
  if (soAdmin && papelDa(sessao) !== 'admin') return <Navigate to="/dashboard" replace />;
  return <>{children}</>;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/login" element={<Login />} />
      <Route path="/trocar-senha" element={<RotaProtegida permiteTrocaPendente><TrocarSenha /></RotaProtegida>} />
      <Route path="/dashboard" element={<RotaProtegida><Dashboard /></RotaProtegida>} />
      <Route path="/empresas" element={<RotaProtegida soAdmin><Empresas /></RotaProtegida>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
