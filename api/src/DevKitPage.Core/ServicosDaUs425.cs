using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Um e-mail a enviar: o destinatário, o assunto e o texto puro.</summary>
public sealed record MensagemDeEmail(string Para, string Assunto, string Texto);

/// <summary>
/// O envio de e-mail (US #425) — não havia nenhum antes desta US. A implementação é configurável (SMTP, ou o log
/// quando não há SMTP), e os testes usam um falso que guarda as mensagens.
/// </summary>
public interface IEnvioDeEmail
{
    Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct);
}

/// <summary>
/// O catálogo da EMPRESA (US #425, fase 2). Toda operação recebe o <see cref="EscopoDaEmpresa"/> e o aplica aqui,
/// num ponto só: o item de outra empresa é <see cref="FalhaNaGestao.Proibida"/> (403), nunca uma lista vazia ou um
/// 404 que deixe sondar.
/// </summary>
public interface ICatalogoDaEmpresa
{
    /// <summary>Os itens da empresa (inclusive os retirados, marcados), com os assinantes de cada um.</summary>
    Task<IReadOnlyList<ItemDaEmpresaV1>> ListarAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>Assinantes do item, somente para o gestor, em páginas de até 100 usuários.</summary>
    Task<ResultadoDaGestao<Pagina<AssinanteDoItemV1>>> AssinantesAsync(EscopoDaEmpresa escopo, long id, int pagina, int tamanho, CancellationToken ct);

    /// <summary>
    /// Publica o item JÁ validado (<see cref="RegrasDoCatalogoDaEmpresa.Validar"/>): o novo nasce na versão 1; o que já
    /// existe ganha versão nova só se o hash mudou; o retirado volta. Publicar por cima do item de outro autor é do
    /// gestor (ou do próprio autor).
    /// </summary>
    Task<ResultadoDaGestao<PublicacaoFeitaV1>> PublicarAsync(EscopoDaEmpresa escopo, PublicacaoV1 publicacao, CancellationToken ct);

    /// <summary>Retira o item: o autor ou o gestor da empresa; o dev que não é o autor recebe a recusa.</summary>
    Task<ResultadoDaGestao<ItemDaEmpresaV1>> RetirarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct);

    /// <summary>Assina o item — e as dependências dele que estão no catálogo da empresa.</summary>
    Task<ResultadoDaGestao<AssinaturaFeitaV1>> AssinarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct);

    /// <summary>Desiste da assinatura (as dependências ficam: outro item pode usá-las).</summary>
    Task<ResultadoDaGestao<AssinaturaFeitaV1>> DesistirAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct);

    /// <summary>O que a sincronia do dev.kit de quem consulta grava: os itens assinados, com o conteúdo da versão atual.</summary>
    Task<IReadOnlyList<ItemAssinadoV1>> AssinadosAsync(EscopoDaEmpresa escopo, CancellationToken ct);
}

/// <summary>
/// A conta do dev (US #425, fase 3): a adesão por código e e-mail, o link de definir a senha e a conta de quem
/// está logado.
/// </summary>
public interface IContas
{
    /// <summary>
    /// Adere à empresa do código, JÁ validado (<see cref="RegrasDaConta.Validar(AdesaoV1?)"/>): cria o dev (o login é o
    /// e-mail) e manda o link. Código desconhecido é <see cref="FalhaNaGestao.NaoEncontrado"/>; e-mail de outra empresa
    /// e empresa sem assento, <see cref="FalhaNaGestao.Conflito"/>.
    /// </summary>
    Task<ResultadoDaGestao<AdesaoFeitaV1>> AderirAsync(AdesaoV1 adesao, CancellationToken ct);

    /// <summary>Manda um link novo ao e-mail, se ele existir — a resposta é a mesma de qualquer jeito.</summary>
    Task PedirLinkAsync(string email, CancellationToken ct);

