namespace DevKitPage.Contracts.V1;

/// <summary>Somente campos editáveis; não aceita snapshot, decisão, recibo, design ou situação.</summary>
public sealed record EdicaoRemotaDoItemV1(int VersaoEsperada, string Titulo, string Descricao,
    string Fluxo, string Agente, string Modelo, IReadOnlyList<int> Dependencias,
    IReadOnlyList<string> Componentes, int Prioridade);
