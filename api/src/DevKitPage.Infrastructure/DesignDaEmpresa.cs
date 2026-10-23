using System.Security.Cryptography;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O design compartilhado (US #669): os projetos por repositório no banco e as imagens no <see cref="IArmazemDeImagens"/>.
/// O isolamento é o <see cref="EscopoDoDesign"/>, aplicado em <see cref="Visiveis"/> e conferido de novo por
/// <see cref="RegrasDoDesign.Visivel"/> — o projeto que não é visível é 404, sem revelar se existe.
/// </summary>
public sealed class DesignDaEmpresa(DevKitPageDb db, IArmazemDeImagens armazem, IOptions<OpcoesDoDesign> opcoes, TimeProvider relogio)
    : IDesignDaEmpresa
{
    public async Task<IReadOnlyList<ProjetoDeDesignV1>> ProjetosAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct)
    {
        var consulta = Visiveis(escopo, time);
        if (desde is { } marco)
        {
            var corte = marco.UtcDateTime;
            consulta = consulta.Where(p => p.AtualizadoEmUtc > corte);
        }

        var projetos = await consulta.OrderBy(p => p.Chave).ThenBy(p => p.Nivel).ThenBy(p => p.Time).ToListAsync(ct);
        return await ParaLeituraAsync(escopo, projetos, ct);
    }

    public async Task<ProjetoDeDesignV1?> ProjetoAsync(EscopoDoDesign escopo, long projeto, CancellationToken ct)
    {
        var achado = await Visiveis(escopo, null).FirstOrDefaultAsync(p => p.Id == projeto, ct);
        return achado is null ? null : (await ParaLeituraAsync(escopo, [achado], ct))[0];
    }

    public async Task<ResultadoDoEnvio> EnviarAsync(EscopoDoDesign escopo, EnvioDeVersaoDeDesign envio, byte[] png, CancellationToken ct)
    {
        var nivel = RegrasDoDesign.NivelEfetivo(escopo, envio.Nivel);
        var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time) : string.Empty;
        if (nivel == NiveisDeConvencao.Time && time.Length == 0)
            return new(SituacaoDoEnvio.Recusada, null, "Informe o time do design de nível time (a Equipe das Configurações do dev.kit).");

        var espaco = RegrasDoDesign.EspacoDe(escopo);
        var dono = nivel == NiveisDeConvencao.Usuario ? escopo.UsuarioId : 0;
        var repositorio = envio.Repositorio!.Trim();
        var chave = DesignV1.ChaveDoRepositorio(repositorio);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var sha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        var (largura, altura) = RegrasDoDesign.DimensoesDoPng(png)!.Value;

        var projeto = await db.ProjetosDeDesign.Include(p => p.Telas).ThenInclude(t => t.Versoes)
            .FirstOrDefaultAsync(p => p.Espaco == espaco && p.Nivel == nivel && p.Time == time && p.UsuarioId == dono && p.Chave == chave, ct);
        var tela = projeto?.Telas.FirstOrDefault(t => t.Identificador == envio.Tela);
        var numero = envio.Numero ?? (tela?.Versoes.Count > 0 ? tela.Versoes.Max(v => v.Numero) + 1 : 1);

        // A idempotência: o mesmo número com o mesmo PNG é o reenvio da fila (200); com outro PNG, conflito (409).
        if (tela?.Versoes.FirstOrDefault(v => v.Numero == numero) is { } existente)
            return existente.Sha256 == sha
                ? new(SituacaoDoEnvio.Repetida, await EnviadaAsync(escopo, projeto!, tela, existente, false, ct))
                : new(SituacaoDoEnvio.Conflito, null, $"A tela {envio.Tela} já tem a v{numero:D3} com outra imagem: o dev.kit não sobrescreve versão.");

        var plano = escopo.EmpresaId is { } empresa
            ? await db.Empresas.AsNoTracking().Where(e => e.Id == empresa).Select(e => e.Plano).FirstOrDefaultAsync(ct) ?? string.Empty
            : string.Empty;
        var (versoes, bytes) = await UsoDoEspacoAsync(espaco, ct);
        var teto = RegrasDoDesign.TetoDo(opcoes.Value, plano);
        var estouro = RegrasDoDesign.ConferirTeto(teto, versoes, bytes, png.Length, NomeDoPlano(plano));
        if (estouro.Length > 0)
            return new(SituacaoDoEnvio.Teto, null, estouro);

        if (projeto is null)
        {
            projeto = new ProjetoDeDesign
            {
                EmpresaId = escopo.EmpresaId, Espaco = espaco, Nivel = nivel, Time = time, UsuarioId = dono,
                Repositorio = repositorio, Chave = chave, CriadoEmUtc = agora,
            };
            db.ProjetosDeDesign.Add(projeto);
        }

        if (tela is null)
        {
            tela = new TelaDeDesign { Identificador = envio.Tela!, Nome = Texto(envio.Nome, envio.Tela!), Estado = Texto(envio.Estado, string.Empty) };
            projeto.Telas.Add(tela);
        }
        else
        {
            // O nome e o estado são os da versão mais nova que os trouxe.
            tela.Nome = Texto(envio.Nome, tela.Nome);
            tela.Estado = envio.Estado is null ? tela.Estado : envio.Estado.Trim();
        }

        projeto.AtualizadoEmUtc = agora;
        var versao = new VersaoDeDesign
        {
            Numero = numero, Tipo = envio.Tipo!, Base = envio.Base, Tema = envio.Tema ?? "light", Largura = largura, Altura = altura,
            Origem = envio.Origem!, Alvo = Texto(envio.Alvo, string.Empty), Branch = Texto(envio.Branch, string.Empty),
            Commit = Texto(envio.Commit, string.Empty), Descricao = Texto(envio.Descricao, string.Empty), AutorId = escopo.UsuarioId,
            AutorTipo = envio.AutorTipo ?? DesignV1.AutorUsuario, Runner = Texto(envio.Runner, string.Empty),
            CriadaEmUtc = envio.CriadaEm?.UtcDateTime ?? agora, EnviadaEmUtc = agora, Sha256 = sha, Bytes = png.Length,
        };
        tela.Versoes.Add(versao);

        try
        {
            // O projeto ganha o id antes da imagem: a chave dela leva o número do projeto.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois envios do mesmo número ao mesmo tempo: o índice único decide, e o perdedor lê o que ficou.
            db.ChangeTracker.Clear();
            return await DepoisDaCorridaAsync(escopo, espaco, nivel, time, dono, chave, envio.Tela!, numero, sha, ct);
        }

        versao.ChaveDaImagem = RegrasDoDesign.ChaveDaImagem(espaco, projeto.Id, tela.Identificador, numero, sha);
        try
        {
            await armazem.GravarAsync(versao.ChaveDaImagem, png, ct);
        }
        catch
        {
            // Sem a imagem, a versão não fica: o reenvio da fila grava as duas de novo.
            tela.Versoes.Remove(versao);
            db.VersoesDeDesign.Remove(versao);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        await db.SaveChangesAsync(ct);
        return new(SituacaoDoEnvio.Nova, await EnviadaAsync(escopo, projeto, tela, versao, true, ct));
    }

    public async Task<ImagemDeDesign?> ImagemAsync(EscopoDoDesign escopo, long projeto, string tela, int numero, CancellationToken ct)
    {
        var versao = await VersaoVisivelAsync(escopo, projeto, tela, numero, ct);
        if (versao is null)
            return null;
        var conteudo = await armazem.LerAsync(versao.ChaveDaImagem, ct);
        return conteudo is null ? null : new ImagemDeDesign(conteudo, versao.Sha256, DevKitPageDb.Utc(versao.CriadaEmUtc));
    }

    public async Task<ResultadoDaGestao<VersaoDeDesignV1>> ApagarAsync(EscopoDoDesign escopo, long projeto, string tela, int numero, CancellationToken ct)
    {
        var versao = await VersaoVisivelAsync(escopo, projeto, tela, numero, ct, rastrear: true);
        if (versao is null)
            return ResultadoDaGestao<VersaoDeDesignV1>.Falhou(FalhaNaGestao.NaoEncontrado, $"A versão v{numero:D3} da tela {tela} não existe neste projeto.");
        var daTela = versao.Tela!;
        var dono = daTela.Projeto!;
        if (!RegrasDoDesign.PodeApagar(escopo, versao.AutorId, dono.EmpresaId))
            return ResultadoDaGestao<VersaoDeDesignV1>.Falhou(FalhaNaGestao.Proibida, "Só quem enviou a versão, ou o gestor da empresa, a apaga.");

        var leitura = Leitura(escopo, dono, daTela, versao, Nome(versao.Autor));
        db.VersoesDeDesign.Remove(versao);
        // A tela sem versão nenhuma sai junto: o dev.kit não guarda tela vazia.
        if (!await db.VersoesDeDesign.AnyAsync(v => v.TelaId == daTela.Id && v.Id != versao.Id, ct))
            db.TelasDeDesign.Remove(daTela);
        dono.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await armazem.ApagarAsync(versao.ChaveDaImagem, ct);
        return new(leitura);
    }

    public async Task<UsoDoDesignV1> UsoAsync(EscopoDoDesign escopo, CancellationToken ct)
    {
        var plano = escopo.EmpresaId is { } empresa
            ? await db.Empresas.AsNoTracking().Where(e => e.Id == empresa).Select(e => e.Plano).FirstOrDefaultAsync(ct) ?? string.Empty
            : string.Empty;
        var (versoes, bytes) = await UsoDoEspacoAsync(RegrasDoDesign.EspacoDe(escopo), ct);
        var teto = RegrasDoDesign.TetoDo(opcoes.Value, plano);
        return new UsoDoDesignV1(versoes, teto.Versoes, bytes, teto.MegaBytes * 1024L * 1024L, NomeDoPlano(plano));
    }

    /// <summary>
    /// Os projetos que o escopo vê, num ponto só: o nível usuário do dono; o empresa e o time na empresa dele (o time só o
    /// declarado, quando veio). É a mesma regra de <see cref="RegrasDoDesign.Visivel"/>, escrita para o banco.
    /// </summary>
    private IQueryable<ProjetoDeDesign> Visiveis(EscopoDoDesign escopo, string? time)
    {
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        var empresa = escopo.EmpresaId;
        return db.ProjetosDeDesign.AsNoTracking()
            .Include(p => p.Telas).ThenInclude(t => t.Versoes).ThenInclude(v => v.Autor)
            .Where(p => (p.Nivel == NiveisDeConvencao.Usuario && p.UsuarioId == escopo.UsuarioId)
                || (empresa != null && p.EmpresaId == empresa
                    && (p.Nivel == NiveisDeConvencao.Empresa || (p.Nivel == NiveisDeConvencao.Time && (doTime == string.Empty || p.Time == doTime)))));
    }

    /// <summary>A versão de um projeto visível, com a tela, o projeto e o autor; nula quando não há.</summary>
    private async Task<VersaoDeDesign?> VersaoVisivelAsync(EscopoDoDesign escopo, long projeto, string tela, int numero, CancellationToken ct, bool rastrear = false)
    {
        if (!RegrasDoDesign.IdDeTelaValido(tela))
            return null;
        var versoes = rastrear ? db.VersoesDeDesign : db.VersoesDeDesign.AsNoTracking();
        var versao = await versoes.Include(v => v.Autor).Include(v => v.Tela!).ThenInclude(t => t.Projeto)
            .FirstOrDefaultAsync(v => v.Numero == numero && v.Tela!.Identificador == tela && v.Tela.ProjetoId == projeto, ct);
        var dono = versao?.Tela?.Projeto;
        return dono is not null && RegrasDoDesign.Visivel(escopo, dono.Nivel, dono.EmpresaId, dono.Time, dono.UsuarioId, null) ? versao : null;
    }

    /// <summary>Quantas versões e quantos bytes o espaço já guarda.</summary>
    private async Task<(int Versoes, long Bytes)> UsoDoEspacoAsync(string espaco, CancellationToken ct)
    {
        var doEspaco = db.VersoesDeDesign.AsNoTracking().Where(v => v.Tela!.Projeto!.Espaco == espaco);
        var versoes = await doEspaco.CountAsync(ct);
        var bytes = versoes == 0 ? 0 : await doEspaco.SumAsync(v => v.Bytes, ct);
        return (versoes, bytes);
    }

    /// <summary>O envio que perdeu a corrida pelo índice único: o mesmo PNG é a repetida; outro, o conflito.</summary>
    private async Task<ResultadoDoEnvio> DepoisDaCorridaAsync(
        EscopoDoDesign escopo, string espaco, string nivel, string time, int dono, string chave, string tela, int numero, string sha, CancellationToken ct)
    {
        var projeto = await db.ProjetosDeDesign.AsNoTracking().Include(p => p.Telas).ThenInclude(t => t.Versoes).ThenInclude(v => v.Autor)
            .FirstOrDefaultAsync(p => p.Espaco == espaco && p.Nivel == nivel && p.Time == time && p.UsuarioId == dono && p.Chave == chave, ct);
        var daTela = projeto?.Telas.FirstOrDefault(t => t.Identificador == tela);
        if (daTela?.Versoes.FirstOrDefault(v => v.Numero == numero) is { } ganhou && ganhou.Sha256 == sha)
            return new(SituacaoDoEnvio.Repetida, await EnviadaAsync(escopo, projeto!, daTela, ganhou, false, ct));
        return new(SituacaoDoEnvio.Conflito, null, $"A v{numero:D3} da tela {tela} acabou de ser enviada por outra pessoa. Confira e envie de novo.");
    }

    private async Task<VersaoEnviadaV1> EnviadaAsync(EscopoDoDesign escopo, ProjetoDeDesign projeto, TelaDeDesign tela, VersaoDeDesign versao, bool nova, CancellationToken ct)
    {
        var autor = versao.Autor ?? await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == versao.AutorId, ct);
        return new VersaoEnviadaV1(projeto.Id, projeto.Repositorio, tela.Identificador, Leitura(escopo, projeto, tela, versao, Nome(autor)), nova);
    }

    private async Task<IReadOnlyList<ProjetoDeDesignV1>> ParaLeituraAsync(EscopoDoDesign escopo, IReadOnlyList<ProjetoDeDesign> projetos, CancellationToken ct)
    {
        var donos = projetos.Where(p => p.UsuarioId > 0).Select(p => p.UsuarioId).Distinct().ToArray();
        var nomes = await db.Usuarios.AsNoTracking().Where(u => donos.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome.Length > 0 ? u.Nome : u.Login, ct);

        return projetos.Select(p => new ProjetoDeDesignV1(
            p.Id, p.Repositorio, p.Chave, p.Nivel, p.Time, p.UsuarioId > 0 ? nomes.GetValueOrDefault(p.UsuarioId, string.Empty) : string.Empty,
            DevKitPageDb.Utc(p.AtualizadoEmUtc),
            p.Telas.Where(t => t.Versoes.Count > 0).OrderBy(t => t.Identificador, StringComparer.Ordinal)
                .Select(t => new TelaDeDesignV1(t.Identificador, t.Nome, t.Estado,
                    t.Versoes.OrderBy(v => v.Numero).Select(v => Leitura(escopo, p, t, v, Nome(v.Autor))).ToArray()))
                .ToArray())).ToArray();
    }

    private static VersaoDeDesignV1 Leitura(EscopoDoDesign escopo, ProjetoDeDesign projeto, TelaDeDesign tela, VersaoDeDesign v, string autor) => new(
        v.Numero, v.Tipo, v.Base, v.Tema, v.Largura, v.Altura, v.Origem, v.Alvo, v.Branch, v.Commit, v.Descricao, autor, v.AutorTipo,
        v.Runner, DevKitPageDb.Utc(v.CriadaEmUtc), v.Sha256, v.Bytes, DesignV1.Link(projeto.Id, tela.Identificador, v.Numero),
        RegrasDoDesign.PodeApagar(escopo, v.AutorId, projeto.EmpresaId));

    private static string Nome(Usuario? usuario) => usuario is null ? string.Empty : usuario.Nome.Length > 0 ? usuario.Nome : usuario.Login;

    private static string NomeDoPlano(string plano) => plano.Length > 0 ? plano : "individual";

    private static string Texto(string? valor, string padrao) => string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
}
