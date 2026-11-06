using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class PushTests
{
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static readonly ECParameters Servidor = NovaChave();
    private static readonly ECParameters Dispositivo = NovaChave();
    private static ECParameters NovaChave() { using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256); return e.ExportParameters(true); }
    private static string Publica(ECParameters p) => B64([4, .. p.Q.X!, .. p.Q.Y!]);
    private static AssinaturaDePushV1 Assinatura(string sufixo = "demo") => new($"https://fcm.googleapis.com/fcm/send/{sufixo}", new(Publica(Dispositivo), B64(new byte[16])));
    private static readonly PendenciaNoRetratoV1 Pergunta = new("p1", "pergunta", "Pergunta privada", Pergunta: "Conteúdo privado", Opcoes: ["Sim", "Não"], JaTentei: "Tentativa privada");
    private static BaseDeTeste NovaBase(TransporteGuardado? falso = null, HttpMessageHandler? http = null) => new(new()
    {
        ["Push:ChavePublica"] = Publica(Servidor), ["Push:ChavePrivada"] = B64(Servidor.D!), ["Push:Assunto"] = "https://site.test",
    }, s =>
    {
        s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        if (falso is not null) s.AddSingleton<ITransporteDePush>(falso);
        if (http is not null) s.AddHttpClient(TransporteDePush.NomeDoCliente).ConfigurePrimaryHttpMessageHandler(() => http);
    });
    private static Task<ResultadoDaDecisao<AssinaturaRegistradaV1>> Assinar(BaseDeTeste b, int usuario = 1, string sufixo = "demo")
        => b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().AssinarAsync(usuario, Assinatura(sufixo), default));
    private static Task<int> Processar(BaseDeTeste b) => b.ComAsync(s => s.GetRequiredService<IProcessamentoDePush>().ProcessarAsync(default));
    private static async Task Retrato(BaseDeTeste b, params PendenciaNoRetratoV1[] pendencias)
    {
        await b.ComAsync(s => s.GetRequiredService<IRemoto>().RegistrarHeartbeatAsync(1, new(1, "notebook", "Demo", "108", "servico", true, "ativo", false), default));
        await b.ComAsync(s => s.GetRequiredService<IRemoto>().GravarRetratoAsync(1, new(1, "notebook", b.Relogio.Agora, [], [], pendencias), default));
    }

    [Fact]
    public async Task Assinatura_idempotente_protegida_isolada_e_cancelamento_idempotente()
    {
        await using var b = NovaBase(); await b.InicializarAsync();
        var a = (await Assinar(b)).Valor!;
        Assert.Equal(a.Id, (await Assinar(b)).Valor!.Id);
        await b.ComAsync(async s =>
        {
            var db = s.GetRequiredService<DevKitPageDb>();
            db.Usuarios.Add(new Usuario { Login = "outro", Papel = "dev", CriadoEmUtc = b.Relogio.Agora.UtcDateTime }); await db.SaveChangesAsync();
            var gravada = await db.AssinaturasDoPush.SingleAsync();
            Assert.DoesNotContain(Assinatura().Endpoint, gravada.DadosProtegidos);
            Assert.DoesNotContain(Assinatura().Keys.Auth, gravada.DadosProtegidos);
            Assert.False(db.Database.HasPendingModelChanges());
            return true;
        });
        Assert.Equal(409, (await Assinar(b, 2)).Status);
        Assert.False(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().AssinadaAsync(2, a.Id, default)));
        await b.ComAsync(async s => { await s.GetRequiredService<IPushDoBoard>().CancelarAsync(2, a.Id, default); return true; });
        Assert.True(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().AssinadaAsync(1, a.Id, default)));
        for (var i = 0; i < 2; i++) await b.ComAsync(async s => { await s.GetRequiredService<IPushDoBoard>().CancelarAsync(1, a.Id, default); return true; });
        Assert.False(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().AssinadaAsync(1, a.Id, default)));
    }

    [Fact]
    public async Task Pergunta_e_aprovacao_geram_uma_entrega_por_snapshot_e_o_link_revalida_dono_e_pendencia()
    {
        var transporte = new TransporteGuardado(); await using var b = NovaBase(transporte); await b.InicializarAsync();
        await Assinar(b); await Retrato(b, Pergunta, new("aprovar", "aprovacao", "Entrega privada"));
        Assert.Equal(2, await Processar(b)); Assert.Equal(0, await Processar(b)); Assert.Equal(2, transporte.Entregas.Count);
        var id = await b.ComAsync(s => s.GetRequiredService<DevKitPageDb>().EntregasDoPush.Where(e => e.PendenciaId == "p1").Select(e => e.Id).SingleAsync());
        Assert.Equal("p1", (await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().DestinoAsync(1, id, default)))!.PendenciaId);
        Assert.Null(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().DestinoAsync(2, id, default)));
        await Retrato(b);
        Assert.Null(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().DestinoAsync(1, id, default)));
    }

    [Fact]
    public async Task Falha_transitoria_repete_com_o_mesmo_id_e_cancelamento_impede_retry()
    {
        var transporte = new TransporteGuardado { Resultado = ResultadoDoEnvioDePush.Repetir };
        await using var b = NovaBase(transporte); await b.InicializarAsync();
        var a = (await Assinar(b)).Valor!; await Retrato(b, Pergunta); Assert.Equal(0, await Processar(b));
        var primeiro = Assert.Single(transporte.Entregas);
        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(31); transporte.Resultado = ResultadoDoEnvioDePush.Aceito;
        Assert.Equal(1, await Processar(b)); Assert.Equal(primeiro, transporte.Entregas[1]);
        await Retrato(b, Pergunta with { Id = "p2" }); transporte.Resultado = ResultadoDoEnvioDePush.Repetir; await Processar(b);
        await b.ComAsync(async s => { await s.GetRequiredService<IPushDoBoard>().CancelarAsync(1, a.Id, default); return true; });
        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(31); Assert.Equal(0, await Processar(b)); Assert.Equal(3, transporte.Entregas.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pendencia_resolvida_ou_snapshot_mudado_cancela_o_aviso_antigo(bool mudar)
    {
        var transporte = new TransporteGuardado { Resultado = ResultadoDoEnvioDePush.Repetir };
        await using var b = NovaBase(transporte); await b.InicializarAsync(); await Assinar(b); await Retrato(b, Pergunta); await Processar(b);
        var id = Assert.Single(transporte.Entregas);
        if (mudar) await Retrato(b, Pergunta with { Pergunta = "Nova pergunta" });
        else
        {
            var d = (await b.ComAsync(s => s.GetRequiredService<IDecisoesRemotas>().PendentesAsync(1, "notebook", default)))!.Single();
            Assert.Null((await b.ComAsync(s => s.GetRequiredService<IDecisoesRemotas>().ResponderAsync(1, "notebook", "p1", new(d.SnapshotId, "responder", "Sim"), default))).Erro);
        }
        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(31); transporte.Resultado = ResultadoDoEnvioDePush.Aceito; await Processar(b);
        Assert.Equal("cancelada", await b.ComAsync(s => s.GetRequiredService<DevKitPageDb>().EntregasDoPush.Where(e => e.Id == id).Select(e => e.Estado).SingleAsync()));
        Assert.Null(await b.ComAsync(s => s.GetRequiredService<IPushDoBoard>().DestinoAsync(1, id, default)));
    }

    [Fact]
    public async Task Assinatura_expirada_e_descartada_sem_repetir_envio()
    {
        var transporte = new TransporteGuardado { Resultado = ResultadoDoEnvioDePush.AssinaturaInvalida };
        await using var b = NovaBase(transporte); await b.InicializarAsync(); await Assinar(b); await Retrato(b, Pergunta);
        Assert.Equal(0, await Processar(b)); Assert.Equal(0, await Processar(b)); Assert.Single(transporte.Entregas);
        Assert.Equal(0, await b.ComAsync(s => s.GetRequiredService<DevKitPageDb>().AssinaturasDoPush.CountAsync()));
    }

    [Fact]
    public async Task Reserva_impede_dois_processadores_de_enviarem_a_mesma_entrega()
    {
        var transporte = new TransporteGuardado { Espera = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var b = NovaBase(transporte); await b.InicializarAsync(); await Assinar(b); await Retrato(b, Pergunta);
        var primeiro = Processar(b); await transporte.Iniciou.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, await Processar(b)); transporte.Espera.SetResult(); Assert.Equal(1, await primeiro);
        Assert.Single(transporte.Entregas);
    }

    [Fact]
    public async Task Transporte_real_cifra_payload_minimo_assina_vapid_e_trata_410()
    {
        var handler = new PushHttp(); await using var b = NovaBase(http: handler); await b.InicializarAsync();
        var id = Guid.NewGuid().ToString("N");
        Assert.Equal(ResultadoDoEnvioDePush.Aceito, await b.ComAsync(s => s.GetRequiredService<ITransporteDePush>().EnviarAsync(Assinatura(), id, default)));
        Assert.Equal("aes128gcm", handler.Codificacao); Assert.StartsWith("vapid ", handler.Autorizacao);
        Assert.Equal("300", handler.Ttl);
        Assert.Equal(JsonSerializer.Serialize(new { entregaId = id }), Decifrar(handler.Corpo!));
        handler.Status = HttpStatusCode.Gone;
        Assert.Equal(ResultadoDoEnvioDePush.AssinaturaInvalida, await b.ComAsync(s => s.GetRequiredService<ITransporteDePush>().EnviarAsync(Assinatura(), id, default)));
        handler.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(ResultadoDoEnvioDePush.Repetir, await b.ComAsync(s => s.GetRequiredService<ITransporteDePush>().EnviarAsync(Assinatura(), id, default)));
    }

    [Fact]
    public void Script_sqlserver_contem_todas_as_colunas_e_indices()
    {
        var sql = BaseDeTeste.ScriptDoAzureSql("Push.sql");
        foreach (var tabela in new[] { "AssinaturasDoPush", "EntregasDoPush" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", sql);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela)) Assert.Contains(coluna, sql);
            Assert.Equal(["Push.sql"], EsquemaDoBanco.ScriptsQueTrazem(tabela, null));
        }
    }

    // Receptor independente (RFC 8291): comprova o conteúdo cifrado produzido pelo transporte de verdade.
    private static string Decifrar(byte[] corpo)
    {
        var salt = corpo[..16]; var publica = corpo[21..(21 + corpo[20])];
        using var cliente = ECDiffieHellman.Create(Dispositivo);
        using var servidor = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publica[1..33], Y = publica[33..65] } });
        var compartilhado = cliente.DeriveRawSecretAgreement(servidor.PublicKey);
        var authPrk = HKDF.Extract(HashAlgorithmName.SHA256, compartilhado, new byte[16]);
        byte[] info = [.. Encoding.UTF8.GetBytes("WebPush: info\0"), 4, .. Dispositivo.Q.X!, .. Dispositivo.Q.Y!, .. publica];
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, authPrk, 32, info);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.UTF8.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.UTF8.GetBytes("Content-Encoding: nonce\0"));
        var cifrado = corpo[(21 + corpo[20])..]; var claro = new byte[cifrado.Length - 16];
        using var aes = new AesGcm(cek, 16); aes.Decrypt(nonce, cifrado[..^16], cifrado[^16..], claro);
        var fim = Array.FindLastIndex(claro, b => b != 0); Assert.Equal(2, claro[fim]);
        return Encoding.UTF8.GetString(claro[..fim]);
    }

    private sealed class TransporteGuardado : ITransporteDePush
    {
        public List<string> Entregas { get; } = [];
        public ResultadoDoEnvioDePush Resultado { get; set; } = ResultadoDoEnvioDePush.Aceito;
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Espera { get; set; }
        public async Task<ResultadoDoEnvioDePush> EnviarAsync(AssinaturaDePushV1 a, string entrega, CancellationToken ct)
        { Entregas.Add(entrega); Iniciou.TrySetResult(); if (Espera is not null) await Espera.Task.WaitAsync(ct); return Resultado; }
    }
    private sealed class PushHttp : HttpMessageHandler
    {
        public byte[]? Corpo { get; private set; }
        public string? Codificacao { get; private set; }
        public string? Autorizacao { get; private set; }
        public string? Ttl { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            Corpo = await pedido.Content!.ReadAsByteArrayAsync(ct); Codificacao = pedido.Content.Headers.ContentEncoding.Single();
            Autorizacao = pedido.Headers.Authorization?.ToString(); Ttl = pedido.Headers.GetValues("TTL").Single();
            return new(Status) { Content = new StringContent("") };
        }
    }
}
