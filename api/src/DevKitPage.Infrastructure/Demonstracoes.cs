using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os pedidos de demonstração da landing. Grava só o que o visitante informou (já validado pela
/// API) e o instante do consentimento; a leitura e a exclusão são do dashboard autenticado.
/// </summary>
public sealed class PedidosDeDemonstracao(DevKitPageDb db, TimeProvider relogio) : IPedidosDeDemonstracao
{
    public async Task<PedidoDeDemonstracaoCriadoV1> RegistrarAsync(PedidoDeDemonstracaoV1 pedido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var novo = new PedidoDeDemonstracao
        {
            Nome = (pedido.Nome ?? string.Empty).Trim(),
            Email = (pedido.Email ?? string.Empty).Trim(),
            Empresa = (pedido.Empresa ?? string.Empty).Trim(),
            Mensagem = (pedido.Mensagem ?? string.Empty).Trim(),
            ConsentimentoEmUtc = agora,
            RecebidoEmUtc = agora,
        };
        db.PedidosDeDemonstracao.Add(novo);
        await db.SaveChangesAsync(ct);
        return new PedidoDeDemonstracaoCriadoV1(novo.Id, DevKitPageDb.Utc(novo.RecebidoEmUtc));
    }

    public async Task<Pagina<DemonstracaoResumo>> ListarAsync(int pagina, int tamanho, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);

        var consulta = db.PedidosDeDemonstracao.AsNoTracking();
        var total = await consulta.CountAsync(ct);
        var linhas = await consulta
            .OrderByDescending(p => p.RecebidoEmUtc).ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .ToListAsync(ct);

        var itens = linhas
            .Select(p => new DemonstracaoResumo(
                p.Id, p.Nome, p.Email, p.Empresa, p.Mensagem, DevKitPageDb.Utc(p.RecebidoEmUtc), DevKitPageDb.Utc(p.ConsentimentoEmUtc)))
            .ToArray();
        return new Pagina<DemonstracaoResumo>(itens, total, pagina, tamanho);
    }

    public async Task<bool> ExcluirAsync(long id, CancellationToken ct)
        => await db.PedidosDeDemonstracao.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;
}

/// <summary>
/// O expurgo dos pedidos além de <see cref="OpcoesDeDemonstracao.RetencaoDias"/> (LGPD: o contato
/// não fica guardado para sempre). Roda na mesma volta do expurgo dos eventos.
/// </summary>
public sealed class ExpurgoDePedidos(DevKitPageDb db, IOptions<OpcoesDeDemonstracao> opcoes, TimeProvider relogio) : IExpurgoDePedidos
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.PedidosDeDemonstracao.Where(p => p.RecebidoEmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
