using System.Globalization;
using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class MatrizesNoBanco(DevKitPageDb db, TimeProvider relogio) : IMatrizes
{
    private static string Espaco(EscopoDoDesign e) => e.EmpresaId is { } id ? $"empresa:{id}" : $"usuario:{e.UsuarioId}";
    private IQueryable<RevisaoMatrixNoBanco> Stream(EscopoDoDesign e, string app, string tipo, string id)
        => db.Set<RevisaoMatrixNoBanco>().AsNoTracking().Where(r => r.Espaco == Espaco(e) && r.Aplicacao == app && r.Tipo == tipo && r.DocumentoId == id);
    private static RevisaoMatrix Ler(RevisaoMatrixNoBanco r) => new(r.Versao, r.AutorId.ToString(CultureInfo.InvariantCulture),
        DevKitPageDb.Utc(r.EmUtc), JsonSerializer.Deserialize<DocumentoMatrix>(r.Documento, RegrasMatrix.Json)!);

    private async Task<int> AcessoAsync(EscopoDoDesign e, RevisaoMatrixNoBanco alvo, bool editar, CancellationToken ct)
    {
        if (alvo.ProjetoId is not { } id) return alvo.DonoId == e.UsuarioId ? 200 : 404;
        var projeto = await db.ProjetosCompartilhados.Include(p => p.Acessos).Visiveis(e).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (projeto is null) return 404;
        return !editar || RegrasDeAcesso.PodeEditar(RegrasDeAcesso.Papel(e, projeto)) ? 200 : 403;
    }

    public async Task<RespostaMatrix<RevisaoMatrix[]>> HistoricoAsync(EscopoDoDesign e, string app, string tipo, string id, CancellationToken ct)
    {
        var linhas = await Stream(e, app, tipo, id).OrderBy(r => r.Versao).ToArrayAsync(ct);
        if (linhas.Length == 0) return new(404, Erro: "Documento matrix não encontrado.");
        var acesso = await AcessoAsync(e, linhas[0], false, ct);
        return acesso == 200 ? new(200, linhas.Select(Ler).ToArray()) : new(acesso, Erro: "Sem acesso ao documento matrix.");
    }

    public async Task<RespostaMatrix<ResultadoMatrix>> EditarAsync(EscopoDoDesign e, EdicaoMatrix edicao, bool aplicar, CancellationToken ct)
    {
        try { RegrasMatrix.Validar(edicao.Documento); }
        catch (ArgumentException ex) { return new(400, Erro: ex.Message); }
        if (edicao.VersaoEsperada < 0 || edicao.VersaoEsperada == int.MaxValue) return new(400, Erro: "Versão esperada inválida.");
        var d = edicao.Documento;
        var anterior = await Stream(e, d.Aplicacao, d.Tipo, d.Id).OrderByDescending(r => r.Versao).FirstOrDefaultAsync(ct);
        long? projetoId = anterior?.ProjetoId;
        var dono = anterior?.DonoId ?? e.UsuarioId;
        if (anterior is not null)
        {
            var acesso = await AcessoAsync(e, anterior, true, ct);
            if (acesso != 200) return new(acesso, Erro: "Alterar matrix exige editor ou dono do projeto/demanda.");
            if (Ler(anterior).Documento.Projeto != d.Projeto) return new(400, Erro: "Escopo da matriz é imutável.");
        }
        else if (d.Projeto.Length > 0)
        {
            var encontrados = await db.ProjetosCompartilhados.Include(p => p.Acessos).Visiveis(e).Where(p => p.Slug == d.Projeto).ToArrayAsync(ct);
            if (encontrados.Length == 0) return new(404, Erro: "Projeto não encontrado.");
            if (encontrados.Length != 1) return new(400, Erro: "Slug de projeto ambíguo neste escopo.");
            var projeto = encontrados[0];
            if (!RegrasDeAcesso.PodeEditar(RegrasDeAcesso.Papel(e, projeto))) return new(403, Erro: "Alterar matrix exige editor ou dono.");
            projetoId = projeto.Id;
        }
        if ((anterior?.Versao ?? 0) != edicao.VersaoEsperada) return new(409, Erro: "Conflito de versão matrix; releia antes de editar.");

        // Leitura de referências sempre passa pelo mesmo ACL. Não aceita mapa de outra aplicação nem outra demanda.
        var referencias = new Dictionary<(string Tipo, string Id), RevisaoMatrix[]>();
        foreach (var alvo in d.Tipo switch
        {
            "matriz" => new[] { ("mapa", d.Aplicacao) },
            "registro" or "vinculo" => new[] { ("matriz", d.Id) },
            _ => Array.Empty<(string, string)>()
        })
        {
            var resposta = await HistoricoAsync(e, d.Aplicacao, alvo.Item1, alvo.Item2, ct);
            if (resposta.Status != 200) return new(resposta.Status, Erro: resposta.Erro);
            referencias[alvo] = resposta.Valor!;
            if (d.Tipo is "registro" or "vinculo" && resposta.Valor![0].Documento.Projeto != d.Projeto)
                return new(400, Erro: "Registro/vínculo pertence a outra demanda.");
        }
        RevisaoMatrix? Referencia(string tipo, string id, int versao) => referencias.TryGetValue((tipo, id), out var h)
            ? versao == 0 ? h.LastOrDefault() : h.FirstOrDefault(r => r.Versao == versao) : null;
        try
        {
            RegrasMatrix.ValidarReferencia(d, Referencia);
            var h = await Stream(e, d.Aplicacao, d.Tipo, d.Id).ToArrayAsync(ct);
            RegrasMatrix.ValidarEstabilidade(h.Select(Ler), d);
        }
        catch (ArgumentException ex) { return new(400, Erro: ex.Message); }

        if (d.Tipo is "registro" or "vinculo")
        {
            var matriz = await Stream(e, d.Aplicacao, "matriz", d.Id).FirstAsync(ct);
            // Acesso de escrita no pai também é obrigatório para criar streams auxiliares.
            var acesso = await AcessoAsync(e, matriz, true, ct);
            if (acesso != 200) return new(acesso, Erro: "Registrar comprovação/vínculo exige editor ou dono.");
            projetoId = matriz.ProjetoId; dono = matriz.DonoId;
        }
        var responsaveis = d.Matriz?.Celulas.SelectMany(c => c.Responsaveis).ToArray() ?? [];
        if (responsaveis.Any(r => r.Item is not null))
        {
            var itens = projetoId is { } p ? await db.ItensDeProjetoCompartilhado.Where(i => i.ProjetoId == p).Select(i => i.Numero).ToArrayAsync(ct) : [];
            if (responsaveis.Any(r => r.Item is { } i && !itens.Contains(i))) return new(400, Erro: "Item não pertence ao projeto.");
        }
        if (d.Vinculo is { } v)
        {
            var maquina = await db.MaquinasRemotas.AsNoTracking().FirstOrDefaultAsync(m => m.UsuarioId == e.UsuarioId && m.MaquinaId == v.Maquina, ct);
            var retrato = maquina is null || maquina.Retrato.Length == 0 ? null : JsonSerializer.Deserialize<RetratoV1>(maquina.Retrato, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var task = retrato?.Fluxos?.FirstOrDefault(f => f.Id == v.Sessao);
            if (task is null || (d.Projeto.Length > 0
                ? task.Projeto != d.Projeto || task.Item != v.Item
                : !string.IsNullOrEmpty(task.Projeto) || task.Item is not null))
                return new(404, Erro: "Sessão/item não encontrado no retrato da máquina do solicitante.");
            var vinculos = await Stream(e, d.Aplicacao, "vinculo", d.Id).ToArrayAsync(ct);
            if (vinculos.Select(Ler).Any(r => r.Documento.Vinculo!.Responsavel == v.Responsavel && r.Documento.Vinculo != v))
                return new(400, Erro: "Responsável já associado a outra sessão.");
        }
        var agora = relogio.GetUtcNow();
        var revisao = new RevisaoMatrix(edicao.VersaoEsperada + 1, e.UsuarioId.ToString(CultureInfo.InvariantCulture), agora, d);
        var resultado = new ResultadoMatrix(aplicar, revisao, RegrasMatrix.Comparar(anterior is null ? null : Ler(anterior).Documento, d));
        if (!aplicar) return new(200, resultado);
        var linha = new RevisaoMatrixNoBanco { Espaco = Espaco(e), Aplicacao = d.Aplicacao, Tipo = d.Tipo, DocumentoId = d.Id,
            Versao = revisao.Versao, DonoId = dono, ProjetoId = projetoId, AutorId = e.UsuarioId, EmUtc = agora.UtcDateTime,
            Documento = JsonSerializer.Serialize(d, RegrasMatrix.Json) };
        db.Add(linha);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(linha).State = EntityState.Detached;
            if (await Stream(e, d.Aplicacao, d.Tipo, d.Id).AnyAsync(r => r.Versao == revisao.Versao, ct))
                return new(409, Erro: "Outra edição venceu a concorrência matrix; releia.");
            throw;
        }
        return new(200, resultado);
    }
}
