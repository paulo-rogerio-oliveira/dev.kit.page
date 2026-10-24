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

    /// <summary>
    /// O teto de ocorrências guardadas POR EMPRESA (as máquinas anônimas contam como um grupo só), antes
    /// do global: uma empresa — ou uma máquina — que gere exceções em laço descarta as dela, e não o
    /// trace das outras.
    /// </summary>
    public int MaxOcorrenciasPorEmpresa { get; set; } = 2_000;
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

/// <summary>Os relatos dos usuários do dev.kit (seção <c>Relatos</c>, US #602).</summary>
public sealed class OpcoesDosRelatos
{
    public const string Secao = "Relatos";

    /// <summary>
    /// Quantos relatos um mesmo usuário envia por hora; acima disto, 429 — e o dev.kit mantém o relato na fila e tenta
    /// de novo depois. Relatar é legítimo, mas não em laço.
    /// </summary>
    public int EnviosPorHora { get; set; } = 20;
}

/// <summary>
/// O design compartilhado (seção <c>Design</c>, US #669): onde as imagens ficam e o teto de cada plano. As imagens vão
/// para o Azure Blob Storage em produção (<see cref="Armazem"/> <c>blob</c>, com a conexão do Key Vault) e para uma pasta
/// em desenvolvimento e nos testes.
/// </summary>
public sealed class OpcoesDoDesign
{
    public const string Secao = "Design";
    public const string Disco = "disco";
    public const string Blob = "blob";

    /// <summary><c>disco</c> (padrão) ou <c>blob</c>.</summary>
    public string Armazem { get; set; } = Disco;

    /// <summary>A pasta das imagens no armazém em disco.</summary>
    public string Pasta { get; set; } = "dados/design";

    /// <summary>A connection string do Storage (segredo: Key Vault ou <c>Design__ConexaoDoBlob</c>).</summary>
    public string ConexaoDoBlob { get; set; } = string.Empty;

    /// <summary>O contêiner das imagens no Storage.</summary>
    public string Conteiner { get; set; } = "design";

    /// <summary>
    /// O teto por PLANO (o <c>Empresa.Plano</c>): quantas versões e quantos MB de imagens o espaço guarda. Ajustável aqui
    /// sem código; o plano fora da lista usa o <see cref="TetoPadrao"/>, e quem não tem empresa também.
    /// </summary>
    public Dictionary<string, TetoDoDesign> Tetos { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Empresarial"] = new() { Versoes = 500, MegaBytes = 2048 },
    };

    /// <summary>O teto de quem não tem empresa e do plano fora de <see cref="Tetos"/>.</summary>
    public TetoDoDesign TetoPadrao { get; set; } = new() { Versoes = 100, MegaBytes = 256 };

    /// <summary>Quantos envios de versão um mesmo usuário faz por minuto; acima disto, 429 (a fila do dev.kit tenta depois).</summary>
    public int EnviosPorMinuto { get; set; } = 60;
}

/// <summary>O teto de um plano no design: a quantidade de versões e o tamanho das imagens.</summary>
public sealed class TetoDoDesign
{
    public int Versoes { get; set; }
    public long MegaBytes { get; set; }
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

/// <summary>
/// A atualização do dev.kit pelas GitHub Releases (seção <c>Atualizacao</c>, US #405). O app e a landing
/// nunca falam com o GitHub: a API consulta a release e faz o proxy do zip, e o token fica SÓ aqui.
/// </summary>
public sealed class OpcoesDeAtualizacao
{
    public const string Secao = "Atualizacao";

    /// <summary>
    /// A variável de ambiente com o PAT do GitHub (leitura do repositório do dev.kit). É a ÚNICA origem
    /// do token: um <c>Atualizacao:Token</c> no appsettings é ignorado (sobrescrito por esta variável),
    /// para o segredo nunca ir parar num arquivo versionado.
    /// </summary>
    public const string VariavelDoToken = "REPO_KEY";

    /// <summary>O repositório das releases, <c>owner/nome</c> — o do dev.kit (git.kit) por padrão.</summary>
    public string Repositorio { get; set; } = "paulo-rogerio-oliveira/git.kit";

    /// <summary>A raiz da API do GitHub (trocada só em teste ou num GitHub Enterprise).</summary>
    public string UrlDaApi { get; set; } = "https://api.github.com/";

    /// <summary>
    /// Por quantos minutos a lista de releases fica em memória: a landing e cada dev.kit perguntam pela
    /// última versão, e o GitHub limita as chamadas por token.
    /// </summary>
    public int CacheMinutos { get; set; } = 5;

