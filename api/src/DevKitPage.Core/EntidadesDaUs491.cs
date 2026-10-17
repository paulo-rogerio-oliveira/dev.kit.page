namespace DevKitPage.Core;

/// <summary>
/// Um repositório no CATÁLOGO DE REPOSITÓRIOS de uma empresa (US #491): o caminho e a descrição que o agente do dev.kit
/// grava ao terminar uma task. Único por (<see cref="EmpresaId"/>, <see cref="Chave"/>): gravar de novo o mesmo
/// caminho troca a descrição — o catálogo é colaborativo, e a última leitura de quem trabalhou nele é a melhor.
/// </summary>
public sealed class RepositorioDaEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary>O caminho como veio (é o que o dev.kit clona).</summary>
    public string Origem { get; set; } = string.Empty;

    /// <summary>O caminho normalizado (<see cref="RegrasDoConhecimentoDaEmpresa.ChaveDaOrigem"/>): a unicidade na empresa.</summary>
    public string Chave { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;

    /// <summary>Quem gravou a última descrição.</summary>
    public int AutorId { get; set; }

    public Usuario? Autor { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
}
