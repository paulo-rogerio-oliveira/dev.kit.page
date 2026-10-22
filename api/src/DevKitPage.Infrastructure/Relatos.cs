using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os relatos dos usuários do dev.kit (US #602). O isolamento da gestão é o <see cref="EscopoDoPainel"/>, aplicado
/// AQUI e só aqui (<see cref="DoEscopo"/>): o gestor nem enxerga o relato de outra empresa — o detalhe, a troca de
/// estado e a resposta devolvem nulo, como para o que não existe.
/// </summary>
public sealed class Relatos(DevKitPageDb db, TimeProvider relogio) : IRelatos
{
    public async Task<RelatoRecebidoV1> RegistrarAsync(int usuarioId, int? empresaId, RelatoNovoV1 relato, byte[]? captura, CancellationToken ct)
    {
        var chave = relato.ChaveDoCliente?.Trim() ?? string.Empty;
        if (chave.Length > 0 && await JaRecebidoAsync(usuarioId, chave, ct) is { } repetido)
            return repetido;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var novo = new Relato
        {
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            Categoria = RegrasDosRelatos.Normalizar(relato.Categoria),
            Texto = RegrasDosRelatos.Mascarar(relato.Texto?.Trim(), RelatosV1.TamanhoMaximoDoTexto),
            Contexto = string.IsNullOrWhiteSpace(relato.Contexto) ? string.Empty : RegrasDosRelatos.Mascarar(relato.Contexto, RelatosV1.TamanhoMaximoDoContexto),
            Captura = captura,
            VersaoDevKit = relato.VersaoDevKit?.Trim() ?? string.Empty,
            ChaveDoCliente = chave,
            Estado = EstadosDeRelato.Novo,
            CriadoEmUtc = agora,
            AtualizadoEmUtc = agora,
        };
        db.Relatos.Add(novo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (chave.Length > 0)
        {
            // O app e o devcli reenviando a mesma fila ao mesmo tempo: o índice único decide, e o segundo recebe o primeiro.
            db.Entry(novo).State = EntityState.Detached;
            return await JaRecebidoAsync(usuarioId, chave, ct) ?? throw new InvalidOperationException("O relato repetido sumiu.");
        }

        return new RelatoRecebidoV1(novo.Id, novo.Estado, DevKitPageDb.Utc(novo.CriadoEmUtc));
    }

    public async Task<Pagina<RelatoResumoV1>> ListarAsync(EscopoDoPainel escopo, FiltroDeRelatos filtro, CancellationToken ct)
    {
        var consulta = DoEscopo(escopo).AsNoTracking();
        if (escopo.EhAdmin && filtro.EmpresaId is { } empresa)
            consulta = consulta.Where(r => r.EmpresaId == empresa);
        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            var estado = RegrasDosRelatos.Normalizar(filtro.Estado);
            consulta = consulta.Where(r => r.Estado == estado);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
        {
            var categoria = RegrasDosRelatos.Normalizar(filtro.Categoria);
            consulta = consulta.Where(r => r.Categoria == categoria);
        }

        if (filtro.De is { } de)
        {
            var inicio = de.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            consulta = consulta.Where(r => r.CriadoEmUtc >= inicio);
        }

        if (filtro.Ate is { } ate)
        {
            var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            consulta = consulta.Where(r => r.CriadoEmUtc < fim);
        }

        var numero = Math.Max(1, filtro.Pagina);
        var tamanho = Math.Clamp(filtro.Tamanho, 1, RegrasDosRelatos.TamanhoMaximoDaPagina);
        var total = await consulta.CountAsync(ct);
        // Sem a captura nem o contexto na lista: só o que a linha mostra (a captura vai no detalhe).
        var linhas = await consulta
            .OrderByDescending(r => r.CriadoEmUtc).ThenByDescending(r => r.Id)
            .Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(r => new
            {
                r.Id, r.Categoria, r.Estado, r.Texto, r.EmpresaId, Empresa = r.Empresa != null ? r.Empresa.Nome : null,
                Autor = r.Usuario != null ? (r.Usuario.Nome != "" ? r.Usuario.Nome : r.Usuario.Login) : "",
                r.CriadoEmUtc, r.AtualizadoEmUtc, TemCaptura = r.Captura != null, Respondido = r.Resposta != "",
            })
            .ToListAsync(ct);

        var itens = linhas.Select(l => new RelatoResumoV1(
                l.Id, l.Categoria, l.Estado, RegrasDosRelatos.Resumo(l.Texto), l.Autor, l.EmpresaId, l.Empresa,
                DevKitPageDb.Utc(l.CriadoEmUtc), DevKitPageDb.Utc(l.AtualizadoEmUtc), l.TemCaptura, l.Respondido))
            .ToArray();
        return new Pagina<RelatoResumoV1>(itens, total, numero, tamanho);
    }

    public async Task<RelatoDetalheV1?> DetalheAsync(EscopoDoPainel escopo, long id, CancellationToken ct)
        => await CarregarAsync(escopo, id, ct) is { } relato ? Detalhe(relato) : null;

    public async Task<RelatoDetalheV1?> AlterarEstadoAsync(EscopoDoPainel escopo, long id, string estado, CancellationToken ct)
    {
        if (await CarregarAsync(escopo, id, ct) is not { } relato)
            return null;
        relato.Estado = RegrasDosRelatos.Normalizar(estado);
        relato.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Detalhe(relato);
    }

    public async Task<RelatoDetalheV1?> ResponderAsync(EscopoDoPainel escopo, long id, int quemResponde, string texto, CancellationToken ct)
    {
        if (await CarregarAsync(escopo, id, ct) is not { } relato)
            return null;
        var agora = relogio.GetUtcNow().UtcDateTime;
        relato.Resposta = texto.Trim();
        relato.RespondidoPorId = quemResponde;
        relato.RespondidoEmUtc = agora;
        relato.AtualizadoEmUtc = agora;
        if (relato.Estado == EstadosDeRelato.Novo)
            relato.Estado = EstadosDeRelato.EmAnalise;
        await db.SaveChangesAsync(ct);
        await db.Entry(relato).Reference(r => r.RespondidoPor).LoadAsync(ct);
        return Detalhe(relato);
    }

    public async Task<IReadOnlyList<MeuRelatoV1>> MeusAsync(int usuarioId, CancellationToken ct)
    {
        var meus = await db.Relatos.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId)
            .OrderByDescending(r => r.CriadoEmUtc).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.Categoria, r.Estado, r.Texto, r.CriadoEmUtc, r.AtualizadoEmUtc, r.Resposta, r.RespondidoEmUtc, r.ChaveDoCliente })
            .ToListAsync(ct);
        return meus.Select(r => new MeuRelatoV1(
                r.Id, r.Categoria, r.Estado, r.Texto, DevKitPageDb.Utc(r.CriadoEmUtc), DevKitPageDb.Utc(r.AtualizadoEmUtc), r.Resposta,
                r.RespondidoEmUtc is { } em ? DevKitPageDb.Utc(em) : null, r.ChaveDoCliente.Length > 0 ? r.ChaveDoCliente : null))
            .ToArray();
    }

    /// <summary>O filtro ÚNICO de isolamento: o admin vê todos; o gestor, só os da empresa dele.</summary>
    private IQueryable<Relato> DoEscopo(EscopoDoPainel escopo)
        => escopo.EhAdmin ? db.Relatos : db.Relatos.Where(r => r.EmpresaId == escopo.EmpresaId);

    private Task<Relato?> CarregarAsync(EscopoDoPainel escopo, long id, CancellationToken ct)
        => DoEscopo(escopo).Include(r => r.Usuario).Include(r => r.Empresa).Include(r => r.RespondidoPor)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    private async Task<RelatoRecebidoV1?> JaRecebidoAsync(int usuarioId, string chave, CancellationToken ct)
    {
        var existente = await db.Relatos.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId && r.ChaveDoCliente == chave)
            .Select(r => new { r.Id, r.Estado, r.CriadoEmUtc })
            .FirstOrDefaultAsync(ct);
        return existente is null ? null : new RelatoRecebidoV1(existente.Id, existente.Estado, DevKitPageDb.Utc(existente.CriadoEmUtc));
    }

    private static RelatoDetalheV1 Detalhe(Relato r) => new(
        r.Id, r.Categoria, r.Estado, r.Texto, r.Contexto, r.Captura is { } captura ? Convert.ToBase64String(captura) : null, r.VersaoDevKit,
        Nome(r.Usuario), r.EmpresaId, r.Empresa?.Nome, DevKitPageDb.Utc(r.CriadoEmUtc), DevKitPageDb.Utc(r.AtualizadoEmUtc),
        r.Resposta, r.RespondidoPorId is null ? string.Empty : Nome(r.RespondidoPor),
        r.RespondidoEmUtc is { } em ? DevKitPageDb.Utc(em) : null);

    private static string Nome(Usuario? usuario)
        => usuario is null ? string.Empty : string.IsNullOrWhiteSpace(usuario.Nome) ? usuario.Login : usuario.Nome;
}
