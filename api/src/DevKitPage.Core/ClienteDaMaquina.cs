using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A máquina registrada como CLIENTE do client credentials (remote-control-v3 item 9): o serviço do dev.kit pede o token com
/// uma asserção assinada pela chave privada que só a máquina tem, e a API a confere com a <see cref="ChavePublica"/> gravada
/// no vínculo (o login do app). Um cliente por máquina, do usuário que a vinculou por último. No Azure SQL a tabela vem de
/// api/scripts/sqlserver/ClienteDaMaquina.sql.
/// </summary>
public sealed class ClienteDaMaquina
{
    public int Id { get; set; }

    /// <summary>A máquina (o <see cref="Maquina.Id"/> interno); o <see cref="Maquina.MaquinaId"/> dela é o client_id.</summary>
    public int MaquinaId { get; set; }

    public Maquina? Maquina { get; set; }

    /// <summary>O usuário em nome de quem o token da máquina age — e só no remoto dele.</summary>
    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>A chave pública ECDSA P-256 (SubjectPublicKeyInfo em base64).</summary>
    public string ChavePublica { get; set; } = string.Empty;

    /// <summary>O carimbo da senha no vínculo (<see cref="RegrasDoClienteDaMaquina.Carimbo"/>): a senha trocada o invalida.</summary>
    public string Carimbo { get; set; } = string.Empty;

    public DateTime RegistradoEmUtc { get; set; }

    /// <summary>O "desvincular" do site, a senha trocada ou o usuário bloqueado; nulo enquanto o cliente vale.</summary>
    public DateTime? RevogadoEmUtc { get; set; }

    /// <summary>O último token emitido (informativo: o site diz desde quando o serviço usa o vínculo).</summary>
    public DateTime? UltimoTokenEmUtc { get; set; }
}

/// <summary>
/// Um <c>jti</c> já usado numa asserção: a mesma asserção não troca por token duas vezes. Guardado até o fim dela (mais a
/// tolerância do relógio); o expurgo diário leva os vencidos.
/// </summary>
public sealed class AssercaoDoCliente
{
    public long Id { get; set; }
    public int ClienteId { get; set; }
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiraEmUtc { get; set; }
}

/// <summary>O cliente lido para emitir o token: a máquina pública, o usuário e o carimbo atual da senha dele.</summary>
public sealed record ClienteParaEmitir(int Id, int MaquinaId, string ClientId, int UsuarioId, string ChavePublica, string Carimbo);

/// <summary>
/// Os clientes das máquinas: o vínculo, a leitura para emitir, o <c>jti</c> de uso único, a revogação e a conferência a cada
/// chamada do token da máquina. Toda revogação é por linha (máquina) ou por usuário — o token emitido antes dela deixa de
/// valer na chamada seguinte.
/// </summary>
public interface IClientesDaMaquina
{
    /// <summary>Vincula (ou revincula) a máquina ao usuário com a chave pública: o vínculo anterior da máquina é substituído.</summary>
    Task RegistrarAsync(int maquinaId, int usuarioId, string chavePublica, string carimbo, CancellationToken ct);

    /// <summary>O cliente ATIVO do client_id (o id público da máquina); nulo sem vínculo ou revogado.</summary>
    Task<ClienteParaEmitir?> AtivoAsync(string clientId, CancellationToken ct);

    /// <summary>Gasta o <paramref name="jti"/> do cliente; falso quando ele já foi usado (a asserção repetida).</summary>
    Task<bool> GastarAssercaoAsync(int clienteId, string jti, DateTime expiraEmUtc, CancellationToken ct);

    /// <summary>Marca a emissão de um token.</summary>
    Task MarcarEmissaoAsync(int clienteId, CancellationToken ct);

    /// <summary>Revoga o cliente (a senha trocada ou o usuário bloqueado vistos na emissão).</summary>
    Task RevogarAsync(int clienteId, CancellationToken ct);

