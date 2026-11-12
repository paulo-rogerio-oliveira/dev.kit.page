namespace DevKit.Matrix;

public sealed record CabecalhoMatrix(string Aplicacao, string Tipo, string Id, string Projeto, int Versao);

// Contrato matrix-v1, espelhado em DevKitPage.Contracts. Não depende da UI ou do transporte.
public sealed record EixoMatrix(string Id, string Nome, string Orientacao = "");
public sealed record RelacaoMatrix(string Id, string Origem, string Destino, string[] Layers,
    string Gatilho, string Motivo, string Propagacao, string Verificacao);
public sealed record MapaMatrix(string Nome, string[] Repositorios, EixoMatrix[] Componentes,
    EixoMatrix[] Layers, RelacaoMatrix[] Relacoes);
public sealed record ResponsavelMatrix(string Id, int? Item = null);
public sealed record CelulaMatrix(string Id, string Componente, string Layer, string Obrigacao,
    bool Aplicavel, string Justificativa, string Origem, ResponsavelMatrix[] Responsaveis,
    string Criterio, string EvidenciaEsperada);
public sealed record MatrizMatrix(int RevisaoDoMapa, EixoMatrix[] Componentes, EixoMatrix[] Layers,
    CelulaMatrix[] Celulas)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CoberturaRelacaoMatrix[]? Coberturas { get; init; }
}
/// <summary>A obrigação de conferir uma relação tem célula e responsáveis explícitos.</summary>
public sealed record CoberturaRelacaoMatrix(string Relacao, string Celula);
public sealed record RegistroMatrix(string Celula, int RevisaoDaDefinicao, string Responsavel,
    string Tipo, string Resultado, string Justificativa, string[] Evidencias, Dictionary<string, string> Shas)
{
    /// <summary>Vínculo emitido pelo host; ausente nos registros anteriores, preservados como histórico.</summary>
    public string Evento { get; init; } = "";
    public string SessaoAvaliada { get; init; } = "";
    public long Avaliador { get; init; }
    public int RevisaoEfetiva { get; init; }
    public int RevisaoDoContexto { get; init; }
    public string RevisaoDaEntrega { get; init; } = "";
}
/// <summary>Recibo imutável do recorte fornecido automaticamente pelo host, um por evento e matriz.</summary>
public sealed record EventoMatrix(string Evento, string Matriz, string Maquina, string SessaoAvaliada,
    long Avaliador, int Revisao, int RevisaoEfetiva, int RevisaoDoContexto,
    string[] Responsaveis, string[] Celulas, Dictionary<string, string> Shas, string RevisaoDaEntrega)
{
    public string[] Dependencias { get; init; } = [];
}
public sealed record VinculoMatrix(string Responsavel, string Sessao, int? Item = null, string Maquina = "");

/// <summary>Um documento por stream. Projeto é slug; vazio é demanda avulsa, cuja identidade é Id.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentoMatrix
{
    public int Contrato { get; init; } = 1;
    public string Aplicacao { get; init; } = "";
    public string Tipo { get; init; } = "mapa";
    public string Id { get; init; } = "";
    public string Projeto { get; init; } = "";
    public MapaMatrix? Mapa { get; init; }
    public MatrizMatrix? Matriz { get; init; }
    public RegistroMatrix? Registro { get; init; }
    public VinculoMatrix? Vinculo { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EventoMatrix? Evento { get; init; }
}
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EdicaoMatrix(int VersaoEsperada, DocumentoMatrix Documento);
public sealed record RevisaoMatrix(int Versao, string Autor, DateTimeOffset Em, DocumentoMatrix Documento);
public sealed record MudancaMatrix(string[] Celulas, string[] Responsaveis);
public sealed record ResultadoMatrix(bool Aplicado, RevisaoMatrix Revisao, MudancaMatrix Mudancas);

/// <summary>Journal imutável; inserir exige versão exata inclusive na primeira gravação (zero).</summary>
public interface IArmazemMatrix
{
    IReadOnlyList<RevisaoMatrix> HistoricoMatrix(string aplicacao, string tipo, string id);
    RevisaoMatrix AcrescentarMatrix(EdicaoMatrix edicao, string autor, DateTimeOffset em);
}
