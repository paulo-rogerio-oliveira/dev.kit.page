using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As licenças assinadas (US #425, fase 4). A chave privada Ed25519 vem do Key Vault (<see cref="OpcoesDeLicenca.ChavePrivada"/>)
/// e só existe aqui; cada emissão fica em <c>LicencasEmitidas</c>. O trial é da IMPRESSÃO da máquina, e não do id
/// anônimo: reinstalar o dev.kit (que gera um id novo) não ganha outros 14 dias.
/// </summary>
public sealed class Licencas(DevKitPageDb db, IOptions<OpcoesDeLicenca> opcoes, TimeProvider relogio, ILogger<Licencas> log) : ILicencas
{
    public bool Disponivel => Semente() is not null;

    public async Task<LicencaV1?> TrialAsync(int maquinaId, string? impressao, CancellationToken ct)
    {
        if (RegrasDeLicenca.Impressao(impressao) is not { } chave)
            return null;
        var maquina = await db.Maquinas.FirstAsync(m => m.Id == maquinaId, ct);
        maquina.Impressao = chave;

        // O PRIMEIRO registro da impressão vale para sempre: é ele que reinstalar não reinicia.
        var trial = await db.TrialsDeMaquina.FirstOrDefaultAsync(t => t.Impressao == chave, ct);
        if (trial is null)
        {
            trial = new TrialDeMaquina { Impressao = chave, IniciadoEmUtc = relogio.GetUtcNow().UtcDateTime };
            db.TrialsDeMaquina.Add(trial);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois registros da mesma impressão ao mesmo tempo: o índice único guarda o primeiro, e ele vale.
            db.Entry(trial).State = EntityState.Detached;
            trial = await db.TrialsDeMaquina.AsNoTracking().FirstAsync(t => t.Impressao == chave, ct);
        }

        var inicio = DevKitPageDb.Utc(trial.IniciadoEmUtc);
        return await EmitirAsync(maquina, null, TiposDeLicenca.Trial, inicio.AddDays(Math.Max(1, opcoes.Value.TrialDias)), ct);
    }

    public async Task<(LicencaV1? Licenca, RecusaDaRenovacao Recusa)> RenovarAsync(int usuarioId, int maquinaId, string? licencaAtual, CancellationToken ct)
    {
        var usuario = await db.Usuarios.Include(u => u.Empresa).FirstAsync(u => u.Id == usuarioId, ct);
        var maquina = await db.Maquinas.FirstAsync(m => m.Id == maquinaId, ct);
        var agora = relogio.GetUtcNow();

        // A licença apresentada só conta se for nossa e DESTA máquina: a de outra não abre a janela de ninguém.
        var atual = Semente() is { } semente ? LicencaAssinada.Ler(licencaAtual, Ed25519.ChavePublica(semente)) : null;
        if (atual is not null && !string.Equals(atual.Maquina, maquina.MaquinaId, StringComparison.Ordinal))
            atual = null;

        var recusa = RegrasDeLicenca.Avaliar(usuario, usuario.Empresa, atual, agora, opcoes.Value.RenovaAPartirDoDia);
        if (recusa == RecusaDaRenovacao.Nenhuma && !await Assentos.DentroAsync(db, usuario, ct))
            recusa = RecusaDaRenovacao.SemAssento;
        if (recusa != RecusaDaRenovacao.Nenhuma)
            return (null, recusa);

        // A validade do admin (US #687) é o TETO da licença emitida: os 30 dias de sempre, sem passar dela.
        var validaAte = RegrasDeLicenca.ValidadeEmitida(RegrasDeLicenca.Licenca(usuario, usuario.Empresa), agora, opcoes.Value.ValidadeDias);
        var licenca = await EmitirAsync(maquina, usuario, TiposDeLicenca.Assinatura, validaAte, ct);
        return (licenca, RecusaDaRenovacao.Nenhuma);
    }

    private async Task<LicencaV1?> EmitirAsync(Maquina maquina, Usuario? usuario, string tipo, DateTimeOffset validaAte, CancellationToken ct)
    {
        if (Semente() is not { } semente)
        {
            await db.SaveChangesAsync(ct);
            return null;
        }

        var agora = relogio.GetUtcNow();
        var conteudo = new ConteudoDaLicenca(LicencaAssinada.VersaoDoFormato, tipo, maquina.MaquinaId, usuario?.Login, agora, validaAte);
        db.LicencasEmitidas.Add(new LicencaEmitida
        {
            MaquinaId = maquina.Id, UsuarioId = usuario?.Id, Tipo = tipo, EmitidaEmUtc = agora.UtcDateTime, ValidaAteUtc = validaAte.UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return new LicencaV1(LicencaAssinada.Emitir(conteudo, semente), tipo, agora, validaAte);
    }

    /// <summary>A semente de 32 bytes da configuração; nula (com o log) quando falta ou é inválida.</summary>
    private byte[]? Semente()
    {
        var texto = opcoes.Value.ChavePrivada;
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(texto.Trim());
            if (bytes.Length == Ed25519.TamanhoDaChave)
                return bytes;
        }
        catch (FormatException)
        {
        }

        log.LogError("Licencas:ChavePrivada não é uma semente Ed25519 de {Bytes} bytes em base64.", Ed25519.TamanhoDaChave);
        return null;
    }
}

/// <summary>
/// A compra individual pelo gateway (US #425, fase 5). O aviso chega JÁ verificado pela API; aqui ficam a idempotência
/// (o id do evento, gravado na MESMA transação do efeito) e o efeito: o pagamento cria a empresa de um assento e o
/// usuário; o cancelamento marca a empresa — a renovação para.
/// </summary>
public sealed class Pagamentos(
    DevKitPageDb db, IContas contas, Microsoft.AspNetCore.Identity.IPasswordHasher<Usuario> hasher, IOptions<OpcoesDePagamento> opcoes, TimeProvider relogio,
    ILogger<Pagamentos> log)
    : IPagamentos
{
    public async Task<bool> ProcessarAsync(AvisoDoGatewayV1 aviso, CancellationToken ct)
    {
        var eventoId = aviso.Id.Trim();
        if (await db.EventosDePagamento.AnyAsync(e => e.EventoId == eventoId, ct))
            return false;

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        db.EventosDePagamento.Add(new EventoDePagamento { EventoId = eventoId, Tipo = Cortar(aviso.Tipo, 50), RecebidoEmUtc = agora });
        Usuario? novo = null;
        switch (aviso.Tipo)
        {
            case TiposDeAviso.PagamentoAprovado:
                novo = await AprovarAsync(aviso.Dados, agora, ct);
                break;
            case TiposDeAviso.AssinaturaCancelada:
                await CancelarAsync(aviso.Dados, agora, ct);
                break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // O mesmo aviso em dois pedidos ao mesmo tempo: o índice único do evento decide, e o segundo não faz nada.
            await transacao.RollbackAsync(ct);
            return false;
        }

        // O e-mail sai DEPOIS do commit: um aviso que falhou não manda link para uma conta que não existe.
        if (novo is not null)
            await contas.EnviarLinkAsync(novo, "Bem-vindo ao dev.kit: defina a sua senha", ct);
        return true;
    }

    public CheckoutCriadoV1 Checkout(CheckoutV1 pedido)
    {
        var url = opcoes.Value.UrlDoCheckout;
        if (string.IsNullOrWhiteSpace(url))
            return new CheckoutCriadoV1($"/compra-concluida?simulado=1&plano={Uri.EscapeDataString(pedido.Plano)}", Simulado: true);
        return new CheckoutCriadoV1(
            url.Replace("{email}", Uri.EscapeDataString(RegrasDaConta.Email(pedido.Email)), StringComparison.Ordinal)
               .Replace("{plano}", Uri.EscapeDataString(pedido.Plano), StringComparison.Ordinal),
            Simulado: false);
    }

    /// <summary>
    /// O pagamento aprovado: a empresa INDIVIDUAL (um assento) e o dev dela. O comprador que já tem conta numa empresa
    /// individual só a reativa; o e-mail de uma empresa do plano empresarial não ganha outra (registrado no log).
    /// </summary>
    private async Task<Usuario?> AprovarAsync(DadosDoAvisoV1? dados, DateTime agora, CancellationToken ct)
    {
        var email = RegrasDaConta.Email(dados?.Email);
        if (!ValidadorDeDemonstracao.EmailValido(email))
        {
            log.LogWarning("Aviso de pagamento sem um e-mail válido: ignorado.");
            return null;
        }

        var existente = await db.Usuarios.Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Email == email || u.Login == email, ct);
        if (existente is not null)
        {
            if (existente.Empresa is { Individual: true } propria)
            {
                propria.AssinaturaCanceladaEmUtc = null;
                propria.AssinaturaExterna = Cortar(dados?.Assinatura, 100);
            }
            else
            {
                log.LogWarning("Pagamento de um e-mail que já é de uma empresa do plano empresarial: nenhuma conta nova.");
            }

            return null;
        }

        var codigo = CodigoDeAdesao.Gerar();
        while (await db.Empresas.AnyAsync(e => e.CodigoDeAdesao == codigo, ct))
            codigo = CodigoDeAdesao.Gerar();
        var nome = (dados?.Nome ?? string.Empty).Trim();
        var empresa = new Empresa
        {
            Nome = Cortar(nome.Length > 0 ? nome : email, ValidadorDeEmpresa.TamanhoMaximoDoNome),
            Plano = Cortar(string.IsNullOrWhiteSpace(dados?.Plano) ? "Individual" : dados.Plano, ValidadorDeEmpresa.TamanhoMaximoDoPlano),
            Assentos = 1,
            CodigoDeAdesao = codigo,
            CriadaEmUtc = agora,
            Individual = true,
            AssinaturaExterna = Cortar(dados?.Assinatura, 100),
        };
        var usuario = new Usuario
        {
            Login = email, Email = email, Nome = Cortar(nome, ValidadorDeUsuario.TamanhoMaximoDoNome), Papel = Papeis.Dev, Empresa = empresa, CriadoEmUtc = agora,
        };
        // Uma senha que ninguém conhece: a conta abre pelo link do e-mail.
        usuario.SenhaHash = hasher.HashPassword(usuario, GeradorDeSenha.Temporaria() + Guid.NewGuid().ToString("N"));
        db.Empresas.Add(empresa);
        db.Usuarios.Add(usuario);
        return usuario;
    }

    private async Task CancelarAsync(DadosDoAvisoV1? dados, DateTime agora, CancellationToken ct)
    {
        var assinatura = (dados?.Assinatura ?? string.Empty).Trim();
        if (assinatura.Length == 0)
            return;
        foreach (var empresa in await db.Empresas.Where(e => e.AssinaturaExterna == assinatura).ToListAsync(ct))
            empresa.AssinaturaCanceladaEmUtc ??= agora;
    }

    private static string Cortar(string? texto, int tamanho)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length <= tamanho ? limpo : limpo[..tamanho];
    }
}
