import { fluxoDeExemplo } from '../../testes/servidor';
import {
  lerFluxo, lerPlano, lerSkill, lerSkillMarkdown, montarPlano, montarSkill, PLANO_VAZIO, SKILL_VAZIA, skillMarkdown, tamanhoEmBytes, tamanhoLegivel, textoDoFluxo,
  validarPlano, validarSkill,
} from './conteudo';

describe('Conteúdo dos itens do catálogo (US #461) — o formato do dev.kit', () => {
  it('monta a skill como o SkillJson: os campos na ordem do arquivo, indentado com dois espaços', () => {
    const json = montarSkill({
      ...SKILL_VAZIA, name: ' compilar-uau ', description: 'Compila o UAU', content: '# Compilar\r\nRode o build.\n\n',
      arguments: 'branch\n\nBranch\nmodo', repositories: 'uau\n', isCommand: true,
    });

    expect(Object.keys(JSON.parse(json))).toEqual(['name', 'description', 'content', 'arguments', 'repositories', 'isCommand', 'isBackground', 'isPinned']);
    expect(json).toBe([
      '{',
      '  "name": "compilar-uau",',
      '  "description": "Compila o UAU",',
      '  "content": "# Compilar\\nRode o build.",',
      '  "arguments": [',
      '    "branch",',
      '    "modo"',
      '  ],',
      '  "repositories": [',
      '    "uau"',
      '  ],',
      '  "isCommand": true,',
      '  "isBackground": false,',
      '  "isPinned": false',
      '}',
    ].join('\n'));
  });

  it('valida a skill com as mensagens do app e o nome que vira pasta', () => {
    expect(validarSkill(SKILL_VAZIA)).toContain('A skill está sem nome (campo "name").');
    expect(validarSkill({ ...SKILL_VAZIA, name: 'compilar-uau' })).toEqual(['A skill "compilar-uau" veio sem conteúdo (campo "content").']);
    expect(validarSkill({ ...SKILL_VAZIA, name: 'Compilar UAU', content: 'x' })[0]).toMatch(/só minúsculas, números e hífen/);
    expect(validarSkill({ ...SKILL_VAZIA, name: 'compilar-uau-2', content: 'x' })).toEqual([]);
  });

  it('monta a Rule como o TechPlanJson: a chave abre, as marcações fecham', () => {
    const json = montarPlano({ ...PLANO_VAZIO, key: 'geral', description: 'Regras gerais', content: 'Siga.', isRequired: true });

    expect(Object.keys(JSON.parse(json))).toEqual(['key', 'description', 'content', 'repository', 'onlyWithWorkItem', 'isRequired', 'isAgentInstruction']);
    expect(JSON.parse(json)).toMatchObject({ key: 'geral', isRequired: true, isAgentInstruction: false });
    expect(validarPlano(PLANO_VAZIO)).toContain('A Rule está sem descrição (campo "description").');
    expect(validarPlano({ ...PLANO_VAZIO, description: 'Regras' })).toEqual(['A Rule "Regras" veio sem conteúdo (campo "content").']);
  });

  it('lê de volta o que montou (e o conteúdo guardado como objeto)', () => {
    const campos = { ...SKILL_VAZIA, name: 'a', description: 'b', content: 'c', arguments: 'x\ny', isPinned: true };
    expect(lerSkill(montarSkill(campos))).toEqual(campos);
    expect(lerPlano({ key: 'k', description: 'd', content: 'c', isAgentInstruction: 'true' })).toMatchObject({ key: 'k', isAgentInstruction: true, repository: '' });
    expect(lerSkill('não é json')).toEqual(SKILL_VAZIA);
  });

  it('confere o envelope do fluxo e monta a prévia dos passos', () => {
    const lido = lerFluxo(JSON.stringify(fluxoDeExemplo));

    expect(lido.erros).toEqual([]);
    expect(lido.nome).toBe('entrega uau');
    expect(lido.passos).toEqual([
      { id: 'task', kind: 'Task', skillName: '' },
      { id: 'e1', kind: 'Step', skillName: 'compilar-uau' },
    ]);
    expect(textoDoFluxo(JSON.stringify(fluxoDeExemplo))).toBe(JSON.stringify(fluxoDeExemplo, null, 2));
  });

  it('recusa o fluxo que não é o envelope devkit-fluxo v1', () => {
    expect(lerFluxo('').erros[0]).toMatch(/Cole o JSON do fluxo/);
    expect(lerFluxo('{ "formato": ').erros[0]).toMatch(/^O JSON do fluxo é inválido/);
    expect(lerFluxo('[]').erros).toEqual(['O JSON não descreve um fluxo (era esperado um objeto).']);

    const erros = lerFluxo(JSON.stringify({ formato: 'outro', versao: 2 })).erros;
    expect(erros).toEqual([
      'O arquivo não é um fluxo do dev.kit (o campo "formato" tem de ser "devkit-fluxo").',
      'A versão 2 do formato não é suportada (o site lê a versão 1).',
      'O fluxo está sem nome (campo "nome").',
      'O fluxo está sem o desenho (campo "desenho" com os "nodes").',
    ]);
  });
});

describe('O SKILL.md e a Rule com stack do editor de artefato (US #687)', () => {
  it('o SKILL.md vai e volta: o frontmatter com o nome e a descrição, e o corpo', () => {
    const md = skillMarkdown({ name: 'compilar-net-framework', description: 'Compilar .NET Framework (MSBuild)', content: '# Compilar\n\n1. Ache o MSBuild.' });

    expect(md).toBe('---\nname: compilar-net-framework\ndescription: Compilar .NET Framework (MSBuild)\n---\n\n# Compilar\n\n1. Ache o MSBuild.');
    expect(lerSkillMarkdown(md)).toEqual({ name: 'compilar-net-framework', description: 'Compilar .NET Framework (MSBuild)', content: '# Compilar\n\n1. Ache o MSBuild.', erros: [] });
  });

  it('o frontmatter que falta ou não fecha é erro', () => {
    expect(lerSkillMarkdown('# só o corpo').erros).toEqual(['O SKILL.md começa pelo frontmatter (a linha ---).']);
    expect(lerSkillMarkdown('---\nname: x').erros).toEqual(['O frontmatter do SKILL.md não fecha (falta a segunda linha ---).']);
    expect(lerSkillMarkdown('---\nname: x\n---\ncorpo').erros).toEqual(['O frontmatter está sem a "description".']);
  });

  it('a Rule leva a stack e o tipo só quando existem (o hash das outras não muda)', () => {
    const sem = montarPlano({ ...PLANO_VAZIO, key: 'build', description: 'Build', content: 'x' });
    const com = montarPlano({ ...PLANO_VAZIO, key: 'licenca', description: 'Licença', content: 'x', stack: 'DotNet; vb6, dotnet', tipo: 'Impacto' });

    expect(sem).not.toContain('stack');
    expect(JSON.parse(com)).toMatchObject({ stack: 'dotnet, vb6', tipo: 'impacto' });
    expect(Object.keys(JSON.parse(com)).slice(-2)).toEqual(['stack', 'tipo']);
    expect(lerPlano(com)).toMatchObject({ stack: 'dotnet, vb6', tipo: 'impacto' });
  });

  it('o tamanho em UTF-8 e como o rodapé o escreve', () => {
    expect(tamanhoEmBytes('ação')).toBe(6);
    expect(tamanhoLegivel(900)).toBe('900 B');
    expect(tamanhoLegivel(1229)).toBe('1,2 KB');
  });
});
