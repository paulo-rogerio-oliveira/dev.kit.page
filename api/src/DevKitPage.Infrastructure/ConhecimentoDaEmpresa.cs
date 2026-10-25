using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O catálogo de repositórios e as convenções da empresa (US #491). O isolamento é o <see cref="EscopoDaEmpresa"/>:
/// toda consulta e toda gravação filtram pela empresa dele — o que é de outra empresa nem aparece, e gravar o mesmo
/// caminho ou a mesma convenção noutra empresa é outra linha. A convenção de nível usuário é do próprio usuário: só o
/// gestor a vê de todos, na auditoria.
/// </summary>
public sealed class ConhecimentoDaEmpresa(DevKitPageDb db, TimeProvider relogio) : IConhecimentoDaEmpresa
{
    public async Task<IReadOnlyList<RepositorioDaEmpresaV1>> RepositoriosAsync(EscopoDaEmpresa escopo, string? filtro, CancellationToken ct)
    {
        var repositorios = await db.RepositoriosDaEmpresa.AsNoTracking().Include(r => r.Autor)
            .Where(r => r.EmpresaId == escopo.EmpresaId)
            .OrderBy(r => r.Chave)
            .ToListAsync(ct);

        // O filtro é em memória, sem distinção de maiúsculas: o catálogo de uma empresa é pequeno, e o LIKE do SQLite só
        // ignora a caixa no ASCII — "Órgão" e "órgão" seriam diferentes no banco.
        var trecho = filtro?.Trim() ?? string.Empty;
        return repositorios
            .Where(r => trecho.Length == 0
                || r.Origem.Contains(trecho, StringComparison.OrdinalIgnoreCase)
                || r.Descricao.Contains(trecho, StringComparison.OrdinalIgnoreCase))
            .Select(Resumo)
            .ToArray();
    }

    public async Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> GravarRepositorioAsync(
        EscopoDaEmpresa escopo, GravacaoDeRepositorioV1 gravacao, CancellationToken ct)
    {
        var origem = gravacao.Origem!.Trim();
        var chave = RegrasDoConhecimentoDaEmpresa.ChaveDaOrigem(origem);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var repositorio = await db.RepositoriosDaEmpresa.FirstOrDefaultAsync(r => r.EmpresaId == escopo.EmpresaId && r.Chave == chave, ct);
        if (repositorio is null)
        {
            repositorio = new RepositorioDaEmpresa { EmpresaId = escopo.EmpresaId, Chave = chave, CriadoEmUtc = agora };
            db.RepositoriosDaEmpresa.Add(repositorio);
        }

        repositorio.Origem = origem;
        repositorio.Descricao = gravacao.Descricao!.Trim();
        repositorio.AutorId = escopo.UsuarioId;
        repositorio.AtualizadoEmUtc = agora;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois dev.kit gravando o mesmo caminho ao mesmo tempo: o índice único decide, e quem perdeu grava de novo.
            return ResultadoDaGestao<RepositorioDaEmpresaV1>.Falhou(FalhaNaGestao.Conflito, $"'{origem}' acabou de ser gravado por outra pessoa. Grave de novo.");
        }

        await db.Entry(repositorio).Reference(r => r.Autor).LoadAsync(ct);
        return new(Resumo(repositorio));
    }

    public async Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> RemoverRepositorioAsync(EscopoDaEmpresa escopo, string origem, CancellationToken ct)
    {
        var chave = RegrasDoConhecimentoDaEmpresa.ChaveDaOrigem(origem);
        var repositorio = await db.RepositoriosDaEmpresa.Include(r => r.Autor)
            .FirstOrDefaultAsync(r => r.EmpresaId == escopo.EmpresaId && r.Chave == chave, ct);
        if (repositorio is null)
            return ResultadoDaGestao<RepositorioDaEmpresaV1>.Falhou(FalhaNaGestao.NaoEncontrado, $"'{origem}' não está no catálogo de repositórios da empresa.");
        if (repositorio.AutorId != escopo.UsuarioId && !escopo.EhGestor)
            return ResultadoDaGestao<RepositorioDaEmpresaV1>.Falhou(FalhaNaGestao.Proibida, "Só o gestor, ou quem gravou a descrição atual, remove o repositório do catálogo.");

        db.RepositoriosDaEmpresa.Remove(repositorio);
        await db.SaveChangesAsync(ct);
        return new(Resumo(repositorio));
    }

    public async Task<IReadOnlyList<ConvencaoV1>> ConvencoesAsync(EscopoDaEmpresa escopo, string? time, bool comSugestoes, CancellationToken ct)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        var convencoes = await Da(escopo)
            .Where(c => comSugestoes || c.Origem == OrigensDeConvencao.Usuario)
            .Where(c => c.Nivel == NiveisDeConvencao.Empresa
                || (doTime.Length > 0 && c.Nivel == NiveisDeConvencao.Time && c.Time == doTime)
                || (c.Nivel == NiveisDeConvencao.Usuario && c.UsuarioId == escopo.UsuarioId))
            .ToListAsync(ct);
        return await ParaLeituraAsync(convencoes, ct);
    }

    public async Task<IReadOnlyList<ConvencaoV1>> AuditoriaDeConvencoesAsync(EscopoDaEmpresa escopo, CancellationToken ct)
    {
        var convencoes = await Da(escopo)
            .Where(c => escopo.EhGestor || c.Nivel != NiveisDeConvencao.Usuario || c.UsuarioId == escopo.UsuarioId)
            .ToListAsync(ct);
        return await ParaLeituraAsync(convencoes, ct);
    }

    public async Task<ResultadoDaGestao<ConvencaoV1>> GravarConvencaoAsync(EscopoDaEmpresa escopo, ConvencaoV1 convencao, CancellationToken ct)
    {
        var nivel = convencao.Nivel!.Trim().ToLowerInvariant();
        var chave = convencao.Chave!.Trim().ToLowerInvariant();
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(convencao.Time) : string.Empty;
        var valor = convencao.Valor?.Trim() ?? string.Empty;

        // O dono da de nível usuário é quem grava (o escopo decide); só o admin geral escolhe outro, pelo login, e só
        // entre os usuários da empresa do caminho.
        var usuario = 0;
        if (nivel == NiveisDeConvencao.Usuario)
        {
            usuario = escopo.UsuarioId;
            var login = convencao.Usuario?.Trim() ?? string.Empty;
            if (escopo.EhAdmin && login.Length > 0)
            {
                var alvo = await db.Usuarios.AsNoTracking()
                    .Where(u => u.EmpresaId == escopo.EmpresaId && u.Login == login)
                    .Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                if (alvo is null)
                    return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.NaoEncontrado, $"'{login}' não é usuário desta empresa.");
                usuario = alvo.Value;
            }
        }

        // A regra de quem grava é uma só (RegrasDoConhecimentoDaEmpresa.PodeGravar): a da empresa e o contexto da equipe
        // são do gestor; o branch de um time, de qualquer um; a do usuário, do dono — e o admin geral, tudo.
        if (!RegrasDoConhecimentoDaEmpresa.PodeGravar(escopo, nivel, chave, usuario))
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Proibida, nivel == NiveisDeConvencao.Empresa
                ? "Só o gestor grava as convenções da empresa; a de um time, qualquer um da empresa grava."
                : "Só o gestor grava o contexto da empresa e o das equipes.");

        // A gravação daqui é sempre a REGRA (US #663): a sugestão do mesmo tema é outra linha, e fica.
        var existente = await db.ConvencoesDaEmpresa.FirstOrDefaultAsync(
            c => c.EmpresaId == escopo.EmpresaId && c.Nivel == nivel && c.Time == time && c.UsuarioId == usuario && c.Chave == chave
                && c.Origem == OrigensDeConvencao.Usuario, ct);

        if (valor.Length == 0)
        {
            // Vazio APAGA a convenção do nível: volta a valer a de baixo.
            if (existente is not null)
            {
                db.ConvencoesDaEmpresa.Remove(existente);
                await db.SaveChangesAsync(ct);
            }

            return new(new ConvencaoV1(nivel, chave, string.Empty, time));
        }

        if (existente is null)
        {
            existente = new ConvencaoDaEmpresa { EmpresaId = escopo.EmpresaId, Nivel = nivel, Time = time, UsuarioId = usuario, Chave = chave, Versao = 0 };
            db.ConvencoesDaEmpresa.Add(existente);
        }

        existente.Valor = valor;
        existente.Versao++;
        existente.AutorId = escopo.UsuarioId;
        existente.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Conflito, $"A convenção '{chave}' acabou de ser gravada por outra pessoa. Grave de novo.");
        }

        await db.Entry(existente).Reference(c => c.Autor).LoadAsync(ct);
        return new((await ParaLeituraAsync(new[] { existente }, ct))[0]);
    }

    public async Task<ResultadoDaGestao<ConvencaoV1>> SugerirAsync(EscopoDaEmpresa escopo, SugestaoDeContextoV1 sugestao, CancellationToken ct)
    {
        var nivel = sugestao.Camada!.Trim().ToLowerInvariant();
        var chave = RegrasDoConhecimentoDaEmpresa.ChaveDoTema(sugestao.Tema);
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(sugestao.Time) : string.Empty;
        var texto = sugestao.Texto!.Trim();
        var usuario = nivel == NiveisDeConvencao.Usuario ? escopo.UsuarioId : 0;

        if (!RegrasDoConhecimentoDaEmpresa.PodeSugerir(escopo, nivel, usuario))
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Proibida, "A sugestão no nível usuário é só no do próprio usuário.");

        // A linha da SUGESTÃO, ao lado da regra do mesmo tema (o índice único inclui a origem): a regra nunca é tocada.
        var existente = await db.ConvencoesDaEmpresa.Include(c => c.Autor).FirstOrDefaultAsync(
            c => c.EmpresaId == escopo.EmpresaId && c.Nivel == nivel && c.Time == time && c.UsuarioId == usuario && c.Chave == chave
                && c.Origem == OrigensDeConvencao.Agente, ct);

        // Idempotente: o reenvio da fila offline do dev.kit (a mesma sugestão do mesmo autor) não sobe a versão.
        if (existente is not null && existente.AutorId == escopo.UsuarioId && existente.Valor == texto)
            return new((await ParaLeituraAsync(new[] { existente }, ct))[0]);

        if (existente is null)
        {
            existente = new ConvencaoDaEmpresa
            {
                EmpresaId = escopo.EmpresaId, Nivel = nivel, Time = time, UsuarioId = usuario, Chave = chave,
                Origem = OrigensDeConvencao.Agente, Versao = 0,
            };
            db.ConvencoesDaEmpresa.Add(existente);
        }

        existente.Valor = texto;
        existente.Versao++;
        existente.AutorId = escopo.UsuarioId;
        existente.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Conflito, $"A sugestão '{chave}' acabou de ser gravada por outro agente. Envie de novo.");
        }

        await db.Entry(existente).Reference(c => c.Autor).LoadAsync(ct);
        return new((await ParaLeituraAsync(new[] { existente }, ct))[0]);
    }

    public async Task<ResultadoDaGestao<ConvencaoV1>> PromoverAsync(EscopoDaEmpresa escopo, long id, int? versaoEsperada, CancellationToken ct)
    {
        var sugestao = await SugestaoVisivelAsync(escopo, id, ct);
        if (sugestao is null)
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "A sugestão não existe mais (alguém a promoveu ou descartou).");
        if (!RegrasDoConhecimentoDaEmpresa.PodePromover(escopo, sugestao.Nivel, sugestao.Chave, sugestao.UsuarioId))
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Proibida, "Só quem grava a regra desta camada promove a sugestão.");
        if (versaoEsperada is { } esperada && esperada != sugestao.Versao)
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Conflito,
                "A sugestão mudou depois que você a leu (outro agente a reescreveu). Confira o texto novo e promova de novo.");

        var regra = await db.ConvencoesDaEmpresa.FirstOrDefaultAsync(
            c => c.EmpresaId == sugestao.EmpresaId && c.Nivel == sugestao.Nivel && c.Time == sugestao.Time && c.UsuarioId == sugestao.UsuarioId
                && c.Chave == sugestao.Chave && c.Origem == OrigensDeConvencao.Usuario, ct);
        if (regra is null)
        {
            regra = new ConvencaoDaEmpresa
            {
                EmpresaId = sugestao.EmpresaId, Nivel = sugestao.Nivel, Time = sugestao.Time, UsuarioId = sugestao.UsuarioId,
                Chave = sugestao.Chave, Origem = OrigensDeConvencao.Usuario, Versao = 0,
            };
            db.ConvencoesDaEmpresa.Add(regra);
        }

        // Numa transação só (um SaveChanges): a regra passa a ter o texto da sugestão, e a sugestão sai.
        regra.Valor = sugestao.Valor;
        regra.Versao++;
        regra.AutorId = escopo.UsuarioId;
        regra.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        db.ConvencoesDaEmpresa.Remove(sugestao);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Conflito, $"A regra '{sugestao.Chave}' acabou de ser gravada por outra pessoa. Promova de novo.");
        }

        await db.Entry(regra).Reference(c => c.Autor).LoadAsync(ct);
        return new((await ParaLeituraAsync(new[] { regra }, ct))[0]);
    }

    public async Task<ResultadoDaGestao<ConvencaoV1>> DescartarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct)
    {
        var sugestao = await SugestaoVisivelAsync(escopo, id, ct);
        if (sugestao is null)
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "A sugestão não existe mais (alguém a promoveu ou descartou).");
        if (!RegrasDoConhecimentoDaEmpresa.PodeDescartar(escopo, sugestao.Nivel, sugestao.Chave, sugestao.UsuarioId, sugestao.AutorId))
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Proibida, "Só quem grava a regra desta camada, ou o autor da sugestão, a descarta.");

        var lida = (await ParaLeituraAsync(new[] { sugestao }, ct))[0];
        db.ConvencoesDaEmpresa.Remove(sugestao);
        await db.SaveChangesAsync(ct);
        return new(lida);
    }

    /// <summary>
    /// A sugestão <paramref name="id"/> da empresa do escopo que quem pede pode ver: a de nível usuário só o dono, o
    /// gestor e o admin a veem (como na auditoria). A regra, a de outra empresa ou a invisível não são achadas.
    /// </summary>
    private Task<ConvencaoDaEmpresa?> SugestaoVisivelAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct)
        => db.ConvencoesDaEmpresa.Include(c => c.Autor).FirstOrDefaultAsync(
            c => c.Id == id && c.EmpresaId == escopo.EmpresaId && c.Origem == OrigensDeConvencao.Agente
                && (escopo.EhGestor || escopo.EhAdmin || c.Nivel != NiveisDeConvencao.Usuario || c.UsuarioId == escopo.UsuarioId), ct);

    public async Task<IReadOnlyList<string>> EquipesAsync(EscopoDaEmpresa escopo, CancellationToken ct)
        => await db.ConvencoesDaEmpresa.AsNoTracking()
            .Where(c => c.EmpresaId == escopo.EmpresaId && c.Nivel == NiveisDeConvencao.Time && c.Time != string.Empty)
            .Select(c => c.Time).Distinct().OrderBy(t => t)
            .ToListAsync(ct);

    /// <summary>As convenções da empresa do escopo, com o autor, na ordem da auditoria.</summary>
    private IQueryable<ConvencaoDaEmpresa> Da(EscopoDaEmpresa escopo) => db.ConvencoesDaEmpresa.AsNoTracking().Include(c => c.Autor)
        .Where(c => c.EmpresaId == escopo.EmpresaId)
        .OrderBy(c => c.Nivel).ThenBy(c => c.Time).ThenBy(c => c.UsuarioId).ThenBy(c => c.Chave);

    /// <summary>A leitura: o dono da de nível usuário e o autor pelo nome (ou login), e a data na borda.</summary>
    private async Task<IReadOnlyList<ConvencaoV1>> ParaLeituraAsync(IReadOnlyList<ConvencaoDaEmpresa> convencoes, CancellationToken ct)
    {
        var donos = convencoes.Where(c => c.UsuarioId > 0).Select(c => c.UsuarioId).Distinct().ToArray();
        var nomes = await db.Usuarios.AsNoTracking().Where(u => donos.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome.Length > 0 ? u.Nome : u.Login, ct);

        return convencoes.Select(c => new ConvencaoV1(
            c.Nivel, c.Chave, c.Valor, c.Time,
            c.UsuarioId > 0 ? nomes.GetValueOrDefault(c.UsuarioId, string.Empty) : string.Empty,
            Nome(c.Autor),
            DevKitPageDb.Utc(c.AtualizadoEmUtc),
            c.Id, c.Origem, c.Versao)).ToArray();
    }

    private static string Nome(Usuario? usuario) => usuario is null ? string.Empty : usuario.Nome.Length > 0 ? usuario.Nome : usuario.Login;

    private static RepositorioDaEmpresaV1 Resumo(RepositorioDaEmpresa r) => new(r.Origem, r.Descricao, Nome(r.Autor), DevKitPageDb.Utc(r.AtualizadoEmUtc));
}
