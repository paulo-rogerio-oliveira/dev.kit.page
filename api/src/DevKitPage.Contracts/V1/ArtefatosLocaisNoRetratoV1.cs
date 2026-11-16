using System.Text.Json.Serialization;

namespace DevKitPage.Contracts.V1;

/// <summary>Extensão opcional do retrato; identifica resultados, sem conceder acesso a downloads ou destinos. Ausência não confirma suporte.</summary>
public sealed record ArtefatosLocaisNoRetratoV1(int Contrato, bool OperacoesDisponiveis,
    IReadOnlyList<ArtefatoLocalNoRetratoV1> Artefatos, string Aviso = "");

/// <summary>Estado confirmado na máquina do envelope. Identidades de repositório/recibo são opacas; não leva caminhos locais.</summary>
public sealed record ArtefatoLocalNoRetratoV1(long Id, string Situacao, string Diagnostico, DateTimeOffset CriadoEm,
    DateTimeOffset? TerminadoEm, string? ConjuntoHash, IReadOnlyList<FonteNoRetratoV1> Fontes,
    IReadOnlyList<AlvoNoRetratoV1> Alvos, string Hash, long Tamanho, ComandoDoArtefatoNoRetratoV1? Comando,
    IReadOnlyList<PreviewLocalNoRetratoV1> Previews,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicacaoNoRetratoV1? Publicacao = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PacoteNoRetratoV1? Pacote = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DadosDoConjuntoNoRetratoV1? Detalhes = null);

/// <summary>Correlação com a consulta autorizada de publicações; nunca contém URL assinada ou caminho do host.</summary>
public sealed record PublicacaoNoRetratoV1(long IdRemoto, string Tipo, string Repositorio, string Sha, string Hash, long Tamanho);
public sealed record PacoteNoRetratoV1(string Repositorio, string Sha, string Versao, string Hash, long Tamanho,
    long IdRemoto, DateTimeOffset? ValidoAte);

public sealed record FonteNoRetratoV1(string Id, string Nome, string Branch, string Sha, string? BranchBase, string? ShaBase);
public sealed record AlvoNoRetratoV1(string Repositorio, string Nome, string Receita, string? Framework, string? Runtime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Saida = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Assembly = null);
public sealed record DadosDoConjuntoNoRetratoV1(string PoliticaDePins, IReadOnlyList<PinNoRetratoV1> Pins,
    IReadOnlyList<ExclusaoNoRetratoV1> Exclusoes, OpcoesDoConjuntoNoRetratoV1? Opcoes = null);
public sealed record PinNoRetratoV1(string Consumidor, string Papel, string Repositorio, string Declarado, string Capturado,
    string ArvoreDeclarada, string ArvoreCapturada, string Raiz, string Politica);
public sealed record ExclusaoNoRetratoV1(string Repositorio, string Caminho, string Motivo);
public sealed record OpcoesDoConjuntoNoRetratoV1(string RepositorioPrincipal, string FormatoSolicitado,
    DestinoSwaNoRetratoV1 PreviewRemoto);
public sealed record DestinoSwaNoRetratoV1(string App, string Grupo, string Assinatura, string Tenant);
public sealed record ComandoDoArtefatoNoRetratoV1(long? Id, string Recibo, DateTimeOffset? Inicio, DateTimeOffset? Fim,
    string? MotivoTermino, int? Exit, string LogParcial);

/// <summary>URL HTTP de loopback exclusivamente na máquina do envelope; nunca um link para o dispositivo do Board.</summary>
public sealed record PreviewLocalNoRetratoV1(string Receita, string Aplicacao, string Ciclo, long Versao, string Pedido,
    string Situacao, bool NestaMaquina, string? Url, string Diagnostico, DateTimeOffset AtualizadoEm,
    ProntidaoNoRetratoV1 Prontidao, IReadOnlyList<ComponenteNoRetratoV1> Componentes, AberturaNoRetratoV1? Abertura = null);

public sealed record ProntidaoNoRetratoV1(bool Front, bool Api, bool Dependencias, bool Jornada);
public sealed record ComponenteNoRetratoV1(string Id, string Repositorio, string Situacao, int? Porta,
    DateTimeOffset? ProntoEm, string Diagnostico, string LogParcial);
public sealed record AberturaNoRetratoV1(string Id, string Situacao, string Diagnostico, DateTimeOffset AtualizadoEm);
