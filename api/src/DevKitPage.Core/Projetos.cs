using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Índice relacional de identidade e concorrência; o estado portátil é um documento versionado.</summary>
public sealed class ProjetoCompartilhado
{
    public long Id { get; set; }
    public string Espaco { get; set; } = "";
    public int? EmpresaId { get; set; }
    public int UsuarioId { get; set; }
    public string Nivel { get; set; } = "";
    public string Time { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Autor { get; set; } = "";
    public int Versao { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
    public string Documento { get; set; } = "";
}

public sealed record GravacaoDoProjeto(int Status, ProjetoV1? Projeto, string Mensagem = "");

public interface IProjetosCompartilhados
{
    Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);
    Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct);
    Task<GravacaoDoProjeto> GravarAsync(EscopoDoDesign escopo, long? id, EnvioDeProjetoV1 envio, CancellationToken ct);
}

public static partial class RegrasDosProjetos
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,95}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugValido();

    public static string Validar(EnvioDeProjetoV1 e)
    {
        if (e.Slug is null || !SlugValido().IsMatch(e.Slug) || string.IsNullOrWhiteSpace(e.Nome) || e.Nome.Length > 200)
            return "Informe slug válido e nome de até 200 caracteres.";
        if (e.Nivel is not ("" or "usuario" or "time" or "empresa") || e.Time is null || e.Time.Length > 100)
            return "Nível ou time inválido.";
        if (e.Paralelismo is < 1 or > 32 || e.Versao is < 0 || e.Itens is null || e.Itens.Count > 1000)
            return "Paralelismo, versão ou quantidade de itens inválidos.";
        if (e.Repositorios is null || e.Anexos is null || e.Branch is null || e.Definicao is null || e.DestinoDaPr is null
            || e.Repositorios.Count > 100 || e.Anexos.Count > 1000 || e.Branch.Length > 250 || e.Definicao.Length > 200000)
            return "Definição ou repositórios inválidos.";
        if (e.Itens.Any(i => i is null || i.Numero <= 0 || string.IsNullOrWhiteSpace(i.Titulo) || i.Titulo.Length > 500
            || i.Repositorios is null || i.Componentes is null || i.Dependencias is null || i.Descricao is null
            || i.Descricao.Length > 200000 || i.Onda < 0 || i.WorkItemId < 0 || i.Prioridade is < 0 or > 4
            || !double.IsFinite(i.Horas) || i.Horas < 0 || i.Branch is null || i.Branch.Length > 250
            || i.Situacao is not ("planejado" or "rodando" or "pergunta" or "integrado" or "falhou")))
            return "Item inválido: confira número, título, situação, prioridade e horas.";
        if (e.Itens.Select(i => i.Numero).Distinct().Count() != e.Itens.Count)
            return "Há números de item repetidos.";
        if (e.Situacao is not ("rascunho" or "aguardando" or "aprovado" or "pausado" or "fechando" or "concluido"))
            return "Situação do projeto inválida.";
        return "";
    }
}
