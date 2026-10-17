namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um item do CONTEXTO do dev.kit (US #556, no lugar das convenções da US #491): o que o usuário, a equipe e a empresa
/// sabem e querem que os agentes considerem. Vale num NÍVEL (<see cref="NiveisDeContexto"/>) e tem uma ORIGEM
/// (<see cref="OrigensDeContexto"/>): o que o usuário escreveu é INSTRUÇÃO; o que um agente anotou ao fim de uma
/// execução é SUGESTÃO, opcional. A instrução e a sugestão da mesma chave coexistem — e a instrução prevalece.
/// </summary>
/// <param name="Id">O item no servidor (o que se exclui e se promove).</param>
/// <param name="Nivel">Um de <see cref="NiveisDeContexto"/>.</param>
/// <param name="Chave">O tema: <c>branch.hotfix</c> (o nome do branch), <c>contexto.testes</c> (texto livre) ou <c>nota.&lt;slug&gt;</c> (a nota do agente).</param>
/// <param name="Valor">O texto (até <see cref="LimitesDoContexto.TamanhoMaximoDoValor"/> caracteres).</param>
/// <param name="Equipe">A equipe do item de nível <c>equipe</c>; vazio nos outros níveis.</param>
/// <param name="Usuario">De quem é o item de nível <c>usuario</c> (o nome, ou o login).</param>
/// <param name="Origem">Um de <see cref="OrigensDeContexto"/>.</param>
/// <param name="Autor">Quem o gravou por último.</param>
/// <param name="AtualizadoEm">Quando ele foi gravado por último.</param>
/// <param name="Versao">A versão do item: cada gravação soma 1 — a edição feita sobre uma versão velha é conflito (409).</param>
public sealed record ContextoV1(
    long Id, string Nivel, string Chave, string Valor, string Equipe, string Usuario, string Origem, string Autor,
    DateTimeOffset AtualizadoEm, int Versao);

/// <summary>
/// O que o site, o dev.kit e o devcli mandam para gravar um item do contexto.
/// </summary>
/// <param name="Nivel">Um de <see cref="NiveisDeContexto"/>.</param>
/// <param name="Chave">O tema (minúsculas, sem acento: <c>contexto.testes</c>).</param>
/// <param name="Valor">O texto; obrigatório (excluir é o <c>DELETE</c>).</param>
/// <param name="Equipe">A equipe do nível <c>equipe</c>.</param>
/// <param name="Origem">Um de <see cref="OrigensDeContexto"/>; vazio é <c>usuario</c>. O devcli manda <c>agente</c> na nota do agente.</param>
/// <param name="VersaoEsperada">
/// A versão sobre a qual a edição foi feita. Diferente da do servidor (ou o item já excluído) é 409, com o item atual —
/// nada se sobrescreve em silêncio. Nula grava por cima (a nota do agente, a convenção antiga).
/// </param>
/// <param name="UsuarioId">Só o admin do board: de quem é o item de nível <c>usuario</c> que ele grava. Para os outros, é sempre quem grava.</param>
public sealed record GravacaoDeContextoV1(
    string? Nivel, string? Chave, string? Valor, string? Equipe = "", string? Origem = "", int? VersaoEsperada = null,
    int? UsuarioId = null);

/// <summary>
/// O contexto COMPOSTO que o dev.kit sincroniza (<c>GET /api/contexto?equipe=</c>): o da empresa, o da equipe declarada
/// (só com a participação aprovada) e o do próprio usuário — instruções e sugestões.
/// </summary>
/// <param name="Itens">Os itens, de todos os níveis e origens: a precedência (usuário &gt; equipe &gt; empresa; instrução &gt; sugestão) é do dev.kit.</param>
/// <param name="Equipe">A equipe declarada, normalizada.</param>
/// <param name="Participacao">Um de <see cref="SituacoesDeParticipacao"/>, ou vazio sem equipe: pendente não traz o contexto da equipe.</param>
public sealed record ContextoCompostoV1(IReadOnlyList<ContextoV1> Itens, string Equipe, string Participacao);

/// <summary>
/// O que a landing mostra a cada perfil (<c>GET /api/contexto/visao</c> e a do admin): o dev vê o dele, o das equipes
/// aprovadas e o da empresa (só leitura); o gestor, também o de todas as equipes, editando o da empresa; o admin do
/// board, tudo da empresa escolhida.
/// </summary>
/// <param name="Papel">O papel com que a visão foi montada (<see cref="Papeis"/>).</param>
/// <param name="Itens">Os itens que o perfil VÊ.</param>
/// <param name="Equipes">As equipes que o perfil vê (as aprovadas do dev; todas as da empresa para o gestor e o admin).</param>
/// <param name="EquipesPendentes">As equipes que o dev pediu e o gestor ainda não aprovou: "aguardando aprovação".</param>
/// <param name="Participantes">Os pedidos e as participações da empresa — só para o gestor e o admin, que decidem.</param>
/// <param name="EditaEmpresa">Se o perfil grava o contexto da empresa.</param>
public sealed record VisaoDoContextoV1(
    string Papel, IReadOnlyList<ContextoV1> Itens, IReadOnlyList<string> Equipes, IReadOnlyList<string> EquipesPendentes,
    IReadOnlyList<ParticipanteDeEquipeV1> Participantes, bool EditaEmpresa);

/// <summary>Quem participa de uma equipe — ou pediu para participar — na empresa.</summary>
/// <param name="UsuarioId">O usuário.</param>
/// <param name="Usuario">O nome (ou o login).</param>
/// <param name="Equipe">A equipe, normalizada.</param>
/// <param name="Situacao">Um de <see cref="SituacoesDeParticipacao"/>.</param>
/// <param name="AtualizadoEm">Quando o pedido chegou, ou quando foi decidido.</param>
public sealed record ParticipanteDeEquipeV1(int UsuarioId, string Usuario, string Equipe, string Situacao, DateTimeOffset AtualizadoEm);

/// <summary>A decisão do gestor (ou do admin) sobre a participação: <c>aprovado</c> dá o acesso; <c>recusado</c> o tira.</summary>
public sealed record DecisaoDeParticipacaoV1(int UsuarioId, string? Situacao);

/// <summary>A resposta do 409: a edição foi feita sobre uma versão velha; o item atual vai junto (nulo se foi excluído).</summary>
public sealed record ConflitoDeContextoV1(string Mensagem, ContextoV1? Atual);

/// <summary>Os níveis de um <see cref="ContextoV1"/>; em conflito vale o mais específico (usuário &gt; equipe &gt; empresa).</summary>
public static class NiveisDeContexto
{
    public const string Usuario = "usuario";
    public const string Equipe = "equipe";
    public const string Empresa = "empresa";
}

/// <summary>A origem de um <see cref="ContextoV1"/>: quem o escreveu decide se ele é obrigatório.</summary>
public static class OrigensDeContexto
{
    /// <summary>Escrito por uma pessoa: INSTRUÇÃO, obrigatória para o agente.</summary>
    public const string Usuario = "usuario";

    /// <summary>Anotado por um agente ao fim de uma execução: SUGESTÃO, opcional — o usuário promove ou descarta.</summary>
    public const string Agente = "agente";
}

/// <summary>A situação de um <see cref="ParticipanteDeEquipeV1"/>.</summary>
public static class SituacoesDeParticipacao
{
    /// <summary>O dev declarou a equipe; o gestor ainda não decidiu — sem acesso.</summary>
    public const string Pendente = "pendente";

    /// <summary>O gestor (ou o admin) aprovou: lê e grava o contexto da equipe.</summary>
    public const string Aprovado = "aprovado";

    /// <summary>Na decisão, tira a participação (o pedido some).</summary>
    public const string Recusado = "recusado";
}

/// <summary>Os tamanhos do contexto, os mesmos no site, na API e no dev.kit.</summary>
public static class LimitesDoContexto
{
    public const int TamanhoMaximoDaChave = 60;
    public const int TamanhoMaximoDoValor = 4000;
    public const int TamanhoMaximoDaEquipe = 80;
    public const int TamanhoMaximoDoNivel = 10;
    public const int TamanhoMaximoDaOrigem = 10;
}

/// <summary>
/// O que parece CREDENCIAL e nunca entra no contexto (US #556, A3b): o contexto é compartilhado com a equipe e a empresa
/// e vai para o arquivo de regras de todo agente. A regra (<c>RegrasDoContexto.ContemSegredo</c>) recusa na API (422) e o
/// devcli, antes de enviar ou de pôr na fila offline, aplica a MESMA lista — espelhada em
/// <c>git.kit/src/GitKit.Abstractions/SegredosNoContexto.cs</c>, e as duas suítes conferem os mesmos
/// <see cref="ExemplosQueSaoSegredo"/> e <see cref="ExemplosQueNaoSaoSegredo"/>.
/// </summary>
public static class SegredosNoContexto
{
    /// <summary>Os padrões (regex .NET, sem distinção de caixa onde marcado com <c>(?i)</c>).</summary>
    public static IReadOnlyList<string> Padroes { get; } =
    [
        @"\bgh[pousr]_[A-Za-z0-9]{20,}",
        @"\bgithub_pat_[A-Za-z0-9_]{20,}",
        @"\bxox[abprs]-[A-Za-z0-9-]{10,}",
        @"\bAKIA[0-9A-Z]{16}\b",
        @"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]{16,}=*",
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}",
        @"(?i)\b(password|passwd|senha|pwd)\s*[=:]\s*[^\s;]+",
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----",
    ];

    /// <summary>A sequência sem espaço que conta como chave aleatória: este tamanho, ou mais...</summary>
    public const int TamanhoMinimoDeAltaEntropia = 32;

    /// <summary>... com maiúscula, minúscula e dígito, e esta entropia (bits por caractere), ou mais.</summary>
    public const double EntropiaMinima = 4.0;

    /// <summary>Os exemplos que as DUAS listas (a do site e a do dev.kit) têm de reconhecer.</summary>
    public static IReadOnlyList<string> ExemplosQueSaoSegredo { get; } =
    [
        "use o token ghp_1234567890abcdefghijABCDEFGHIJ",
        "github_pat_11ABCDEFG0123456789_abcdefghijklmnop",
        "xoxb-1234567890-abcdefghij",
        "AKIAIOSFODNN7EXAMPLE",
        "Authorization: Bearer abcdefghijklmnop1234567890",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U",
        "password=Sup3rSecreta",
        "a senha: minhaSenha123",
        "Server=db;Database=erp;User Id=sa;Pwd=Abc12345;",
        "-----BEGIN RSA PRIVATE KEY-----",
        "chave Zx8Kq2Lm9Pw4Rt6Yv1Bn3Cs5Df7Gh0Jk2Qa",
    ];

    /// <summary>Os exemplos de texto comum que as DUAS listas NÃO podem recusar.</summary>
    public static IReadOnlyList<string> ExemplosQueNaoSaoSegredo { get; } =
    [
        "Os testes de integração rodam com o SQLite em arquivo temporário.",
        "https://github.com/acme/gbl-2000",
        "feature/{equipe}/{id}",
        "O work item 3f2504e0-4f89-11d3-9a0c-0305e82c3301 é o épico de vendas.",
        "Commit 9fceb02d0ae598e95dc970b74767f19372d61af8 corrigiu o build.",
        "Prefira a senha de serviço guardada no Key Vault, nunca no código.",
    ];
}
