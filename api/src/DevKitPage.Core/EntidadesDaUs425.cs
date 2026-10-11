namespace DevKitPage.Core;

/// <summary>
/// Um item do catálogo de uma EMPRESA (US #425, fase 2): a skill, o Tech Plan ou o fluxo que um usuário dela
/// publicou para o time assinar. Único por (<see cref="EmpresaId"/>, <see cref="Tipo"/>, <see cref="Nome"/>): publicar
/// de novo cria uma <see cref="VersaoDeItem"/>, e não outro item. Retirar não apaga — a sincronia das máquinas
/// precisa saber que ele saiu.
/// </summary>
public sealed class ItemDeCatalogo
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Quem publicou a primeira versão: só ele (ou o gestor) retira o item.</summary>
    public int AutorId { get; set; }

    public Usuario? Autor { get; set; }
    public int VersaoAtual { get; set; }
    public string HashAtual { get; set; } = string.Empty;

    /// <summary>As dependências (<c>tipo:nome</c>), uma por linha — assinar o item assina também as do catálogo da empresa.</summary>
    public string Dependencias { get; set; } = string.Empty;

    public DateTime CriadoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
    public DateTime? RetiradoEmUtc { get; set; }
}

/// <summary>Uma versão publicada de um <see cref="ItemDeCatalogo"/>: o conteúdo (até 256 KB) e o hash dele.</summary>
public sealed class VersaoDeItem
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public ItemDeCatalogo? Item { get; set; }
    public int Versao { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string Conteudo { get; set; } = string.Empty;
    public int AutorId { get; set; }
    public DateTime PublicadaEmUtc { get; set; }
}

/// <summary>Um usuário que assina um item do catálogo da empresa: a sincronia do dev.kit dele traz o item.</summary>
public sealed class AssinaturaDeCatalogo
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public ItemDeCatalogo? Item { get; set; }
    public int UsuarioId { get; set; }
    public DateTime AssinadaEmUtc { get; set; }
}

/// <summary>
/// O link de definir a senha (US #425, fase 3): uso ÚNICO, válido por <see cref="OpcoesDaConta.ValidadeDoLinkHoras"/>.
/// Só o hash do token fica no banco — quem lê a base não entra na conta de ninguém.
/// </summary>
public sealed class LinkDeSenha
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CriadoEmUtc { get; set; }
    public DateTime ExpiraEmUtc { get; set; }
    public DateTime? UsadoEmUtc { get; set; }
}

/// <summary>
/// O trial de uma máquina (US #425, fase 4), pela IMPRESSÃO dela — o hash que o dev.kit tira da instalação do
/// Windows, e não o id anônimo (que reinstalar troca). É o que impede reinstalar de reiniciar os 14 dias.
/// </summary>
public sealed class TrialDeMaquina
{
    public long Id { get; set; }
    public string Impressao { get; set; } = string.Empty;
    public DateTime IniciadoEmUtc { get; set; }
}

/// <summary>Uma licença assinada que a API emitiu (US #425, fase 4) — a trilha da renovação e a base da janela.</summary>
public sealed class LicencaEmitida
{
    public long Id { get; set; }
    public int MaquinaId { get; set; }
    public Maquina? Maquina { get; set; }
    public int? UsuarioId { get; set; }

    /// <summary>Um de <c>TiposDeLicenca</c>: o trial ou a assinatura.</summary>
    public string Tipo { get; set; } = string.Empty;

    public DateTime EmitidaEmUtc { get; set; }
    public DateTime ValidaAteUtc { get; set; }
}

/// <summary>Um aviso do gateway de pagamento já processado (US #425, fase 5): o id do evento é único — o reenvio não duplica.</summary>
public sealed class EventoDePagamento
{
    public long Id { get; set; }
    public string EventoId { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public DateTime RecebidoEmUtc { get; set; }
}
