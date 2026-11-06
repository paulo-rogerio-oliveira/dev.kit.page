namespace DevKitPage.Provisao;

/// <summary>
/// O que SÓ o worker lê (a mesma seção <c>Provisao</c> das <see cref="Core.OpcoesDaProvisao"/>): a assinatura, a identidade e o
/// ritmo da varredura. Nenhum segredo: a credencial é a identidade gerenciada (o <see cref="IdentidadeClientId"/> é um id
/// público), e no desenvolvimento o <c>az login</c> de quem roda.
/// </summary>
public sealed class OpcoesDoWorker
{
    public const string Secao = "Provisao";
    public const string ProvedorAzure = "azure";
    public const string ProvedorSimulado = "simulado";

    /// <summary><c>azure</c> (padrão) ou <c>simulado</c> — o de desenvolvimento e da demonstração, que não sai da máquina e é recusado em Production.</summary>
    public string Provedor { get; set; } = ProvedorAzure;

    /// <summary>A assinatura do Azure do dev.kit, onde ficam os grupos dos usuários (obrigatória com o provedor <c>azure</c>).</summary>
    public string AssinaturaId { get; set; } = string.Empty;

    /// <summary>
    /// O client id da identidade gerenciada ATRIBUÍDA AO USUÁRIO do worker. Vazio: a credencial padrão do Azure (a identidade
    /// do sistema no Azure, o <c>az login</c> no desenvolvimento) — nunca um client secret.
    /// </summary>
    public string IdentidadeClientId { get; set; } = string.Empty;

    /// <summary>A raiz do Azure Resource Manager (trocada só nos testes ou numa nuvem soberana).</summary>
    public string UrlDoArm { get; set; } = "https://management.azure.com/";

    /// <summary>A espera da varredura quando não há pedido.</summary>
    public int IntervaloSegundos { get; set; } = 10;

    /// <summary>A espera entre as consultas de uma operação longa do ARM, quando ele não manda o <c>Retry-After</c>.</summary>
    public int EsperaDaOperacaoSegundos { get; set; } = 5;

    /// <summary>Quantas consultas da operação longa antes de desistir desta tentativa (transitória: o pedido volta).</summary>
    public int MaximoDeConsultas { get; set; } = 60;

    /// <summary>O provedor simulado: quanto ele "demora" e quantas falhas transitórias dá antes de acertar (a repetição na demonstração).</summary>
    public int SimulacaoSegundos { get; set; } = 3;

    public int SimulacaoFalhasTransitorias { get; set; }

    /// <summary>
    /// O provedor simulado: o aplicativo cujo nome contém este texto falha DE VEZ no passo do aplicativo — com o plano do App
    /// Service já criado, a falha parcial da demonstração. Vazio: nenhum falha.
    /// </summary>
    public string SimulacaoFalhaNoAplicativo { get; set; } = string.Empty;
}
