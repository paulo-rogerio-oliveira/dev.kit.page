import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { entraNoPainel, papelDa, useSessao } from './sessao';
import { Landing } from './paginas/Landing';
import { Login } from './paginas/Login';
import { TrocarSenha } from './paginas/TrocarSenha';
import { Dashboard } from './paginas/Dashboard';
import { Empresas } from './paginas/Empresas';
import { Usuarios } from './paginas/Usuarios';
import { Catalogo } from './paginas/Catalogo';
import { Aderir } from './paginas/Aderir';
import { DefinirSenha } from './paginas/DefinirSenha';
import { Conta } from './paginas/Conta';
import { Comprar } from './paginas/Comprar';
import { CompraConcluida } from './paginas/CompraConcluida';
import { EmpresaUsuarios } from './paginas/EmpresaUsuarios';
import { EmpresaCatalogo } from './paginas/EmpresaCatalogo';
import { EmpresaConhecimento } from './paginas/EmpresaConhecimento';
import { Relatos } from './paginas/Relatos';
import { Design, VersaoDoDesign } from './paginas/Design';

/**
 * A rota protegida: sem sessão (ou com ela vencida) volta ao login; com a troca de senha
 * pendente, só a troca é acessível — a mesma regra que a API aplica (403). `soAdmin` (US #381)
 * leva o gestor de volta ao dashboard da empresa dele; `doPainel` (US #405) leva o dev — que não
 * entra no painel — à conta dele (US #425); `soGestor` (US #425) é a administração da empresa: o admin
 * (que não é de empresa nenhuma) volta ao dashboard, e o dev à conta. `daEmpresa` (US #491) é de quem é DA empresa —
 * o gestor e o dev —: o admin volta ao dashboard.
 */
export function RotaProtegida({ children, permiteTrocaPendente = false, soAdmin = false, doPainel = false, soGestor = false, daEmpresa = false }: {
  children: ReactNode;
  permiteTrocaPendente?: boolean;
  soAdmin?: boolean;
  doPainel?: boolean;
  soGestor?: boolean;
  daEmpresa?: boolean;
}) {
  const { sessao } = useSessao();
  const local = useLocation();

  if (!sessao) return <Navigate to="/login" replace state={{ de: local.pathname }} />;
  if (sessao.deveTrocarSenha && !permiteTrocaPendente) return <Navigate to="/trocar-senha" replace />;
  if ((doPainel || soAdmin || soGestor) && !entraNoPainel(sessao)) return <Navigate to="/conta" replace />;
  if (soAdmin && papelDa(sessao) !== 'admin') return <Navigate to="/dashboard" replace />;
  if (soGestor && papelDa(sessao) !== 'gestor') return <Navigate to="/dashboard" replace />;
  if (daEmpresa && papelDa(sessao) === 'admin') return <Navigate to="/dashboard" replace />;
  return <>{children}</>;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/login" element={<Login />} />
      <Route path="/catalogo" element={<Catalogo />} />
      <Route path="/aderir" element={<Aderir />} />
      <Route path="/definir-senha" element={<DefinirSenha />} />
      <Route path="/comprar" element={<Comprar />} />
      <Route path="/compra-concluida" element={<CompraConcluida />} />
      <Route path="/trocar-senha" element={<RotaProtegida permiteTrocaPendente><TrocarSenha /></RotaProtegida>} />
      <Route path="/conta" element={<RotaProtegida><Conta /></RotaProtegida>} />
      <Route path="/dashboard" element={<RotaProtegida doPainel><Dashboard /></RotaProtegida>} />
      <Route path="/empresas" element={<RotaProtegida soAdmin><Empresas /></RotaProtegida>} />
      <Route path="/usuarios" element={<RotaProtegida soAdmin><Usuarios /></RotaProtegida>} />
      {/* A página das releases do básico (US #425) virou a aba Releases do /catalogo (US #461): o link antigo leva para lá. */}
      <Route path="/catalogo-base" element={<RotaProtegida soAdmin><Navigate to="/catalogo?aba=releases" replace /></RotaProtegida>} />
      <Route path="/empresa/usuarios" element={<RotaProtegida soGestor><EmpresaUsuarios /></RotaProtegida>} />
      <Route path="/empresa/catalogo" element={<RotaProtegida soGestor><EmpresaCatalogo /></RotaProtegida>} />
      <Route path="/empresa/conhecimento" element={<RotaProtegida daEmpresa><EmpresaConhecimento /></RotaProtegida>} />
      {/* Os relatos dos usuários (US #602): do painel — o admin geral vê todos, o gestor os da empresa dele; o dev vai à conta. */}
      <Route path="/relatos" element={<RotaProtegida doPainel><Relatos /></RotaProtegida>} />
      {/* O design compartilhado (US #669): de todo usuário logado (sem empresa, o nível usuário) e o link estável da PR. */}
      <Route path="/design" element={<RotaProtegida><Design /></RotaProtegida>} />
      <Route path="/design/:projeto/:tela/:versao" element={<RotaProtegida><VersaoDoDesign /></RotaProtegida>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
