using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas do ACESSO ao projeto de design, sob <c>/api/design/projetos/{id}</c>: quem tem acesso lê a lista; só o dono
/// concede, muda o papel, remove e transfere (os outros recebem 403 com o motivo; quem não tem acesso, 404).
/// <list type="bullet">
/// <item><c>GET /acessos</c>: o dono, os acessos com o papel, o papel de quem consulta e, para o dono, os candidatos;</item>
/// <item><c>POST /acessos</c> (<see cref="ConcessaoDeAcessoV1"/>): concede leitor ou editor a um usuário da empresa (409 se já tem);</item>
/// <item><c>PUT /acessos/{usuario}</c> (<see cref="ConcessaoDeAcessoV1"/>, só o papel): muda o papel;</item>
/// <item><c>DELETE /acessos/{usuario}</c>: tira o acesso (o dono não se remove: 400);</item>
/// <item><c>POST /dono</c> (<see cref="TransferenciaDeDonoV1"/>): transfere o dono, só com <c>confirmar: true</c>.</item>
/// </list>
/// </summary>
public static class EndpointsDoAcessoAoDesign
{
    public static void MapearAcessosDoDesign(this RouteGroupBuilder grupo)
    {
        grupo.MapGet("/projetos/{id:long}/acessos", (long id, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => EndpointsDaUs669.ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.AcessosAsync(escopo, id, ct))));

        grupo.MapPost("/projetos/{id:long}/acessos", (long id, ConcessaoDeAcessoV1 pedido, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => EndpointsDaUs669.ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.ConcederAsync(escopo, id, pedido, ct))));

        grupo.MapPut("/projetos/{id:long}/acessos/{usuario:int}", (long id, int usuario, ConcessaoDeAcessoV1 pedido, ClaimsPrincipal quem,
                IDesignDaEmpresa design, CancellationToken ct)
            => EndpointsDaUs669.ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.AlterarPapelAsync(escopo, id, usuario, pedido.Papel, ct))));

        grupo.MapDelete("/projetos/{id:long}/acessos/{usuario:int}", (long id, int usuario, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => EndpointsDaUs669.ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.RemoverAcessoAsync(escopo, id, usuario, ct))));

        grupo.MapPost("/projetos/{id:long}/dono", (long id, TransferenciaDeDonoV1 pedido, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => EndpointsDaUs669.ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.TransferirAsync(escopo, id, pedido, ct))));
    }
}
