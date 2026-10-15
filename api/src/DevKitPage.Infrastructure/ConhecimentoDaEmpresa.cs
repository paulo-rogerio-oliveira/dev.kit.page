using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O catálogo de repositórios e as convenções da empresa (US #491). O isolamento é o <see cref="EscopoDaEmpresa"/>:
/// toda consulta e toda gravação filtram pela empresa dele — o que é de outra empresa nem aparece, e gravar o mesmo
/// caminho ou a mesma convenção noutra empresa é outra linha.
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

    public async Task<IReadOnlyList<ConvencaoV1>> ConvencoesAsync(EscopoDaEmpresa escopo, string? time, CancellationToken ct)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        return await db.ConvencoesDaEmpresa.AsNoTracking()
            .Where(c => c.EmpresaId == escopo.EmpresaId
                && (c.Nivel == NiveisDeConvencao.Empresa || (doTime.Length > 0 && c.Nivel == NiveisDeConvencao.Time && c.Time == doTime)))
            .OrderBy(c => c.Nivel).ThenBy(c => c.Chave)
            .Select(c => new ConvencaoV1(c.Nivel, c.Chave, c.Valor, c.Time))
            .ToListAsync(ct);
    }

    public async Task<ResultadoDaGestao<ConvencaoV1>> GravarConvencaoAsync(EscopoDaEmpresa escopo, ConvencaoV1 convencao, CancellationToken ct)
    {
        var nivel = convencao.Nivel!.Trim().ToLowerInvariant();
        var chave = convencao.Chave!.Trim().ToLowerInvariant();
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(convencao.Time) : string.Empty;
        var valor = convencao.Valor?.Trim() ?? string.Empty;

        // A da empresa vale para todos, então é do gestor; a de um time é de quem trabalha nele — qualquer um da empresa.
        if (nivel == NiveisDeConvencao.Empresa && !escopo.EhGestor)
            return ResultadoDaGestao<ConvencaoV1>.Falhou(FalhaNaGestao.Proibida, "Só o gestor grava as convenções da empresa; a de um time, qualquer um da empresa grava.");

        var existente = await db.ConvencoesDaEmpresa.FirstOrDefaultAsync(
            c => c.EmpresaId == escopo.EmpresaId && c.Nivel == nivel && c.Time == time && c.Chave == chave, ct);

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
            existente = new ConvencaoDaEmpresa { EmpresaId = escopo.EmpresaId, Nivel = nivel, Time = time, Chave = chave };
            db.ConvencoesDaEmpresa.Add(existente);
        }

        existente.Valor = valor;
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

        return new(new ConvencaoV1(nivel, chave, valor, time));
    }

    private static RepositorioDaEmpresaV1 Resumo(RepositorioDaEmpresa r) => new(
        r.Origem, r.Descricao,
        r.Autor is null ? string.Empty : r.Autor.Nome.Length > 0 ? r.Autor.Nome : r.Autor.Login,
        DevKitPageDb.Utc(r.AtualizadoEmUtc));
}
