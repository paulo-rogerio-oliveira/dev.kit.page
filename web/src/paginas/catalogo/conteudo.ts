// O conteúdo de um item editável (US #461) — o MESMO JSON que o dev.kit desktop grava, montado a partir do
// formulário com os mesmos campos e na MESMA ORDEM (a ordem das propriedades é a do arquivo): o `SkillJson` e o
// `TechPlanJson` do git.kit (a Rule; o nome interno segue o antigo Tech Plan) e o envelope `devkit-fluxo` do
// `FlowCatalogFiles`. As mensagens de erro são as do app, com o nome que a tela mostra (Rule), para o admin ver no
// site o que veria no dev.kit.

/** Os campos do formulário da skill (as listas como texto, uma linha por item). */
export interface CamposDaSkill {
  name: string;
  description: string;
  content: string;
  arguments: string;
  repositories: string;
  isCommand: boolean;
  isBackground: boolean;
  isPinned: boolean;
}

/** Os campos do formulário da Rule. */
export interface CamposDoPlano {
  key: string;
  description: string;
  content: string;
  repository: string;
  onlyWithWorkItem: boolean;
  isRequired: boolean;
  isAgentInstruction: boolean;
}

export const SKILL_VAZIA: CamposDaSkill = {
  name: '', description: '', content: '', arguments: '', repositories: '', isCommand: false, isBackground: false, isPinned: false,
};

export const PLANO_VAZIO: CamposDoPlano = {
  key: '', description: '', content: '', repository: '', onlyWithWorkItem: false, isRequired: false, isAgentInstruction: false,
};

/** O nome da skill é a pasta dela na máquina: minúsculas, números e hífen. */
export const NOME_DE_SKILL = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** Uma lista em linhas (como o app aceita): sem as vazias, sem espaço nas pontas e sem repetição (maiúsculas à parte). */
function linhas(texto: string): string[] {
  const vistas = new Set<string>();
  return texto.split('\n').map((l) => l.trim()).filter((l) => {
    const chave = l.toLowerCase();
    if (!l || vistas.has(chave)) return false;
    vistas.add(chave);
    return true;
  });
}

/** O conteúdo como o app guarda: fim de linha do Unix e sem espaço no fim. */
const texto = (valor: string) => valor.replace(/\r\n/g, '\n').trimEnd();

/** O JSON da skill — o `SkillJson.Export` do git.kit: indentado com dois espaços, os campos na ordem do arquivo. */
export function montarSkill(c: CamposDaSkill): string {
  return JSON.stringify({
    name: c.name.trim(),
    description: c.description.trim(),
    content: texto(c.content),
    arguments: linhas(c.arguments),
    repositories: linhas(c.repositories),
    isCommand: c.isCommand,
    isBackground: c.isBackground,
    isPinned: c.isPinned,
  }, null, 2);
}

/** Os erros da skill antes de mandar à API — os textos do `SkillJson.Read`, e o do nome que vira pasta. */
export function validarSkill(c: CamposDaSkill): string[] {
  const erros: string[] = [];
  const nome = c.name.trim();
  if (!nome) erros.push('A skill está sem nome (campo "name").');
  else if (!NOME_DE_SKILL.test(nome)) erros.push(`O nome "${nome}" tem de usar só minúsculas, números e hífen (é a pasta da skill na máquina).`);
  if (!c.content.trim()) erros.push(`A skill "${nome}" veio sem conteúdo (campo "content").`);
  return erros;
}

/** O JSON da Rule — o `TechPlanJson.Export`: a chave (a identidade) abre o arquivo, as marcações o fecham. */
export function montarPlano(c: CamposDoPlano): string {
  return JSON.stringify({
    key: c.key.trim(),
    description: c.description.trim(),
    content: texto(c.content),
    repository: c.repository.trim(),
    onlyWithWorkItem: c.onlyWithWorkItem,
    isRequired: c.isRequired,
    isAgentInstruction: c.isAgentInstruction,
  }, null, 2);
}

/** Os erros da Rule — os textos do `TechPlanJson.Read` (descrição e conteúdo são os obrigatórios). */
export function validarPlano(c: CamposDoPlano): string[] {
  const erros: string[] = [];
  const descricao = c.description.trim();
  if (!descricao) erros.push('A Rule está sem descrição (campo "description").');
  if (!c.content.trim()) erros.push(`A Rule "${descricao}" veio sem conteúdo (campo "content").`);
  return erros;
}

/** Lê o objeto do conteúdo guardado — a API o devolve como texto; uma versão que o devolva como objeto também serve. */
function objeto(conteudo: unknown): Record<string, unknown> {
  if (conteudo && typeof conteudo === 'object') return conteudo as Record<string, unknown>;
  try {
    const lido = JSON.parse(String(conteudo ?? '')) as unknown;
    return lido && typeof lido === 'object' ? lido as Record<string, unknown> : {};
  } catch {
    return {};
  }
}

