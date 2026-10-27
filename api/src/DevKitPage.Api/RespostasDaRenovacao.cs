using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// O status e a mensagem (o <c>detail</c> do ProblemDetails) de cada recusa da renovação da licença — o que o dev.kit
/// mostra no aviso da licença vencida, palavra por palavra. Fica num ponto só porque é CONTRATO: o
/// <c>ContratoDaRenovacao/respostas.json</c> dos dois repositórios trava cada par, e o git.kit confere que o aviso dele
/// leva a mensagem da API (e não uma genérica).
/// </summary>
public static class RespostasDaRenovacao
{
    /// <summary>
    /// A API sem a chave privada que assina as licenças (<c>Licencas:ChavePrivada</c>, segredo <c>Licencas--ChavePrivada</c>
    /// do Key Vault). Era "indisponível no momento, tente de novo em alguns minutos" — e tentar de novo não resolvia nada:
    /// sem a chave, a renovação não volta sozinha (bug de 27/10). O <c>/health</c> diz o mesmo (<c>LicencasCheck</c>).
    /// </summary>
    public const string Indisponivel =
        "A renovação da licença está fora do ar: o dev.kit.page está sem a chave que assina as licenças (Licencas:ChavePrivada). " +
        "Avise o admin do dev.kit.page — o /health da API mostra o que falta.";

    /// <summary>O status e a mensagem de uma recusa; <see cref="RecusaDaRenovacao.Nenhuma"/> não é recusa.</summary>
    public static (int Status, string Detalhe) Para(RecusaDaRenovacao recusa) => recusa switch
    {
        RecusaDaRenovacao.UsuarioBloqueado => (StatusCodes.Status403Forbidden, "Usuário bloqueado: a licença não renova. Fale com o gestor."),
        RecusaDaRenovacao.SemAssento => (StatusCodes.Status403Forbidden, "A sua empresa não tem assento para você: a licença não renova. Fale com o gestor."),
        RecusaDaRenovacao.SemEmpresa => (StatusCodes.Status403Forbidden, "O seu usuário não é de uma empresa com assinatura: a licença não renova."),
        RecusaDaRenovacao.AssinaturaCancelada => (StatusCodes.Status402PaymentRequired, "A assinatura foi cancelada: a licença não renova."),
        // US #687: a validade que o admin definiu (ou a do contrato da empresa) é respeitada pela renovação.
        RecusaDaRenovacao.LicencaVencida => (StatusCodes.Status403Forbidden,
            "A validade da sua licença terminou: ela não renova. Fale com o gestor ou com o admin do dev.kit.page."),
        RecusaDaRenovacao.LicencaRevogada => (StatusCodes.Status403Forbidden,
            "A sua licença foi revogada pelo admin: ela não renova. Fale com o gestor ou com o admin do dev.kit.page."),
        RecusaDaRenovacao.TesteEncerrado => (StatusCodes.Status403Forbidden,
            "O seu teste grátis do dev.kit terminou: assine em dev.kit.page, ou entre com a conta da sua empresa, para continuar."),
        _ => (StatusCodes.Status409Conflict, "Ainda não é hora de renovar: a licença atual renova a partir do 7º dia."),
    };
}
