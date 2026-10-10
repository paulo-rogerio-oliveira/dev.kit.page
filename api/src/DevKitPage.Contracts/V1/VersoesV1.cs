namespace DevKitPage.Contracts.V1;

/// <summary>
/// A última versão estável do dev.kit (US #405, <c>GET /api/versoes/ultima</c>, anônima) — o que o app
/// e a landing leem para oferecer a atualização. Vem da GitHub Release do repositório do dev.kit, mas
/// nada aqui aponta para o GitHub: o download passa pela API (<see cref="UrlDownload"/>), que é quem
/// conhece o token.
/// </summary>
/// <param name="Versao">O número do dev.kit (a tag <c>v136</c> é 136; a legada <c>v1.2.3</c> é 1).</param>
/// <param name="Tag">A tag da release, como está no GitHub.</param>
/// <param name="Nome">O nome da release (ou a tag, sem nome).</param>
/// <param name="PublicadaEm">Quando a release foi publicada.</param>
/// <param name="Destaques">Os itens de lista do corpo da release, sem markdown, no máximo 5.</param>
/// <param name="TamanhoBytes">O tamanho do pacote (o zip).</param>
/// <param name="Sha256">O SHA-256 do zip em hex minúsculo (o <c>digest</c> do asset ou o <c>&lt;zip&gt;.sha256</c>); nulo quando a release não o tem.</param>
/// <param name="UrlDownload">A rota da API que entrega o zip (autenticada).</param>
/// <param name="LoginObrigatorio">A política de login do app — a mesma de <c>GET /api/auth/politica</c>.</param>
public sealed record VersaoV1(
    int Versao, string Tag, string Nome, DateTimeOffset PublicadaEm, IReadOnlyList<string> Destaques,
    long TamanhoBytes, string? Sha256, string UrlDownload, bool LoginObrigatorio);
