using DevKitPage.Contracts.V1;

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

/// <summary>
/// Uma CONVENÇÃO de uma empresa (US #491), no nível dela, no de um time ou no de um usuário. Única por
/// (<see cref="EmpresaId"/>, <see cref="Nivel"/>, <see cref="Time"/>, <see cref="UsuarioId"/>, <see cref="Chave"/>). O
/// time é um NOME — o que o dev declara nas Configurações do dev.kit, o mesmo do <c>{equipe}</c> do branch —, e não um
/// cadastro: a empresa não precisa montar times aqui para compartilhar a convenção de um.
/// </summary>
public sealed class ConvencaoDaEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary><c>empresa</c> ou <c>time</c> (<c>NiveisDeConvencao</c>).</summary>
    public string Nivel { get; set; } = string.Empty;

    /// <summary>O time (normalizado) da convenção de nível time; vazio nos outros níveis.</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>O dono da convenção de nível usuário; 0 nos outros níveis.</summary>
    public int UsuarioId { get; set; }

    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;

    /// <summary>
    /// De quem é o texto (US #663): <c>usuario</c> — a REGRA, escrita por pessoa — ou <c>agente</c> — a SUGESTÃO que um
    /// agente do dev.kit registrou (<see cref="OrigensDeConvencao"/>). A regra e a sugestão do mesmo tema coexistem.
    /// </summary>
    public string Origem { get; set; } = OrigensDeConvencao.Usuario;

    /// <summary>Sobe a cada gravação (US #663): a promoção com a versão velha é conflito.</summary>
    public int Versao { get; set; } = 1;

    public int AutorId { get; set; }
    public Usuario? Autor { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
}
