using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os projetos compartilhados (US #693) no banco. O isolamento é o <see cref="EscopoDoDesign"/>, aplicado em
/// <see cref="Visiveis"/> com a MESMA regra do design (<see cref="RegrasDoDesign.Visivel"/>) — o projeto que não é visível é
/// 404, sem revelar se existe. A versão é a concorrência: gravar com a versão velha é conflito, e o dev.kit relê.
/// </summary>
public sealed class ProjetosDaEmpresa(DevKitPageDb db, TimeProvider relogio) : IProjetosDaEmpresa
{
    public async Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct)
    {
        var consulta = Visiveis(escopo, time);
        if (desde is { } marco)
        {
            var corte = marco.UtcDateTime;
            consulta = consulta.Where(p => p.AtualizadoEmUtc > corte);
        }

        var projetos = await consulta.OrderBy(p => p.Slug).ThenBy(p => p.Nivel).ThenBy(p => p.Time).ToListAsync(ct);
        return projetos.Select(p => Leitura(escopo, p)).ToArray();
    }

    public async Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
        => await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == id, ct) is { } achado ? Leitura(escopo, achado) : null;

    public async Task<ResultadoDaGestao<ProjetoGravado>> GravarAsync(EscopoDoDesign escopo, EnvioDeProjetoV1 envio, CancellationToken ct)
    {
        var nivel = RegrasDoDesign.NivelEfetivo(escopo, envio.Nivel);
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time) : string.Empty;
        if (nivel == NiveisDeConvencao.Time && time.Length == 0)
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, "Informe o time do projeto de nível time (a Equipe das Configurações do dev.kit).");

        var espaco = RegrasDoDesign.EspacoDe(escopo);
        var dono = nivel == NiveisDeConvencao.Usuario ? escopo.UsuarioId : 0;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var projeto = await db.ProjetosCompartilhados.Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Espaco == espaco && p.Nivel == nivel && p.Time == time && p.UsuarioId == dono && p.Slug == envio.Slug, ct);

        var novo = projeto is null;
        if (projeto is null)
        {
            if (envio.Versao is { } lida && lida > 0)
                return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.NaoEncontrado, $"O projeto {envio.Slug} não existe neste escopo: envie sem versão para criá-lo.");
            projeto = new ProjetoCompartilhado
            {
                EmpresaId = escopo.EmpresaId, Espaco = espaco, Nivel = nivel, Time = time, UsuarioId = dono, Slug = envio.Slug!,
                AutorId = escopo.UsuarioId, CriadoEmUtc = agora,
            };
            db.ProjetosCompartilhados.Add(projeto);
        }
        else if (envio.Versao != projeto.Versao)
        {
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito, envio.Versao is null
                ? $"O projeto {envio.Slug} já existe neste escopo (v{projeto.Versao}): leia-o e envie com a versão."
                : $"O projeto {envio.Slug} mudou (v{projeto.Versao}, você leu a v{envio.Versao}): releia e envie de novo.");
        }

        projeto.Nome = envio.Nome!.Trim();
        projeto.Repositorios = RegrasDosProjetos.Linhas(envio.Repositorios);
        projeto.Branch = envio.Branch!.Trim();
        projeto.SemanaConvertida = envio.SemanaConvertida;
        projeto.Definicao = envio.Definicao?.Trim() ?? string.Empty;
        projeto.Anexos = RegrasDosProjetos.Linhas(envio.Anexos);
        projeto.Autonomia = envio.Autonomia;
        projeto.Paralelismo = envio.Paralelismo;
        projeto.DestinoDaPr = envio.DestinoDaPr?.Trim() ?? string.Empty;
        projeto.Situacao = envio.Situacao ?? ProjetosV1.Rascunho;
        projeto.Versao++;
        projeto.AtualizadoPorId = escopo.UsuarioId;
        projeto.AtualizadoEmUtc = agora;

        // O plano é uma coisa só: os itens vêm inteiros e substituem os gravados.
        db.ItensDeProjetoCompartilhado.RemoveRange(projeto.Itens);
        projeto.Itens = (envio.Itens ?? []).OrderBy(i => i.Numero).Select(i => new ItemDeProjetoCompartilhado
        {
            Numero = i.Numero, Titulo = i.Titulo.Trim(), Descricao = i.Descricao?.Trim() ?? string.Empty, Fluxo = i.Fluxo?.Trim() ?? string.Empty,
            Repositorios = RegrasDosProjetos.Linhas(i.Repositorios), Componentes = RegrasDosProjetos.Linhas(i.Componentes),
            Dependencias = string.Join(',', (i.Dependencias ?? []).Distinct().Order()), Onda = i.Onda, WorkItemId = i.WorkItemId,
            Situacao = i.Situacao ?? ProjetosV1.ItemPlanejado,
        }).ToList();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois envios do mesmo slug ao mesmo tempo: o índice único decide, e o perdedor relê.
            db.ChangeTracker.Clear();
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito, $"O projeto {envio.Slug} acabou de ser gravado por outra pessoa: releia e envie de novo.");
        }

        var gravado = await Visiveis(escopo, null).FirstAsync(p => p.Id == projeto.Id, ct);
        return new(new ProjetoGravado(Leitura(escopo, gravado), novo));
    }

    public async Task<ResultadoDaGestao<ProjetoV1>> ApagarAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
    {
        var projeto = await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (projeto is null)
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        if (!RegrasDoDesign.PodeApagar(escopo, projeto.AutorId, projeto.EmpresaId))
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.Proibida, "Só quem criou o projeto, ou o gestor da empresa, o apaga.");

        var leitura = Leitura(escopo, projeto);
        await db.ItensDeProjetoCompartilhado.Where(i => i.ProjetoId == id).ExecuteDeleteAsync(ct);
        await db.ProjetosCompartilhados.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return new(leitura);
    }

    /// <summary>Os projetos que o escopo vê: a regra do design (<see cref="RegrasDoDesign.Visivel"/>), escrita para o banco.</summary>
    private IQueryable<ProjetoCompartilhado> Visiveis(EscopoDoDesign escopo, string? time)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        var empresa = escopo.EmpresaId;
        return db.ProjetosCompartilhados.AsNoTracking().Include(p => p.Itens).Include(p => p.Autor)
            .Where(p => (p.Nivel == NiveisDeConvencao.Usuario && p.UsuarioId == escopo.UsuarioId)
                || (empresa != null && p.EmpresaId == empresa
                    && (p.Nivel == NiveisDeConvencao.Empresa || (p.Nivel == NiveisDeConvencao.Time && (doTime == string.Empty || p.Time == doTime)))));
    }

    private static ProjetoV1 Leitura(EscopoDoDesign escopo, ProjetoCompartilhado p) => new(
        p.Id, p.Slug, p.Nome, p.Nivel, p.Time, RegrasDosProjetos.Lista(p.Repositorios), p.Branch, p.SemanaConvertida, p.Definicao,
        RegrasDosProjetos.Lista(p.Anexos), p.Autonomia, p.Paralelismo, p.DestinoDaPr, p.Situacao, p.Versao,
        p.Autor is null ? string.Empty : p.Autor.Nome.Length > 0 ? p.Autor.Nome : p.Autor.Login, DevKitPageDb.Utc(p.AtualizadoEmUtc),
        p.Itens.OrderBy(i => i.Numero).Select(i => new ItemDoProjetoV1(
            i.Numero, i.Titulo, i.Descricao, i.Fluxo, RegrasDosProjetos.Lista(i.Repositorios), RegrasDosProjetos.Lista(i.Componentes),
            RegrasDosProjetos.Dependencias(i.Dependencias), i.Onda, i.WorkItemId, i.Situacao)).ToArray(),
        RegrasDoDesign.PodeApagar(escopo, p.AutorId, p.EmpresaId));
}