    /// <summary>O "desvincular" do site: revoga o cliente da máquina pública do usuário; falso quando não há vínculo dele nela.</summary>
    Task<bool> DesvincularAsync(int usuarioId, string maquinaPublica, CancellationToken ct);

    /// <summary>As máquinas públicas do usuário com vínculo ativo.</summary>
    Task<IReadOnlySet<string>> VinculadasAsync(int usuarioId, CancellationToken ct);

    /// <summary>
    /// A máquina (o <see cref="Maquina.Id"/> interno) tem o vínculo REVOGADO? Ela também não renova pela máquina (a t_194): sem
    /// isto, o serviço desvinculado seguiria pela renovação antiga. O login seguinte no app vincula de novo.
    /// </summary>
    Task<bool> RevogadaAsync(int maquinaId, CancellationToken ct);

    /// <summary>
    /// O token da máquina ainda vale? O cliente existe, é do usuário, não foi revogado, e o usuário não trocou a senha, não
    /// foi bloqueado pelo admin nem tem a troca pendente. É a revogação imediata: conferida a cada chamada.
    /// </summary>
    Task<bool> ValeAsync(int clienteId, int usuarioId, CancellationToken ct);
}

/// <summary>A configuração do client credentials da máquina (seção <c>ClienteDaMaquina</c>), conferida na subida.</summary>
public sealed class OpcoesDoClienteDaMaquina
{
    public const string Secao = "ClienteDaMaquina";

    /// <summary>Desligado, o registro e o token respondem 404: o dev.kit não vê o anúncio e segue na renovação pela máquina.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>A validade do token da máquina (1 a 60 min): curta, porque não há renovação — a máquina pede outro.</summary>
    public int ValidadeDoTokenMinutos { get; set; } = 15;

    /// <summary>O maior intervalo entre o <c>iat</c> e o <c>exp</c> da asserção (30 a 600 s).</summary>
    public int ValidadeMaximaDaAssercaoSegundos { get; set; } = 300;

    /// <summary>A tolerância do relógio da máquina (0 a 300 s).</summary>
    public int ToleranciaDoRelogioSegundos { get; set; } = 60;

    /// <summary>O problema da configuração, ou vazio quando ela vale — a API não sobe com ela torta.</summary>
    public string Problema()
        => ValidadeDoTokenMinutos is < 1 or > 60 ? $"{Secao}:ValidadeDoTokenMinutos vai de 1 a 60 (está {ValidadeDoTokenMinutos})."
            : ValidadeMaximaDaAssercaoSegundos is < 30 or > 600 ? $"{Secao}:ValidadeMaximaDaAssercaoSegundos vai de 30 a 600 (está {ValidadeMaximaDaAssercaoSegundos})."
            : ToleranciaDoRelogioSegundos is < 0 or > 300 ? $"{Secao}:ToleranciaDoRelogioSegundos vai de 0 a 300 (está {ToleranciaDoRelogioSegundos})."
            : string.Empty;

    /// <summary>A pré-checagem da subida: a configuração torta derruba a API com a frase do que corrigir.</summary>
    public static void Validar(OpcoesDoClienteDaMaquina opcoes)
    {
        if (opcoes.Problema() is { Length: > 0 } problema)
            throw new InvalidOperationException(problema);
    }
}

/// <summary>As regras puras do cliente da máquina: a chave pública aceita e o carimbo da senha. Sem banco.</summary>
public static class RegrasDoClienteDaMaquina
{
    /// <summary>A chave é um SubjectPublicKeyInfo ECDSA P-256 em base64 (o par que o dev.kit gera)?</summary>
    public static bool ChavePublicaValida(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > ClientCredentialsV1.TamanhoMaximoDaChave)
            return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(chave), out var lidos);
            return lidos == Convert.FromBase64String(chave).Length
                   && ecdsa.ExportParameters(false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>O carimbo da senha: muda quando a senha muda (o hash é outro), e não revela nada dela.</summary>
    public static string Carimbo(string? senhaHash)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(senhaHash ?? string.Empty)))[..16];
}
