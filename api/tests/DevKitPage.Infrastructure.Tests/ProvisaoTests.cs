using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O Azure de mentira dos testes do motor: responde o roteiro, na ordem (o último se repete), guarda cada alvo que recebeu e
/// pode rodar uma ação NO MEIO da chamada — é assim que o teste põe outro trabalhador para agir enquanto este está no Azure.
/// </summary>
public sealed class NuvemDeTeste : IProvedorDeNuvem
{
    private readonly Queue<Func<AlvoNaNuvem, CancellationToken, Task<ResultadoNaNuvem>>> _roteiro = new();
    private Func<AlvoNaNuvem, CancellationToken, Task<ResultadoNaNuvem>> _ultimo = (_, _) => Task.FromResult(new ResultadoNaNuvem(SituacaoNaNuvem.Pronto));

    public List<(string Operacao, AlvoNaNuvem Alvo)> Chamadas { get; } = [];

    public NuvemDeTeste Responde(params SituacaoNaNuvem[] situacoes)
    {
        foreach (var situacao in situacoes)
            Faz((_, _) => Task.FromResult(new ResultadoNaNuvem(situacao, $"resposta {situacao}")));
        return this;
    }

    public NuvemDeTeste Faz(Func<AlvoNaNuvem, CancellationToken, Task<ResultadoNaNuvem>> passo)
    {
        _roteiro.Enqueue(passo);
        _ultimo = passo;
        return this;
    }

    public Task<ResultadoNaNuvem> GarantirFilaAsync(AlvoNaNuvem alvo, CancellationToken ct) => Proximo(ProvisaoV1.OperacaoCriar, alvo, ct);

    public Task<ResultadoNaNuvem> RemoverFilaAsync(AlvoNaNuvem alvo, CancellationToken ct) => Proximo(ProvisaoV1.OperacaoRemover, alvo, ct);

    private Task<ResultadoNaNuvem> Proximo(string operacao, AlvoNaNuvem alvo, CancellationToken ct)
    {
        Chamadas.Add((operacao, alvo));
        return (_roteiro.Count > 0 ? _roteiro.Dequeue() : _ultimo)(alvo, ct);
    }
}

/// <summary>
/// A provisão no banco (projeto controle-remoto, item 4): o script e a migration, a liberação, o pedido idempotente e o
/// concorrente, e o motor — em andamento, repetição com espera, falha definitiva, a concessão vencida retomada por outro
/// trabalhador, o desfecho do trabalhador que perdeu a vez, a remoção e o que vai para a auditoria.
/// </summary>
public sealed class ProvisaoTests : IAsyncLifetime
{
    private const string Regiao = "brazilsouth";

    private readonly BaseDeTeste _base = new(new Dictionary<string, string?>
    {
        ["Provisao:Ambiente"] = "teste",
        ["Provisao:MaximoDeTentativas"] = "3",
        ["Provisao:EsperaInicialSegundos"] = "30",
        ["Provisao:EsperaMaximaSegundos"] = "1800",
        ["Provisao:AcompanhamentoSegundos"] = "15",
        ["Provisao:ConcessaoSegundos"] = "300",
        ["Provisao:PlanosQueLiberam:0"] = "Pro",
    });

    private int _ana;
    private int _bruno;

    public async Task InitializeAsync()
    {
        await _base.InicializarAsync();
        _ana = await UsuarioAsync("ana");
        _bruno = await UsuarioAsync("bruno");
        await LiberarAsync(_ana, true);
    }

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<int> UsuarioAsync(string login, int? empresa = null) => _base.ComAsync(async sp =>
    {
        var db = sp.GetRequiredService<DevKitPageDb>();
        var usuario = new Usuario { Login = login, Papel = Papeis.Dev, EmpresaId = empresa, CriadoEmUtc = _base.Relogio.Agora.UtcDateTime };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario.Id;
    });

    private Task<ResultadoDaGestao<LiberacaoDaProvisaoV1>> LiberarAsync(int usuario, bool liberada)
        => _base.ComAsync(sp => sp.GetRequiredService<ILiberacoesDaProvisao>().DefinirAsync(usuario, liberada, "admin", default));

