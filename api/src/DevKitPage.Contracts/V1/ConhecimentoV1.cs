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
/// Uma CONVENÇÃO compartilhada (US #491) — hoje, o modelo do nome do branch da task (<c>branch.padrao</c>,
/// <c>branch.bug</c>). Vale num NÍVEL: <c>empresa</c> (para todos) ou <c>time</c> (para quem declara aquele time — a
/// Equipe das Configurações do dev.kit). A do usuário fica na máquina dele e não passa por aqui.
/// </summary>
/// <param name="Nivel">Um de <see cref="NiveisDeConvencao"/>.</param>
/// <param name="Chave">A convenção (<c>branch.padrao</c>).</param>
/// <param name="Valor">O valor (<c>feature/{equipe}/{id}</c>); vazio, na gravação, APAGA a convenção do nível.</param>
/// <param name="Time">O time da convenção de nível <c>time</c>; vazio na de nível <c>empresa</c>.</param>
public sealed record ConvencaoV1(string? Nivel, string? Chave, string? Valor, string? Time = "");

/// <summary>Os níveis de uma <see cref="ConvencaoV1"/> compartilhada.</summary>
public static class NiveisDeConvencao
{
    public const string Empresa = "empresa";
    public const string Time = "time";
}