    /// <summary>O token, lido de <see cref="VariavelDoToken"/> na composição. Vazio: a atualização responde 503.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Quantos downloads (do dev.kit e do catálogo básico) um mesmo IP faz por hora (US #425): o download é
    /// anônimo desde a reversão da US #405, e o limite por IP é o que o protege de laço.
    /// </summary>
    public int DownloadsPorHora { get; set; } = 20;
}

/// <summary>A política de login do app (US #405), devolvida ao dev.kit e à landing.</summary>
public sealed class OpcoesDeLogin
{
    /// <summary>A variável de ambiente que liga a política (<c>true</c> ou <c>1</c>); desligada por padrão.</summary>
    public const string VariavelDeAmbiente = "DEVKIT_LOGIN_OBRIGATORIO";

    /// <summary>Com ela ligada, o dev.kit pede o login (um usuário daqui) antes de abrir.</summary>
    public bool Obrigatorio { get; set; }

    /// <summary>O valor da variável como booleano: <c>true</c>/<c>1</c> liga; qualquer outra coisa (ou nada) desliga.</summary>
    public static bool Ler(string? valor)
    {
        var texto = (valor ?? string.Empty).Trim();
        return texto == "1" || string.Equals(texto, "true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A conta do dev (seção <c>Conta</c>, US #425): a adesão por código e e-mail e o link de definir a senha.</summary>
public sealed class OpcoesDaConta
{
    public const string Secao = "Conta";

    /// <summary>
    /// A raiz PÚBLICA do site (o Static Web App), de onde sai o link <c>/definir-senha?token=…</c> do e-mail. Vazia,
    /// o link é relativo — serve ao teste, e o e-mail de verdade precisa dela.
    /// </summary>
    public string UrlDoSite { get; set; } = string.Empty;

    /// <summary>Por quantas horas o link de definir a senha vale (uso único).</summary>
    public int ValidadeDoLinkHoras { get; set; } = 24;

    /// <summary>Quantos pedidos anônimos da conta (adesão, link, senha, checkout) um mesmo IP faz por minuto; acima, 429.</summary>
    public int LimitePorMinuto { get; set; } = 10;
}

/// <summary>
/// O envio de e-mail (seção <c>Email</c>, US #425) por SMTP. Sem <see cref="Host"/>, nada sai: o e-mail vai para o
/// log (desenvolvimento) — a senha do SMTP vem do Key Vault, como o segredo do JWT.
/// </summary>
public sealed class OpcoesDeEmail
{
    public const string Secao = "Email";

    public string Host { get; set; } = string.Empty;
    public int Porta { get; set; } = 587;
    public bool Ssl { get; set; } = true;
    public string Usuario { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string Remetente { get; set; } = "dev.kit <nao-responda@devkit.app>";
}

/// <summary>
/// A licença assinada do dev.kit (seção <c>Licencas</c>, US #425): Ed25519, com a chave privada no Key Vault
/// (o segredo <c>Licencas--ChavePrivada</c>, a semente de 32 bytes em base64). A pública vai embutida no app.
/// </summary>
public sealed class OpcoesDeLicenca
{
    public const string Secao = "Licencas";

    /// <summary>A semente Ed25519 em base64. Vazia: as rotas de licença respondem 503 (o app segue com o que tem).</summary>
    public string ChavePrivada { get; set; } = string.Empty;

    /// <summary>A validade da licença de quem assina (dias).</summary>
    public int ValidadeDias { get; set; } = 30;

    /// <summary>O trial, contado da primeira abertura (dias).</summary>
    public int TrialDias { get; set; } = 14;

    /// <summary>A partir de qual dia da licença atual a renovação vale: antes disto, a renovação é recusada (409).</summary>
    public int RenovaAPartirDoDia { get; set; } = 7;
}

/// <summary>
/// O gateway de pagamento da compra individual (seção <c>Pagamentos</c>, US #425, fase 5). O segredo do webhook vem
/// do Key Vault; sem ele, o webhook recusa tudo (401).
/// </summary>
public sealed class OpcoesDePagamento
{
    public const string Secao = "Pagamentos";

    /// <summary>O segredo HMAC-SHA256 com que o gateway assina o aviso (o cabeçalho <c>X-Gateway-Assinatura</c>).</summary>
    public string SegredoDoWebhook { get; set; } = string.Empty;

    /// <summary>A tolerância do carimbo de tempo da assinatura: o aviso mais velho que isto é recusado (repetição).</summary>
    public int ToleranciaSegundos { get; set; } = 300;

    /// <summary>
    /// O checkout do gateway, com <c>{email}</c> e <c>{plano}</c> substituídos. Vazio: o site usa o checkout
    /// SIMULADO (vai direto a <c>/compra-concluida?simulado=1</c>) — o de desenvolvimento e o do e2e.
    /// </summary>
    public string UrlDoCheckout { get; set; } = string.Empty;

    /// <summary>O portal do gateway onde o cliente gerencia a assinatura (o "Gerenciar assinatura" da /conta).</summary>
    public string UrlDoPortal { get; set; } = string.Empty;
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
