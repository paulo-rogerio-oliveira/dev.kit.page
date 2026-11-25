using System.Text.Json;
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
        var leituras = new List<ProjetoV1>();
        foreach (var p in projetos) leituras.Add(await LeituraEfetivaAsync(escopo, p, ct));
        return leituras;
    }

    public async Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
        => await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == id, ct) is { } achado ? await LeituraEfetivaAsync(escopo, achado, ct) : null;

    public async Task<ResultadoDaGestao<ProjetoGravado>> GravarAsync(EscopoDoDesign escopo, EnvioDeProjetoV1 envio, CancellationToken ct)
    {
        var problema = RegrasDosProjetos.Validar(envio);
        if (problema.Length > 0)
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, problema);
        var nivel = RegrasDoDesign.NivelEfetivo(escopo, envio.Nivel);
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time) : string.Empty;
        if (nivel == NiveisDeConvencao.Time && time.Length == 0)
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, "Informe o time do projeto de nível time (a Equipe das Configurações do dev.kit).");

        var espaco = RegrasDoDesign.EspacoDe(escopo);
        var dono = nivel == NiveisDeConvencao.Usuario ? escopo.UsuarioId : 0;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var projeto = await db.ProjetosCompartilhados.Include(p => p.Itens).Include(p => p.Acessos)
            .FirstOrDefaultAsync(p => p.Espaco == espaco && p.Nivel == nivel && p.Time == time && p.UsuarioId == dono && p.Slug == envio.Slug, ct);

        var novo = projeto is null;
        if (projeto is null)
        {
            if (envio.Versao is { } lida && lida > 0)
                return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.NaoEncontrado, $"O projeto {envio.Slug} não existe neste escopo: envie sem versão para criá-lo.");
            projeto = new ProjetoCompartilhado
            {
                EmpresaId = escopo.EmpresaId, Espaco = espaco, Nivel = nivel, Time = time, UsuarioId = dono, Slug = envio.Slug!,
                AutorId = escopo.UsuarioId, DonoId = escopo.UsuarioId, CriadoEmUtc = agora,
            };
            db.ProjetosCompartilhados.Add(projeto);
        }
        else if (!RegrasDeAcesso.PodeEditar(RegrasDeAcesso.Papel(escopo, projeto)))
        {
            var quem = await db.Usuarios.AsNoTracking().Where(u => u.Id == escopo.UsuarioId).Select(u => u.Login).FirstOrDefaultAsync(ct);
            var papel = RegrasDeAcesso.Papel(escopo, projeto);
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Proibida,
                $"O usuário {quem ?? $"#{escopo.UsuarioId}"} não pode alterar, aprovar ou disparar o projeto {projeto.Nome}: papel atual {(papel.Length == 0 ? "sem acesso" : papel)}; é exigido editor ou dono.");
        }
        else if (envio.Versao != projeto.Versao)
        {
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito, envio.Versao is null
                ? $"O projeto {envio.Slug} já existe neste escopo (v{projeto.Versao}): leia-o e envie com a versão."
                : $"O projeto {envio.Slug} mudou (v{projeto.Versao}, você leu a v{envio.Versao}): releia e envie de novo.");
        }

        var anteriorProjeto = novo ? null : Leitura(escopo, projeto);
        var anteriores = anteriorProjeto?.Itens ?? [];
        if (EscolhasDosProjetos.RebaixaReferencia(anteriorProjeto?.FluxoPadrao, envio.FluxoPadrao)
            || (envio.Itens ?? []).Any(i => EscolhasDosProjetos.RebaixaReferencia(anteriorProjeto?.Itens.FirstOrDefault(a => a.Numero == i.Numero)?.Fluxo, i.Fluxo)))
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito,
                "A escolha do fluxo tem identidade e revisão portáteis: preserve a referência ou selecione explicitamente outra referência compatível. Um ID local/nome de cliente antigo não pode sobrescrevê-la.");
        if (envio.PoliticaDeFinalizacao is { } recebida && anteriorProjeto?.PoliticaDeFinalizacao is { } politicaAnterior
            && recebida != politicaAnterior && recebida.Revisao <= politicaAnterior.Revisao)
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito,
                "A política da finalização mudou: releia a revisão atual antes de reenviar.");

        // Campos opcionais ausentes em clientes antigos não apagam a definição portátil.
        envio = envio with
        {
            BaseDoBranch = string.IsNullOrEmpty(envio.BaseDoBranch) ? anteriorProjeto?.BaseDoBranch ?? envio.BaseDoBranch : envio.BaseDoBranch,
            FluxoPadrao = envio.FluxoPadrao ?? anteriorProjeto?.FluxoPadrao,
            Agente = envio.Agente ?? anteriorProjeto?.Agente,
            Modelo = envio.Modelo ?? anteriorProjeto?.Modelo,
            Modelos = envio.Modelos ?? anteriorProjeto?.Modelos,
            PoliticaDeFinalizacao = envio.PoliticaDeFinalizacao ?? anteriorProjeto?.PoliticaDeFinalizacao,
            FeatureId = envio.FeatureId > 0 ? envio.FeatureId : anteriorProjeto?.FeatureId ?? 0,
            EpicId = envio.EpicId > 0 ? envio.EpicId : anteriorProjeto?.EpicId ?? 0,
            Iteracao = !string.IsNullOrEmpty(envio.Iteracao) ? envio.Iteracao : anteriorProjeto?.Iteracao ?? "",
            Board = !string.IsNullOrEmpty(envio.Board) ? envio.Board : anteriorProjeto?.Board ?? "",
            BranchesDasTasks = envio.BranchesDasTasks ?? anteriorProjeto?.BranchesDasTasks,
        };
        // O número muda no replanejamento. A proposta acompanha a identidade do item,
        // como no desktop/CLI; o número fica como reserva para renomeações de clientes antigos.
        ItemDoProjetoV1? AnteriorDoDesign(ItemDoProjetoV1 item)
            => anteriores.FirstOrDefault(a => item.WorkItemId > 0 && a.WorkItemId == item.WorkItemId)
                ?? anteriores.FirstOrDefault(a => string.Equals(a.Titulo.Trim(), item.Titulo.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? anteriores.FirstOrDefault(a => a.Numero == item.Numero);
        var enviados = (envio.Itens ?? []).Select(i =>
        {
            var anterior = AnteriorDoDesign(i);
            return i with
            {
                Design = i.Design ?? anterior?.Design,
                Fluxo = i.Fluxo ?? anterior?.Fluxo ?? "",
                Agente = i.Agente ?? anterior?.Agente,
                Modelo = i.Modelo ?? anterior?.Modelo,
                WorkItemId = i.WorkItemId > 0 ? i.WorkItemId : anterior?.WorkItemId ?? 0,
                Branch = i.Branch.Length > 0 ? i.Branch : anterior?.Branch ?? "",
                Horas = i.AtualizadoEm is null && i.Horas == 0 ? anterior?.Horas ?? 0 : i.Horas,
                VersaoDaEdicaoDoPlano = envio.EdicaoPeloBoard && i.AtualizadoEm != anterior?.AtualizadoEm
                    ? projeto.Versao + 1 : anterior?.VersaoDaEdicaoDoPlano ?? 0,
            };
        }).ToArray();
        foreach (var item in enviados)
        {
            var anterior = AnteriorDoDesign(item);
            var recusa = DesignDosItens.Mudanca(anterior?.Design, item.Design);
            if (recusa.Length > 0)
                return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, $"Item {item.Numero}: {recusa}");
            if (anterior is not null && envio.Situacao == ProjetosV1.Aprovado
                && JsonSerializer.Serialize(anterior.Design) != JsonSerializer.Serialize(item.Design))
                return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, "Alterar o design exige devolver o plano à aprovação (situação aguardando).");
        }
        var itens = anteriores.Where(i => enviados.All(n => n.Numero != i.Numero))
            .Concat(enviados).OrderBy(i => i.Numero).ToArray();
        var erroDoPlano = RegrasDosProjetos.Validar(envio with { Itens = itens });
        if (erroDoPlano.Length > 0)
            return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada, erroDoPlano);
        envio = envio with { Itens = itens };
        if (!novo && envio.PoliticaDeFinalizacao?.Politica == "finalizacao-unica")
        {
            try { await PlanoEfetivoDaFinalizacao.ConferirAsync(db, projeto.Id, envio, ct); }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            { return ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Conflito, "Transferência da validação: " + ex.Message); }
        }
        projeto.Documento = JsonSerializer.Serialize(envio);
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

        // A lista consolidada preserva os itens ausentes do envio mais recente.
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
        return new(new ProjetoGravado(await LeituraEfetivaAsync(escopo, gravado, ct), novo));
    }

    public async Task<ResultadoDaGestao<ProjetoV1>> ApagarAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
    {
        var projeto = await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (projeto is null)
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        if (RegrasDeAcesso.Papel(escopo, projeto) != AcessoV1.PapelDono)
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.Proibida, "Só o dono do projeto o apaga.");

        var leitura = Leitura(escopo, projeto);
        await db.AcessosAosProjetos.Where(a => a.ProjetoEmOndasId == id).ExecuteDeleteAsync(ct);
        await db.ItensDeProjetoCompartilhado.Where(i => i.ProjetoId == id).ExecuteDeleteAsync(ct);
        await db.ProjetosCompartilhados.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return new(leitura);
    }

    /// <summary>Os projetos que o escopo vê: a regra do design (<see cref="RegrasDoDesign.Visivel"/>), escrita para o banco.</summary>
    private IQueryable<ProjetoCompartilhado> Visiveis(EscopoDoDesign escopo, string? time)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        return db.ProjetosCompartilhados.AsNoTracking().Include(p => p.Itens).Include(p => p.Autor).Include(p => p.Acessos)
            .Visiveis(escopo)
            .Where(p => p.Nivel != NiveisDeConvencao.Time || doTime == string.Empty || p.Time == doTime);
    }

    private async Task<ProjetoV1> LeituraEfetivaAsync(EscopoDoDesign escopo, ProjetoCompartilhado p, CancellationToken ct)
    {
        var leitura = Leitura(escopo, p);
        if (leitura.PoliticaDeFinalizacao?.Politica != "finalizacao-unica") return leitura;
        try
        {
            var documento = JsonSerializer.Deserialize<EnvioDeProjetoV1>(p.Documento) ?? throw new JsonException("Documento ausente.");
            var plano = await PlanoEfetivoDaFinalizacao.ConferirAsync(db, p.Id, documento, ct);
            return leitura with { ItensDoGrafo = plano.Itens.Select(i => i.Numero).ToArray() };
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { return leitura with { ItensDoGrafo = [], PendenciaDoGrafo = "Plano operacional bloqueado: " + ex.Message }; }
    }

    private static ProjetoV1 Leitura(EscopoDoDesign escopo, ProjetoCompartilhado p)
    {
        var documento = p.Documento.Length == 0 ? null : JsonSerializer.Deserialize<EnvioDeProjetoV1>(p.Documento);
        return new ProjetoV1(
        p.Id, p.Slug, p.Nome, p.Nivel, p.Time, RegrasDosProjetos.Lista(p.Repositorios), p.Branch, p.SemanaConvertida, p.Definicao,
        RegrasDosProjetos.Lista(p.Anexos), p.Autonomia, p.Paralelismo, p.DestinoDaPr, p.Situacao, p.Versao,
        p.Autor is null ? string.Empty : p.Autor.Nome.Length > 0 ? p.Autor.Nome : p.Autor.Login, DevKitPageDb.Utc(p.AtualizadoEmUtc),
        p.Itens.OrderBy(i => i.Numero).Select(i => new ItemDoProjetoV1(
            i.Numero, i.Titulo, i.Descricao, i.Fluxo, RegrasDosProjetos.Lista(i.Repositorios), RegrasDosProjetos.Lista(i.Componentes),
            RegrasDosProjetos.Dependencias(i.Dependencias), i.Onda, i.WorkItemId, i.Situacao)).ToArray(),
        RegrasDeAcesso.Papel(escopo, p) == AcessoV1.PapelDono,
        documento?.FeatureId ?? 0, documento?.EpicId ?? 0, documento?.Iteracao ?? "", documento?.Board ?? "",
        documento?.BranchesDasTasks, documento?.AtualizadoEm, RegrasDeAcesso.Papel(escopo, p),
        documento?.BaseDoBranch ?? "", documento?.FluxoPadrao, documento?.Agente, documento?.Modelo, documento?.Modelos,
        documento?.PoliticaDeFinalizacao) with
        {
            Itens = documento?.Itens ?? p.Itens.OrderBy(i => i.Numero).Select(i => new ItemDoProjetoV1(
                i.Numero, i.Titulo, i.Descricao, i.Fluxo, RegrasDosProjetos.Lista(i.Repositorios),
                RegrasDosProjetos.Lista(i.Componentes), RegrasDosProjetos.Dependencias(i.Dependencias),
                i.Onda, i.WorkItemId, i.Situacao)).ToArray()
        };
    }
}
