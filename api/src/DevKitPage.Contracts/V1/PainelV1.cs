namespace DevKitPage.Contracts.V1;

/// <summary>O login do usuário do dashboard.</summary>
public sealed record LoginRequest(string Login, string Senha);

/// <summary>O token e o que a tela precisa saber dele.</summary>
/// <param name="Token">O JWT (Bearer).</param>
/// <param name="ExpiraEm">Quando ele vence — a web volta ao login nesse momento.</param>
/// <param name="DeveTrocarSenha">O usuário só acessa a troca de senha enquanto isto for verdade.</param>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiraEm, bool DeveTrocarSenha, string Login, bool EhAdmin);

/// <summary>A troca de senha (obrigatória no primeiro acesso do admin).</summary>
public sealed record TrocarSenhaRequest(string SenhaAtual, string NovaSenha);

/// <summary>Uma máquina que envia telemetria.</summary>
/// <param name="Id">O id interno — é o filtro das outras consultas.</param>
/// <param name="MaquinaId">O id anônimo que o dev.kit gerou.</param>
/// <param name="Apelido">Como a tela a mostra.</param>
public sealed record MaquinaResumo(
    int Id, string MaquinaId, string Apelido, string VersaoDevKit, DateTimeOffset RegistradaEm, DateTimeOffset? UltimoEnvioEm, long Eventos);

/// <summary>Um dia da série de quantidade.</summary>
public sealed record DiaDeUso(
    DateOnly Dia, long Sessoes, long Turnos, long Fluxos, long Ferramentas, long ComandosDelegados, long ArquivosAlterados);

/// <summary>A QUANTIDADE de uso no período (e por máquina, quando filtrada).</summary>
public sealed record QuantidadeResposta(
    DateOnly De, DateOnly Ate, long Sessoes, long Turnos, long Fluxos, long Ferramentas, long ComandosDelegados,
    long ArquivosAlterados, IReadOnlyList<DiaDeUso> SerieDiaria);

/// <summary>Uma causa de falha de turno (as de <c>TurnFailures</c> do dev.kit) e quantas vezes.</summary>
public sealed record CausaDeFalha(string Causa, long Quantidade);

/// <summary>
/// A QUALIDADE de uso no período. As taxas são nulas quando não há denominador (nenhum turno,
/// nenhuma nota, nenhum objetivo) — a tela mostra um traço, nunca NaN.
/// </summary>
/// <param name="TaxaDeFalha">Turnos com falha sobre turnos executados.</param>
/// <param name="NotaMedia">A média das notas dos avaliadores (0–100).</param>
/// <param name="RazaoCumpridosRecusados">Objetivos cumpridos por recusado.</param>
/// <param name="TurnosPorObjetivoCumprido">O retrabalho: quantos turnos custou cada objetivo aceito.</param>
public sealed record QualidadeResposta(
    DateOnly De, DateOnly Ate, long Turnos, long TurnosComFalha, double? TaxaDeFalha, double? DuracaoMediaDoTurnoMs,
    IReadOnlyList<CausaDeFalha> FalhasPorCausa, long Avaliacoes, double? NotaMedia, long ObjetivosCumpridos,
    long ObjetivosRecusados, double? RazaoCumpridosRecusados, double? TurnosPorObjetivoCumprido);

/// <summary>Uma linha do log de eventos.</summary>
public sealed record EventoDoLog(
    string EventId, int MaquinaId, string Apelido, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em);

/// <summary>Uma página de resultados.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int NumeroDaPagina, int Tamanho);
