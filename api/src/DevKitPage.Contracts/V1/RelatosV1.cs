namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um RELATO do usuário do dev.kit (US #602): um defeito, uma sugestão ou uma dúvida, mandado pelo app (a ação Relatar
/// do Board e da janela da task) ou pelo <c>devcli relatar</c>. O usuário e a empresa vêm do JWT — nunca do corpo.
/// </summary>
/// <param name="Categoria">Uma de <see cref="CategoriasDeRelato"/>.</param>
/// <param name="Texto">O relato, em texto livre (obrigatório).</param>
/// <param name="Contexto">
/// O contexto que o usuário CONSENTIU em mandar (versão, task, repositórios, último erro), já sanitizado pelo dev.kit —
/// em JSON ou texto; nulo sem consentimento. A API o mascara de novo (defesa em profundidade).
/// </param>
/// <param name="Captura">A captura de tela opcional, PNG ou JPEG em base64, com o teto de <see cref="RelatosV1.TamanhoMaximoDaCaptura"/> bytes.</param>
/// <param name="VersaoDevKit">A versão do dev.kit que mandou.</param>
/// <param name="ChaveDoCliente">
/// O id que o dev.kit gerou para o relato ao enfileirá-lo: o reenvio da fila offline (o pedido que chegou mas cuja
/// resposta se perdeu) devolve o MESMO relato, em vez de gravar outro.
/// </param>
public sealed record RelatoNovoV1(
    string? Categoria, string? Texto, string? Contexto = null, string? Captura = null, string? VersaoDevKit = null, string? ChaveDoCliente = null);

/// <summary>A confirmação do relato gravado: o número e o estado (o reenvio da mesma chave devolve o mesmo).</summary>
public sealed record RelatoRecebidoV1(long Id, string Estado, DateTimeOffset RecebidoEm);

/// <summary>Um relato na lista da gestão (o admin geral e o gestor da empresa).</summary>
/// <param name="Resumo">O começo do texto, para a linha da lista.</param>
/// <param name="Autor">Quem relatou (o nome, ou o login).</param>
/// <param name="Empresa">A empresa de quem relatou; nula para quem não tem empresa.</param>
/// <param name="Respondido">O relato já tem resposta.</param>
public sealed record RelatoResumoV1(
    long Id, string Categoria, string Estado, string Resumo, string Autor, int? EmpresaId, string? Empresa,
    DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm, bool TemCaptura, bool Respondido);

/// <summary>O relato inteiro, para o detalhe da gestão.</summary>
/// <param name="Contexto">O contexto mascarado; vazio quando o usuário não consentiu.</param>
/// <param name="Captura">A captura em base64; nula sem captura.</param>
/// <param name="Resposta">A resposta do admin ou do gestor; vazia enquanto não houver.</param>
/// <param name="RespondidoPor">Quem respondeu (o nome, ou o login); vazio sem resposta.</param>
public sealed record RelatoDetalheV1(
    long Id, string Categoria, string Estado, string Texto, string Contexto, string? Captura, string VersaoDevKit,
    string Autor, int? EmpresaId, string? Empresa, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm,
    string Resposta, string RespondidoPor, DateTimeOffset? RespondidoEm);

/// <summary>
/// Um relato do PRÓPRIO usuário (<c>GET /api/relatos/meus</c>): o estado e a resposta, para a tela Meus relatos e o
/// <c>devcli relatar --meus</c>. Não leva a captura nem o contexto — o autor já os tem.
/// </summary>
public sealed record MeuRelatoV1(
    long Id, string Categoria, string Estado, string Texto, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm,
    string Resposta, DateTimeOffset? RespondidoEm, string? ChaveDoCliente);

/// <summary>O pedido de troca de estado de um relato.</summary>
public sealed record AlterarEstadoDoRelatoV1(string? Estado);

/// <summary>A resposta ao relato (responder de novo substitui a anterior).</summary>
public sealed record RespostaAoRelatoV1(string? Texto);

/// <summary>As categorias de um relato.</summary>
public static class CategoriasDeRelato
{
    public const string Defeito = "defeito";
    public const string Sugestao = "sugestao";
    public const string Duvida = "duvida";

    public static IReadOnlyList<string> Todas { get; } = [Defeito, Sugestao, Duvida];
}

/// <summary>Os estados de um relato: chega <see cref="Novo"/>, e quem administra o leva a em análise e a resolvido.</summary>
public static class EstadosDeRelato
{
    public const string Novo = "novo";
    public const string EmAnalise = "em-analise";
    public const string Resolvido = "resolvido";

    public static IReadOnlyList<string> Todos { get; } = [Novo, EmAnalise, Resolvido];
}

/// <summary>Os tetos do contrato v1 dos relatos — o dev.kit os confere antes de enfileirar.</summary>
public static class RelatosV1
{
    /// <summary>O texto do relato.</summary>
    public const int TamanhoMaximoDoTexto = 4000;

    /// <summary>O contexto (o mesmo teto do trace de uma exceção).</summary>
    public const int TamanhoMaximoDoContexto = 8192;

    /// <summary>A resposta do admin.</summary>
    public const int TamanhoMaximoDaResposta = 4000;

    /// <summary>A captura, em bytes (depois de decodificar o base64): 2 MB.</summary>
    public const int TamanhoMaximoDaCaptura = 2 * 1024 * 1024;

    /// <summary>A chave do cliente (um GUID cabe com folga).</summary>
    public const int TamanhoMaximoDaChave = 64;

    /// <summary>A versão do dev.kit.</summary>
    public const int TamanhoMaximoDaVersao = 50;
}