const campo = (o: Record<string, unknown>, nome: string) => (typeof o[nome] === 'string' ? o[nome] as string : '');
const marca = (o: Record<string, unknown>, nome: string) => o[nome] === true || o[nome] === 'true';
const lista = (o: Record<string, unknown>, nome: string) =>
  (Array.isArray(o[nome]) ? (o[nome] as unknown[]).map(String).join('\n') : campo(o, nome));

/** Os campos do formulário a partir da skill guardada (tolerante como o app: o que falta fica vazio). */
export function lerSkill(conteudo: unknown): CamposDaSkill {
  const o = objeto(conteudo);
  return {
    name: campo(o, 'name'), description: campo(o, 'description'), content: campo(o, 'content'),
    arguments: lista(o, 'arguments'), repositories: lista(o, 'repositories'),
    isCommand: marca(o, 'isCommand'), isBackground: marca(o, 'isBackground'), isPinned: marca(o, 'isPinned'),
  };
}

/** Os campos do formulário a partir da Rule guardada. */
export function lerPlano(conteudo: unknown): CamposDoPlano {
  const o = objeto(conteudo);
  return {
    key: campo(o, 'key'), description: campo(o, 'description'), content: campo(o, 'content'), repository: campo(o, 'repository'),
    onlyWithWorkItem: marca(o, 'onlyWithWorkItem'), isRequired: marca(o, 'isRequired'), isAgentInstruction: marca(o, 'isAgentInstruction'),
  };
}

/** O texto do fluxo para o editor: o JSON guardado, indentado (o objeto que vier, como texto). */
export function textoDoFluxo(conteudo: unknown): string {
  if (conteudo && typeof conteudo === 'object') return JSON.stringify(conteudo, null, 2);
  const bruto = String(conteudo ?? '');
  try {
    return JSON.stringify(JSON.parse(bruto), null, 2);
  } catch {
    return bruto;
  }
}

/** Um passo da prévia do fluxo: o nó do desenho, o tipo dele e a skill que ele roda. */
export interface PassoDoFluxo {
  id: string;
  kind: string;
  skillName: string;
}

/** O fluxo lido do texto: o nome, a descrição e os passos — ou os erros que impedem salvar. */
export interface FluxoLido {
  nome: string;
  descricao: string;
  passos: PassoDoFluxo[];
  /** As Rules embutidas (`planos[]`): chegam à máquina junto com o fluxo. */
  planos: number;
  erros: string[];
}

/**
 * Confere o envelope `devkit-fluxo` (o que `devcli fluxos --exportar` grava): JSON válido, o `formato`, a `versao` 1,
 * o `nome` e o `desenho` com os nós. A validação completa (as referências, o desenho que o dev.kit lê) é da API;
 * esta é a que dá para fazer antes de mandar, e a que monta a prévia.
 */
export function lerFluxo(texto: string): FluxoLido {
  const vazio: FluxoLido = { nome: '', descricao: '', passos: [], planos: 0, erros: [] };
  if (!texto.trim()) return { ...vazio, erros: ['Cole o JSON do fluxo ou carregue o arquivo exportado pelo dev.kit.'] };

  let lido: unknown;
  try {
    lido = JSON.parse(texto);
  } catch (falha) {
    return { ...vazio, erros: [`O JSON do fluxo é inválido: ${falha instanceof Error ? falha.message : String(falha)}`] };
  }
  if (!lido || typeof lido !== 'object' || Array.isArray(lido)) return { ...vazio, erros: ['O JSON não descreve um fluxo (era esperado um objeto).'] };

  const o = lido as Record<string, unknown>;
  const erros: string[] = [];
  if (o.formato !== 'devkit-fluxo') erros.push('O arquivo não é um fluxo do dev.kit (o campo "formato" tem de ser "devkit-fluxo").');
  if (o.versao !== 1) erros.push(`A versão ${String(o.versao ?? '(ausente)')} do formato não é suportada (o site lê a versão 1).`);
  const nome = campo(o, 'nome').trim();
  if (!nome) erros.push('O fluxo está sem nome (campo "nome").');

  const desenho = o.desenho && typeof o.desenho === 'object' ? o.desenho as Record<string, unknown> : null;
  const nos = desenho && Array.isArray(desenho.nodes) ? desenho.nodes as Record<string, unknown>[] : null;
  if (!nos) erros.push('O fluxo está sem o desenho (campo "desenho" com os "nodes").');

  const passos = (nos ?? []).filter((n) => n && typeof n === 'object').map((n) => ({
    id: campo(n, 'id'), kind: campo(n, 'kind'), skillName: campo(n, 'skillName'),
  }));
  return { nome, descricao: campo(o, 'descricao'), passos, planos: Array.isArray(o.planos) ? o.planos.length : 0, erros };
}
