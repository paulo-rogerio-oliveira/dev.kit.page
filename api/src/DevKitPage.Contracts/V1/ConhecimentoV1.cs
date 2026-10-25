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
/// <param name="Id">Na leitura (US #663), o id da linha: é por ele que se promove ou descarta uma sugestão.</param>
/// <param name="Origem">Na leitura (US #663), um de <see cref="OrigensDeConvencao"/>: a regra do usuário ou a sugestão do agente.
/// Na gravação a rota decide (o <c>PUT</c> grava regra; o <c>POST .../sugestoes</c>, sugestão) — o corpo não escolhe.</param>
/// <param name="Versao">Na leitura (US #663), a versão da linha; a promoção a confere.</param>
public sealed record ConvencaoV1(
    string? Nivel, string? Chave, string? Valor, string? Time = "",
    string? Usuario = "", string? Autor = "", DateTimeOffset? AtualizadoEm = null,
    long? Id = null, string? Origem = null, int? Versao = null);

/// <summary>Os níveis de uma <see cref="ConvencaoV1"/>.</summary>
public static class NiveisDeConvencao
{
    public const string Empresa = "empresa";
    public const string Time = "time";
    public const string Usuario = "usuario";
}

/// <summary>
/// A origem de uma <see cref="ConvencaoV1"/> de contexto (US #663): o que a PESSOA escreveu vale como regra para o
/// agente; o que o AGENTE registrou é sugestão — consulta, nunca obrigação — até alguém promovê-la.
/// </summary>
public static class OrigensDeConvencao
{
    public const string Usuario = "usuario";
    public const string Agente = "agente";

    /// <summary>O valor de <c>?origem=</c> que traz as duas na leitura; sem ele, só as regras (o dev.kit antigo).</summary>
    public const string Todas = "todas";
}

/// <summary>
/// A SUGESTÃO de contexto que o agente do dev.kit registra ao concluir uma task (US #663), com o token do usuário.
/// </summary>
/// <param name="Camada">Um de <see cref="NiveisDeConvencao"/> (<c>usuario</c>, <c>time</c> ou <c>empresa</c>).</param>
/// <param name="Tema">O tema, sem o prefixo (<c>stack</c> vira <c>contexto.stack</c>); com ele também vale.</param>
/// <param name="Texto">O que o agente aprendeu.</param>
/// <param name="Time">A equipe, na camada <c>time</c>.</param>
public sealed record SugestaoDeContextoV1(string? Camada, string? Tema, string? Texto, string? Time = "");

/// <summary>A promoção de uma sugestão a regra (US #663): a versão que quem promove leu; outra é conflito (409).</summary>
public sealed record PromocaoDeContextoV1(int? VersaoEsperada);
