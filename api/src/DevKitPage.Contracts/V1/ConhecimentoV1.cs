namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um repositório do CATÁLOGO DE REPOSITÓRIOS da empresa (US #491): o caminho e o resumo do que ele é, que o agente
/// do dev.kit grava ao terminar cada task e todos da empresa consultam para resolver o repositório de um pedido.
/// </summary>
/// <param name="Origem">O caminho como o dev.kit o clona (URL do GitHub ou pasta).</param>
/// <param name="Descricao">Para que ele serve e o que tem.</param>
/// <param name="Autor">Quem gravou a última descrição (o nome, ou o login).</param>
/// <param name="AtualizadoEm">Quando a descrição foi gravada pela última vez.</param>
public sealed record RepositorioDaEmpresaV1(string Origem, string Descricao, string Autor, DateTimeOffset AtualizadoEm);

/// <summary>O que o dev.kit manda para gravar um repositório no catálogo da empresa.</summary>
public sealed record GravacaoDeRepositorioV1(string? Origem, string? Descricao);

/// <summary>
/// Uma CONVENÇÃO (US #491) — hoje, o modelo do nome do branch da task, uma por TIPO DE BRANCH (<c>branch.feature</c>,
/// <c>branch.bug</c>, <c>branch.hotfix</c> ou <c>branch.&lt;tipo do work item&gt;</c>; a antiga <c>branch.padrao</c> o
/// dev.kit lê como a da feature). Vale
/// num NÍVEL: <c>empresa</c> (para todos), <c>time</c> (para quem declara aquele time — a Equipe das Configurações do
/// dev.kit) ou <c>usuario</c> (só para quem a gravou). A precedência (usuário &gt; time &gt; empresa) é do dev.kit; aqui
/// elas ficam para serem auditadas e editadas no site.
/// </summary>
/// <param name="Nivel">Um de <see cref="NiveisDeConvencao"/>.</param>
/// <param name="Chave">A convenção (<c>branch.hotfix</c>).</param>
/// <param name="Valor">O valor (<c>feature/{equipe}/{id}</c>); vazio, na gravação, APAGA a convenção do nível.</param>
/// <param name="Time">O time da convenção de nível <c>time</c>; vazio nos outros níveis.</param>
/// <param name="Usuario">Na leitura, de quem é a convenção de nível <c>usuario</c> (o nome, ou o login).</param>
/// <param name="Autor">Na leitura, quem a gravou por último.</param>
/// <param name="AtualizadoEm">Na leitura, quando ela foi gravada por último.</param>
public sealed record ConvencaoV1(
    string? Nivel, string? Chave, string? Valor, string? Time = "",
    string? Usuario = "", string? Autor = "", DateTimeOffset? AtualizadoEm = null);

/// <summary>Os níveis de uma <see cref="ConvencaoV1"/>.</summary>
public static class NiveisDeConvencao
{
    public const string Empresa = "empresa";
    public const string Time = "time";
    public const string Usuario = "usuario";
}
