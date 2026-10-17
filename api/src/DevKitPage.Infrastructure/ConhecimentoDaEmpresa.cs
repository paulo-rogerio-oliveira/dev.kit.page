using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O catálogo de repositórios da empresa (US #491). O isolamento é o <see cref="EscopoDaEmpresa"/>: toda consulta e toda
/// gravação filtram pela empresa dele — o que é de outra empresa nem aparece, e gravar o mesmo caminho noutra empresa é
/// outra linha. As convenções viraram o contexto (<see cref="ContextoDaEmpresa"/>, US #556).
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

    /// <summary>O nome de exibição (o nome, ou o login sem nome) — o mesmo do contexto.</summary>
    internal static string Nome(Usuario? usuario) => usuario is null ? string.Empty : usuario.Nome.Length > 0 ? usuario.Nome : usuario.Login;

    private static RepositorioDaEmpresaV1 Resumo(RepositorioDaEmpresa r) => new(r.Origem, r.Descricao, Nome(r.Autor), DevKitPageDb.Utc(r.AtualizadoEmUtc));
}