    private Task<T> ComProvisaoAsync<T>(Func<IProvisao, Task<T>> acao) => _base.ComAsync(sp => acao(sp.GetRequiredService<IProvisao>()));

    private Task<ResultadoDaGestao<PedidoAceito>> PedirAsync(int usuario, string regiao = Regiao, string? chave = null)
        => ComProvisaoAsync(p => p.PedirFilaAsync(usuario, new PedidoDeFilaV1(regiao, chave), default));

    private async Task<long> FilaAsync(int usuario) => (await PedirAsync(usuario)).Valor!.Recurso.Id;

    private Task<RecursoNaNuvemV1> RecursoAsync(long id, int? usuario = null)
        => ComProvisaoAsync(async p => (await p.RecursoAsync(usuario ?? _ana, id, default))!);

    /// <summary>Uma volta do motor, num escopo próprio, como o trabalhador faz.</summary>
    private Task<bool> VoltaAsync(NuvemDeTeste nuvem, string trabalhador = "t1", CancellationToken ct = default) => _base.ComAsync(sp =>
        new MotorDeProvisao(sp.GetRequiredService<DevKitPageDb>(), nuvem, sp.GetRequiredService<IOptions<OpcoesDaProvisao>>(), _base.Relogio,
            NullLogger<MotorDeProvisao>.Instance).ProcessarProximoAsync(trabalhador, ct));

    private void Avancar(int segundos) => _base.Relogio.Agora = _base.Relogio.Agora.AddSeconds(segundos);

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Provisao.sql");

        foreach (var tabela in new[] { "LiberacoesDeProvisao", "RecursosNaNuvem", "EventosDaProvisao" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
            Assert.Equal(["Provisao.sql"], EsquemaDoBanco.ScriptsQueTrazem(tabela, null));
        }

        Assert.Contains("CREATE UNIQUE INDEX [IX_RecursosNaNuvem_UsuarioId_Tipo_Geracao]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_RecursosNaNuvem_Conta]", script);
        Assert.Contains("CREATE INDEX [IX_RecursosNaNuvem_Estado_ProximaTentativaEmUtc]", script);
        Assert.Contains("CREATE INDEX [IX_EventosDaProvisao_RecursoId]", script);
    }