    /// <summary>Define a senha pelo token do link (uso único, dentro da validade). Devolve o erro, ou vazio.</summary>
    Task<string> DefinirSenhaAsync(string token, string novaSenha, CancellationToken ct);

    /// <summary>
    /// Cria o link de definir a senha de um usuário que já existe e o manda por e-mail — o que a adesão e a compra
    /// individual fazem depois de criá-lo.
    /// </summary>
    Task EnviarLinkAsync(Usuario usuario, string assunto, CancellationToken ct);

    /// <summary>A conta de quem está logado; nula quando o usuário não existe mais.</summary>
    Task<ContaV1?> ContaAsync(int usuarioId, CancellationToken ct);

    /// <summary>A política de login da empresa (ou a global, sem empresa ou sem escolha dela).</summary>
    Task<bool> LoginObrigatorioAsync(int? empresaId, CancellationToken ct);
}

/// <summary>
/// A empresa administrada pelo GESTOR (US #425, fase 3): os usuários dela (bloquear libera o assento), o código de
/// adesão novo e a política de login — tudo no <see cref="EscopoDaEmpresa"/>, como o catálogo.
/// </summary>
public interface IGestaoDaEmpresa
{
    Task<EmpresaDoGestorV1?> EmpresaAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    Task<IReadOnlyList<UsuarioDaEmpresaV1>> UsuariosAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>Bloqueia ou desbloqueia um usuário DA empresa; o de outra é <see cref="FalhaNaGestao.Proibida"/>.</summary>
    Task<ResultadoDaGestao<UsuarioDaEmpresaV1>> BloquearAsync(EscopoDaEmpresa escopo, int usuarioId, bool bloqueado, CancellationToken ct);

    /// <summary>Gera um código de adesão novo: o antigo deixa de valer na hora.</summary>
    Task<CodigoDeAdesaoV1> NovoCodigoAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>Grava a política de login da empresa (nulo volta ao padrão global).</summary>
    Task<EmpresaDoGestorV1> DefinirPoliticaAsync(EscopoDaEmpresa escopo, bool? loginObrigatorio, CancellationToken ct);
}

/// <summary>
/// As licenças assinadas (US #425, fase 4): o trial pela impressão da máquina e a renovação pelo login. Sem a chave
/// privada configurada, <see cref="Disponivel"/> é falso e as rotas respondem 503.
/// </summary>
public interface ILicencas
{
    bool Disponivel { get; }

    /// <summary>
    /// O trial da máquina pela <paramref name="impressao"/>: contado do PRIMEIRO registro dela (reinstalar não reinicia),
    /// e gravado na máquina. Nulo sem impressão válida ou sem a chave.
    /// </summary>
    Task<LicencaV1?> TrialAsync(int maquinaId, string? impressao, CancellationToken ct);

    /// <summary>A licença de 30 dias para o usuário nesta máquina, ou a recusa (bloqueado, sem empresa, cancelada, cedo demais).</summary>
    Task<(LicencaV1? Licenca, RecusaDaRenovacao Recusa)> RenovarAsync(int usuarioId, int maquinaId, string? licencaAtual, CancellationToken ct);
}

/// <summary>A compra individual pelo gateway de pagamento (US #425, fase 5).</summary>
public interface IPagamentos
{
    /// <summary>
    /// Processa o aviso JÁ verificado (<see cref="RegrasDoGateway.Valida"/>): o pagamento aprovado cria a empresa de um
    /// assento e o usuário (e manda o link de definir a senha); o cancelamento corta a renovação. Falso quando o evento
    /// já tinha sido processado — o reenvio não duplica nada.
    /// </summary>
    Task<bool> ProcessarAsync(AvisoDoGatewayV1 aviso, CancellationToken ct);

    /// <summary>Para onde o comprador vai: o checkout do gateway, ou o simulado (sem gateway configurado).</summary>
    CheckoutCriadoV1 Checkout(CheckoutV1 pedido);
}
