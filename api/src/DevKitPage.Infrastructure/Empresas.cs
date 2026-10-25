using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As empresas do plano empresarial e os gestores delas (US #381). O gestor é um <see cref="Usuario"/>
/// com o papel <c>gestor</c> — o MESMO login, o mesmo JWT e a mesma troca obrigatória de senha do
/// admin, sem um segundo fluxo de autenticação. Desde a US #405 o convite é a criação de usuário da
/// <see cref="IGestaoDeUsuarios"/> com o papel <c>gestor</c>: um caminho só para criar usuário e gerar a
/// senha temporária.
/// </summary>
public sealed class Empresas(DevKitPageDb db, IGestaoDeUsuarios usuarios, TimeProvider relogio) : IEmpresas
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
        return new EmpresaResumo(nova.Id, nova.Nome, nova.Plano, nova.Assentos, nova.CodigoDeAdesao, 0, DevKitPageDb.Utc(nova.CriadaEmUtc), Dados: Dados(nova));
    }

    public async Task<ResultadoDaGestao<EmpresaResumo>> EditarAsync(int empresaId, EmpresaEditadaV1 edicao, string autor, CancellationToken ct)
    {
        if (await db.Empresas.FirstOrDefaultAsync(e => e.Id == empresaId, ct) is not { } empresa)
            return ResultadoDaGestao<EmpresaResumo>.Falhou(FalhaNaGestao.NaoEncontrado, "Empresa não encontrada.");

        // Os assentos não descem abaixo dos em uso (a MESMA conta do login e da licença): quem está dentro fica.
        var emUso = await Assentos.OcupadosAsync(db, empresaId, ct);
        if (RegrasDaEmpresa.ValidarAssentos(edicao.Assentos, emUso) is { Length: > 0 } problema)
            return ResultadoDaGestao<EmpresaResumo>.Falhou(FalhaNaGestao.Recusada, problema);

        empresa.Nome = edicao.Nome.Trim();
        empresa.Plano = edicao.Plano.Trim();
        empresa.Assentos = edicao.Assentos;
        empresa.Cnpj = RegrasDaEmpresa.Cnpj(edicao.Cnpj);
        empresa.Segmento = Limpo(edicao.Segmento);
        empresa.Cidade = Limpo(edicao.Cidade);
        empresa.Uf = Limpo(edicao.Uf).ToUpperInvariant();
        empresa.Contato = Limpo(edicao.Contato);
        empresa.Telefone = Limpo(edicao.Telefone);
        empresa.Site = Limpo(edicao.Site);
        // O contrato vale o dia inteiro, como a validade da licença que ele limita.
        empresa.ContratoAteUtc = edicao.ContratoAte is { } ate ? RegrasDeLicenca.FimDoDia(ate).UtcDateTime : null;
        empresa.AlteradaEmUtc = relogio.GetUtcNow().UtcDateTime;
        empresa.AlteradaPor = autor.Length <= ValidadorDeEmpresa.TamanhoMaximoDoLogin ? autor : autor[..ValidadorDeEmpresa.TamanhoMaximoDoLogin];
        await db.SaveChangesAsync(ct);
        return new((await ListarAsync(ct)).First(e => e.Id == empresaId));
    }

    /// <summary>Os dados básicos e o contrato como o contrato v1 os leva (o CNPJ só com os dígitos).</summary>
    internal static DadosDaEmpresaV1 Dados(Empresa e)
        => new(e.Cnpj, e.Segmento, e.Cidade, e.Uf, e.Contato, e.Telefone, e.Site,
            e.ContratoAteUtc is { } ate ? DateOnly.FromDateTime(ate) : null, e.AlteradaEmUtc is { } em ? DevKitPageDb.Utc(em) : null, e.AlteradaPor);

    private static string Limpo(string? texto) => (texto ?? string.Empty).Trim();

    public async Task<IReadOnlyList<EmpresaResumo>> ListarAsync(CancellationToken ct)
    {
        var colaboradores = await db.Maquinas
            .Where(m => m.EmpresaId != null && m.ConsentiuEmUtc != null)
            .GroupBy(m => m.EmpresaId!.Value)
            .Select(g => new { Empresa = g.Key, Quantas = g.Count() })
            .ToDictionaryAsync(x => x.Empresa, x => x.Quantas, ct);

        // A tela de Empresas (US #461) mostra o gestor e os assentos pela MESMA conta da adesão e da licença
        // (Assentos.Ocupantes: os devs ativos), e não pelas máquinas.
        var ocupados = new Dictionary<int, int>();
        var empresas = await db.Empresas.AsNoTracking().OrderBy(e => e.Nome).ToListAsync(ct);
        foreach (var empresa in empresas)
            ocupados[empresa.Id] = await Assentos.OcupadosAsync(db, empresa.Id, ct);
        var gestores = (await db.Usuarios.AsNoTracking()
                .Where(u => u.Papel == Papeis.Gestor && u.EmpresaId != null)
                .OrderBy(u => u.Id)
                .Select(u => new { Empresa = u.EmpresaId!.Value, u.Nome, u.Login })
                .ToListAsync(ct))
            .GroupBy(g => g.Empresa)
            .ToDictionary(g => g.Key, g => g.First().Nome.Length > 0 ? g.First().Nome : g.First().Login);

        return empresas
            .Select(e => new EmpresaResumo(e.Id, e.Nome, e.Plano, e.Assentos, e.CodigoDeAdesao, colaboradores.GetValueOrDefault(e.Id), DevKitPageDb.Utc(e.CriadaEmUtc),
                gestores.GetValueOrDefault(e.Id), ocupados.GetValueOrDefault(e.Id), Dados(e)))
            .ToArray();
    }

    public async Task<(GestorCriado? Gestor, string Erro)> CriarGestorAsync(int empresaId, string login, CancellationToken ct)
    {
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId, ct))
            return (null, string.Empty);

        var criado = await usuarios.CriarAsync(new UsuarioNovo(login, string.Empty, Papeis.Gestor, empresaId), ct);
        return criado.Valor is { } gestor
            ? (new GestorCriado(gestor.Usuario.Id, gestor.Usuario.Login, gestor.SenhaTemporaria), string.Empty)
            : (null, criado.Mensagem);
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
