namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um ARTEFATO gerado do projeto (controle-v4, item 2): o pacote .NET que o dev.kit montou (publish, release.json e zip com
/// SHA-256) para BAIXAR, ou a URL do PREVIEW da página estática num ambiente de preview do Static Web App. Nunca é produção:
/// o site guarda o zip e a URL do preview, e o link de download é assinado e vence no prazo.
/// </summary>
/// <param name="Id">O id do artefato no site.</param>
/// <param name="ProjetoId">O projeto compartilhado (<see cref="ProjetoV1.Id"/>).</param>
/// <param name="Tipo"><see cref="ArtefatosV1.TipoPacote"/> ou <see cref="ArtefatosV1.TipoPreview"/>.</param>
/// <param name="Repositorio">O repositório de onde o artefato saiu (a URL ou o nome do catálogo).</param>
/// <param name="Sha">O commit gerado (de 7 a 40 caracteres hexadecimais).</param>
/// <param name="Link">
/// O link acessível: no pacote, o download assinado (SAS de leitura, só HTTPS, com prazo — sem a chave da conta); no preview, a
/// URL do ambiente.
/// </param>
/// <param name="ExpiraEm">Quando o LINK vence (o pacote); nulo no preview.</param>
/// <param name="Hash">O SHA-256 do zip (64 hexadecimais), conferido no recebimento; vazio no preview.</param>
/// <param name="Tamanho">O tamanho do zip em bytes; 0 no preview.</param>
/// <param name="Ambiente">O ambiente de preview do Static Web App (o slug do projeto); vazio no pacote.</param>
/// <param name="Autor">Quem registrou (o nome ou o login).</param>
/// <param name="CriadoEm">Quando foi registrado.</param>
/// <param name="RetidoAte">Até quando o zip fica guardado (a retenção); nulo no preview.</param>
public sealed record ArtefatoV1(long Id, long ProjetoId, string Tipo, string Repositorio, string Sha, string Link,
    DateTimeOffset? ExpiraEm, string Hash, long Tamanho, string Ambiente, string Autor, DateTimeOffset CriadoEm,
    DateTimeOffset? RetidoAte = null);

/// <summary>Os últimos artefatos de um projeto, para o card do Board (<c>GET /api/projetos/artefatos</c>).</summary>
public sealed record ArtefatosDoProjetoV1(long ProjetoId, string Slug, string Nome, IReadOnlyList<ArtefatoV1> Artefatos);

/// <summary>
/// O registro de um PREVIEW (<c>POST /api/projetos/{id}/artefatos</c> em JSON): a URL do ambiente de preview que o dev.kit
/// publicou com o SWA CLI. O pacote vai no mesmo POST como <c>multipart/form-data</c> (campos <c>repositorio</c>, <c>sha</c>,
/// <c>hash</c> e o zip em <c>arquivo</c>).
/// </summary>
public sealed record PreviewDoProjetoV1(string? Repositorio, string? Sha, string? Url, string? Ambiente);

/// <summary>
/// Uma AÇÃO no projeto pelo site (controle-v4, item 2): o JSON do <c>texto</c> do comando <see cref="ConversaV1.ComandoProjeto"/>
/// com <see cref="Acao"/> preenchida. Sem <c>acao</c>, o texto continua sendo o <see cref="NovoProjetoRemotoV1"/> (a criação):
/// o contrato é opcional e o dev.kit antigo recusa a ação com o motivo, sem criar nada.
/// </summary>
/// <param name="Acao">Hoje só <see cref="ArtefatosV1.AcaoArtefato"/>.</param>
/// <param name="Projeto">O slug do projeto (o do retrato da máquina).</param>
/// <param name="Repositorio">O repositório do projeto (opcional: sem ele, o primeiro do projeto).</param>
/// <param name="Sha">O commit (opcional: sem ele, o HEAD do branch do projeto).</param>
public sealed record AcaoNoProjetoRemotoV1(string Acao, string Projeto, string? Repositorio = null, string? Sha = null);

/// <summary>Os tipos e os tetos dos artefatos do projeto.</summary>
public static class ArtefatosV1
{
    /// <summary>O pacote .NET para baixar (o zip com o release.json).</summary>
    public const string TipoPacote = "pacote";

    /// <summary>A URL de preview da página estática.</summary>
    public const string TipoPreview = "preview";

    /// <summary>A ação do comando <see cref="ConversaV1.ComandoProjeto"/> que pede um artefato ao dev.kit da máquina.</summary>
    public const string AcaoArtefato = "artefato";

    /// <summary>O ambiente que o artefato NUNCA usa: o preview não vira produção.</summary>
    public const string AmbienteDeProducao = "production";

    /// <summary>Quantos artefatos o card do projeto mostra.</summary>
    public const int UltimosNoCard = 3;

    public const int TamanhoMaximoDaUrl = 500;
    public const int TamanhoMaximoDoRepositorio = 300;
    public const int TamanhoMaximoDoAmbiente = 40;
}
