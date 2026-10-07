using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As empresas do plano empresarial e os gestores delas (US #381). O gestor é um <see cref="Usuario"/>
/// com o papel <c>gestor</c> — o MESMO login, o mesmo JWT e a mesma troca obrigatória de senha do
/// admin, sem um segundo fluxo de autenticação.
/// </summary>
public sealed class Empresas(DevKitPageDb db, IPasswordHasher<Usuario> hasher, TimeProvider relogio) : IEmpresas
{
    public async Task<EmpresaResumo> CriarAsync(EmpresaNova empresa, CancellationToken ct)
    {
        // O código é aleatório; a colisão (32^8) é improvável, mas o índice único é quem garante.
        var codigo = CodigoDeAdesao.Gerar();
        while (await db.Empresas.AnyAsync(e => e.CodigoDeAdesao == codigo, ct))
            codigo = CodigoDeAdesao.Gerar();

        var nova = new Empresa
        {
            Nome = empresa.Nome.Trim(),
            Plano = empresa.Plano.Trim(),
            Assentos = empresa.Assentos,
            CodigoDeAdesao = codigo,
            CriadaEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        db.Empresas.Add(nova);
        await db.SaveChangesAsync(ct);
        return new EmpresaResumo(nova.Id, nova.Nome, nova.Plano, nova.Assentos, nova.CodigoDeAdesao, 0, DevKitPageDb.Utc(nova.CriadaEmUtc));
    }

    public async Task<IReadOnlyList<EmpresaResumo>> ListarAsync(CancellationToken ct)
    {
        var ocupados = await db.Maquinas
            .Where(m => m.EmpresaId != null && m.ConsentiuEmUtc != null)
            .GroupBy(m => m.EmpresaId!.Value)
            .Select(g => new { Empresa = g.Key, Quantas = g.Count() })
            .ToDictionaryAsync(x => x.Empresa, x => x.Quantas, ct);

        return (await db.Empresas.AsNoTracking().OrderBy(e => e.Nome).ToListAsync(ct))
            .Select(e => new EmpresaResumo(e.Id, e.Nome, e.Plano, e.Assentos, e.CodigoDeAdesao, ocupados.GetValueOrDefault(e.Id), DevKitPageDb.Utc(e.CriadaEmUtc)))
            .ToArray();
    }

    public async Task<(GestorCriado? Gestor, string Erro)> CriarGestorAsync(int empresaId, string login, CancellationToken ct)
    {
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId, ct))
            return (null, string.Empty);

        var limpo = login.Trim();
        if (await db.Usuarios.AnyAsync(u => u.Login == limpo, ct))
            return (null, $"Já existe um usuário '{limpo}'.");

        var senha = InicializadorDaBase.SenhaAleatoria();
        var gestor = new Usuario
        {
            Login = limpo,
            EhAdmin = false,
            Papel = Papeis.Gestor,
            EmpresaId = empresaId,
            DeveTrocarSenha = true,
            CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        gestor.SenhaHash = hasher.HashPassword(gestor, senha);
        db.Usuarios.Add(gestor);
        await db.SaveChangesAsync(ct);
        return (new GestorCriado(gestor.Id, gestor.Login, senha), string.Empty);
    }
}

/// <summary>A trilha de auditoria: uma linha por acesso aos dados dos colaboradores (a exportação).</summary>
public sealed class AuditoriaDeAcesso(DevKitPageDb db, TimeProvider relogio) : IAuditoriaDeAcesso
{
    public async Task RegistrarAsync(int usuarioId, string login, EscopoDoPainel escopo, string oQue, int linhas, CancellationToken ct)
    {
        db.AcessosAosDados.Add(new AcessoAosDados
        {
            UsuarioId = usuarioId,
            Login = login.Length <= ValidadorDeEmpresa.TamanhoMaximoDoLogin ? login : login[..ValidadorDeEmpresa.TamanhoMaximoDoLogin],
            EmpresaId = escopo.EmpresaId,
            OQue = oQue.Length <= 500 ? oQue : oQue[..500],
            Linhas = linhas,
            EmUtc = relogio.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>O expurgo da trilha de auditoria além de <see cref="OpcoesDeTelemetria.RetencaoDias"/>, na volta diária.</summary>
public sealed class ExpurgoDeAcessos(DevKitPageDb db, IOptions<OpcoesDeTelemetria> opcoes, TimeProvider relogio) : IExpurgoDeAcessos
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.AcessosAosDados.Where(a => a.EmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
