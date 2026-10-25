using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A licença de cada usuário na gestão do admin (US #687). O que fica gravado aqui é o que a renovação do dev.kit
/// (<see cref="Licencas"/>) respeita: a validade é o teto da licença emitida, a vencida e a revogada são recusadas, e a
/// alteração depois da última emissão abre a janela — "a licença renova no próximo login, sem precisar sair". Cada
/// ação leva o motivo ao histórico.
/// </summary>
public sealed class LicencasDosUsuarios(DevKitPageDb db, TimeProvider relogio) : ILicencasDosUsuarios
{
    public async Task<ResultadoDaGestao<DetalheDaLicencaV1>> DetalheAsync(int usuarioId, CancellationToken ct)
        => await db.Usuarios.AsNoTracking().Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct) is { } usuario
            ? new(await MontarAsync(usuario, ct))
            : NaoEncontrado<DetalheDaLicencaV1>();

    public async Task<ResultadoDaGestao<PreviaDeLicencaV1>> PreviaAsync(int usuarioId, DefinicaoDeLicencaV1 definicao, CancellationToken ct)
    {
        if (await db.Usuarios.AsNoTracking().Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct) is not { } usuario)
            return NaoEncontrado<PreviaDeLicencaV1>();
        if (Recusa(usuario) is { } recusa)
            return ResultadoDaGestao<PreviaDeLicencaV1>.Falhou(FalhaNaGestao.Recusada, recusa);

        var agora = relogio.GetUtcNow();
        var atual = RegrasDeLicenca.Licenca(usuario, usuario.Empresa);
        var (validaAte, semPrazo) = RegrasDeLicenca.NovaValidade(definicao, atual, agora);
        var tipo = Tipo(definicao, atual);
        int? dias = validaAte is { } fim ? RegrasDeLicenca.DiasAte(fim, agora) : null;
        var quem = usuario.Nome.Length > 0 ? usuario.Nome.Split(' ')[0] : usuario.Login;
        var mensagem = semPrazo
            ? $"Sem prazo: a licença do dev.kit de {quem} renova sem data-limite, no próximo login."
            : $"Nova validade: {validaAte!.Value.UtcDateTime:dd/MM/yyyy} ({Dias(dias!.Value)} a partir de hoje). A licença do dev.kit de {quem} renova no próximo login, sem precisar sair.";
        return new(new PreviaDeLicencaV1(validaAte, semPrazo, dias, tipo, mensagem));
    }

    public async Task<ResultadoDaGestao<DetalheDaLicencaV1>> DefinirAsync(int usuarioId, DefinicaoDeLicencaV1 definicao, string autor, CancellationToken ct)
    {
        if (await db.Usuarios.Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct) is not { } usuario)
            return NaoEncontrado<DetalheDaLicencaV1>();
        if (Recusa(usuario) is { } recusa)
            return ResultadoDaGestao<DetalheDaLicencaV1>.Falhou(FalhaNaGestao.Recusada, recusa);

        var agora = relogio.GetUtcNow();
        var atual = RegrasDeLicenca.Licenca(usuario, usuario.Empresa);
        var (validaAte, semPrazo) = RegrasDeLicenca.NovaValidade(definicao, atual, agora);
        var tipo = Tipo(definicao, atual);
        usuario.LicencaValidaAteUtc = validaAte?.UtcDateTime;
        usuario.LicencaSemPrazo = semPrazo;
        usuario.LicencaTipo = tipo;
        usuario.LicencaRevogadaEmUtc = null;
        usuario.LicencaAlteradaEmUtc = agora.UtcDateTime;
        Registrar(usuario.Id, semPrazo ? AcoesNaLicenca.SemPrazo : AcoesNaLicenca.Definida, RegrasDeLicenca.Descrever(validaAte, semPrazo, tipo),
            definicao.Motivo, validaAte, autor, agora);
        await db.SaveChangesAsync(ct);
        return new(await MontarAsync(usuario, ct));
    }

    public async Task<ResultadoDaGestao<DetalheDaLicencaV1>> RevogarAsync(int usuarioId, string motivo, string autor, CancellationToken ct)
    {
        if (await db.Usuarios.Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct) is not { } usuario)
            return NaoEncontrado<DetalheDaLicencaV1>();
        if (Recusa(usuario) is { } recusa)
            return ResultadoDaGestao<DetalheDaLicencaV1>.Falhou(FalhaNaGestao.Recusada, recusa);

        var agora = relogio.GetUtcNow();
        usuario.LicencaRevogadaEmUtc = agora.UtcDateTime;
        usuario.LicencaAlteradaEmUtc = agora.UtcDateTime;
        Registrar(usuario.Id, AcoesNaLicenca.Revogada, "licença revogada: a renovação para", motivo, null, autor, agora);
        await db.SaveChangesAsync(ct);
        return new(await MontarAsync(usuario, ct));
    }

    /// <summary>A conta de administração não tem licença a definir — nem a do próprio admin, nem a de outro.</summary>
    private static string? Recusa(Usuario usuario)
        => usuario.EhAdmin || usuario.Papel == Papeis.Admin ? "A conta de administração não tem prazo de licença para definir." : null;

    /// <summary>O tipo escolhido na definição, ou o que o usuário já tem.</summary>
    private static string Tipo(DefinicaoDeLicencaV1 definicao, LicencaDoUsuario atual)
        => definicao.Tipo is { Length: > 0 } escolhido ? escolhido : atual.Tipo;

    private void Registrar(int usuarioId, string acao, string descricao, string? motivo, DateTimeOffset? validaAte, string autor, DateTimeOffset agora)
        => db.HistoricosDeLicenca.Add(new HistoricoDeLicenca
        {
            UsuarioId = usuarioId,
            Acao = acao,
            Descricao = Cortar(descricao, 200),
            Motivo = Cortar(motivo, RegrasDeLicenca.TamanhoMaximoDoMotivo),
            ValidaAteUtc = validaAte?.UtcDateTime,
            Autor = Cortar(autor, ValidadorDeEmpresa.TamanhoMaximoDoLogin),
            EmUtc = agora.UtcDateTime,
        });

    /// <summary>
    /// A licença e o histórico: as ações do admin, as renovações do dev.kit (a trilha das licenças emitidas, que já existe)
    /// e o cadastro — o começo da licença —, do mais novo ao mais antigo.
    /// </summary>
    private async Task<DetalheDaLicencaV1> MontarAsync(Usuario usuario, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var acoes = (await db.HistoricosDeLicenca.AsNoTracking().Where(h => h.UsuarioId == usuario.Id).ToListAsync(ct))
            .Select(h => new HistoricoDeLicencaV1(DevKitPageDb.Utc(h.EmUtc), h.Acao, h.Descricao, h.Motivo, h.Autor));
        var renovacoes = (await db.LicencasEmitidas.AsNoTracking()
                .Where(l => l.UsuarioId == usuario.Id)
                .OrderByDescending(l => l.EmitidaEmUtc)
                .Take(20)
                .ToListAsync(ct))
            .Select(l => new HistoricoDeLicencaV1(DevKitPageDb.Utc(l.EmitidaEmUtc), AcoesNaLicenca.Renovada,
                $"login renovou a licença do dev.kit até {l.ValidaAteUtc:dd/MM/yyyy}", string.Empty, usuario.Login));
        var cadastro = new HistoricoDeLicencaV1(DevKitPageDb.Utc(usuario.CriadoEmUtc), AcoesNaLicenca.Criada, "usuário cadastrado: começo da licença", string.Empty, string.Empty);
        var historico = acoes.Concat(renovacoes).Append(cadastro).OrderByDescending(h => h.Em).ToArray();

        var ultimoUso = (await GestaoDeUsuarios.UltimosUsosAsync(db, usuario.Id, ct)).GetValueOrDefault(usuario.Id);
        return new DetalheDaLicencaV1(usuario.Id, usuario.Login, usuario.Nome, usuario.EhAdmin ? Papeis.Admin : usuario.Papel, usuario.Empresa?.Nome,
            GestaoDeUsuarios.Licenca(usuario, agora, ultimoUso == default ? null : ultimoUso), historico);
    }

    private static string Dias(int dias) => dias == 1 ? "1 dia" : $"{dias} dias";

    private static string Cortar(string? texto, int tamanho)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length <= tamanho ? limpo : limpo[..tamanho];
    }

    private static ResultadoDaGestao<T> NaoEncontrado<T>() where T : class
        => ResultadoDaGestao<T>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");
}
