namespace DevKitPage.Contracts.V1;

/// <summary>
/// Projeção de leitura. Não é um snapshot e não pode ser usada para gravar ou sincronizar planos. A marca de disparo
/// (<see cref="DisparadoEm"/>, opcional) separa o aprovado que aguarda o disparo do que já está em execução.
/// </summary>
public sealed record ResumoDoProjetoV1(
    long Id, string Slug, string Nome, string Nivel, string Time, string Situacao, int Versao,
    DateTimeOffset AtualizadoEm, string Papel, int TotalDeItens, int ItensConcluidos,
    int ItensEmExecucao, int ItensComPergunta, int ItensFalhos, DateTimeOffset? DisparadoEm = null)
{
    public int Progresso => TotalDeItens == 0 ? 0 : 100 * ItensConcluidos / TotalDeItens;
    public bool TemPendencias => ItensComPergunta > 0 || ItensFalhos > 0;
}
