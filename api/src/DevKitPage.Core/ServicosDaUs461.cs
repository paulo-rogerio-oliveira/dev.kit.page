using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// Quem pede um catálogo (US #461): o admin vê todos; o usuário de uma empresa, o básico e os relacionados a ela (pelo
/// <see cref="EscopoDaEmpresa"/>, o mesmo isolamento da US #425); o anônimo, só o básico. O catálogo que a pessoa não
/// pode ver responde como inexistente (404), para a rota não servir para sondar.
/// </summary>
public sealed record AcessoAoCatalogo(bool Admin, EscopoDaEmpresa? Escopo)
{
    public static AcessoAoCatalogo Anonimo { get; } = new(false, null);
}

/// <summary>
/// A release que a API serve de um catálogo: a publicada no site (<see cref="VersaoId"/>) ou, no básico ainda sem release
/// no site, a do GitHub (<see cref="DoGitHub"/>) — o caminho de antes, mantido para o dev.kit e a vitrine não ficarem
/// sem catálogo até a primeira publicação.
/// </summary>
public sealed record ReleaseServida(
    string Slug, int Versao, string Tag, DateTimeOffset PublicadaEm, ManifestoDoCatalogoV1 Manifesto, string? Sha256, long TamanhoBytes,
    long? VersaoId, ReleaseDoCatalogo? DoGitHub);

/// <summary>
/// O armazém dos zips das releases (US #461): no banco hoje — o pacote é pequeno —, e atrás desta interface para ir a um
/// Blob Storage depois sem mudar regra nenhuma.
/// </summary>
public interface IArmazemDePacotes
{
    Task GuardarAsync(long versaoId, byte[] zip, CancellationToken ct);

    /// <summary>O zip da release; nulo quando não há.</summary>
    Task<byte[]?> AbrirAsync(long versaoId, CancellationToken ct);
}

/// <summary>
/// A GESTÃO dos catálogos nomeados pelo admin (US #461): o cadastro, o vínculo com as empresas (com a recusa da colisão
/// de nomes) e os itens editáveis — que gravam rascunho: nada chega às máquinas antes de publicar.
/// </summary>
public interface IGestaoDeCatalogos
{
    /// <summary>Os catálogos (o básico primeiro), com a versão publicada, os itens, as pendências e as empresas.</summary>
    Task<IReadOnlyList<CatalogoV1>> ListarAsync(CancellationToken ct);

    /// <summary>Cria o catálogo JÁ validado (<see cref="RegrasDosCatalogos.Validar(CatalogoNovoV1?)"/>); slug repetido é conflito.</summary>
    Task<ResultadoDaGestao<CatalogoV1>> CriarAsync(CatalogoNovoV1 novo, CancellationToken ct);

    Task<ResultadoDaGestao<CatalogoV1>> EditarAsync(string slug, CatalogoEditadoV1 edicao, CancellationToken ct);

    /// <summary>Exclui o catálogo (o básico, não): as máquinas o perdem na próxima sincronia, como no desvínculo.</summary>
    Task<ResultadoDaGestao<CatalogoV1>> ExcluirAsync(string slug, CancellationToken ct);

    /// <summary>Relaciona a empresa; recusa (conflito) quando um item colide com outro catálogo já relacionado a ela.</summary>
    Task<ResultadoDaGestao<CatalogoV1>> RelacionarAsync(string slug, int empresaId, CancellationToken ct);

    Task<ResultadoDaGestao<CatalogoV1>> DesrelacionarAsync(string slug, int empresaId, CancellationToken ct);

    /// <summary>Os itens do catálogo, com a situação de cada um frente à última release (sem o conteúdo).</summary>
    Task<ResultadoDaGestao<IReadOnlyList<ItemEditavelV1>>> ItensAsync(string slug, CancellationToken ct);

    /// <summary>Um item, com o conteúdo.</summary>
    Task<ResultadoDaGestao<ItemEditavelV1>> ItemAsync(string slug, long id, CancellationToken ct);

