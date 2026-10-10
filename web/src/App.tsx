import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { entraNoPainel, papelDa, useSessao } from './sessao';
import { Landing } from './paginas/Landing';
import { Login } from './paginas/Login';
import { TrocarSenha } from './paginas/TrocarSenha';
import { Dashboard } from './paginas/Dashboard';
import { Empresas } from './paginas/Empresas';
import { Usuarios } from './paginas/Usuarios';

/**
 * A rota protegida: sem sessão (ou com ela vencida) volta ao login; com a troca de senha
 * pendente, só a troca é acessível — a mesma regra que a API aplica (403). `soAdmin` (US #381)
 * leva o gestor de volta ao dashboard da empresa dele; `doPainel` (US #405) leva o dev — que não
 * entra no painel — à página inicial, onde ele baixa o dev.kit.
 */
export function RotaProtegida({ children, permiteTrocaPendente = false, soAdmin = false, doPainel = false }: {
  children: ReactNode;
  permiteTrocaPendente?: boolean;
  soAdmin?: boolean;
  doPainel?: boolean;
}) {
  const { sessao } = useSessao();
  const local = useLocation();

  if (!sessao) return <Navigate to="/login" replace state={{ de: local.pathname }} />;
  if (sessao.deveTrocarSenha && !permiteTrocaPendente) return <Navigate to="/trocar-senha" replace />;
  if ((doPainel || soAdmin) && !entraNoPainel(sessao)) return <Navigate to="/" replace />;
  if (soAdmin && papelDa(sessao) !== 'admin') return <Navigate to="/dashboard" replace />;
  return <>{children}</>;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/login" element={<Login />} />
      <Route path="/trocar-senha" element={<RotaProtegida permiteTrocaPendente><TrocarSenha /></RotaProtegida>} />
      <Route path="/dashboard" element={<RotaProtegida doPainel><Dashboard /></RotaProtegida>} />
      <Route path="/empresas" element={<RotaProtegida soAdmin><Empresas /></RotaProtegida>} />
      <Route path="/usuarios" element={<RotaProtegida soAdmin><Usuarios /></RotaProtegida>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
