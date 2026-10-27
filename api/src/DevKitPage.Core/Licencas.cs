using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O que uma licença assinada diz (US #425) — o payload do <c>DKL2</c>, em JSON camelCase. É o MESMO JSON que o
/// <c>LicencaAssinada</c> do git.kit lê: um campo novo é opcional, e mudar um nome quebra as máquinas instaladas.
/// </summary>
/// <param name="Versao">A versão do formato (2).</param>
/// <param name="Tipo">Um de <see cref="TiposDeLicenca"/>.</param>
/// <param name="Maquina">O id anônimo da máquina a que a licença foi emitida.</param>
/// <param name="Usuario">O login de quem renovou; nulo no trial.</param>
public sealed record ConteudoDaLicenca(int Versao, string Tipo, string Maquina, string? Usuario, DateTimeOffset EmitidaEm, DateTimeOffset ValidaAte);

/// <summary>
/// A licença assinada do dev.kit (US #425): <c>DKL2.&lt;payload&gt;.&lt;assinatura&gt;</c>, os dois em base64url, a
/// assinatura Ed25519 sobre os BYTES do payload. Substitui o <c>DKL1</c> (AES-GCM com chave simétrica no binário,
/// que só impedia a edição casual): a chave privada fica só na configuração da API (<c>Licencas__ChavePrivada</c>), e o app só tem a pública.
/// </summary>
public static class LicencaAssinada
{
    /// <summary>O prefixo do formato.</summary>
    public const string Prefixo = "DKL2";

    /// <summary>A versão do payload.</summary>
    public const int VersaoDoFormato = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A licença assinada com a <paramref name="semente"/> (a chave privada Ed25519).</summary>
    public static string Emitir(ConteudoDaLicenca conteudo, ReadOnlySpan<byte> semente)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(conteudo, Json);
        return $"{Prefixo}.{Base64Url(payload)}.{Base64Url(Ed25519.Assinar(semente, payload))}";
    }

    /// <summary>O conteúdo da licença, se a assinatura confere com a <paramref name="chavePublica"/>; nulo em qualquer outro caso.</summary>
    public static ConteudoDaLicenca? Ler(string? texto, ReadOnlySpan<byte> chavePublica)
    {
        var partes = (texto ?? string.Empty).Trim().Split('.');
        if (partes.Length != 3 || partes[0] != Prefixo)
            return null;
        try
        {
            var payload = DeBase64Url(partes[1]);
            if (!Ed25519.Verificar(chavePublica, payload, DeBase64Url(partes[2])))
                return null;
            var conteudo = JsonSerializer.Deserialize<ConteudoDaLicenca>(payload, Json);
            return conteudo is { Versao: VersaoDoFormato } ? conteudo : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DeBase64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}

/// <summary>Por que a renovação foi recusada — a API traduz em status HTTP e mensagem própria.</summary>
public enum RecusaDaRenovacao
{
    Nenhuma,

    /// <summary>O usuário foi bloqueado pelo gestor ou pelo admin (403).</summary>
    UsuarioBloqueado,

    /// <summary>
    /// O usuário não é de nenhuma empresa — quem renova é quem assina (403). Desde o bug do trial (26/10) a regra não a
    /// devolve mais: o usuário sem empresa é o do TESTE, e renova até o fim dele (<see cref="TesteEncerrado"/>).
    /// </summary>
    SemEmpresa,

    /// <summary>A assinatura da empresa foi cancelada no gateway (402).</summary>
    AssinaturaCancelada,

    /// <summary>A licença atual ainda não chegou ao dia da renovação (409).</summary>
    CedoDemais,

    /// <summary>A empresa do dev não tem assento para ele (403).</summary>
    SemAssento,

    /// <summary>A validade que o admin definiu (ou a do contrato da empresa) passou (US #687): "Bloqueado: licença" (403).</summary>
    LicencaVencida,

    /// <summary>O admin revogou a licença do usuário (US #687, 403).</summary>
    LicencaRevogada,

    /// <summary>O teste grátis do usuário sem empresa terminou e ninguém definiu outra validade para ele (403).</summary>
    TesteEncerrado,
}

/// <summary>
/// A licença do dev.kit de um usuário como a regra a vê (US #687): o tipo, a validade que a renovação respeita (a definida
/// pelo admin para ele, ou a do contrato da empresa) e as marcas do admin.
/// </summary>
/// <param name="ValidaAte">O fim do último dia da validade; nulo sem prazo, revogada ou sem validade definida por ninguém.</param>
/// <param name="DefinidaPeloAdmin">O admin definiu a licença DESTE usuário (validade, sem prazo ou revogação).</param>
public sealed record LicencaDoUsuario(string Tipo, DateTimeOffset? ValidaAte, bool SemPrazo, bool Revogada, bool DefinidaPeloAdmin);

/// <summary>
/// As regras puras da licença assinada (US #425, fase 4): o trial, a validade e a janela de renovação. Desde a US #687,
/// também a validade que o ADMIN define por usuário — que a renovação respeita: ela nunca emite além da validade, recusa
/// a vencida e a revogada, e abre a janela quando o admin mudou a licença depois da última emissão.
/// </summary>
public static partial class RegrasDeLicenca
{
    /// <summary>O teto de dias que o admin soma de uma vez (dez anos): mais que isto é engano de digitação.</summary>
    public const int DiasMaximosDeUmaVez = 3650;

    /// <summary>O tamanho máximo do motivo no histórico.</summary>
    public const int TamanhoMaximoDoMotivo = 300;

    /// <summary>Os dias do teste grátis (o padrão de <see cref="OpcoesDeLicenca.TrialDias"/>).</summary>
    public const int DiasDoTeste = 14;

    /// <summary>A licença atual pode ser renovada? Só a partir do <paramref name="aPartirDoDia"/>º dia da emissão.</summary>
    public static bool NaJanela(DateTimeOffset emitidaEm, DateTimeOffset agora, int aPartirDoDia)
        => agora >= emitidaEm.AddDays(Math.Max(0, aPartirDoDia));

    /// <summary>
    /// A janela da renovação com a VALIDADE da licença (US #687): a partir do <paramref name="aPartirDoDia"/>º dia da emissão
    /// OU a menos de <paramref name="aPartirDoDia"/> dias do fim. Sem a segunda metade, a licença curta que o admin definiu
    /// (3 dias) venceria antes do 7º dia sem que a máquina pedisse a renovação. É a MESMA regra do
    /// <c>LicencaDoDevKit.NaJanela</c> do git.kit — travada pelo contrato <c>ContratoDaRenovacao/casos.json</c>.
    /// </summary>
    public static bool NaJanela(DateTimeOffset emitidaEm, DateTimeOffset validaAte, DateTimeOffset agora, int aPartirDoDia)
        => NaJanela(emitidaEm, agora, aPartirDoDia) || agora >= validaAte.AddDays(-Math.Max(0, aPartirDoDia));

    /// <summary>
    /// A recusa da renovação, ou <see cref="RecusaDaRenovacao.Nenhuma"/>. A <paramref name="atual"/> é a licença que a
    /// máquina apresentou (já conferida): a de assinatura fora da janela é cedo demais — a menos que o admin tenha mudado
    /// a licença (do usuário ou o contrato da empresa) depois da emissão dela; o trial renova a qualquer hora (é a
    /// primeira licença de quem acabou de entrar). A validade do admin vem antes da empresa: a vencida e a revogada são
    /// recusadas, e a licença definida para um usuário sem empresa renova.
    /// <para>
    /// O usuário SEM empresa e sem licença definida pelo admin é o do teste grátis: renova até o fim do teste
    /// (<paramref name="diasDoTeste"/> dias do cadastro), e depois a recusa é <see cref="RecusaDaRenovacao.TesteEncerrado"/>.
    /// Antes (bug de 26/10) ele caía no <see cref="RecusaDaRenovacao.SemEmpresa"/> e não renovava nunca.
    /// </para>
    /// </summary>
    public static RecusaDaRenovacao Avaliar(
        Usuario usuario, Empresa? empresa, ConteudoDaLicenca? atual, DateTimeOffset agora, int aPartirDoDia, int diasDoTeste = DiasDoTeste)
    {
        if (usuario.BloqueadoPeloAdmin)
            return RecusaDaRenovacao.UsuarioBloqueado;
        var licenca = Licenca(usuario, empresa, diasDoTeste);
        if (licenca.Revogada)
            return RecusaDaRenovacao.LicencaRevogada;
        if (licenca.ValidaAte is { } fim && fim <= agora)
            return empresa is null && !licenca.DefinidaPeloAdmin ? RecusaDaRenovacao.TesteEncerrado : RecusaDaRenovacao.LicencaVencida;
        if (empresa?.AssinaturaCanceladaEmUtc is not null)
            return RecusaDaRenovacao.AssinaturaCancelada;
        if (atual is { Tipo: TiposDeLicenca.Assinatura } && !NaJanela(atual.EmitidaEm, atual.ValidaAte, agora, aPartirDoDia)
            && !AlteradaDepois(usuario, empresa, atual.EmitidaEm))
            return RecusaDaRenovacao.CedoDemais;
        return RecusaDaRenovacao.Nenhuma;
    }

    /// <summary>
    /// Até quando vai a licença emitida agora: os <paramref name="validadeDias"/> de sempre, sem passar da validade do
    /// admin (ou do contrato). A licença continua curta e renovada pelo login — o corte do gestor segue valendo —, e a
    /// data do admin é o teto.
    /// </summary>
    public static DateTimeOffset ValidadeEmitida(LicencaDoUsuario licenca, DateTimeOffset agora, int validadeDias)
    {
        var padrao = agora.AddDays(Math.Max(1, validadeDias));
        return licenca.ValidaAte is { } fim && fim < padrao ? fim : padrao;
    }

    /// <summary>
    /// A licença do usuário: a conta de administração não tem prazo; a revogação e a validade do admin valem antes do
    /// contrato da empresa; sem nada definido, a renovação segue como antes (sem data-limite, a situação automática) — menos
    /// para o usuário sem empresa, que é o do TESTE: a validade dele é o fim do teste, <paramref name="diasDoTeste"/> dias
    /// depois do cadastro (o "começo da licença" do histórico).
    /// </summary>
    public static LicencaDoUsuario Licenca(Usuario usuario, Empresa? empresa, int diasDoTeste = DiasDoTeste)
    {
        if (usuario.EhAdmin || usuario.Papel == Papeis.Admin)
            return new LicencaDoUsuario(TiposDaLicencaDoUsuario.Admin, null, SemPrazo: true, Revogada: false, DefinidaPeloAdmin: false);

        var tipo = usuario.LicencaTipo.Length > 0
            ? usuario.LicencaTipo
            : empresa is null ? TiposDaLicencaDoUsuario.Trial : empresa.Individual ? TiposDaLicencaDoUsuario.Individual : TiposDaLicencaDoUsuario.Empresa;
        if (usuario.LicencaRevogadaEmUtc is not null)
            return new LicencaDoUsuario(tipo, null, false, Revogada: true, DefinidaPeloAdmin: true);
        if (usuario.LicencaSemPrazo)
            return new LicencaDoUsuario(tipo, null, SemPrazo: true, false, DefinidaPeloAdmin: true);
        if (usuario.LicencaValidaAteUtc is { } doAdmin)
            return new LicencaDoUsuario(tipo, Utc(doAdmin), false, false, DefinidaPeloAdmin: true);
        if (empresa?.ContratoAteUtc is { } contrato)
            return new LicencaDoUsuario(tipo, Utc(contrato), false, false, DefinidaPeloAdmin: false);
        if (empresa is null)
            return new LicencaDoUsuario(tipo, FimDoTeste(usuario.CriadoEmUtc, diasDoTeste), false, false, false);
        return new LicencaDoUsuario(tipo, null, false, false, false);
    }

    /// <summary>O fim do teste grátis: o fim do dia do cadastro mais os <paramref name="diasDoTeste"/> (cadastro em 26/10 → até 09/11).</summary>
    public static DateTimeOffset FimDoTeste(DateTime cadastroUtc, int diasDoTeste)
        => FimDoDia(DateOnly.FromDateTime(cadastroUtc).AddDays(Math.Max(1, diasDoTeste)));

    /// <summary>A situação (<see cref="SituacoesDaLicenca"/>) e os dias que faltam, contados por DATA (vence amanhã = 1).</summary>
    public static (string Situacao, int? DiasRestantes) Situacao(LicencaDoUsuario licenca, DateTimeOffset agora)
    {
        if (licenca.Revogada)
            return (SituacoesDaLicenca.Revogada, null);
        if (licenca.SemPrazo)
            return (SituacoesDaLicenca.SemPrazo, null);
        if (licenca.ValidaAte is not { } fim)
            return (SituacoesDaLicenca.Automatica, null);

        var dias = DiasAte(fim, agora);
        var situacao = fim <= agora ? SituacoesDaLicenca.Vencida
            : dias <= SituacoesDaLicenca.DiasDoAviso ? SituacoesDaLicenca.Vencendo
            : SituacoesDaLicenca.Valida;
        return (situacao, dias);
    }

    /// <summary>Os dias entre as DATAS (UTC) de agora e do fim: vence hoje = 0, amanhã = 1, ontem = -1.</summary>
    public static int DiasAte(DateTimeOffset fim, DateTimeOffset agora) => (fim.UtcDateTime.Date - agora.UtcDateTime.Date).Days;

    /// <summary>O fim do dia (UTC): a validade "até 28/10" vale o dia 28 inteiro.</summary>
    public static DateTimeOffset FimDoDia(DateOnly dia) => new(dia.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

    /// <summary>O problema da definição (vazio quando ela pode ser aplicada). Ao aplicar, o motivo é obrigatório.</summary>
    public static string ValidarDefinicao(DefinicaoDeLicencaV1? definicao, bool exigirMotivo, DateTimeOffset agora)
    {
        if (definicao is null)
            return "Informe a validade da licença.";
        if (!ModosDaValidade.Todos.Contains(definicao.Modo ?? string.Empty))
            return $"Modo inválido: use {string.Join(", ", ModosDaValidade.Todos)}.";
        if (definicao.Modo == ModosDaValidade.Somar && definicao.Dias is not (>= 1 and <= DiasMaximosDeUmaVez))
            return $"Informe quantos dias somar: de 1 a {DiasMaximosDeUmaVez}.";
        if (definicao.Modo == ModosDaValidade.AteData)
        {
            if (definicao.Ate is not { } ate)
                return "Informe a data-limite da licença.";
            var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
            if (ate < hoje)
                return "A data já passou: escolha hoje ou uma data futura (para cortar o acesso, use Revogar).";
            if (ate > hoje.AddDays(DiasMaximosDeUmaVez))
                return "A data passa de dez anos: confira o ano.";
        }

        if (definicao.Tipo is { Length: > 0 } tipo && !TiposDaLicencaDoUsuario.Escolhiveis.Contains(tipo))
            return $"Tipo inválido: use {string.Join(", ", TiposDaLicencaDoUsuario.Escolhiveis)}.";
        return ValidarMotivo(definicao.Motivo, exigirMotivo);
    }

    /// <summary>O problema do motivo (obrigatório quando <paramref name="exigir"/>); vazio quando serve.</summary>
    public static string ValidarMotivo(string? motivo, bool exigir)
    {
        var limpo = (motivo ?? string.Empty).Trim();
        if (exigir && limpo.Length == 0)
            return "Informe o motivo: ele vai para o histórico da licença.";
        return limpo.Length > TamanhoMaximoDoMotivo ? $"O motivo tem até {TamanhoMaximoDoMotivo} caracteres." : string.Empty;
    }

    /// <summary>
    /// A validade que a definição produz: sem prazo; até o fim do dia escolhido; ou os dias SOMADOS à validade atual —
    /// a partir de hoje quando ela já passou, não tem data ou foi revogada.
    /// </summary>
    public static (DateTimeOffset? ValidaAte, bool SemPrazo) NovaValidade(DefinicaoDeLicencaV1 definicao, LicencaDoUsuario atual, DateTimeOffset agora)
    {
        if (definicao.Modo == ModosDaValidade.SemPrazo)
            return (null, true);
        if (definicao.Modo == ModosDaValidade.AteData)
            return (FimDoDia(definicao.Ate!.Value), false);

        var partida = atual is { Revogada: false, ValidaAte: { } fim } && fim > agora ? fim : agora;
        return (FimDoDia(DateOnly.FromDateTime(partida.UtcDateTime).AddDays(definicao.Dias!.Value)), false);
    }

    /// <summary>A frase da prévia e do histórico ("validade até 27/11/2026 · individual").</summary>
    public static string Descrever(DateTimeOffset? validaAte, bool semPrazo, string tipo)
        => semPrazo ? $"sem prazo · {tipo}" : $"validade até {validaAte!.Value.UtcDateTime:dd/MM/yyyy} · {tipo}";

    /// <summary>O admin mudou a licença do usuário (ou o contrato da empresa) DEPOIS da emissão da licença apresentada.</summary>
    private static bool AlteradaDepois(Usuario usuario, Empresa? empresa, DateTimeOffset emitidaEm)
        => (usuario.LicencaAlteradaEmUtc is { } doUsuario && Utc(doUsuario) > emitidaEm)
           || (empresa?.AlteradaEmUtc is { } daEmpresa && Utc(daEmpresa) > emitidaEm);

    private static DateTimeOffset Utc(DateTime data) => new(DateTime.SpecifyKind(data, DateTimeKind.Utc));

    /// <summary>A impressão da máquina como o banco a guarda: hex minúsculo de até 64 caracteres; nula quando não é uma.</summary>
    public static string? Impressao(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim().ToLowerInvariant();
        return Hex().IsMatch(limpo) ? limpo : null;
    }

    [GeneratedRegex("^[0-9a-f]{16,64}$")]
    private static partial Regex Hex();
}

/// <summary>
/// As regras da conta do dev (US #425, fase 3): a adesão por código e e-mail, o link de definir a senha e a
/// política de login por empresa.
/// </summary>
public static class RegrasDaConta
{
    /// <summary>O e-mail como se compara e se guarda: sem espaços e em minúsculas.</summary>
    public static string Email(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Os erros por campo da adesão; vazio quando ela pode seguir.</summary>
    public static Dictionary<string, string[]> Validar(AdesaoV1? adesao)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (adesao is null)
        {
            erros["adesao"] = ["O corpo do pedido é obrigatório."];
            return erros;
        }

        if (CodigoDeAdesao.Normalizar(adesao.Codigo).Length == 0)
            erros["codigo"] = ["Informe o código de adesão que o gestor entregou."];
        var email = Email(adesao.Email);
        if (email.Length is 0 or > ValidadorDeDemonstracao.TamanhoMaximoDoEmail || !ValidadorDeDemonstracao.EmailValido(email))
            erros["email"] = ["Informe um e-mail válido."];
        if ((adesao.Nome ?? string.Empty).Trim().Length > ValidadorDeUsuario.TamanhoMaximoDoNome)
            erros["nome"] = [$"O nome tem até {ValidadorDeUsuario.TamanhoMaximoDoNome} caracteres."];
        return erros;
    }

    /// <summary>O token do link de definir a senha (vai no e-mail) e o hash dele (vai para o banco) — a mesma geração da chave da máquina.</summary>
    public static (string Token, string Hash) TokenDoLink() => ChaveDeMaquina.Gerar();

    /// <summary>O link do e-mail: a raiz do site (sem barra no fim) e o token na query.</summary>
    public static string Link(string urlDoSite, string token)
        => $"{(urlDoSite ?? string.Empty).TrimEnd('/')}/definir-senha?token={Uri.EscapeDataString(token)}";

    /// <summary>A política de login que vale: a da empresa, quando ela escolheu; senão o padrão global.</summary>
    public static bool LoginObrigatorio(bool? daEmpresa, bool global) => daEmpresa ?? global;
}

/// <summary>
/// A verificação do aviso do gateway de pagamento (US #425, fase 5): o cabeçalho <c>X-Gateway-Assinatura</c> é
/// <c>t=&lt;unix&gt;,v1=&lt;hmac&gt;</c>, o HMAC-SHA256 do segredo sobre <c>"&lt;t&gt;.&lt;corpo&gt;"</c> — o esquema do Stripe, que
/// amarra o corpo ao instante e impede repetir um aviso velho.
/// </summary>
public static class RegrasDoGateway
{
    /// <summary>O cabeçalho da assinatura.</summary>
    public const string Cabecalho = "X-Gateway-Assinatura";

    /// <summary>O cabeçalho de um corpo assinado com o segredo, no instante dado (o que o gateway — e o teste — enviam).</summary>
    public static string Assinar(string segredo, string corpo, DateTimeOffset em)
    {
        var t = em.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"t={t},v1={Hmac(segredo, t, corpo)}";
    }

    /// <summary>A assinatura confere com o segredo e o carimbo está dentro da tolerância? Sem segredo, nada confere.</summary>
    public static bool Valida(string? cabecalho, string segredo, string corpo, DateTimeOffset agora, int toleranciaSegundos)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrWhiteSpace(cabecalho))
            return false;

        string? t = null, v1 = null;
        foreach (var parte in cabecalho.Split(','))
        {
            var par = parte.Trim().Split('=', 2);
            if (par.Length != 2)
                continue;
            if (par[0] == "t")
                t = par[1];
            else if (par[0] == "v1")
                v1 = par[1];
        }

        if (t is null || v1 is null || !long.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var segundos))
            return false;
        if (Math.Abs(agora.ToUnixTimeSeconds() - segundos) > Math.Max(1, toleranciaSegundos))
            return false;
        return ChaveDeMaquina.Iguais(Hmac(segredo, t, corpo), v1.ToLowerInvariant());
    }

    private static string Hmac(string segredo, string t, string corpo)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes($"{t}.{corpo}")));
}
