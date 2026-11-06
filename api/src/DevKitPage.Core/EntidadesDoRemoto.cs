namespace DevKitPage.Core;

/// <summary>
/// Uma máquina do dev.kit vista pelo controle remoto: o último heartbeat e o último retrato. Única por
/// (<see cref="UsuarioId"/>, <see cref="MaquinaId"/>) — o mesmo id anônimo enviado por outro usuário é OUTRA linha, e
/// nunca a máquina dele. O retrato fica inteiro em JSON: é lido e substituído inteiro, e uma tabela para cada lista só
/// multiplicaria o esquema.
/// </summary>
public sealed class MaquinaRemota
{
    public long Id { get; set; }

    /// <summary>O dono: o usuário do JWT que publicou. Só ele lê.</summary>
    public int UsuarioId { get; set; }

    public string MaquinaId { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string VersaoDevKit { get; set; } = string.Empty;
    public string Origem { get; set; } = string.Empty;
    public bool AppAberto { get; set; }
    public string EstadoDoAgente { get; set; } = string.Empty;
    public bool TurnoEmAndamento { get; set; }

    /// <summary>Quando a API recebeu o último heartbeat (o relógio dela); nulo antes do primeiro.</summary>
    public DateTime? UltimoHeartbeatEmUtc { get; set; }

    /// <summary>O último retrato aplicado, em JSON (<c>RetratoV1</c>); vazio antes do primeiro.</summary>
    public string Retrato { get; set; } = string.Empty;

    /// <summary>O <c>GeradoEm</c> do retrato gravado: o mais antigo que chegar depois não o substitui.</summary>
    public DateTime? RetratoGeradoEmUtc { get; set; }

    public DateTime? RetratoRecebidoEmUtc { get; set; }
    public DateTime CriadaEmUtc { get; set; }

    /// <summary>O último contato (heartbeat ou retrato): a retenção conta daqui.</summary>
    public DateTime AtualizadaEmUtc { get; set; }
}
