using System.Globalization;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>Uma única gestão de acessos para design e projetos em ondas.</summary>
public sealed class AcessosAosProjetos(DevKitPageDb db, TimeProvider relogio) : IAcessosAosProjetos
{
    public async Task<ResultadoDaGestao<AcessosDoProjetoV1>> ListarAsync(EscopoDoDesign escopo, string recurso, long projeto, CancellationToken ct)
    {
        var achado = await ComAcessosAsync(recurso, projeto, ct);
        return achado is null || RegrasDeAcesso.Papel(escopo, achado).Length == 0
            ? ProjetoNaoEncontrado()
            : new(await LeituraDosAcessosAsync(escopo, recurso, achado, ct));
    }

    public async Task<ResultadoDaGestao<AcessosDoProjetoV1>> ConcederAsync(EscopoDoDesign escopo, string recurso, long projeto, ConcessaoDeAcessoV1 pedido, CancellationToken ct)
    {
        var (achado, falha) = await DoDonoAsync(escopo, recurso, projeto, ct);
        if (falha is not null)
            return falha;
        var papel = RegrasDoDesign.PapelDeAcesso(pedido.Papel);
        if (papel.Length == 0)
            return PapelInvalido();
        var (alvo, semAlvo) = await UsuarioDaEmpresaAsync(achado!, pedido.Usuario, ct);
        if (semAlvo is not null)
            return semAlvo;
        if (alvo!.Id == achado!.DonoId)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada, $"{alvo.Login} já é o dono do projeto.");
        if (achado.Acessos.FirstOrDefault(a => a.UsuarioId == alvo.Id) is { } existente)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Conflito,
                $"{alvo.Login} já tem acesso ao projeto, como {existente.Papel}: altere o papel em vez de conceder de novo.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        achado.Acessos.Add(new AcessoAoProjeto { UsuarioId = alvo.Id, Papel = papel, ConcedidoPorId = escopo.UsuarioId, ConcedidoEmUtc = agora });
        // O projeto "mudou" para quem ganhou o acesso: a sincronização incremental dele (?desde=) passa a trazê-lo.
        achado.AtualizadoEmUtc = agora;
        await db.SaveChangesAsync(ct);
        return new(await LeituraDosAcessosAsync(escopo, recurso, achado, ct));
    }

    public async Task<ResultadoDaGestao<AcessosDoProjetoV1>> AlterarAsync(EscopoDoDesign escopo, string recurso, long projeto, int usuario, string? papel, CancellationToken ct)
    {
        var (achado, falha) = await DoDonoAsync(escopo, recurso, projeto, ct);
        if (falha is not null)
            return falha;
        var novo = RegrasDoDesign.PapelDeAcesso(papel);
        if (novo.Length == 0)
            return PapelInvalido();
        if (usuario == achado!.DonoId)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada,
                "O dono não tem papel a alterar: para passar o projeto a outra pessoa, transfira o dono.");
        if (achado.Acessos.FirstOrDefault(a => a.UsuarioId == usuario) is not { } acesso)
            return SemAcesso(usuario);

        acesso.Papel = novo;
        achado.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new(await LeituraDosAcessosAsync(escopo, recurso, achado, ct));
    }

    public async Task<ResultadoDaGestao<AcessosDoProjetoV1>> RemoverAsync(EscopoDoDesign escopo, string recurso, long projeto, int usuario, CancellationToken ct)
    {
        var (achado, falha) = await DoDonoAsync(escopo, recurso, projeto, ct);
        if (falha is not null)
            return falha;
        if (usuario == achado!.DonoId)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada,
                "O dono não se remove do projeto: para sair dele, transfira o dono a outra pessoa antes.");
        if (achado.Acessos.FirstOrDefault(a => a.UsuarioId == usuario) is not { } acesso)
            return SemAcesso(usuario);

        achado.Acessos.Remove(acesso);
        db.AcessosAosProjetos.Remove(acesso);
        await db.SaveChangesAsync(ct);
        return new(await LeituraDosAcessosAsync(escopo, recurso, achado, ct));
    }

    public async Task<ResultadoDaGestao<AcessosDoProjetoV1>> TransferirAsync(EscopoDoDesign escopo, string recurso, long projeto, TransferenciaDeDonoV1 pedido, CancellationToken ct)
    {
        var (achado, falha) = await DoDonoAsync(escopo, recurso, projeto, ct);
        if (falha is not null)
            return falha;
        if (!pedido.Confirmar)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada,
                "A transferência do dono exige a confirmação explícita (confirmar: true): o dono novo passa a decidir quem tem acesso, e você fica editor.");
        var (alvo, semAlvo) = await UsuarioDaEmpresaAsync(achado!, pedido.Usuario, ct);
        if (semAlvo is not null)
            return semAlvo;
        if (alvo!.Id == achado!.DonoId)
            return ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada, $"{alvo.Login} já é o dono do projeto.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (achado.Acessos.FirstOrDefault(a => a.UsuarioId == alvo.Id) is { } doNovo)
        {
            achado.Acessos.Remove(doNovo);
            db.AcessosAosProjetos.Remove(doNovo);
        }

        achado.Acessos.Add(new AcessoAoProjeto { UsuarioId = achado.DonoId, Papel = DesignV1.PapelEditor, ConcedidoPorId = escopo.UsuarioId, ConcedidoEmUtc = agora });
        achado.DonoId = alvo.Id;
        achado.AtualizadoEmUtc = agora;
        await db.SaveChangesAsync(ct);
        return new(await LeituraDosAcessosAsync(escopo, recurso, achado, ct));
    }

    /// <summary>O projeto com os acessos, rastreado (a gestão grava nele); nulo quando não existe.</summary>
    private async Task<IProjetoComAcessos?> ComAcessosAsync(string recurso, long projeto, CancellationToken ct)
        => recurso switch
        {
            AcessoV1.RecursoDesign => await db.ProjetosDeDesign.Include(p => p.Acessos).FirstOrDefaultAsync(p => p.Id == projeto, ct),
            AcessoV1.RecursoProjeto => await db.ProjetosCompartilhados.Include(p => p.Acessos).FirstOrDefaultAsync(p => p.Id == projeto, ct),
            _ => null,
        };

    /// <summary>O projeto que o escopo GERE: sem acesso, 404 (sem revelar que existe); com acesso sem ser o dono, 403 com o motivo.</summary>
    private async Task<(IProjetoComAcessos? Projeto, ResultadoDaGestao<AcessosDoProjetoV1>? Falha)> DoDonoAsync(EscopoDoDesign escopo, string recurso, long projeto, CancellationToken ct)
    {
        var achado = await ComAcessosAsync(recurso, projeto, ct);
        var papel = achado is null ? string.Empty : RegrasDeAcesso.Papel(escopo, achado);
        if (achado is null || papel.Length == 0)
            return (null, ProjetoNaoEncontrado());
        if (papel != DesignV1.PapelDono)
        {
            var usuarioDono = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == achado.DonoId, ct);
            var dono = usuarioDono is null ? string.Empty : usuarioDono.Nome.Length > 0 ? usuarioDono.Nome : usuarioDono.Login;
            return (null, ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Proibida,
                $"Só o dono do projeto{(dono.Length > 0 ? $" ({dono})" : string.Empty)} concede, altera e remove acesso; você tem o papel {papel}."));
        }

        return (achado, null);
    }

    /// <summary>O usuário da EMPRESA do projeto pelo login, pelo e-mail ou pelo id; a falha diz por que não há.</summary>
    private async Task<(Usuario? Usuario, ResultadoDaGestao<AcessosDoProjetoV1>? Falha)> UsuarioDaEmpresaAsync(IProjetoComAcessos projeto, string? quem, CancellationToken ct)
    {
        var alvo = quem?.Trim() ?? string.Empty;
        if (alvo.Length == 0)
            return (null, ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada, "Informe o usuário (o login, o e-mail ou o id)."));
        if (projeto.EmpresaId is not { } empresa)
            return (null, ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada,
                "Este projeto é de quem não tem empresa: o acesso ao projeto só se dá a usuários da mesma empresa."));

        var minusculo = alvo.ToLowerInvariant();
        var id = int.TryParse(alvo, NumberStyles.None, CultureInfo.InvariantCulture, out var lido) ? lido : 0;
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.EmpresaId == empresa && (u.Id == id || u.Login.ToLower() == minusculo || u.Email.ToLower() == minusculo))
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct);
        return usuario is null
            ? (null, ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado,
                $"O usuário {alvo} não está na empresa do projeto: o acesso ao projeto é só para quem é da mesma empresa."))
            : (usuario, null);
    }

    private async Task<AcessosDoProjetoV1> LeituraDosAcessosAsync(EscopoDoDesign escopo, string recurso, IProjetoComAcessos projeto, CancellationToken ct)
    {
        var ids = projeto.Acessos.Select(a => a.UsuarioId).Append(projeto.DonoId).Distinct().ToArray();
        var usuarios = await db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var papel = RegrasDeAcesso.Papel(escopo, projeto);
        var acessos = projeto.Acessos
            .Select(a => new AcessoConcedidoV1(Resumo(usuarios.GetValueOrDefault(a.UsuarioId), a.UsuarioId), a.Papel, DevKitPageDb.Utc(a.ConcedidoEmUtc)))
            .OrderBy(a => a.Usuario.Login, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<UsuarioDoAcessoV1> candidatos = [];
        if (papel == DesignV1.PapelDono && projeto.EmpresaId is { } empresa)
        {
            var fora = ids;
            candidatos = (await db.Usuarios.AsNoTracking()
                    .Where(u => u.EmpresaId == empresa && !u.BloqueadoPeloAdmin && !fora.Contains(u.Id))
                    .OrderBy(u => u.Login)
                    .ToListAsync(ct))
                .Select(u => Resumo(u, u.Id))
                .ToArray();
        }

        var dono = projeto.DonoId > 0 ? Resumo(usuarios.GetValueOrDefault(projeto.DonoId), projeto.DonoId) : null;
        return new AcessosDoProjetoV1(recurso, projeto.Id, projeto.NomeDoAcesso, dono, papel, acessos, candidatos);
    }

    private static UsuarioDoAcessoV1 Resumo(Usuario? usuario, int id)
        => usuario is null ? new(id, $"#{id}", string.Empty) : new(usuario.Id, usuario.Login, usuario.Nome);

    private static ResultadoDaGestao<AcessosDoProjetoV1> ProjetoNaoEncontrado()
        => ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");

    private static ResultadoDaGestao<AcessosDoProjetoV1> PapelInvalido()
        => ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.Recusada,
            $"O papel é '{DesignV1.PapelLeitor}' (vê o projeto) ou '{DesignV1.PapelEditor}' (também altera).");

    private static ResultadoDaGestao<AcessosDoProjetoV1> SemAcesso(int usuario)
        => ResultadoDaGestao<AcessosDoProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado, $"O usuário #{usuario} não tem acesso a este projeto.");
}
