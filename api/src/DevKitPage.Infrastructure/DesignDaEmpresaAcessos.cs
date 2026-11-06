using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Infrastructure;

/// <summary>Compatibilidade do contrato de design sobre a gestão compartilhada de acessos.</summary>
public sealed partial class DesignDaEmpresa
{
    public async Task<ResultadoDaGestao<AcessosDoDesignV1>> AcessosAsync(EscopoDoDesign escopo, long projeto, CancellationToken ct)
        => ParaDesign(await new AcessosAosProjetos(db, relogio).ListarAsync(escopo, AcessoV1.RecursoDesign, projeto, ct));

    public async Task<ResultadoDaGestao<AcessosDoDesignV1>> ConcederAsync(EscopoDoDesign escopo, long projeto, ConcessaoDeAcessoV1 pedido, CancellationToken ct)
        => ParaDesign(await new AcessosAosProjetos(db, relogio).ConcederAsync(escopo, AcessoV1.RecursoDesign, projeto, pedido, ct));

    public async Task<ResultadoDaGestao<AcessosDoDesignV1>> AlterarPapelAsync(EscopoDoDesign escopo, long projeto, int usuario, string? papel, CancellationToken ct)
        => ParaDesign(await new AcessosAosProjetos(db, relogio).AlterarAsync(escopo, AcessoV1.RecursoDesign, projeto, usuario, papel, ct));

    public async Task<ResultadoDaGestao<AcessosDoDesignV1>> RemoverAcessoAsync(EscopoDoDesign escopo, long projeto, int usuario, CancellationToken ct)
        => ParaDesign(await new AcessosAosProjetos(db, relogio).RemoverAsync(escopo, AcessoV1.RecursoDesign, projeto, usuario, ct));

    public async Task<ResultadoDaGestao<AcessosDoDesignV1>> TransferirAsync(EscopoDoDesign escopo, long projeto, TransferenciaDeDonoV1 pedido, CancellationToken ct)
        => ParaDesign(await new AcessosAosProjetos(db, relogio).TransferirAsync(escopo, AcessoV1.RecursoDesign, projeto, pedido, ct));

    private static ResultadoDaGestao<AcessosDoDesignV1> ParaDesign(ResultadoDaGestao<AcessosDoProjetoV1> resultado)
    {
        if (resultado.Valor is not { } p)
            return ResultadoDaGestao<AcessosDoDesignV1>.Falhou(resultado.Falha, resultado.Mensagem);
        static UsuarioDoDesignV1 Usuario(UsuarioDoAcessoV1 u) => new(u.Id, u.Login, u.Nome);
        return new(new(p.Projeto, p.Nome, p.Dono is null ? null : Usuario(p.Dono), p.Papel,
            p.Acessos.Select(a => new AcessoAoDesignV1(Usuario(a.Usuario), a.Papel, a.ConcedidoEm)).ToArray(),
            p.Candidatos.Select(Usuario).ToArray()));
    }
}
