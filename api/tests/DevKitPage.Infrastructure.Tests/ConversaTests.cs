using System.Collections.Concurrent;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>A fila falsa da conversa: guarda o que a API pôs (com o TTL) e a SAS pedida, e falha quando o teste manda.</summary>
public sealed class FilaDeTeste : IFilaDeComandos
{
    public ConcurrentQueue<(FilaDoUsuario Fila, string Mensagem, TimeSpan Ttl)> Enviadas { get; } = new();
    public List<(FilaDoUsuario Fila, DateTimeOffset Ate)> Sas { get; } = [];
    public bool Falhar { get; set; }

    public Task EnviarAsync(FilaDoUsuario fila, string mensagem, TimeSpan ttl, CancellationToken ct)
    {
        if (Falhar)
            throw new FalhaNaFila("A fila recusou o comando (503 ServerBusy).");
        Enviadas.Enqueue((fila, mensagem, ttl));
        return Task.CompletedTask;
    }

    public Task<AcessoAFila> EmitirSasAsync(FilaDoUsuario fila, DateTimeOffset ate, CancellationToken ct)
    {
        if (Falhar)
            throw new FalhaNaFila("A identidade da API não tem permissão para ler a chave da fila (listKeys).");
        Sas.Add((fila, ate));
        return Task.FromResult(new AcessoAFila($"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}", "sv=2021-12-02&sp=p&sig=teste", ate));
    }
}

/// <summary>
/// A conversa remota na base real (fase 2 do controle remoto): o script do Azure SQL e a migration; o comando gravado no SQL
/// e na fila com id, destino e TTL; o TTL que vence em <c>expirado</c> (e não executa); a REPETIÇÃO — o reenvio do site, o
/// recebimento duplicado da fila (também simultâneo) e a resposta reenviada pelo dev.kit —; o ISOLAMENTO entre usuários; o
/// LIMITE por hora; a fila fora do ar; o reinício seguro; a SAS; e a retenção.
/// </summary>
public sealed class ConversaTests
{
    private const string Maquina = "3f2b9c0e5d7a";
    private const string OutraMaquina = "9a8b7c6d5e4f";

