namespace DevKitPage.Core;

/// <summary>
/// A autenticação do usuário (seção <c>Jwt</c>). O segredo NÃO é versionado: vem de variável de
/// ambiente (<c>Jwt__Segredo</c>), User Secrets ou Key Vault — e a API não sobe em Production sem ele.
/// </summary>
public sealed class OpcoesDeAutenticacao
{
    public const string Secao = "Jwt";

    /// <summary>O tamanho mínimo do segredo (HMAC-SHA256 pede 256 bits).</summary>
    public const int TamanhoMinimoDoSegredo = 32;

    public string Segredo { get; set; } = string.Empty;
    public string Emissor { get; set; } = "dev.kit.page";
    public string Audiencia { get; set; } = "dev.kit.page";
    public int ExpiracaoMinutos { get; set; } = 60;

    /// <summary>Falhas de login seguidas antes do bloqueio.</summary>
    public int MaxFalhas { get; set; } = 5;

    public int BloqueioMinutos { get; set; } = 15;
}

/// <summary>A ingestão e a retenção (seção <c>Telemetria</c>).</summary>
public sealed class OpcoesDeTelemetria
{
    public const string Secao = "Telemetria";

    /// <summary>Por quantos dias o evento bruto é guardado. Os totais diários ficam para sempre.</summary>
    public int RetencaoDias { get; set; } = 180;

    /// <summary>Acima disto o lote é recusado com 413 (o dev.kit manda 200 por lote).</summary>
    public int MaxEventosPorLote { get; set; } = 1000;

    /// <summary>O corpo máximo do lote, em bytes (413 acima disto).</summary>
    public long MaxBytesPorLote { get; set; } = 1_048_576;

    /// <summary>
    /// O código que uma máquina apresenta para se registrar sem token de admin. Vazio desliga o
    /// registro por código (só o admin registra). Segredo: fora do repositório, como o JWT.
    /// </summary>
    public string CodigoDeRegistro { get; set; } = string.Empty;

    /// <summary>
    /// O teto GLOBAL de ocorrências de exceção guardadas (com o trace), somando todos os grupos (US #381):
    /// além das 20 por grupo, uma máquina que gere assinaturas sem fim não enche a base. Passou, saem as
    /// mais antigas; os grupos e as contagens (nos totais diários) ficam.
    /// </summary>
    public int MaxOcorrenciasGuardadas { get; set; } = 10_000;
}

/// <summary>O formulário público de pedido de demonstração (seção <c>Demonstracoes</c>).</summary>
public sealed class OpcoesDeDemonstracao
{
    public const string Secao = "Demonstracoes";

    /// <summary>Por quantos dias o pedido é guardado (LGPD): depois disso o expurgo diário o apaga.</summary>
    public int RetencaoDias { get; set; } = 365;

    /// <summary>Quantos pedidos um mesmo IP de cliente envia por minuto; acima disto, 429.</summary>
    public int LimitePorMinuto { get; set; } = 5;
}

/// <summary>O painel empresarial (seção <c>Painel</c>, US #381).</summary>
public sealed class OpcoesDoPainel
{
    public const string Secao = "Painel";

    /// <summary>
    /// Quantas exportações (e criações de empresa ou gestor) um mesmo usuário faz por minuto; acima
    /// disto, 429. Coletar os dados dos colaboradores é legítimo, mas não em laço.
    /// </summary>
    public int ExportacoesPorMinuto { get; set; } = 10;
}

/// <summary>
/// O proxy na frente da API (seção <c>Proxy</c>). No Azure Container Apps todo visitante chega com o
/// IP do proxy: o IP do cliente vem no <c>X-Forwarded-For</c>, aceito SÓ de quem está nestas redes.
/// </summary>
public sealed class OpcoesDoProxy
{
    public const string Secao = "Proxy";

    /// <summary>As redes (CIDR, ex.: <c>100.100.0.0/16</c>) dos proxies confiáveis. Vazio: só o loopback.</summary>
    public string[] RedesConfiaveis { get; set; } = [];
}

/// <summary>A base embarcada (seção <c>Banco</c>): o provider é configuração, não código.</summary>
public sealed class OpcoesDoBanco
{
    public const string Secao = "Banco";
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";

    /// <summary><c>Sqlite</c> (padrão, base embarcada) ou <c>SqlServer</c> (Azure SQL).</summary>
    public string Provider { get; set; } = Sqlite;
}

/// <summary>A semente da base (seção <c>Seed</c>).</summary>
public sealed class OpcoesDaSemente
{
    public const string Secao = "Seed";

    public string AdminLogin { get; set; } = "admin";

    /// <summary>
    /// A senha inicial do admin (variável <c>Seed__AdminPassword</c> ou User Secrets). Vazia, a API
    /// gera uma aleatória e a escreve UMA vez no log da primeira subida.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;
}
