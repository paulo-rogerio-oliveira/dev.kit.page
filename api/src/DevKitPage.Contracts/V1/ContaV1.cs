namespace DevKitPage.Contracts.V1;

/// <summary>
/// Os códigos das recusas de login (US #425) — a extensão <c>codigo</c> do ProblemDetails, para o dev.kit mostrar a
/// mensagem PRÓPRIA de cada caso sem depender do texto.
/// </summary>
public static class CodigosDeRecusa
{
    /// <summary>O bloqueio temporário por falhas seguidas (423, com <c>bloqueadoAte</c>).</summary>
    public const string BloqueadoTemporario = "bloqueado-temporario";

    /// <summary>O gestor ou o admin bloqueou o usuário (423).</summary>
    public const string BloqueadoPeloAdmin = "bloqueado-pelo-admin";

    /// <summary>A empresa do dev não tem assento para ele (403).</summary>
    public const string SemAssento = "sem-assento";
}

/// <summary>A adesão do dev por código e e-mail (US #425): cria o usuário e manda o link de definir a senha.</summary>
/// <param name="Codigo">O código de adesão que o gestor entregou ao time.</param>
public sealed record AdesaoV1(string Codigo, string Email, string? Nome = null);

/// <summary>O que a adesão (ou o pedido de link) fez: a mensagem para quem pediu — o link vai por e-mail, nunca aqui.</summary>
public sealed record AdesaoFeitaV1(string Mensagem);

/// <summary>O pedido de um link novo de definir a senha (o "esqueci a senha"): a resposta é a mesma exista ou não o e-mail.</summary>
public sealed record PedidoDeLinkV1(string Email);

/// <summary>A senha nova pelo link do e-mail (uso único, 24 h).</summary>
public sealed record DefinirSenhaV1(string Token, string NovaSenha);

/// <summary>A conta de quem está logado (<c>GET /api/conta</c>, a página <c>/conta</c>).</summary>
/// <param name="Individual">A empresa é de um assento, da compra individual: a página mostra "Gerenciar assinatura".</param>
/// <param name="AssinaturaAtiva">Falso quando o gateway avisou o cancelamento: a licença não renova mais.</param>
/// <param name="UrlGerenciarAssinatura">O portal do gateway, só para a empresa individual.</param>
/// <param name="LoginObrigatorio">A política de login que vale para esta conta (a da empresa, ou a global).</param>
public sealed record ContaV1(
    string Login, string Nome, string Email, string Papel, string? Empresa, bool Individual, bool AssinaturaAtiva,
    string? UrlGerenciarAssinatura, bool LoginObrigatorio);

/// <summary>A empresa vista pelo gestor (<c>/api/empresa</c>): os assentos por usuário e a política de login dela.</summary>
/// <param name="LoginObrigatorio">O que o gestor escolheu; nulo segue o padrão global.</param>
/// <param name="LoginObrigatorioEfetivo">O que vale de fato (a escolha, ou o padrão global).</param>
public sealed record EmpresaDoGestorV1(
    int Id, string Nome, string Plano, int Assentos, int AssentosOcupados, string CodigoDeAdesao, bool? LoginObrigatorio, bool LoginObrigatorioEfetivo);

/// <summary>Um usuário da empresa na lista do gestor.</summary>
public sealed record UsuarioDaEmpresaV1(int Id, string Login, string Nome, string Email, string Papel, bool Bloqueado, DateTimeOffset CriadoEm);

/// <summary>A política de login da empresa: <c>true</c>/<c>false</c> sobrepõem o padrão global; nulo volta a ele.</summary>
public sealed record PoliticaDaEmpresaV1(bool? LoginObrigatorio);

/// <summary>O código de adesão novo (o antigo deixa de valer na hora).</summary>
public sealed record CodigoDeAdesaoV1(string CodigoDeAdesao);

/// <summary>Os tipos de licença assinada (US #425, fase 4).</summary>
public static class TiposDeLicenca
{
    /// <summary>Os 14 dias contados da primeira abertura.</summary>
    public const string Trial = "trial";

    /// <summary>A de quem assina: 30 dias, renovada pelo login.</summary>
    public const string Assinatura = "assinatura";
}

/// <summary>
/// Uma licença assinada (US #425): o <see cref="Conteudo"/> é o <c>license.dat</c> no formato <c>DKL2</c> —
/// <c>DKL2.&lt;payload base64url&gt;.&lt;assinatura Ed25519 base64url&gt;</c> —, que o dev.kit confere com a chave pública embutida.
/// </summary>
public sealed record LicencaV1(string Conteudo, string Tipo, DateTimeOffset EmitidaEm, DateTimeOffset ValidaAte);

/// <summary>O pedido de renovação: a licença que a máquina tem (para a janela de renovação), quando tem.</summary>
public sealed record RenovacaoV1(string? LicencaAtual = null);

/// <summary>Os tipos de aviso do gateway de pagamento que a API trata (o resto é aceito e ignorado).</summary>
public static class TiposDeAviso
{
    public const string PagamentoAprovado = "pagamento.aprovado";
    public const string AssinaturaCancelada = "assinatura.cancelada";
}

/// <summary>O aviso (webhook) do gateway: o id do evento é a chave da idempotência.</summary>
public sealed record AvisoDoGatewayV1(string Id, string Tipo, DadosDoAvisoV1? Dados);

/// <summary>Os dados do aviso: o comprador e a assinatura no gateway.</summary>
public sealed record DadosDoAvisoV1(string? Email, string? Nome, string? Assinatura, string? Plano);

/// <summary>O pedido de checkout da página <c>/comprar</c>.</summary>
public sealed record CheckoutV1(string Email, string? Nome, string Plano);

/// <summary>Para onde o site leva o comprador: o checkout do gateway, ou o simulado (sem gateway configurado).</summary>
public sealed record CheckoutCriadoV1(string Url, bool Simulado);
