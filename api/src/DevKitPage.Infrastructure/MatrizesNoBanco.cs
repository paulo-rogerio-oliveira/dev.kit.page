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

    public async Task<RespostaMatrix<CabecalhoMatrix[]>> ListarAsync(EscopoDoDesign e, CancellationToken ct)
    {
        var projetos = db.ProjetosCompartilhados.Visiveis(e).Select(p => p.Id);
        var linhas = await db.Set<RevisaoMatrixNoBanco>().AsNoTracking()
            .Where(r => r.Espaco == Espaco(e) && (r.ProjetoId.HasValue
                ? projetos.Contains(r.ProjetoId.Value) : r.DonoId == e.UsuarioId))
            .GroupBy(r => new { r.Aplicacao, r.Tipo, r.DocumentoId })
            .Select(g => g.OrderByDescending(r => r.Versao).First()).ToArrayAsync(ct);
        return new(200, linhas.Select(r => new CabecalhoMatrix(r.Aplicacao, r.Tipo, r.DocumentoId,
            Ler(r).Documento.Projeto, r.Versao)).OrderBy(r => r.Aplicacao).ThenBy(r => r.Tipo).ThenBy(r => r.Id).ToArray());
    }

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
        await using var transacao = aplicar ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
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
        if (d.Matriz is { } matrizAnterior && anterior is not null)
            d = d with { Matriz = RegrasDaTransferenciaMatrix.PreservarOmissoes(matrizAnterior with
                { Coberturas = matrizAnterior.Coberturas ?? Ler(anterior).Documento.Matriz?.Coberturas }, Ler(anterior).Documento.Matriz) };

        // Leitura de referências sempre passa pelo mesmo ACL. Não aceita mapa de outra aplicação nem outra demanda.
        var referencias = new Dictionary<(string Tipo, string Id), RevisaoMatrix[]>();
        foreach (var alvo in d.Tipo switch
        {
            "matriz" => new[] { ("mapa", d.Aplicacao) },
            "registro" or "vinculo" => d.Registro is { Evento.Length: > 0 } r
                ? new[] { ("matriz", d.Id), ("evento", RegrasAvaliacaoMatrix.IdDoEvento(r.Evento, d.Id, r.SessaoAvaliada)) }
                : new[] { ("matriz", d.Id) },
            "evento" => new[] { ("matriz", d.Evento!.Matriz) },
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
            if (d.Registro is { Evento.Length: > 0 } registro)
                RegrasAvaliacaoMatrix.ConferirLeitura(Referencia("evento", RegrasAvaliacaoMatrix.IdDoEvento(registro.Evento, d.Id, registro.SessaoAvaliada), 0)!.Documento.Evento!,
                    referencias[("matriz", d.Id)]);
            var h = await Stream(e, d.Aplicacao, d.Tipo, d.Id).ToArrayAsync(ct);
            RegrasMatrix.ValidarEstabilidade(h.Select(Ler), d);
        }
        catch (ArgumentException ex) { return new(400, Erro: ex.Message); }

        if (d.Matriz?.Transferencias is { Length: > 0 } transferencias)
        {
            var atual = projetoId is { } id ? await new ProjetosDaEmpresa(db, relogio).ObterAsync(e, id, ct) : null;
            if (atual is null) return new(404, Erro: "Projeto da transferência indisponível.");
            var fonte = new EnvioDeProjetoV1(atual.Slug, atual.Nome, atual.Nivel, atual.Time, atual.Repositorios, atual.Branch,
                atual.SemanaConvertida, atual.Definicao, atual.Anexos, atual.Autonomia, atual.Paralelismo, atual.DestinoDaPr,
                atual.Situacao, atual.Versao, atual.Itens);
            try { FonteDaTransferenciaMatrix.Conferir(fonte, transferencias); }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
            { return new(400, Erro: ex.Message); }
            // A conferência interna considera os editores do projeto; não devolve seus retratos privados ao solicitante.
            var donoDoProjeto = await db.ProjetosCompartilhados.AsNoTracking().Where(p => p.Id == projetoId).Select(p => p.DonoId).SingleAsync(ct);
            var editoresDoProjeto = await db.AcessosAosProjetos.AsNoTracking().Where(a => a.ProjetoEmOndasId == projetoId
                && a.Papel == AcessoV1.PapelEditor).Select(a => a.UsuarioId).ToListAsync(ct);
            editoresDoProjeto.Add(donoDoProjeto); editoresDoProjeto.Add(e.UsuarioId);
            var retratosDaFonte = await db.MaquinasRemotas.AsNoTracking().Where(m => editoresDoProjeto.Contains(m.UsuarioId) && m.Retrato != "")
                .Select(m => m.Retrato).ToArrayAsync(ct);
            var itensAtivos = retratosDaFonte.Select(r => JsonSerializer.Deserialize<RetratoV1>(r, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                .SelectMany(r => r?.Fluxos ?? []).Where(f => f.Projeto == d.Projeto && f.Item is not null).Select(f => f.Item).ToHashSet();
            if (transferencias.Any(t => t.Revogacao is null && itensAtivos.Contains(t.Item)))
                return new(400, Erro: "A execução do item ainda aparece no retrato de uma máquina do projeto; preserve a sessão antes de transferir.");
        }

        if (d.Evento is { } evento)
        {
            if (anterior is not null || edicao.VersaoEsperada != 0) return new(409, Erro: "Recibo do host é imutável.");
            var matriz = await Stream(e, d.Aplicacao, "matriz", evento.Matriz).FirstAsync(ct);
            var acesso = await AcessoAsync(e, matriz, true, ct);
            if (acesso != 200) return new(acesso, Erro: "Emitir recibo exige acesso à demanda.");
            projetoId = matriz.ProjetoId; dono = matriz.DonoId;
            var maquina = await db.MaquinasRemotas.AsNoTracking().FirstOrDefaultAsync(m => m.UsuarioId == e.UsuarioId && m.MaquinaId == evento.Maquina, ct);
            var retrato = maquina is null || maquina.Retrato.Length == 0 ? null
                : JsonSerializer.Deserialize<RetratoV1>(maquina.Retrato, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var task = retrato?.Fluxos?.SingleOrDefault(f => f.Id == evento.SessaoAvaliada);
            if (task is null || (task.Projeto ?? "") != d.Projeto)
                return new(404, Erro: "Host/sessão avaliada não confirmado no retrato da máquina do solicitante.");
            if (evento.Finalizacao is not null)
            {
                var problema = await ConferirFinalAsync(e, d, projetoId, ct);
                if (problema.Length > 0) return new(400, Erro: problema);
            }
        }

        if (d.Tipo is "registro" or "vinculo")
        {
            var matriz = await Stream(e, d.Aplicacao, "matriz", d.Id).FirstAsync(ct);
            // Acesso de escrita no pai também é obrigatório para criar streams auxiliares.
            var acesso = await AcessoAsync(e, matriz, true, ct);
            if (acesso != 200) return new(acesso, Erro: "Registrar comprovação/vínculo exige editor ou dono.");
            projetoId = matriz.ProjetoId; dono = matriz.DonoId;
            if (d.Registro is { Tipo: RegrasVerificacaoFinalMatrix.Tipo } final)
            {
                var recibo = Referencia("evento", RegrasAvaliacaoMatrix.IdDoEvento(final.Evento, d.Id, final.SessaoAvaliada), 0)!.Documento;
                var problema = await ConferirFinalAsync(e, recibo, projetoId, ct);
                if (problema.Length > 0) return new(400, Erro: problema);
            }
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
            var finalizacaoConfirmada = false;
            if (d.Projeto.Length > 0 && v.Item is null)
            {
                var projeto = await db.ProjetosCompartilhados.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projetoId, ct);
                var documento = projeto is null || projeto.Documento.Length == 0 ? null
                    : JsonSerializer.Deserialize<EnvioDeProjetoV1>(projeto.Documento);
                try
                {
                    if (documento is not null && projeto is not null)
                        documento = await PlanoEfetivoDaFinalizacao.ConferirAsync(db, projeto.Id, documento, ct);
                }
                catch (Exception ex) when (ex is ArgumentException or JsonException) { return new(400, Erro: ex.Message); }
                var recusa = RegrasDaFinalizacao.Conferir(task.Finalizacao, v.Sessao, documento);
                if (recusa.Length > 0) return new(400, Erro: recusa);
                finalizacaoConfirmada = true;
            }
            var vinculos = await Stream(e, d.Aplicacao, "vinculo", d.Id).ToArrayAsync(ct);
            if (vinculos.Select(Ler).Any(r => r.Documento.Vinculo is { } existente
                && existente.Responsavel == v.Responsavel && existente != v
                && !(finalizacaoConfirmada && (existente.Item is null
                    || Referencia("matriz", d.Id, 0)?.Documento.Matriz?.Transferencias?.Any(t => t.Revogacao is null
                        && t.Item == existente.Item && t.ResponsavelDaFinalizacao == v.Responsavel) == true))))
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
        await transacao!.CommitAsync(ct);
        return new(200, resultado);
    }

    private async Task<string> ConferirFinalAsync(EscopoDoDesign e, DocumentoMatrix d, long? projetoId, CancellationToken ct)
    {
        var evento = d.Evento!;
        var maquina = await db.MaquinasRemotas.AsNoTracking().SingleOrDefaultAsync(m => m.UsuarioId == e.UsuarioId && m.MaquinaId == evento.Maquina, ct);
        var retrato = maquina is null || maquina.Retrato.Length == 0 ? null
            : JsonSerializer.Deserialize<RetratoV1>(maquina.Retrato, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var task = retrato?.Fluxos?.SingleOrDefault(f => f.Id == evento.SessaoAvaliada);
        if (task is not { Item: null, Finalizacao: not null } || task.Projeto != d.Projeto
            || !RegrasVerificacaoFinalMatrix.MesmaRevisao(task.Finalizacao, evento.RevisaoDaEntrega))
            return "Verificação final diverge da reserva/composição publicada pelo host do solicitante.";
        var projeto = await db.ProjetosCompartilhados.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projetoId, ct);
        var documento = projeto is null || projeto.Documento.Length == 0 ? null : JsonSerializer.Deserialize<EnvioDeProjetoV1>(projeto.Documento);
        try
        {
            if (documento is not null && projeto is not null) documento = await PlanoEfetivoDaFinalizacao.ConferirAsync(db, projeto.Id, documento, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException) { return ex.Message; }
        var problema = RegrasDaFinalizacao.Conferir(task.Finalizacao, task.Id, documento);
        if (problema.Length > 0) return problema;
        var vinculos = (await Stream(e, d.Aplicacao, "vinculo", evento.Matriz).ToArrayAsync(ct)).Select(Ler)
            .GroupBy(r => r.Documento.Vinculo!.Responsavel).Select(g => g.MaxBy(r => r.Versao)!.Documento.Vinculo!).ToArray();
        return evento.Responsaveis.All(dono => vinculos.Any(v => v.Responsavel == dono && v.Item is null
            && v.Sessao == evento.SessaoAvaliada && v.Maquina == evento.Maquina)) ? ""
            : "Verificação final sem vínculo formal do responsável com a sessão e máquina do host.";
    }
}
