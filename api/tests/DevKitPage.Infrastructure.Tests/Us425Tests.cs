using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #425 na base de verdade: o catálogo da empresa (versões pelo hash, dependências, isolamento, quem retira), a
/// conta do dev (assentos por usuário, link de uso único que vence em 24 h), a licença (o trial pela impressão, a
/// janela de renovação no relógio) e o gateway (idempotência, empresa individual, cancelamento) — e o script do Azure SQL.
/// </summary>
public sealed class Us425Tests : IAsyncLifetime
{
    private static readonly byte[] Semente = Enumerable.Range(40, 32).Select(i => (byte)i).ToArray();

    private readonly BaseDeTeste _base = new(new() { ["Licencas:ChavePrivada"] = Convert.ToBase64String(Semente), ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<EmpresaResumo> EmpresaAsync(string nome, int assentos = 5)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", assentos), default));

    /// <summary>Um dev que aderiu pelo código e definiu a senha pelo link; devolve o id dele.</summary>
    private async Task<int> DevAsync(EmpresaResumo empresa, string email)
    {
        var adesao = await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, "Dev"), default));
        Assert.NotNull(adesao.Valor);
        Assert.Empty(await Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(_base.Emails.TokenPara(email), "senhaDoDev2026", default)));
        return await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
    }

    private Task<ResultadoDaGestao<PublicacaoFeitaV1>> Publicar(EscopoDaEmpresa escopo, string nome, string conteudo, string tipo = TiposDeItem.Skill, string[]? deps = null)
        => Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().PublicarAsync(escopo, new PublicacaoV1(tipo, nome, "descrição", conteudo, deps), default));

    [Fact]
    public async Task Publicar_de_novo_so_gera_versao_quando_o_hash_muda()
    {
        var empresa = await EmpresaAsync("Empresa A");
        var escopo = new EscopoDaEmpresa(empresa.Id, await DevAsync(empresa, "ana@a.com"), EhGestor: false);

        var primeira = (await Publicar(escopo, "compilar-uau", "conteudo 1")).Valor!;
        var igual = (await Publicar(escopo, "compilar-uau", "conteudo 1")).Valor!;
        var igualCrlf = (await Publicar(escopo, "compilar-uau", "conteudo 1".Replace("\n", "\r\n", StringComparison.Ordinal))).Valor!;
        var segunda = (await Publicar(escopo, "compilar-uau", "conteudo 2")).Valor!;

        Assert.Equal((1, true), (primeira.Versao, primeira.Nova));
        Assert.Equal((1, false), (igual.Versao, igual.Nova));
        Assert.False(igualCrlf.Nova);
        Assert.Equal((2, true), (segunda.Versao, segunda.Nova));
        Assert.Equal(2, await Com(sp => sp.GetRequiredService<DevKitPageDb>().VersoesDeItem.CountAsync()));
    }

    [Fact]
    public async Task Isolamento_entre_empresas_e_quem_retira()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var ana = new EscopoDaEmpresa(a.Id, await DevAsync(a, "ana@a.com"), false);
        var bia = new EscopoDaEmpresa(a.Id, await DevAsync(a, "bia@a.com"), false);
        var gestorDeA = new EscopoDaEmpresa(a.Id, bia.UsuarioId, true); // o papel de gestor vem do token
        var caio = new EscopoDaEmpresa(b.Id, await DevAsync(b, "caio@b.com"), false);
        var item = (await Publicar(ana, "compilar-uau", "x")).Valor!;
        var catalogo = (Func<ICatalogoDaEmpresa, Task<FalhaNaGestao>> acao) => Com(sp => acao(sp.GetRequiredService<ICatalogoDaEmpresa>()));

        // Outra empresa: proibido em tudo, e a lista dela não mostra o item.
        Assert.Equal(FalhaNaGestao.Proibida, await catalogo(async c => (await c.AssinarAsync(caio, item.Id, default)).Falha));
        Assert.Equal(FalhaNaGestao.Proibida, await catalogo(async c => (await c.RetirarAsync(caio, item.Id, default)).Falha));
        Assert.Empty(await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().ListarAsync(caio, default)));
        // O dev que não é o autor não retira nem publica por cima; o gestor retira.
        Assert.Equal(FalhaNaGestao.Proibida, await catalogo(async c => (await c.RetirarAsync(bia, item.Id, default)).Falha));
        Assert.Equal(FalhaNaGestao.Proibida, (await Publicar(bia, "compilar-uau", "por cima")).Falha);
        var retirado = await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().RetirarAsync(gestorDeA, item.Id, default));
        Assert.NotNull(retirado.Valor!.Retirado);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, await catalogo(async c => (await c.AssinarAsync(ana, 999, default)).Falha));
    }

    [Fact]
    public async Task Assinar_um_fluxo_traz_as_dependencias_da_empresa_e_os_assinados_levam_o_conteudo()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = new EscopoDaEmpresa(a.Id, await DevAsync(a, "ana@a.com"), false);
        await Publicar(ana, "compilar-uau", "skill uau");
        await Publicar(ana, "plano-uau", "plan uau", TiposDeItem.Plan, ["skill:compilar-uau"]);
        var fluxo = (await Publicar(ana, "entrega-uau", "fluxo uau", TiposDeItem.Fluxo, ["plan:plano-uau", "skill:revisar-alteracoes"])).Valor!;

        var assinatura = (await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().AssinarAsync(ana, fluxo.Id, default))).Valor!;
        var assinados = await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().AssinadosAsync(ana, default));

        // O plan e a skill dele (transitiva) vêm; a do básico (revisar-alteracoes) não está na empresa e fica de fora.
        Assert.Equal(new[] { "plan:plano-uau", "skill:compilar-uau" }, assinatura.Trouxe.Order());
        Assert.Equal(new[] { "entrega-uau", "plano-uau", "compilar-uau" }, assinados.Select(i => i.Nome));
        Assert.Equal("fluxo uau", assinados.Single(i => i.Nome == "entrega-uau").Conteudo);

        await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().RetirarAsync(ana, fluxo.Id, default));
        var depois = await Com(sp => sp.GetRequiredService<ICatalogoDaEmpresa>().AssinadosAsync(ana, default));
        Assert.True(depois.Single(i => i.Nome == "entrega-uau") is { Retirado: true, Conteudo: "" });
    }

    [Fact]
    public async Task Link_de_senha_vale_24_horas_uma_vez_e_o_novo_invalida_o_anterior()
    {
        var a = await EmpresaAsync("Empresa A");
        await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(a.CodigoDeAdesao, "Ana@A.com", "Ana"), default));
        var primeiro = _base.Emails.TokenPara("ana@a.com");
        Assert.Contains("https://site.test/definir-senha?token=", _base.Emails.Mensagens.Last().Texto);

        await Com(async sp => { await sp.GetRequiredService<IContas>().PedirLinkAsync("ana@a.com", default); return 0; });
        var segundo = _base.Emails.TokenPara("ana@a.com");
        var contas = (Func<string, Task<string>>)(token => Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(token, "senhaDaAna2026", default)));

        Assert.Contains("já foi usado", await contas(primeiro)); // o novo invalidou o anterior
        _base.Relogio.Agora = _base.Relogio.Agora.AddHours(25);
        Assert.Contains("expirou", await contas(segundo));

        await Com(async sp => { await sp.GetRequiredService<IContas>().PedirLinkAsync("ana@a.com", default); return 0; });
        var terceiro = _base.Emails.TokenPara("ana@a.com");
        Assert.Contains("10 caracteres", await Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(terceiro, "curta", default)));
        Assert.Empty(await contas(terceiro));
        Assert.Contains("já foi usado", await contas(terceiro));
        Assert.Contains("inválido", await contas("token-que-nao-existe"));
        Assert.Equal(ResultadoDoLogin.Ok, (await Com(sp => sp.GetRequiredService<IUsuarios>().AutenticarAsync("ana@a.com", "senhaDaAna2026", default))).Resultado);
    }

    [Fact]
    public async Task Assento_conta_por_usuario_e_bloquear_libera()
    {
        var a = await EmpresaAsync("Empresa A", assentos: 1);
        var b = await EmpresaAsync("Empresa B");
        var ana = await DevAsync(a, "ana@a.com");
        var contas = (Func<AdesaoV1, Task<ResultadoDaGestao<AdesaoFeitaV1>>>)(adesao => Com(sp => sp.GetRequiredService<IContas>().AderirAsync(adesao, default)));

        Assert.Contains("assento", (await contas(new AdesaoV1(a.CodigoDeAdesao, "bia@a.com"))).Mensagem);
        Assert.Contains("outra empresa", (await contas(new AdesaoV1(b.CodigoDeAdesao, "ana@a.com"))).Mensagem);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await contas(new AdesaoV1("DK-XXXX-XXXX", "bia@a.com"))).Falha);

        var gestor = new EscopoDaEmpresa(a.Id, 1, true);
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IGestaoDaEmpresa>().BloquearAsync(gestor, ana, true, default))).Valor);
        Assert.NotNull((await contas(new AdesaoV1(a.CodigoDeAdesao, "bia@a.com"))).Valor); // o assento da Ana ficou livre
        // Desbloquear a Ana agora passaria do teto: recusado até liberar outro.
        Assert.Equal(FalhaNaGestao.Conflito, (await Com(sp => sp.GetRequiredService<IGestaoDaEmpresa>().BloquearAsync(gestor, ana, false, default))).Falha);
        Assert.Equal(1, (await Com(sp => sp.GetRequiredService<IGestaoDaEmpresa>().EmpresaAsync(gestor, default)))!.AssentosOcupados);
    }

    [Fact]
    public async Task Dev_alem_dos_assentos_nao_entra()
    {
        var a = await EmpresaAsync("Empresa A", assentos: 1);
        await DevAsync(a, "ana@a.com");
        // O admin cria um segundo dev na empresa de um assento só: ele existe, mas não entra.
        var criado = (await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().CriarAsync(new UsuarioNovo("bia", "Bia", Papeis.Dev, a.Id), default))).Valor!;

        var (resultado, _) = await Com(sp => sp.GetRequiredService<IUsuarios>().AutenticarAsync("bia", criado.SenhaTemporaria, default));

        Assert.Equal(ResultadoDoLogin.SemAssento, resultado);
        Assert.Equal(ResultadoDoLogin.Ok, (await Com(sp => sp.GetRequiredService<IUsuarios>().AutenticarAsync("ana@a.com", "senhaDoDev2026", default))).Resultado);
    }

    [Fact]
    public async Task Trial_e_da_impressao_e_reinstalar_nao_reinicia()
    {
        const string impressao = "0123456789abcdef0123456789abcdef";
        var primeira = await _base.MaquinaAsync("maquina-1");
        var primeiro = await Com(sp => sp.GetRequiredService<ILicencas>().TrialAsync(primeira, impressao, default));
        _base.Relogio.Agora = _base.Relogio.Agora.AddDays(5);
        var reinstalada = await _base.MaquinaAsync("maquina-reinstalada");
        var segundo = await Com(sp => sp.GetRequiredService<ILicencas>().TrialAsync(reinstalada, impressao, default));

        Assert.Equal(primeiro!.ValidaAte, segundo!.ValidaAte); // os 14 dias contam do primeiro registro
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), segundo.ValidaAte);
        var lida = LicencaAssinada.Ler(segundo.Conteudo, Ed25519.ChavePublica(Semente))!;
        Assert.Equal((TiposDeLicenca.Trial, "maquina-reinstalada", (string?)null), (lida.Tipo, lida.Maquina, lida.Usuario));
        Assert.Null(await Com(sp => sp.GetRequiredService<ILicencas>().TrialAsync(reinstalada, "torta", default)));
    }

    [Fact]
    public async Task Renovacao_abre_no_setimo_dia_e_para_no_bloqueio_e_no_cancelamento()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var maquina = await _base.MaquinaAsync("maquina-1");
        var renovar = (Func<string?, Task<(LicencaV1? Licenca, RecusaDaRenovacao Recusa)>>)(atual
            => Com(sp => sp.GetRequiredService<ILicencas>().RenovarAsync(ana, maquina, atual, default)));

        var primeira = await renovar(null);
        Assert.Equal(RecusaDaRenovacao.Nenhuma, primeira.Recusa);
        Assert.Equal(_base.Relogio.Agora.AddDays(30), primeira.Licenca!.ValidaAte);
        Assert.Equal(RecusaDaRenovacao.CedoDemais, (await renovar(primeira.Licenca.Conteudo)).Recusa);

        _base.Relogio.Agora = _base.Relogio.Agora.AddDays(7);
        var segunda = await renovar(primeira.Licenca.Conteudo);
        Assert.Equal(RecusaDaRenovacao.Nenhuma, segunda.Recusa);
        Assert.Equal(2, await Com(sp => sp.GetRequiredService<DevKitPageDb>().LicencasEmitidas.CountAsync(l => l.Tipo == TiposDeLicenca.Assinatura)));

        await Com(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            (await db.Empresas.SingleAsync(e => e.Id == a.Id)).AssinaturaCanceladaEmUtc = _base.Relogio.Agora.UtcDateTime;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(RecusaDaRenovacao.AssinaturaCancelada, (await renovar(null)).Recusa);

        await Com(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            (await db.Usuarios.SingleAsync(u => u.Id == ana)).BloqueadoPeloAdmin = true;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(RecusaDaRenovacao.UsuarioBloqueado, (await renovar(null)).Recusa);
    }

    [Fact]
    public async Task Pagamento_cria_a_empresa_individual_uma_vez_e_o_cancelamento_a_marca()
    {
        var aviso = new AvisoDoGatewayV1("evt_1", TiposDeAviso.PagamentoAprovado, new DadosDoAvisoV1("Rui@Solo.com", "Rui", "sub_1", "Individual mensal"));

        Assert.True(await Com(sp => sp.GetRequiredService<IPagamentos>().ProcessarAsync(aviso, default)));
        Assert.False(await Com(sp => sp.GetRequiredService<IPagamentos>().ProcessarAsync(aviso, default))); // o reenvio não duplica

        var empresa = await Com(sp => sp.GetRequiredService<DevKitPageDb>().Empresas.AsNoTracking().SingleAsync(e => e.Individual));
        Assert.Equal(("Rui", 1, "sub_1", "Individual mensal"), (empresa.Nome, empresa.Assentos, empresa.AssinaturaExterna, empresa.Plano));
        var rui = await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.AsNoTracking().SingleAsync(u => u.Email == "rui@solo.com"));
        Assert.Equal((Papeis.Dev, (int?)empresa.Id), (rui.Papel, rui.EmpresaId));
        Assert.Single(_base.Emails.Mensagens, m => m.Para == "rui@solo.com"); // um link, uma vez

        Assert.True(await Com(sp => sp.GetRequiredService<IPagamentos>().ProcessarAsync(
            new AvisoDoGatewayV1("evt_2", TiposDeAviso.AssinaturaCancelada, new DadosDoAvisoV1(null, null, "sub_1", null)), default)));
        Assert.NotNull((await Com(sp => sp.GetRequiredService<DevKitPageDb>().Empresas.AsNoTracking().SingleAsync(e => e.Id == empresa.Id))).AssinaturaCanceladaEmUtc);
    }

    [Theory]
    [InlineData("ItensDeCatalogo")]
    [InlineData("VersoesDeItem")]
    [InlineData("AssinaturasDeCatalogo")]
    [InlineData("LinksDeSenha")]
    [InlineData("TrialsDeMaquina")]
    [InlineData("LicencasEmitidas")]
    [InlineData("EventosDePagamento")]
    public void Script_do_azure_sql_cria_cada_tabela_com_as_colunas_do_modelo(string tabela)
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us425.sql");

        Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
            Assert.Contains(coluna, script);
    }

    [Theory]
    [InlineData("Usuarios", "Email")]
    [InlineData("Maquinas", "Impressao")]
    [InlineData("Empresas", "LoginObrigatorio")]
    [InlineData("Empresas", "Individual")]
    [InlineData("Empresas", "AssinaturaExterna")]
    [InlineData("Empresas", "AssinaturaCanceladaEmUtc")]
    public void Script_do_azure_sql_acrescenta_cada_coluna_com_o_tipo_do_modelo(string tabela, string coluna)
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us425.sql");
        var doModelo = BaseDeTeste.ColunasNoSqlServer(tabela).Single(c => c.StartsWith($"[{coluna}]", StringComparison.Ordinal));

        Assert.Contains($"COL_LENGTH(N'dbo.{tabela}', N'{coluna}') IS NULL", script);
        Assert.Contains(doModelo.Replace(" NOT NULL", string.Empty, StringComparison.Ordinal).Replace(" NULL", string.Empty, StringComparison.Ordinal), script);
    }
}
