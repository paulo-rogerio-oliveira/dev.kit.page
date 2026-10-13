using System.IO.Compression;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #461 na base de verdade (SQLite com as migrations): o básico semeado, o catálogo nomeado e o slug único, o item
/// editável (rascunho → release → versão nova só do que mudou → retirada), o zip e o manifesto gravados, o fechamento
/// das dependências que recusa a publicação, a colisão de nomes, o isolamento entre empresas na distribuição, os
/// assentos e o gestor da tela de Empresas — e o script do Azure SQL.
/// </summary>
public sealed class Us461Tests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<T> Gestao<T>(Func<IGestaoDeCatalogos, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IGestaoDeCatalogos>()));

    private Task<T> Publicacao<T>(Func<IPublicacaoDeCatalogos, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IPublicacaoDeCatalogos>()));

    private static string SkillJson(string nome, string conteudo = "passo 1")
        => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, $"Sobre {nome}", conteudo, [], [], false, false, false));

    private static string FluxoJson(string nome, params string[] skills)
        => $$"""
            { "formato": "devkit-fluxo", "versao": 1, "nome": "{{nome}}", "descricao": "O fluxo {{nome}}",
              "desenho": { "nodes": [ {{string.Join(", ", skills.Select((s, i) => $$"""{ "id": "e{{i}}", "kind": "Skill", "skillName": "{{s}}" }"""))}} ] },
              "agentes": [], "planos": [] }
            """;

    private async Task<ItemEditavelV1> ItemAsync(string slug, string tipo, string conteudo, long? id = null)
    {
        var (conferido, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(tipo, conteudo));
        Assert.True(conferido is not null, erro);
        var salvo = await Gestao(g => g.SalvarItemAsync(slug, id, conferido, null, "admin", default));
        Assert.True(salvo.Valor is not null, salvo.Mensagem);
        return salvo.Valor;
    }

    private async Task<CatalogoV1> CatalogoAsync(string slug, string nome)
        => (await Gestao(g => g.CriarAsync(new CatalogoNovoV1(slug, nome, "do time"), default))).Valor!;

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    [Fact]
    public async Task O_basico_e_semeado_uma_vez_e_o_slug_e_unico()
    {
        await _base.InicializarAsync(); // a segunda subida não semeia de novo

        var criado = await Gestao(g => g.CriarAsync(new CatalogoNovoV1("ferramentas-uau", "Ferramentas UAU", null), default));
        var repetido = await Gestao(g => g.CriarAsync(new CatalogoNovoV1("ferramentas-uau", "Outro", null), default));
        var lista = await Gestao(g => g.ListarAsync(default));

        Assert.Equal(new[] { (Catalogos.Basico, true), ("ferramentas-uau", false) }, lista.Select(c => (c.Slug, c.Basico)));
        Assert.Equal(FalhaNaGestao.Conflito, repetido.Falha);
        Assert.Equal((0, 0), (criado.Valor!.Versao, criado.Valor.Itens));
        Assert.Equal(FalhaNaGestao.Recusada, (await Gestao(g => g.ExcluirAsync(Catalogos.Basico, default))).Falha);
    }

    [Fact]
    public async Task Rascunho_release_versao_nova_so_do_que_mudou_e_retirada()
    {
        await CatalogoAsync("ferramentas-uau", "Ferramentas UAU");
        var skill = await ItemAsync("ferramentas-uau", TiposDeItem.Skill, SkillJson("compilar-uau"));
        var fluxo = await ItemAsync("ferramentas-uau", TiposDeItem.Fluxo, FluxoJson("Entrega UAU", "compilar-uau"));
        Assert.Equal((SituacoesDoItem.Novo, "entrega-uau"), (fluxo.Situacao, fluxo.Nome));

        var primeira = await Publicacao(p => p.PublicarAsync("ferramentas-uau", null, "admin", default));
        Assert.True(primeira.Feita is not null, primeira.Mensagem + string.Join("; ", primeira.Recusa?.Problemas ?? []));
        Assert.Equal(("ferramentas-uau-v1", 2, 0), (primeira.Feita.Release.Tag, primeira.Feita.Release.Itens, primeira.Feita.Release.Retirados));
        Assert.All((await Gestao(g => g.ItensAsync("ferramentas-uau", default))).Valor!, i => Assert.Equal(SituacoesDoItem.Publicado, i.Situacao));

        // Editar a skill: só ela ganha versão nova; o fluxo continua na delta da versão 1.
        var editada = await ItemAsync("ferramentas-uau", TiposDeItem.Skill, SkillJson("compilar-uau", "passo 1 corrigido"), skill.Id);
        Assert.Equal((SituacoesDoItem.Alterado, 1), (editada.Situacao, editada.VersaoPublicada));
        var conferencia = (await Publicacao(p => p.ConferirAsync("ferramentas-uau", null, default))).Valor!;
        Assert.Equal(new[] { "~ skill:compilar-uau v2" }, conferencia.Mudancas);
        await Publicacao(p => p.PublicarAsync("ferramentas-uau", null, "admin", default));

        var acesso = new AcessoAoCatalogo(true, null);
        var v2 = (await Publicacao(p => p.UltimaAsync("ferramentas-uau", acesso, default)))!;
        Assert.Equal((2, 2), (v2.Versao, v2.Manifesto.Itens.Single(i => i.Nome == "compilar-uau").Versao));
        Assert.Equal(1, v2.Manifesto.Itens.Single(i => i.Nome == "entrega-uau").Catalogo);
        Assert.Equal(new[] { "skill:compilar-uau" }, v2.Manifesto.Itens.Single(i => i.Nome == "entrega-uau").Dependencias);

        // O zip leva os itens nos caminhos do manifesto e o próprio manifesto.
        await using (var pacote = await Publicacao(p => p.AbrirPacoteAsync(v2, default)))
        {
            using var zip = new ZipArchive(pacote!.Conteudo, ZipArchiveMode.Read);
            Assert.Equal(new[] { "fluxos/entrega-uau.json", "manifesto.json", "skills/compilar-uau.json" }, zip.Entries.Select(e => e.FullName).Order());
            using var leitor = new StreamReader(zip.GetEntry("manifesto.json")!.Open(), Encoding.UTF8);
            Assert.Equal(2, ManifestoJson.Ler(await leitor.ReadToEndAsync()).Versao);
        }

        // Retirar: pendente até publicar; publicado, vai para os retirados. O nunca publicado some.
        var retirado = (await Gestao(g => g.RetirarItemAsync("ferramentas-uau", fluxo.Id, "admin", default))).Valor!;
        Assert.Equal(SituacoesDoItem.RetiradaPendente, retirado.Situacao);
        await Publicacao(p => p.PublicarAsync("ferramentas-uau", null, "admin", default));
        var v3 = (await Publicacao(p => p.UltimaAsync("ferramentas-uau", acesso, default)))!;
        Assert.Equal(new ItemRetiradoV1(TiposDeItem.Fluxo, "entrega-uau", 1, 3), Assert.Single(v3.Manifesto.Retirados));
        var rascunho = await ItemAsync("ferramentas-uau", TiposDeItem.Skill, SkillJson("nunca-publicada"));
        var apagado = await Gestao(g => g.RetirarItemAsync("ferramentas-uau", rascunho.Id, "admin", default));
        Assert.Equal((FalhaNaGestao.Nenhuma, (ItemEditavelV1?)null), (apagado.Falha, apagado.Valor));

        Assert.Equal(new[] { 3, 2, 1 }, (await Publicacao(p => p.ReleasesAsync("ferramentas-uau", default)))!.Select(r => r.Versao));
        var semMudanca = await Publicacao(p => p.PublicarAsync("ferramentas-uau", null, "admin", default));
        Assert.Equal(FalhaNaGestao.Recusada, semMudanca.Falha);
    }

    [Fact]
    public async Task A_dependencia_sem_item_recusa_a_publicacao_com_o_relatorio()
    {
        await CatalogoAsync("ferramentas-uau", "Ferramentas UAU");
        await CatalogoAsync("outro", "Outro");
        await ItemAsync("outro", TiposDeItem.Skill, SkillJson("de-outro"));
        await ItemAsync("ferramentas-uau", TiposDeItem.Fluxo, FluxoJson("Entrega UAU", "de-outro", "sumiu"));

        var recusa = await Publicacao(p => p.PublicarAsync("ferramentas-uau", null, "admin", default));

        Assert.Null(recusa.Feita);
        Assert.Equal(FalhaNaGestao.Conflito, recusa.Falha);
        Assert.False(recusa.Recusa!.PodePublicar);
        Assert.Equal(2, recusa.Recusa.Problemas.Count);
        Assert.Contains(recusa.Recusa.Problemas, p => p.Contains("skill:de-outro, que está no catálogo 'Outro'", StringComparison.Ordinal));
        Assert.Equal(0, await Com(sp => sp.GetRequiredService<DevKitPageDb>().VersoesDoCatalogo.CountAsync()));
    }

    [Fact]
    public async Task Colisao_com_o_basico_e_entre_catalogos_da_mesma_empresa_e_recusada()
    {
        await ItemAsync(Catalogos.Basico, TiposDeItem.Skill, SkillJson("revisar-alteracoes"));
        await CatalogoAsync("a", "Catálogo A");
        await CatalogoAsync("b", "Catálogo B");
        await ItemAsync("a", TiposDeItem.Skill, SkillJson("compilar-uau"));
        await ItemAsync("b", TiposDeItem.Skill, SkillJson("compilar-uau"));
        var empresa = await EmpresaAsync("Empresa A");

        var (conferido, _) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Skill, SkillJson("revisar-alteracoes")));
        var doBasico = await Gestao(g => g.SalvarItemAsync("a", null, conferido!, null, "admin", default));
        var primeiro = await Gestao(g => g.RelacionarAsync("a", empresa.Id, default));
        var segundo = await Gestao(g => g.RelacionarAsync("b", empresa.Id, default));

        Assert.Equal(FalhaNaGestao.Conflito, doBasico.Falha);
        Assert.StartsWith("Já existe 'revisar-alteracoes' no catálogo básico", doBasico.Mensagem);
        Assert.Equal("Empresa A", Assert.Single(primeiro.Valor!.Empresas).Nome);
        Assert.Equal(FalhaNaGestao.Conflito, segundo.Falha);
        Assert.Contains("skill:compilar-uau (no catálogo 'Catálogo A')", segundo.Mensagem);
        Assert.Equal(FalhaNaGestao.Recusada, (await Gestao(g => g.RelacionarAsync(Catalogos.Basico, empresa.Id, default))).Falha);
    }

    [Fact]
    public async Task A_distribuicao_isola_as_empresas()
    {
        await CatalogoAsync("ferramentas-uau", "Ferramentas UAU");
        await ItemAsync("ferramentas-uau", TiposDeItem.Skill, SkillJson("compilar-uau"));
        await Publicacao(p => p.PublicarAsync("ferramentas-uau", 160, "admin", default));
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        await Gestao(g => g.RelacionarAsync("ferramentas-uau", a.Id, default));
        var daA = new AcessoAoCatalogo(false, new EscopoDaEmpresa(a.Id, 99, false));
        var daB = new AcessoAoCatalogo(false, new EscopoDaEmpresa(b.Id, 98, false));

        var meusDaA = await Publicacao(p => p.DisponiveisAsync(daA, default));
        var meusDaB = await Publicacao(p => p.DisponiveisAsync(daB, default));

        Assert.Equal(new[] { (Catalogos.Basico, 0), ("ferramentas-uau", 1) }, meusDaA.Select(c => (c.Slug, c.Versao)));
        Assert.Equal(new[] { Catalogos.Basico }, meusDaB.Select(c => c.Slug));
        Assert.Equal(160, (await Publicacao(p => p.UltimaAsync("ferramentas-uau", daA, default)))!.Manifesto.MinDevKit);
        Assert.Null(await Publicacao(p => p.UltimaAsync("ferramentas-uau", daB, default)));
        Assert.Null(await Publicacao(p => p.UltimaAsync("ferramentas-uau", AcessoAoCatalogo.Anonimo, default)));
        Assert.Equal(2, (await Publicacao(p => p.DisponiveisAsync(new AcessoAoCatalogo(true, null), default))).Count);
    }

    [Fact]
    public async Task Empresas_trazem_o_gestor_e_os_assentos_pelos_devs_ativos()
    {
        var empresa = await EmpresaAsync("Empresa A");
        var gestao = (IServiceProvider sp) => sp.GetRequiredService<IGestaoDeUsuarios>();
        await Com(sp => gestao(sp).CriarAsync(new UsuarioNovo("gestor.a", "Gestora A", Papeis.Gestor, empresa.Id), default));
        await Com(sp => gestao(sp).CriarAsync(new UsuarioNovo("ana.dev", "Ana", Papeis.Dev, empresa.Id), default));
        var bia = (await Com(sp => gestao(sp).CriarAsync(new UsuarioNovo("bia.dev", "Bia", Papeis.Dev, empresa.Id), default))).Valor!;
        await Com(sp => gestao(sp).BloquearAsync(bia.Usuario.Id, true, 0, default));
        await EmpresaAsync("Empresa B");

        var lista = await Com(sp => sp.GetRequiredService<IEmpresas>().ListarAsync(default));

        Assert.Equal(("Gestora A", 1), (lista[0].Gestor, lista[0].AssentosEmUso)); // a bloqueada libera o assento
        Assert.Equal(((string?)null, 0), (lista[1].Gestor, lista[1].AssentosEmUso));
    }

    [Theory]
    [InlineData("Catalogos")]
    [InlineData("VinculosDeCatalogo")]
    [InlineData("ItensEditaveisDeCatalogo")]
    [InlineData("VersoesDoCatalogo")]
    [InlineData("PacotesDoCatalogo")]
    public void Script_do_azure_sql_cria_cada_tabela_com_as_colunas_do_modelo(string tabela)
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us461.sql");

        Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
            Assert.Contains(coluna, script);
    }
}