    [Fact]
    public async Task A_migracao_cria_as_tabelas_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        var (aplicadas, pendente) = await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_Provisao", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task Sem_liberacao_o_pedido_e_proibido_e_nada_e_gravado()
    {
        var resultado = await PedirAsync(_bruno);

        Assert.Equal(FalhaNaGestao.Proibida, resultado.Falha);
        var situacao = await ComProvisaoAsync(p => p.SituacaoAsync(_bruno, default));
        Assert.False(situacao.Liberada);
        Assert.Null(situacao.Origem);
        Assert.Empty(situacao.Recursos);
        Assert.Equal(0, await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosNaNuvem.CountAsync()));
    }

    [Fact]
    public async Task Sem_liberacao_o_corpo_invalido_tambem_e_proibido_e_com_ela_e_recusado()
    {
        Assert.Equal(FalhaNaGestao.Proibida, (await PedirAsync(_bruno, "westeurope")).Falha);
        Assert.Equal(FalhaNaGestao.Recusada, (await PedirAsync(_ana, "westeurope")).Falha);
    }

    [Fact]
    public async Task A_revogacao_guarda_quem_revogou_e_tira_a_liberacao()
    {
        await LiberarAsync(_ana, false);

        Assert.Equal(FalhaNaGestao.Proibida, (await PedirAsync(_ana)).Falha);
        var lista = await _base.ComAsync(sp => sp.GetRequiredService<ILiberacoesDaProvisao>().ListarAsync(default));
        var linha = Assert.Single(lista);
        Assert.Equal(("ana", false, "admin"), (linha.Login, linha.Liberada, linha.AlteradaPor));
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await LiberarAsync(9999, true)).Falha);
    }

    [Fact]
    public async Task O_plano_da_empresa_libera_sem_o_admin_mas_nao_com_a_assinatura_cancelada()
    {
        var empresa = await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var nova = new Empresa { Nome = "Pro SA", Plano = "Pro", Assentos = 5, CodigoDeAdesao = "DK-PRO1-0001", CriadaEmUtc = _base.Relogio.Agora.UtcDateTime };
            db.Empresas.Add(nova);
            await db.SaveChangesAsync();
            return nova.Id;
        });
        var carla = await UsuarioAsync("carla", empresa);

        var situacao = await ComProvisaoAsync(p => p.SituacaoAsync(carla, default));
        Assert.Equal((true, ProvisaoV1.OrigemPlano), (situacao.Liberada, situacao.Origem));

        await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Empresas.Where(e => e.Id == empresa)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AssinaturaCanceladaEmUtc, _base.Relogio.Agora.UtcDateTime)));
        Assert.Equal(FalhaNaGestao.Proibida, (await PedirAsync(carla)).Falha);
    }

    [Fact]
    public async Task O_pedido_grava_os_nomes_do_servidor_e_o_repetido_devolve_a_mesma_fila()
    {
        var primeiro = (await PedirAsync(_ana, " BrazilSouth ", "clique-1")).Valor!;
        var segundo = (await PedirAsync(_ana)).Valor!;
        var mesmaChave = (await PedirAsync(_ana, Regiao, "clique-1")).Valor!;

        Assert.True(primeiro.Novo);
        Assert.False(segundo.Novo);
        Assert.False(mesmaChave.Novo);
        Assert.Equal(primeiro.Recurso.Id, segundo.Recurso.Id);
        Assert.Equal(primeiro.Recurso.Id, mesmaChave.Recurso.Id);

        var fila = primeiro.Recurso;
        Assert.Equal((ProvisaoV1.TipoFila, ProvisaoV1.OperacaoCriar, ProvisaoV1.Pedido, Regiao), (fila.Tipo, fila.Operacao, fila.Estado, fila.Regiao));
        Assert.Equal($"rg-devkit-u{_ana}", fila.GrupoDeRecursos);
        Assert.Equal(RegrasDaProvisao.NomeDaConta("teste", _ana, 1), fila.Conta);
        Assert.Equal("comandos", fila.Fila);
        var lido = await RecursoAsync(fila.Id);
        var evento = Assert.Single(lido.Historico!);
        Assert.Equal((ProvisaoV1.Pedido, ProvisaoV1.AutorUsuario), (evento.Estado, evento.Autor));
    }

    [Fact]
    public async Task Dois_pedidos_ao_mesmo_tempo_criam_uma_fila_so()
    {
        var pedidos = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PedirAsync(_ana)));

        Assert.All(pedidos, p => Assert.NotNull(p.Valor));
        Assert.Single(pedidos.Select(p => p.Valor!.Recurso.Id).Distinct());
        Assert.Equal(1, await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosNaNuvem.CountAsync()));
    }

    [Fact]
    public async Task O_motor_acompanha_o_que_o_azure_ainda_faz_sem_contar_falha_e_depois_deixa_pronto()
    {
        var id = await FilaAsync(_ana);
        var nuvem = new NuvemDeTeste().Responde(SituacaoNaNuvem.EmAndamento, SituacaoNaNuvem.Pronto);

        Assert.True(await VoltaAsync(nuvem));
        var acompanhando = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Pedido, 0), (acompanhando.Estado, acompanhando.Tentativas));
        Assert.False(await VoltaAsync(nuvem)); // ainda não venceu o acompanhamento

        Avancar(15);
        Assert.True(await VoltaAsync(nuvem));
        var pronta = await RecursoAsync(id);
        Assert.Equal(ProvisaoV1.Pronto, pronta.Estado);
        Assert.NotNull(pronta.ProntoEm);
        Assert.Equal([ProvisaoV1.Pedido, ProvisaoV1.Pronto], pronta.Historico!.Select(e => e.Estado));
        Assert.Equal("trabalhador:t1", pronta.Historico![^1].Autor);

        // As duas voltas acertaram o MESMO alvo, com as etiquetas de custo.
        Assert.Equal(2, nuvem.Chamadas.Count);
        Assert.All(nuvem.Chamadas, c => Assert.Equal((pronta.GrupoDeRecursos, pronta.Conta, "comandos", Regiao), (c.Alvo.GrupoDeRecursos, c.Alvo.Conta, c.Alvo.Fila, c.Alvo.Regiao)));
        Assert.Equal(_ana.ToString(System.Globalization.CultureInfo.InvariantCulture), nuvem.Chamadas[0].Alvo.Etiquetas["devkit-usuario"]);
        Assert.False(await VoltaAsync(nuvem)); // pronto não volta à fila
    }

    [Fact]
    public async Task A_falha_transitoria_espera_cada_vez_mais_e_para_no_maximo_de_tentativas()
    {
        var id = await FilaAsync(_ana);
        var nuvem = new NuvemDeTeste().Responde(SituacaoNaNuvem.FalhaTransitoria);

        Assert.True(await VoltaAsync(nuvem));
        var primeira = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Pedido, 1), (primeira.Estado, primeira.Tentativas));
        Assert.Contains("FalhaTransitoria", primeira.UltimoErro, StringComparison.Ordinal);

        Avancar(29);
        Assert.False(await VoltaAsync(nuvem)); // 30s depois da primeira falha
        Avancar(1);
        Assert.True(await VoltaAsync(nuvem));
        Assert.Equal(2, (await RecursoAsync(id)).Tentativas);

        Avancar(59);
        Assert.False(await VoltaAsync(nuvem)); // 60s depois da segunda
        Avancar(1);
        Assert.True(await VoltaAsync(nuvem));

        var falhou = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Falhou, 3), (falhou.Estado, falhou.Tentativas));
        Assert.Contains("Falhou depois de 3 tentativas", falhou.Historico![^1].Mensagem, StringComparison.Ordinal);
        Avancar(3600);
        Assert.False(await VoltaAsync(nuvem));
        Assert.Equal(3, nuvem.Chamadas.Count);
    }

    [Fact]
    public async Task A_falha_definitiva_falha_na_hora_e_repetir_acerta_o_mesmo_recurso()
    {
        var id = await FilaAsync(_ana);
        var nuvem = new NuvemDeTeste().Responde(SituacaoNaNuvem.FalhaDefinitiva, SituacaoNaNuvem.Pronto);

        await VoltaAsync(nuvem);
        var falhou = await RecursoAsync(id);
        Assert.Equal(ProvisaoV1.Falhou, falhou.Estado);
        Assert.StartsWith("Falha definitiva, sem nova tentativa", falhou.Historico![^1].Mensagem, StringComparison.Ordinal);

        var repetido = await ComProvisaoAsync(p => p.RepetirAsync(_ana, id, default));
        Assert.Equal((ProvisaoV1.Pedido, 0, string.Empty), (repetido.Valor!.Estado, repetido.Valor.Tentativas, repetido.Valor.UltimoErro));
        // O repetir em dobro não muda nada.
        Assert.Equal(ProvisaoV1.Pedido, (await ComProvisaoAsync(p => p.RepetirAsync(_ana, id, default))).Valor!.Estado);

        await VoltaAsync(nuvem);
        var pronta = await RecursoAsync(id);
        Assert.Equal(ProvisaoV1.Pronto, pronta.Estado);
        Assert.Equal(nuvem.Chamadas[0].Alvo.Conta, nuvem.Chamadas[1].Alvo.Conta);
        Assert.Equal([ProvisaoV1.Pedido, ProvisaoV1.Falhou, ProvisaoV1.Pedido, ProvisaoV1.Pronto], pronta.Historico!.Select(e => e.Estado));
    }

    [Fact]
    public async Task Repetir_so_vale_para_o_que_falhou_e_a_criacao_exige_a_liberacao()
    {
        var id = await FilaAsync(_ana);
        await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto));
        Assert.Equal(FalhaNaGestao.Conflito, (await ComProvisaoAsync(p => p.RepetirAsync(_ana, id, default))).Falha);

        var outra = await UsuarioAsync("dora");
        await LiberarAsync(outra, true);
        var dela = await FilaAsync(outra);
        await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.FalhaDefinitiva));
        await LiberarAsync(outra, false);

        Assert.Equal(FalhaNaGestao.Proibida, (await ComProvisaoAsync(p => p.RepetirAsync(outra, dela, default))).Falha);
    }

    [Fact]
    public async Task O_recurso_de_outro_usuario_nao_e_lido_nem_movido()
    {
        var id = await FilaAsync(_ana);
        await LiberarAsync(_bruno, true);

        Assert.Null(await ComProvisaoAsync(p => p.RecursoAsync(_bruno, id, default)));
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await ComProvisaoAsync(p => p.RepetirAsync(_bruno, id, default))).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await ComProvisaoAsync(p => p.RemoverAsync(_bruno, id, default))).Falha);
        Assert.Empty((await ComProvisaoAsync(p => p.SituacaoAsync(_bruno, default))).Recursos);
    }

    [Fact]
    public async Task A_remocao_espera_a_criacao_terminar_e_depois_libera_uma_fila_nova_com_outro_nome()
    {
        var id = await FilaAsync(_ana);
        Assert.Equal(FalhaNaGestao.Conflito, (await ComProvisaoAsync(p => p.RemoverAsync(_ana, id, default))).Falha);

        var nuvem = new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto, SituacaoNaNuvem.EmAndamento, SituacaoNaNuvem.Pronto);
        await VoltaAsync(nuvem);
        var remocao = (await ComProvisaoAsync(p => p.RemoverAsync(_ana, id, default))).Valor!;
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.Pedido), (remocao.Operacao, remocao.Estado));
        Assert.Equal(remocao.Id, (await ComProvisaoAsync(p => p.RemoverAsync(_ana, id, default))).Valor!.Id); // em dobro

        // Enquanto remove, não há fila nova.
        Assert.Equal(FalhaNaGestao.Conflito, (await PedirAsync(_ana)).Falha);

        await VoltaAsync(nuvem);
        Avancar(15);
        await VoltaAsync(nuvem);
        var removida = await RecursoAsync(id);
        Assert.Equal(ProvisaoV1.Removido, removida.Estado);
        Assert.Equal([ProvisaoV1.OperacaoCriar, ProvisaoV1.OperacaoRemover, ProvisaoV1.OperacaoRemover], nuvem.Chamadas.Select(c => c.Operacao));
        Assert.Equal(ProvisaoV1.Removido, (await ComProvisaoAsync(p => p.RemoverAsync(_ana, id, default))).Valor!.Estado);

        var nova = (await PedirAsync(_ana)).Valor!;
        Assert.True(nova.Novo);
        Assert.NotEqual(removida.Conta, nova.Recurso.Conta);
        Assert.Equal(RegrasDaProvisao.NomeDaConta("teste", _ana, 2), nova.Recurso.Conta);
        Assert.Equal(2, (await ComProvisaoAsync(p => p.SituacaoAsync(_ana, default))).Recursos.Count);
    }

    [Fact]
    public async Task A_remocao_nao_exige_a_liberacao_e_a_que_falhou_se_repete()
    {
        var id = await FilaAsync(_ana);
        await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto));
        await LiberarAsync(_ana, false);

        Assert.NotNull((await ComProvisaoAsync(p => p.RemoverAsync(_ana, id, default))).Valor);
        await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.FalhaDefinitiva));
        var falhou = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.Falhou), (falhou.Operacao, falhou.Estado));
        Assert.Equal(FalhaNaGestao.Proibida, (await PedirAsync(_ana)).Falha);

        var repetida = await ComProvisaoAsync(p => p.RepetirAsync(_ana, id, default));
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.Pedido), (repetida.Valor!.Operacao, repetida.Valor.Estado));
        await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto));
        Assert.Equal(ProvisaoV1.Removido, (await RecursoAsync(id)).Estado);
    }

    [Fact]
    public async Task Outro_trabalhador_nao_pega_o_pedido_que_ja_tem_dono()
    {
        await FilaAsync(_ana);
        bool? outroPegou = null;
        var nuvem = new NuvemDeTeste().Faz(async (_, _) =>
        {
            outroPegou = await VoltaAsync(new NuvemDeTeste(), "t2");
            return new ResultadoNaNuvem(SituacaoNaNuvem.Pronto);
        });

        Assert.True(await VoltaAsync(nuvem, "t1"));

        Assert.False(outroPegou);
        Assert.Single(nuvem.Chamadas);
    }

    [Fact]
    public async Task A_concessao_vencida_e_retomada_contando_uma_falha_e_o_desfecho_de_quem_perdeu_a_vez_nao_vale()
    {
        var id = await FilaAsync(_ana);
        var retomada = new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto);
        var travada = new NuvemDeTeste().Faz(async (_, _) =>
        {
            // O t1 "travou" no Azure além da concessão: o t2 retoma e termina.
            Avancar(301);
            Assert.True(await VoltaAsync(retomada, "t2"));
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva, "desfecho atrasado do t1");
        });

        Assert.True(await VoltaAsync(travada, "t1"));

        var recurso = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Pronto, 0), (recurso.Estado, recurso.Tentativas));
        Assert.Contains(recurso.Historico!, e => e.Autor == "trabalhador:t2" && e.Mensagem.Contains("concessão do trabalhador t1 venceu", StringComparison.Ordinal));
        Assert.DoesNotContain(recurso.Historico!, e => e.Mensagem.Contains("desfecho atrasado", StringComparison.Ordinal));
        Assert.Equal(recurso.Conta, retomada.Chamadas.Single().Alvo.Conta);
    }

    [Fact]
    public async Task O_trabalhador_que_cai_sempre_no_meio_leva_o_pedido_a_falhou()
    {
        var id = await FilaAsync(_ana);
        // Três vezes a concessão vence sem desfecho (o processo morreu). O Azure só responde "em andamento", que não conta:
        // as falhas são as quedas, e na terceira retomada o pedido falha sem ir ao Azure.
        for (var i = 0; i < 3; i++)
        {
            await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosNaNuvem.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, ProvisaoV1.EmProvisao).SetProperty(r => r.Trabalhador, "morto")
                    .SetProperty(r => r.ConcessaoAteUtc, _base.Relogio.Agora.UtcDateTime.AddSeconds(-1))));
            var nuvem = new NuvemDeTeste().Responde(SituacaoNaNuvem.EmAndamento);
            Assert.True(await VoltaAsync(nuvem));
            Assert.Equal(i < 2 ? 1 : 0, nuvem.Chamadas.Count);
        }

        var recurso = await RecursoAsync(id);
        Assert.Equal(ProvisaoV1.Falhou, recurso.Estado);
        Assert.Contains("venceu no meio da operação", recurso.UltimoErro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_excecao_do_provedor_e_falha_transitoria_sem_a_mensagem_dela()
    {
        var id = await FilaAsync(_ana);
        var nuvem = new NuvemDeTeste().Faz((_, _) => throw new InvalidOperationException("DefaultEndpointsProtocol=https;AccountName=x;AccountKey=c2VncmVkbw=="));

        Assert.True(await VoltaAsync(nuvem));

        var recurso = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Pedido, 1), (recurso.Estado, recurso.Tentativas));
        Assert.Contains("InvalidOperationException", recurso.UltimoErro, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountKey", recurso.UltimoErro, StringComparison.Ordinal);
        Assert.All(recurso.Historico!, e => Assert.DoesNotContain("AccountKey", e.Mensagem, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_mensagem_do_azure_vai_para_a_auditoria_sem_o_id_da_assinatura()
    {
        var id = await FilaAsync(_ana);
        await VoltaAsync(new NuvemDeTeste().Faz((_, _) => Task.FromResult(new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva,
            "AuthorizationFailed em /subscriptions/0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0/resourcegroups/rg"))));

        var recurso = await RecursoAsync(id);
        Assert.Contains("AuthorizationFailed", recurso.UltimoErro, StringComparison.Ordinal);
        Assert.DoesNotContain("0b1c2d3e", recurso.UltimoErro, StringComparison.Ordinal);
        Assert.DoesNotContain("0b1c2d3e", recurso.Historico![^1].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_trabalhador_que_para_devolve_o_pedido_a_fila()
    {
        var id = await FilaAsync(_ana);
        using var parada = new CancellationTokenSource();
        var nuvem = new NuvemDeTeste().Faz((_, ct) =>
        {
            parada.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ResultadoNaNuvem(SituacaoNaNuvem.Pronto));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VoltaAsync(nuvem, ct: parada.Token));

        var recurso = await RecursoAsync(id);
        Assert.Equal((ProvisaoV1.Pedido, 0), (recurso.Estado, recurso.Tentativas));
        Assert.True(await VoltaAsync(new NuvemDeTeste().Responde(SituacaoNaNuvem.Pronto), "t2"));
    }
}