    [Fact]
    public async Task A_task_tem_historico_proprio_e_a_fila_e_o_sse_conservam_o_canal()
    {
        var (b, fila, ana, bia) = await BaseAsync();
        await using var descarte = b;
        var canal = "task-" + new string('a', 32);
        var outro = "task-" + new string('b', 32);
        await EnviarAsync(b, ana, Mensagem("orq"));
        await FalarAsync(b, ana, "fala-orq", ConversaV1.AutorOrquestrador);
        var pedido = Mensagem("task-msg") with { Tipo = ConversaV1.ComandoMensagemTask, ConversaId = canal };
        Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, pedido)).Falha);
        var transporte = JsonSerializer.Deserialize<MensagemDaFilaV1>(fila.Enviadas.Last().Mensagem, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal((ConversaV1.ComandoMensagemTask, canal), (transporte.Tipo, transporte.ConversaId));
        await ReceberAsync(b, ana, pedido.Id!);
        await ResponderAsync(b, ana, "ack-task", pedido.Id, ConversaV1.RespostaConcluido, "Mensagem registrada.");
        await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().FalarAsync(ana,
            new FalaDoDevKitV1(RemotoV1.Contrato, Maquina, "fala-task", ConversaV1.AutorAgente, ConversaV1.OrigemDesktop,
                "Estou conferindo os testes.", b.Relogio.Agora, ConversaId: canal), default));

        var historico = (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(ana, Maquina, default, canal)))!;
        Assert.Equal(pedido.Id, Assert.Single(historico.Comandos).Id);
        Assert.Equal(canal, Assert.Single(historico.Respostas).ConversaId);
        Assert.Equal("fala-task", Assert.Single(historico.Falas).Id);
        var orquestrador = (await ConversaAsync(b, ana))!;
        Assert.Equal("orq", Assert.Single(orquestrador.Comandos).Id);
        Assert.Equal("fala-orq", Assert.Single(orquestrador.Falas).Id);
        Assert.Empty(orquestrador.Respostas);
        var vazio = (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(ana, Maquina, default, outro)))!;
        Assert.Empty(vazio.Comandos);
        Assert.Empty(vazio.Falas);
        Assert.Null(await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(bia, Maquina, default, canal)));
        Assert.Contains(await EventosAsync(b, ana), e => e.Fala?.ConversaId == canal);
    }

    [Fact]
    public async Task Reenvio_da_mensagem_nao_troca_a_task_e_abertura_pendente_nao_duplica_nem_gasta_limite()
    {
        var (b, fila, ana, _) = await BaseAsync(comandosPorHora: 1);
        await using var descarte = b;
        var canal = "task-" + new string('a', 32);
        var outro = "task-" + new string('b', 32);
        var mensagem = Mensagem("task-msg") with { Tipo = ConversaV1.ComandoMensagemTask, ConversaId = canal };
        await EnviarAsync(b, ana, mensagem);
        Assert.False((await EnviarAsync(b, ana, mensagem)).Valor!.Novo);
        Assert.Equal(FalhaNaConversa.IdEmUso, (await EnviarAsync(b, ana, mensagem with { ConversaId = outro })).Falha);
        var abertura = mensagem with { Id = "abrir", Tipo = ConversaV1.ComandoConversaTask, Texto = "" };
        Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, abertura)).Falha);
        Assert.Equal("abrir", (await EnviarAsync(b, ana, abertura with { Id = "renovar" })).Valor!.Comando.Id);
        Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, abertura with { Id = "outra", ConversaId = outro })).Falha);
        Assert.Equal(3, fila.Enviadas.Count);
        Assert.Equal(FalhaNaConversa.Limite, (await EnviarAsync(b, ana, mensagem with { Id = "nova" })).Falha);
    }

    [Fact]
    public async Task Uma_fala_nao_pode_ser_reutilizada_em_outro_canal_nem_ligada_a_comando_de_outra_task()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var descarte = b;
        var canal = "task-" + new string('a', 32);
        var outro = "task-" + new string('b', 32);
        await EnviarAsync(b, ana, Mensagem("task-msg") with { Tipo = ConversaV1.ComandoMensagemTask, ConversaId = canal });
        var fala = new FalaDoDevKitV1(RemotoV1.Contrato, Maquina, "f-task", ConversaV1.AutorAgente,
            ConversaV1.OrigemDesktop, "Conferido.", b.Relogio.Agora, ConversaId: canal);
        Task<ResultadoDaConversa<FalaRecebidaV1>> Publicar(FalaDoDevKitV1 f)
            => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().FalarAsync(ana, f, default));
        Assert.Equal(FalhaNaConversa.Nenhuma, (await Publicar(fala)).Falha);
        Assert.True((await Publicar(fala)).Valor!.Repetida);
        Assert.Equal(FalhaNaConversa.IdEmUso, (await Publicar(fala with { ConversaId = outro })).Falha);
        Assert.Equal(FalhaNaConversa.IdEmUso, (await Publicar(fala with { Id = "f-outra", ConversaId = outro, ComandoId = "task-msg" })).Falha);
    }

    private static async Task<(BaseDeTeste Base, FilaDeTeste Fila, int Ana, int Bia)> BaseAsync(int comandosPorHora = 30)
    {
        var fila = new FilaDeTeste();
        var baseDeTeste = new BaseDeTeste(new()
        {
            ["Remoto:ComandosPorHora"] = comandosPorHora.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Remoto:TtlMinimoSegundos"] = "30",
            ["Remoto:RetencaoDaConversaDias"] = "30",
        }, s => s.AddSingleton<IFilaDeComandos>(fila));
        await baseDeTeste.InicializarAsync();
        var (ana, bia) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var a = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            var b = new Usuario { Login = "bia", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            db.Usuarios.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        await HeartbeatAsync(baseDeTeste, ana, Maquina);
        await HeartbeatAsync(baseDeTeste, ana, OutraMaquina);
        await FilaProntaAsync(baseDeTeste, ana);
        return (baseDeTeste, fila, ana, bia);
    }

    private static Task HeartbeatAsync(BaseDeTeste b, int usuario, string maquina, bool turno = false)
        => b.ComAsync(sp => sp.GetRequiredService<IRemoto>().RegistrarHeartbeatAsync(usuario,
            new HeartbeatV1(RemotoV1.Contrato, maquina, "Notebook", "75", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, turno), default));

    /// <summary>A fila do usuário como o worker da provisão a deixa: pronta, com o id do Azure e o endereço.</summary>
    private static Task<long> FilaProntaAsync(BaseDeTeste b, int usuario, string estado = ProvisaoV1.EstadoPronto)
        => b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var agora = b.Relogio.Agora.UtcDateTime;
            var conta = $"dkq{usuario}abcd1234";
            var recurso = new RecursoProvisionado
            {
                UsuarioId = usuario, Tipo = ProvisaoV1.TipoFila, Nome = conta, Fila = ProvisaoV1.NomeDaFila, Regiao = "brazilsouth",
                GrupoDeRecursos = $"rg-devkit-u{usuario}", Operacao = ProvisaoV1.OperacaoCriar, Estado = estado,
                IdNoAzure = $"/subscriptions/3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f/resourceGroups/rg-devkit-u{usuario}/providers/Microsoft.Storage/storageAccounts/{conta}",
                Endpoint = $"https://{conta}.queue.core.windows.net/comandos", CriadoEmUtc = agora, AtualizadoEmUtc = agora,
                ProntoEmUtc = estado == ProvisaoV1.EstadoPronto ? agora : null,
            };
            db.RecursosProvisionados.Add(recurso);
            await db.SaveChangesAsync();
            return recurso.Id;
        });

    private static ComandoNovoV1 Mensagem(string? id = null, string texto = "Como está o projeto controle-remoto-v2?", int? ttl = null, string maquina = Maquina)
        => new(id, maquina, ConversaV1.ComandoMensagem, texto, ttl, ConversaV1.DispositivoCelular);

    private static Task<ResultadoDaConversa<ComandoAceito>> EnviarAsync(BaseDeTeste b, int usuario, ComandoNovoV1 comando)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().EnviarAsync(usuario, comando, default));

    private static Task<ResultadoDaConversa<RecebimentoAceitoV1>> ReceberAsync(BaseDeTeste b, int usuario, string comando, string maquina = Maquina)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ReceberAsync(usuario, comando, maquina, default));

    private static Task<ResultadoDaConversa<RespostaRecebidaV1>> ResponderAsync(BaseDeTeste b, int usuario, string id, string? comando,
        string tipo = ConversaV1.RespostaMensagem, string texto = "O item 5 está em implementação.", string maquina = Maquina)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ResponderAsync(usuario,
            new RespostaDoDevKitV1(RemotoV1.Contrato, maquina, id, comando, tipo, texto, b.Relogio.Agora), default));

    private static Task<ComandoRemotoV1?> ComandoAsync(BaseDeTeste b, int usuario, string id)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ComandoAsync(usuario, id, default));

    private static Task<IReadOnlyList<EventoDaConversaV1>> EventosAsync(BaseDeTeste b, int usuario, long depoisDe = 0, string? maquina = null)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().EventosAsync(usuario, maquina, depoisDe, 100, default));

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Conversa.sql");

        foreach (var tabela in new[] { "ComandosRemotos", "RespostasRemotas", "EventosDaConversa", "FalasRemotas" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
            Assert.Equal(["Conversa.sql"], EsquemaDoBanco.ScriptsQueTrazem(tabela, null));
        }
        Assert.Contains("CREATE UNIQUE INDEX [IX_ComandosRemotos_UsuarioId_ComandoId]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_RespostasRemotas_UsuarioId_MaquinaId_RespostaId]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_FalasRemotas_UsuarioId_MaquinaId_FalaId]", script);
        Assert.Contains("CREATE INDEX [IX_FalasRemotas_UsuarioId_MaquinaId_DitaEmUtc]", script);
    }

    [Fact]
    public async Task A_migracao_cria_as_tabelas_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_ConversaRemota", StringComparison.Ordinal));
        Assert.Contains(aplicadas, m => m.EndsWith("_FalasDoDesktop", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    private static Task<ResultadoDaConversa<FalaRecebidaV1>> FalarAsync(BaseDeTeste b, int usuario, string id, string autor = ConversaV1.AutorDev,
        string origem = ConversaV1.OrigemDesktop, string texto = "Como está o item 13?", DateTimeOffset? ditaEm = null, string? comando = null,
        string maquina = Maquina)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().FalarAsync(usuario,
            new FalaDoDevKitV1(RemotoV1.Contrato, maquina, id, autor, origem, texto, ditaEm ?? b.Relogio.Agora, comando), default));

    private static Task<ConversaDaMaquinaV1?> ConversaAsync(BaseDeTeste b, int usuario, string maquina = Maquina)
        => b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(usuario, maquina, default));

    [Fact]
    public async Task A_fala_guarda_o_autor_e_a_origem_vira_evento_e_volta_no_historico_na_ordem_em_que_foi_dita()
    {
        var (b, fila, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));
        var t0 = b.Relogio.Agora;

        // A fala mais antiga chega por último (o dev.kit reenviou depois de cair): no histórico ela volta para o lugar dela.
        Assert.Equal(FalhaNaConversa.Nenhuma, (await FalarAsync(b, ana, "f-2", ConversaV1.AutorOrquestrador, texto: "Está em implementação.", ditaEm: t0.AddSeconds(5))).Falha);
        Assert.Equal(FalhaNaConversa.Nenhuma, (await FalarAsync(b, ana, "f-3", origem: ConversaV1.OrigemRemoto, texto: "Como está o projeto controle-remoto-v2?", ditaEm: t0.AddSeconds(9), comando: "c-1")).Falha);
        var primeira = await FalarAsync(b, ana, "f-1", origem: "Desktop", autor: "DEV", ditaEm: t0);

        Assert.Equal(("f-1", false, t0), (primeira.Valor!.Id, primeira.Valor.Repetida, primeira.Valor.RecebidaEm));
        var conversa = (await ConversaAsync(b, ana))!;
        Assert.Equal(
            [("f-1", ConversaV1.AutorDev, ConversaV1.OrigemDesktop, (string?)null), ("f-2", ConversaV1.AutorOrquestrador, ConversaV1.OrigemDesktop, null),
             ("f-3", ConversaV1.AutorDev, ConversaV1.OrigemRemoto, "c-1")],
            conversa.Falas.Select(f => (f.Id, f.Autor, f.Origem, f.ComandoId)));
        Assert.Equal((t0.AddSeconds(5), "Está em implementação."), (conversa.Falas[1].DitaEm, conversa.Falas[1].Texto));

        // Cada fala é um evento "fala" (sem comando nem resposta), e o comando não mudou de estado por ela.
        var falas = (await EventosAsync(b, ana)).Where(e => e.Tipo == ConversaV1.EventoFala).ToList();
        Assert.Equal(["f-2", "f-3", "f-1"], falas.Select(e => e.Fala!.Id));
        Assert.All(falas, e => Assert.True(e.Comando is null && e.Resposta is null && e.MaquinaId == Maquina));
        Assert.Equal(ConversaV1.EstadoNaFila, (await ComandoAsync(b, ana, "c-1"))!.Estado);
        Assert.Empty(conversa.Respostas);
        Assert.Single(fila.Enviadas);
    }

    [Fact]
    public async Task A_fala_reenviada_nao_duplica_mesmo_ao_mesmo_tempo_e_o_id_com_outro_conteudo_e_recusado()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        var ditaEm = b.Relogio.Agora;

        var simultaneas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => FalarAsync(b, ana, "f-1", ditaEm: ditaEm)));
        b.Relogio.Agora = b.Relogio.Agora.AddMinutes(1);
        var depois = await FalarAsync(b, ana, "f-1", ditaEm: ditaEm);

        Assert.All(simultaneas, r => Assert.Equal(FalhaNaConversa.Nenhuma, r.Falha));
        Assert.Single(simultaneas, r => !r.Valor!.Repetida);
        // O reenvio devolve a data em que a API a recebeu da primeira vez.
        Assert.Equal((true, ditaEm), (depois.Valor!.Repetida, depois.Valor.RecebidaEm));
        Assert.Equal(1, await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().FalasRemotas.CountAsync()));
        Assert.Single(await EventosAsync(b, ana), e => e.Tipo == ConversaV1.EventoFala);

        Assert.Equal(FalhaNaConversa.IdEmUso, (await FalarAsync(b, ana, "f-1", ConversaV1.AutorOrquestrador, ditaEm: ditaEm)).Falha);
        Assert.Equal(FalhaNaConversa.IdEmUso, (await FalarAsync(b, ana, "f-1", texto: "Outra coisa", ditaEm: ditaEm)).Falha);
        // O mesmo id em OUTRA máquina é outra fala.
        Assert.Equal(FalhaNaConversa.Nenhuma, (await FalarAsync(b, ana, "f-1", maquina: OutraMaquina)).Falha);
        Assert.Equal(2, await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().FalasRemotas.CountAsync()));
    }

    [Fact]
    public async Task A_fala_e_so_da_maquina_do_usuario_e_a_remota_aponta_um_comando_dela()
    {
        var (b, _, ana, bia) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));

        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await FalarAsync(b, bia, "f-bia")).Falha);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await FalarAsync(b, ana, "f-1", origem: ConversaV1.OrigemRemoto, comando: "c-outro")).Falha);
        Assert.Equal(FalhaNaConversa.OutraMaquina, (await FalarAsync(b, ana, "f-1", origem: ConversaV1.OrigemRemoto, comando: "c-1", maquina: OutraMaquina)).Falha);

        await HeartbeatAsync(b, bia, Maquina);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await FalarAsync(b, bia, "f-1", origem: ConversaV1.OrigemRemoto, comando: "c-1")).Falha);
        Assert.Equal(FalhaNaConversa.Nenhuma, (await FalarAsync(b, bia, "f-1", texto: "Da bia")).Falha);
        Assert.Empty((await ConversaAsync(b, ana))!.Falas);
        Assert.Equal("Da bia", Assert.Single((await ConversaAsync(b, bia))!.Falas).Texto);
        Assert.DoesNotContain(await EventosAsync(b, ana), e => e.Tipo == ConversaV1.EventoFala);
    }

    [Fact]
    public async Task O_evento_gravado_antes_das_falas_continua_legivel()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        // O conteúdo de um evento gravado pela API do item 5, sem o campo fala.
        await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.EventosDaConversa.Add(new EventoDaConversa
            {
                UsuarioId = ana, MaquinaId = Maquina, Tipo = ConversaV1.EventoResposta, EmUtc = b.Relogio.Agora.UtcDateTime,
                Conteudo = $$"""{"id":0,"tipo":"resposta","maquinaId":"{{Maquina}}","comando":null,"resposta":{"id":"r-0","maquinaId":"{{Maquina}}","comandoId":null,"tipo":"mensagem","texto":"Antiga.","geradaEm":"2026-11-03T12:00:00+00:00","recebidaEm":"2026-11-03T12:00:00+00:00"},"em":"2026-11-03T12:00:00+00:00"}""",
            });
            return await db.SaveChangesAsync();
        });

        var evento = Assert.Single(await EventosAsync(b, ana));

        Assert.Equal(("r-0", "Antiga."), (evento.Resposta!.Id, evento.Resposta.Texto));
        Assert.Null(evento.Fala);
    }

    [Fact]
    public async Task O_comando_vai_ao_sql_e_a_fila_com_id_destino_e_ttl_e_vira_evento()
    {
        var (b, fila, ana, _) = await BaseAsync();
        await using var _ = b;

        var resultado = await EnviarAsync(b, ana, Mensagem("c-1", ttl: 120));

        Assert.Equal(FalhaNaConversa.Nenhuma, resultado.Falha);
        var comando = resultado.Valor!.Comando;
        Assert.True(resultado.Valor.Novo);
        Assert.Equal(("c-1", Maquina, ConversaV1.ComandoMensagem, ConversaV1.EstadoNaFila, ConversaV1.DispositivoCelular),
            (comando.Id, comando.MaquinaId, comando.Tipo, comando.Estado, comando.Dispositivo));
        Assert.Equal(b.Relogio.Agora.AddSeconds(120), comando.ExpiraEm);

        var (filaDoUsuario, texto, ttl) = Assert.Single(fila.Enviadas);
        Assert.Equal(($"dkq{ana}abcd1234", ProvisaoV1.NomeDaFila, ana), (filaDoUsuario.Conta, filaDoUsuario.Fila, filaDoUsuario.UsuarioId));
        Assert.Equal(TimeSpan.FromSeconds(120), ttl);
        var mensagem = JsonSerializer.Deserialize<MensagemDaFilaV1>(texto, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal((RemotoV1.Contrato, "c-1", Maquina, ConversaV1.ComandoMensagem, ConversaV1.OrigemRemoto, comando.ExpiraEm),
            (mensagem.Contrato, mensagem.Id, mensagem.MaquinaId, mensagem.Tipo, mensagem.Origem, mensagem.ExpiraEm));
        Assert.Equal("Como está o projeto controle-remoto-v2?", mensagem.Texto);

        var evento = Assert.Single(await EventosAsync(b, ana));
        Assert.Equal((ConversaV1.EventoComando, ConversaV1.EstadoNaFila, "c-1"), (evento.Tipo, evento.Comando!.Estado, evento.Comando.Id));
        Assert.True(evento.Id > 0);
    }

    [Fact]
    public async Task O_ttl_vencido_antes_da_entrega_volta_expirado_e_nao_executa()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("vence", ttl: 60));
        await EnviarAsync(b, ana, Mensagem("chega", ttl: 60));
        Assert.True((await ReceberAsync(b, ana, "chega")).Valor!.Executar);

        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(61);

        Assert.Equal(ConversaV1.EstadoExpirado, (await ComandoAsync(b, ana, "vence"))!.Estado);
        // O que já foi entregue não vence: o dev.kit o tem.
        Assert.Equal(ConversaV1.EstadoEntregue, (await ComandoAsync(b, ana, "chega"))!.Estado);
        // A fila ainda entregou depois do TTL (o relógio da máquina, um atraso): o dev.kit é avisado e não executa.
        var tardio = (await ReceberAsync(b, ana, "vence")).Valor!;
        Assert.Equal((ConversaV1.EstadoExpirado, false, false), (tardio.Estado, tardio.Executar, tardio.Repetido));
        // A resposta que chega mesmo assim fica no histórico, e o comando continua expirado.
        Assert.Equal(FalhaNaConversa.Nenhuma, (await ResponderAsync(b, ana, "r-tarde", "vence")).Falha);
        Assert.Equal(ConversaV1.EstadoExpirado, (await ComandoAsync(b, ana, "vence"))!.Estado);

        Assert.Equal("vence", Assert.Single(await EventosAsync(b, ana), e => e.Comando?.Estado == ConversaV1.EstadoExpirado).Comando!.Id);
        // A segunda passada não expira de novo nem repete o evento.
        Assert.Equal(0, await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ExpirarVencidosAsync(null, default)));
    }

    [Fact]
    public async Task O_reenvio_do_site_com_o_mesmo_id_nao_duplica_e_com_outro_conteudo_e_recusado()
    {
        var (b, fila, ana, _) = await BaseAsync();
        await using var _ = b;

        var primeiro = (await EnviarAsync(b, ana, Mensagem("c-1"))).Valor!;
        var repetido = (await EnviarAsync(b, ana, Mensagem("c-1"))).Valor!;
        var outro = await EnviarAsync(b, ana, Mensagem("c-1", texto: "Outra coisa"));

        Assert.True(primeiro.Novo);
        Assert.False(repetido.Novo);
        Assert.Equal(primeiro.Comando.Id, repetido.Comando.Id);
        Assert.Equal(FalhaNaConversa.IdEmUso, outro.Falha);
        Assert.Single(fila.Enviadas);
        Assert.Equal(1, await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ComandosRemotos.CountAsync()));
    }

    [Fact]
    public async Task A_fila_que_entrega_de_novo_so_manda_executar_uma_vez_mesmo_ao_mesmo_tempo()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));

        var recebimentos = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ReceberAsync(b, ana, "c-1")));

        Assert.Single(recebimentos, r => r.Valor!.Executar);
        Assert.All(recebimentos.Where(r => !r.Valor!.Executar), r => Assert.True(r.Valor!.Repetido));
        Assert.All(recebimentos, r => Assert.Equal(ConversaV1.EstadoEntregue, r.Valor!.Estado));
        var depois = (await ReceberAsync(b, ana, "c-1")).Valor!;
        Assert.Equal((false, true), (depois.Executar, depois.Repetido));
        Assert.Single(await EventosAsync(b, ana), e => e.Comando?.Estado == ConversaV1.EstadoEntregue);
    }

    [Fact]
    public async Task A_resposta_reenviada_pelo_dev_kit_nao_duplica_e_leva_o_comando_ao_estado_dela()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));
        await EnviarAsync(b, ana, new ComandoNovoV1("c-2", Maquina, ConversaV1.ComandoLigar));
        await ReceberAsync(b, ana, "c-1");

        var primeira = (await ResponderAsync(b, ana, "r-1", "c-1")).Valor!;
        var reenviada = (await ResponderAsync(b, ana, "r-1", "c-1")).Valor!;
        await ResponderAsync(b, ana, "r-2", "c-2", ConversaV1.RespostaConcluido, "");
        await ResponderAsync(b, ana, "r-3", null, texto: "A pergunta do item 4 está pendente.");

        Assert.False(primeira.Repetida);
        Assert.True(reenviada.Repetida);
        Assert.Equal(ConversaV1.EstadoRespondido, (await ComandoAsync(b, ana, "c-1"))!.Estado);
        var ligar = (await ComandoAsync(b, ana, "c-2"))!;
        Assert.Equal(ConversaV1.EstadoConcluido, ligar.Estado);
        Assert.NotNull(ligar.ConcluidoEm);
        Assert.NotNull(ligar.EntregueEm);

        var conversa = (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(ana, Maquina, default)))!;
        Assert.Equal(["c-1", "c-2"], conversa.Comandos.Select(c => c.Id));
        Assert.Equal(["r-1", "r-2", "r-3"], conversa.Respostas.Select(r => r.Id));
        Assert.Null(conversa.Respostas[2].ComandoId);
        Assert.Equal((await EventosAsync(b, ana)).Max(e => e.Id), conversa.UltimoEvento);
        // Os eventos depois de um cursor: só os novos, em ordem.
        var cursor = (await EventosAsync(b, ana)).First().Id;
        Assert.All(await EventosAsync(b, ana, cursor), e => Assert.True(e.Id > cursor));
    }

    [Fact]
    public async Task Outro_usuario_nao_ve_nao_recebe_nem_manda_na_maquina_dele()
    {
        var (b, fila, ana, bia) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));
        await ResponderAsync(b, ana, "r-1", "c-1");

        Assert.Null(await ComandoAsync(b, bia, "c-1"));
        Assert.Null(await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(bia, Maquina, default)));
        Assert.Empty(await EventosAsync(b, bia));
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await ReceberAsync(b, bia, "c-1")).Falha);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await EnviarAsync(b, bia, Mensagem("c-bia"))).Falha);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().SasAsync(bia, Maquina, default))).Falha);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await ResponderAsync(b, bia, "r-bia", "c-1")).Falha);

        // A bia com a MESMA id anônima de máquina é outra máquina: a dela não tem a conversa da ana, e sem fila não manda nada.
        await HeartbeatAsync(b, bia, Maquina);
        Assert.Empty((await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(bia, Maquina, default)))!.Comandos);
        Assert.Equal(FalhaNaConversa.SemFila, (await EnviarAsync(b, bia, Mensagem("c-1"))).Falha);
        Assert.Equal(FalhaNaConversa.SemFila, (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().SasAsync(bia, Maquina, default))).Falha);
        Assert.Equal(FalhaNaConversa.NaoEncontrado, (await ResponderAsync(b, bia, "r-bia", "c-1")).Falha);
        Assert.Single(fila.Enviadas);
    }

    [Fact]
    public async Task O_comando_de_uma_maquina_nao_e_recebido_nem_respondido_por_outra_do_mesmo_usuario()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("c-1"));

        Assert.Equal(FalhaNaConversa.OutraMaquina, (await ReceberAsync(b, ana, "c-1", OutraMaquina)).Falha);
        Assert.Equal(FalhaNaConversa.OutraMaquina, (await ResponderAsync(b, ana, "r-1", "c-1", maquina: OutraMaquina)).Falha);
        Assert.Equal(ConversaV1.EstadoNaFila, (await ComandoAsync(b, ana, "c-1"))!.Estado);
        Assert.True((await ReceberAsync(b, ana, "c-1")).Valor!.Executar);
    }

    [Fact]
    public async Task O_limite_por_hora_recusa_com_a_espera_nao_conta_o_reenvio_e_volta_quando_a_janela_passa()
    {
        var (b, fila, ana, _) = await BaseAsync(comandosPorHora: 3);
        await using var _ = b;
        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, Mensagem($"c-{i}"))).Falha);
            b.Relogio.Agora = b.Relogio.Agora.AddMinutes(10);
        }

        var acima = await EnviarAsync(b, ana, Mensagem("c-4"));
        // O reenvio de um que já existe não conta no limite.
        var reenvio = await EnviarAsync(b, ana, Mensagem("c-1"));

        Assert.Equal(FalhaNaConversa.Limite, acima.Falha);
        Assert.Equal(TimeSpan.FromMinutes(30), acima.TenteEm);
        Assert.Equal(FalhaNaConversa.Nenhuma, reenvio.Falha);
        Assert.Equal(3, fila.Enviadas.Count);

        b.Relogio.Agora = b.Relogio.Agora.AddMinutes(31);
        Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, Mensagem("c-4"))).Falha);
    }

    [Fact]
    public async Task A_fila_fora_do_ar_deixa_o_comando_falhou_e_o_reenvio_com_o_mesmo_id_tenta_de_novo()
    {
        var (b, fila, ana, _) = await BaseAsync();
        await using var _ = b;
        fila.Falhar = true;

        var falhou = await EnviarAsync(b, ana, Mensagem("c-1"));

        Assert.Equal(FalhaNaConversa.FilaIndisponivel, falhou.Falha);
        Assert.Equal(ConversaV1.EstadoFalhou, falhou.Valor!.Comando.Estado);
        Assert.Contains("503", falhou.Valor.Comando.Erro);

        fila.Falhar = false;
        var deNovo = await EnviarAsync(b, ana, Mensagem("c-1"));
        Assert.Equal(FalhaNaConversa.Nenhuma, deNovo.Falha);
        Assert.Equal((ConversaV1.EstadoNaFila, false, ""), (deNovo.Valor!.Comando.Estado, deNovo.Valor.Novo, deNovo.Valor.Comando.Erro));
        Assert.Single(fila.Enviadas);
    }

    [Fact]
    public async Task Reiniciar_com_turno_em_andamento_pede_confirmacao_e_sem_fila_pronta_nada_sai()
    {
        var (b, fila, ana, bia) = await BaseAsync();
        await using var _ = b;
        await HeartbeatAsync(b, ana, Maquina, turno: true);

        var semConfirmar = await EnviarAsync(b, ana, new ComandoNovoV1("c-1", Maquina, ConversaV1.ComandoReiniciar));
        var confirmado = await EnviarAsync(b, ana, new ComandoNovoV1("c-2", Maquina, ConversaV1.ComandoReiniciar, ConfirmarReinicio: true));

        Assert.Equal(FalhaNaConversa.TurnoEmAndamento, semConfirmar.Falha);
        Assert.Equal(ConversaV1.EstadoNaFila, confirmado.Valor!.Comando.Estado);
        // (item 8) A confirmação vai na mensagem: o serviço confere o turno de novo na hora de reiniciar.
        var (_, texto, _) = Assert.Single(fila.Enviadas);
        var mensagem = JsonSerializer.Deserialize<MensagemDaFilaV1>(texto, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(("c-2", ConversaV1.ComandoReiniciar, true), (mensagem.Id, mensagem.Tipo, mensagem.ConfirmarReinicio));

        // Sem turno, o reinício não pede confirmação — e a mensagem não finge que houve uma.
        await HeartbeatAsync(b, ana, Maquina, turno: false);
        Assert.Equal(FalhaNaConversa.Nenhuma, (await EnviarAsync(b, ana, new ComandoNovoV1("c-3", Maquina, ConversaV1.ComandoReiniciar))).Falha);
        var semTurno = JsonSerializer.Deserialize<MensagemDaFilaV1>(fila.Enviadas.Last().Mensagem, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(semTurno.ConfirmarReinicio);

        // A fila da bia ainda em provisão não serve.
        await HeartbeatAsync(b, bia, OutraMaquina);
        await FilaProntaAsync(b, bia, ProvisaoV1.EstadoEmProvisao);
        Assert.Equal(FalhaNaConversa.SemFila, (await EnviarAsync(b, bia, Mensagem("c-1", maquina: OutraMaquina))).Falha);
    }

    [Fact]
    public async Task A_sas_e_da_fila_do_usuario_curta_e_a_falha_dela_e_fila_indisponivel()
    {
        var (b, fila, ana, _) = await BaseAsync();
        await using var _ = b;

        var sas = (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().SasAsync(ana, Maquina, default))).Valor!;

        Assert.Equal(ConversaV1.PermissaoDaSas, sas.Permissoes);
        Assert.Equal(b.Relogio.Agora.AddMinutes(15), sas.ExpiraEm);
        Assert.Equal($"https://dkq{ana}abcd1234.queue.core.windows.net/comandos", sas.Endpoint);
        var (filaDoUsuario, ate) = Assert.Single(fila.Sas);
        Assert.Equal((ana, b.Relogio.Agora.AddMinutes(15)), (filaDoUsuario.UsuarioId, ate));

        fila.Falhar = true;
        var falha = await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().SasAsync(ana, Maquina, default));
        Assert.Equal(FalhaNaConversa.FilaIndisponivel, falha.Falha);
        Assert.Contains("listKeys", falha.Mensagem);
    }

    [Fact]
    public async Task O_expurgo_tira_a_conversa_alem_da_retencao_e_guarda_a_recente()
    {
        var (b, _, ana, _) = await BaseAsync();
        await using var _ = b;
        await EnviarAsync(b, ana, Mensagem("velho"));
        await ResponderAsync(b, ana, "r-velha", "velho");
        await FalarAsync(b, ana, "f-velha");
        b.Relogio.Agora = b.Relogio.Agora.AddDays(31);
        await HeartbeatAsync(b, ana, Maquina);
        await EnviarAsync(b, ana, Mensagem("novo"));
        await FalarAsync(b, ana, "f-nova");

        var apagados = await b.ComAsync(sp => sp.GetRequiredService<IExpurgoDaConversa>().ExpurgarAsync(default));

        Assert.True(apagados >= 4);
        var conversa = (await b.ComAsync(sp => sp.GetRequiredService<IConversaRemota>().ConversaAsync(ana, Maquina, default)))!;
        Assert.Equal(["novo"], conversa.Comandos.Select(c => c.Id));
        Assert.Empty(conversa.Respostas);
        Assert.Equal(["f-nova"], conversa.Falas.Select(f => f.Id));
        Assert.All(await EventosAsync(b, ana), e => Assert.Contains(e.Comando?.Id ?? e.Fala?.Id, new[] { "novo", "f-nova" }));
    }
}
