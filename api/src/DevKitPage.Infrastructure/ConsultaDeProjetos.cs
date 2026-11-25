using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class ConsultaDeProjetos(DevKitPageDb db) : IConsultaDeProjetos
{
    public async Task<IReadOnlyList<ResumoDoProjetoV1>> ListarResumosAsync(
        EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        var consulta = db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo)
            .Where(p => p.Nivel != NiveisDeConvencao.Time || doTime == "" || p.Time == doTime);
        if (desde is { } marco)
        {
            var corte = marco.UtcDateTime;
            consulta = consulta.Where(p => p.AtualizadoEmUtc > corte);
        }

        // Só escalares: nenhuma entidade ou coluna Documento/Descricao passa pelo leitor.
        var linhas = await consulta.OrderBy(p => p.Slug).ThenBy(p => p.Nivel).ThenBy(p => p.Time)
            .Select(p => new
            {
                p.Id, p.Slug, p.Nome, p.Nivel, p.Time, p.Situacao, p.Versao, p.AtualizadoEmUtc,
                Papel = p.DonoId == escopo.UsuarioId ? "dono"
                    : p.Acessos.Where(a => a.UsuarioId == escopo.UsuarioId).Select(a => a.Papel).FirstOrDefault() ?? "",
                Total = p.Itens.Count,
                Concluidos = p.Itens.Count(i => i.Situacao == "integrado" || i.Situacao == "concluido-manual" || i.Situacao == "cancelado"),
                Executando = p.Itens.Count(i => i.Situacao == "rodando" || i.Situacao == "solicitado" || i.Situacao == "aguardando-confirmacao"),
                Perguntas = p.Itens.Count(i => i.Situacao == "pergunta"),
                Falhos = p.Itens.Count(i => i.Situacao == "falhou"),
            }).ToListAsync(ct);
        return linhas.Select(p => new ResumoDoProjetoV1(p.Id, p.Slug, p.Nome, p.Nivel, p.Time,
            p.Situacao, p.Versao, DevKitPageDb.Utc(p.AtualizadoEmUtc), p.Papel,
            p.Total, p.Concluidos, p.Executando, p.Perguntas, p.Falhos)).ToArray();
    }
}
