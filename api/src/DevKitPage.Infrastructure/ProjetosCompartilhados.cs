using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class ProjetosCompartilhados(DevKitPageDb db, TimeProvider relogio) : IProjetosCompartilhados
{
    private IQueryable<ProjetoCompartilhado> Visiveis(EscopoDoDesign e, string? time)
    {
        var equipe = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        return db.ProjetosCompartilhados.Where(p =>
            p.Nivel == "usuario" && p.UsuarioId == e.UsuarioId ||
            e.EmpresaId != null && p.EmpresaId == e.EmpresaId &&
            (p.Nivel == "empresa" || p.Nivel == "time" && (equipe == "" || p.Time == equipe)));
    }

    public async Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct)
    {
        var consulta = Visiveis(escopo, time).AsNoTracking();
        if (desde is { } d) consulta = consulta.Where(p => p.AtualizadoEmUtc > d.UtcDateTime);
        return (await consulta.OrderBy(p => p.Slug).ToListAsync(ct)).Select(p => Ler(p, escopo)).ToArray();
    }

    public async Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
        => await Visiveis(escopo, null).AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } p ? Ler(p, escopo) : null;

    public async Task<GravacaoDoProjeto> GravarAsync(EscopoDoDesign escopo, long? id, EnvioDeProjetoV1 envio, CancellationToken ct)
    {
        var erro = RegrasDosProjetos.Validar(envio);
        if (erro.Length > 0) return new(400, null, erro);
        var nivel = RegrasDeCompartilhamento.NivelEfetivo(escopo, envio.Nivel);
        var time = nivel == "time" ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time) : "";
        if (nivel == "time" && time.Length == 0) return new(400, null, "Informe o time.");
        var espaco = RegrasDeCompartilhamento.EspacoDe(escopo);
        var dono = nivel == "usuario" ? escopo.UsuarioId : 0;
        var p = id is { } numero
            ? await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == numero, ct)
            : await db.ProjetosCompartilhados.FirstOrDefaultAsync(p => p.Espaco == espaco && p.Nivel == nivel && p.Time == time && p.UsuarioId == dono && p.Slug == envio.Slug, ct);
        if (id.HasValue && p is null) return new(404, null, "Projeto não encontrado.");
        var novo = p is null;
        if (p is not null && (p.Nivel != nivel || p.Time != time || p.Slug != envio.Slug))
            return new(400, null, "A identidade do projeto não pode mudar durante a sincronização.");
        if (p is not null && p.Versao != (envio.Versao ?? 0)) return new(409, Ler(p, escopo), "O projeto mudou em outra máquina. Mescle antes de reenviar.");
        if (p is null && envio.Versao is > 0) return new(409, null, "Versão remota inexistente.");
        var agora = relogio.GetUtcNow();
        if (p is null)
        {
            var autor = await db.Usuarios.Where(u => u.Id == escopo.UsuarioId).Select(u => u.Nome).FirstOrDefaultAsync(ct) ?? "";
            p = new() { Espaco = espaco, EmpresaId = escopo.EmpresaId, Nivel = nivel, Time = time, UsuarioId = dono, Slug = envio.Slug, Autor = autor };
            db.ProjetosCompartilhados.Add(p);
        }
        // Nenhuma ausência no snapshot apaga um item remoto. A remoção exige uma operação explícita.
        var antigos = novo ? Array.Empty<ItemDoProjetoV1>() : JsonSerializer.Deserialize<EnvioDeProjetoV1>(p.Documento)!.Itens;
        var itens = antigos.Where(i => envio.Itens.All(n => n.Numero != i.Numero)).Concat(envio.Itens).OrderBy(i => i.Numero).ToArray();
        p.Documento = JsonSerializer.Serialize(envio with { Nivel = nivel, Time = time, Itens = itens });
        p.AtualizadoEmUtc = agora.UtcDateTime;
        p.Versao++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var atual = await db.ProjetosCompartilhados.AsNoTracking().FirstOrDefaultAsync(x => x.Espaco == espaco && x.Nivel == nivel && x.Time == time && x.UsuarioId == dono && x.Slug == envio.Slug, ct);
            if (atual is null) throw;
            return new(409, Ler(atual, escopo), "Outro envio alterou o projeto. Mescle antes de reenviar.");
        }
        return new(novo ? 201 : 200, Ler(p, escopo));
    }

    private static ProjetoV1 Ler(ProjetoCompartilhado p, EscopoDoDesign e)
    {
        var d = JsonSerializer.Deserialize<EnvioDeProjetoV1>(p.Documento)!;
        return new(p.Id, p.Slug, d.Nome, p.Nivel, p.Time, d.Repositorios, d.Branch, d.SemanaConvertida, d.Definicao,
            d.Anexos, d.Autonomia, d.Paralelismo, d.DestinoDaPr, d.Situacao, p.Versao, p.Autor, DevKitPageDb.Utc(p.AtualizadoEmUtc),
            d.Itens, RegrasDoDesign.PodeApagar(e, p.UsuarioId, p.EmpresaId), d.FeatureId, d.EpicId, d.Iteracao, d.Board, d.BranchesDasTasks, d.AtualizadoEm);
    }
}
