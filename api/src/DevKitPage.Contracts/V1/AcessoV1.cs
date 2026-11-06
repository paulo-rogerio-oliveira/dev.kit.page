namespace DevKitPage.Contracts.V1;

/// <summary>Um usuário no acesso a um projeto (de design ou do Orquestrador): o id, o login e o nome (vazio sem nome).</summary>
public sealed record UsuarioDoAcessoV1(int Id, string Login, string Nome);

/// <summary>Um acesso concedido pelo dono: o usuário, o papel (<c>leitor</c> ou <c>editor</c>) e quando.</summary>
public sealed record AcessoConcedidoV1(UsuarioDoAcessoV1 Usuario, string Papel, DateTimeOffset ConcedidoEm);

/// <summary>
/// Os ACESSOS de um projeto — de design (<see cref="AcessoV1.RecursoDesign"/>) ou do Orquestrador
/// (<see cref="AcessoV1.RecursoProjeto"/>), no MESMO modelo: o dono, quem mais tem acesso e com que papel, o papel de quem
/// consulta e — só para o dono — os usuários da empresa a quem ele ainda pode dar acesso (o seletor do Dar acesso).
/// </summary>
/// <param name="Nome">O que identifica o projeto para quem lê: o repositório (design) ou o nome (projeto).</param>
public sealed record AcessosDoProjetoV1(
    string Recurso, long Projeto, string Nome, UsuarioDoAcessoV1? Dono, string Papel, IReadOnlyList<AcessoConcedidoV1> Acessos,
    IReadOnlyList<UsuarioDoAcessoV1> Candidatos);

/// <summary>O pedido de conceder (ou de mudar) o acesso: o usuário (o login, o e-mail ou o id) e o papel.</summary>
public sealed record ConcessaoDeAcessoV1(string? Usuario, string? Papel);

/// <summary>A transferência do projeto a outro dono: o usuário e a confirmação explícita (sem ela, 400).</summary>
public sealed record TransferenciaDeDonoV1(string? Usuario, bool Confirmar);

/// <summary>
/// Os nomes do ACESSO aos projetos, um modelo só para o design e para os projetos do Orquestrador: o DONO criou o projeto
/// e dá e tira acesso; o EDITOR vê e também altera (no design, envia versões; no projeto, edita, aprova e dispara); o
/// LEITOR só vê (as versões; o projeto, os itens e o andamento).
/// </summary>
public static class AcessoV1
{
    public const string PapelDono = "dono";
    public const string PapelEditor = "editor";
    public const string PapelLeitor = "leitor";

    /// <summary>Os papéis que o dono concede (o dono não se concede: ele se transfere).</summary>
    public static IReadOnlyList<string> PapeisConcedidos { get; } = [PapelLeitor, PapelEditor];

    /// <summary>O projeto de design (US #669), em <c>/api/design/projetos/{id}/acessos</c>.</summary>
    public const string RecursoDesign = "design";

    /// <summary>O projeto do Orquestrador (US #693), em <c>/api/projetos/{id}/acessos</c>.</summary>
    public const string RecursoProjeto = "projeto";

    public static IReadOnlyList<string> Recursos { get; } = [RecursoDesign, RecursoProjeto];
}
