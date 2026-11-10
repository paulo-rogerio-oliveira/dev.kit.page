namespace DevKit.Matrix;

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
    CelulaMatrix[] Celulas);
public sealed record RegistroMatrix(string Celula, int RevisaoDaDefinicao, string Responsavel,
    string Tipo, string Resultado, string Justificativa, string[] Evidencias, Dictionary<string, string> Shas);
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
