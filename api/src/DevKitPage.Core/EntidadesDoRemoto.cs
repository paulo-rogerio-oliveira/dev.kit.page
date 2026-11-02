namespace DevKitPage.Core;

/// <summary>
/// Uma máquina no controle remoto: o último heartbeat que o dono publicou com o JWT dele. É DIFERENTE da <see cref="Maquina"/>
/// da telemetria, que entra pela chave e é anônima: aqui a identidade é (<see cref="UsuarioId"/>, <see cref="MaquinaId"/>), e o
/// mesmo GUID em dois usuários são duas linhas que não se enxergam.
/// </summary>
public sealed class MaquinaRemota
{
    public long Id { get; set; }

    /// <summary>O dono: o usuário do token. O site só lê a máquina dele.</summary>
    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>O GUID anônimo, em 32 dígitos minúsculos (<see cref="RegrasDoRemoto.NormalizarMaquina"/>).</summary>
    public string MaquinaId { get; set; } = string.Empty;

    public string Apelido { get; set; } = string.Empty;
    public string VersaoDevKit { get; set; } = string.Empty;
    public bool DevKitAberto { get; set; }
    public string Agente { get; set; } = string.Empty;
    public int TurnosEmAndamento { get; set; }

    /// <summary>A hora do SERVIDOR: um relógio errado na máquina não a deixa parecer conectada.</summary>
    public DateTime UltimoHeartbeatEmUtc { get; set; }

    public DateTime CriadaEmUtc { get; set; }

    public RetratoRemoto? Retrato { get; set; }
}

/// <summary>
/// O último retrato de uma máquina, à parte da <see cref="MaquinaRemota"/>: o heartbeat, bem mais frequente, não regrava o
/// documento, e a lista de máquinas não o carrega. O <see cref="Documento"/> é o <c>RetratoV1</c> em JSON, lido inteiro.
/// </summary>
public sealed class RetratoRemoto
{
    public long MaquinaRemotaId { get; set; }

    public MaquinaRemota? Maquina { get; set; }

    /// <summary>A data do retrato, limitada à hora do servidor: é ela que descarta o que chega fora de ordem.</summary>
    public DateTime GeradoEmUtc { get; set; }

    public DateTime RecebidoEmUtc { get; set; }

    public string Documento { get; set; } = string.Empty;
}