    /// <summary>
    /// Grava o rascunho de um item JÁ conferido (<see cref="RegrasDosCatalogos.Conferir"/>): cria (<paramref name="id"/>
    /// nulo) ou edita. O nome é a identidade na máquina e não muda na edição; num catálogo que não é o básico, o nome que
    /// já existe no básico é conflito (a mesma regra do catálogo da empresa).
    /// </summary>
    Task<ResultadoDaGestao<ItemEditavelV1>> SalvarItemAsync(string slug, long? id, ItemConferido item, string? categoria, string autor, CancellationToken ct);

    /// <summary>
    /// Retira o item: o publicado vira retirada pendente (a próxima release o tira das máquinas); o que nunca foi
    /// publicado é apagado — o resultado volta sem valor e sem falha.
    /// </summary>
    Task<ResultadoDaGestao<ItemEditavelV1>> RetirarItemAsync(string slug, long id, string autor, CancellationToken ct);

    /// <summary>
    /// Importa UMA vez a última release <c>catalogo-vN</c> do GitHub para o básico do site — os itens editáveis e a
    /// própria release (o mesmo zip, o mesmo manifesto) —, para a edição partir do que as máquinas têm e a numeração
    /// continuar (<c>catalogo-v(N+1)</c>).
    /// </summary>
    Task<ImportacaoDoBasicoV1> ImportarBasicoAsync(string autor, CancellationToken ct);
}

/// <summary>
/// A PUBLICAÇÃO e a DISTRIBUIÇÃO dos catálogos (US #461): o manifesto gerado pela regra do git.kit, o fechamento das
/// dependências, a release com o zip e o que cada máquina baixa.
/// </summary>
public interface IPublicacaoDeCatalogos
{
    /// <summary>O que a próxima publicação faria (as mudanças e os problemas), sem gravar nada.</summary>
    Task<ResultadoDaGestao<RelatorioDePublicacaoV1>> ConferirAsync(string slug, int? minDevKit, CancellationToken ct);

    /// <summary>
    /// Publica: gera o manifesto e o zip, grava a release e o pacote. Com problema, o relatório volta na falha
    /// (<see cref="FalhaNaGestao.Conflito"/>); sem mudança, <see cref="FalhaNaGestao.Recusada"/>.
    /// </summary>
    Task<(PublicacaoDoCatalogoV1? Feita, RelatorioDePublicacaoV1? Recusa, FalhaNaGestao Falha, string Mensagem)> PublicarAsync(
        string slug, int? minDevKit, string autor, CancellationToken ct);

    /// <summary>Os catálogos que quem pede sincroniza: o básico e, conforme o acesso, os relacionados (ou todos, no admin).</summary>
    Task<IReadOnlyList<CatalogoDisponivelV1>> DisponiveisAsync(AcessoAoCatalogo acesso, CancellationToken ct);

    /// <summary>A última release do catálogo para quem pede; nula quando ele não existe, não é visível ou não tem release.</summary>
    Task<ReleaseServida?> UltimaAsync(string slug, AcessoAoCatalogo acesso, CancellationToken ct);

    /// <summary>A release de uma versão; nula como em <see cref="UltimaAsync"/>.</summary>
    Task<ReleaseServida?> ReleaseAsync(string slug, int versao, AcessoAoCatalogo acesso, CancellationToken ct);

    /// <summary>As releases do catálogo, da mais nova para a mais velha; nulo quando o catálogo não existe (ou o GitHub, no básico sem release no site, está fora).</summary>
    Task<IReadOnlyList<ReleaseDoCatalogoV1>?> ReleasesAsync(string slug, CancellationToken ct);

    /// <summary>O zip da release (do banco ou, no básico sem release no site, do GitHub em streaming).</summary>
    Task<PacoteAberto?> AbrirPacoteAsync(ReleaseServida release, CancellationToken ct);
}
