using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Journal aditivo. ProjetoId fixa o ACL; sem projeto somente o dono da demanda/aplicação.</summary>
public sealed class RevisaoMatrixNoBanco
{
    public long Id { get; set; }
    public string Espaco { get; set; } = "";
    public string Aplicacao { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string DocumentoId { get; set; } = "";
    public int Versao { get; set; }
    public int DonoId { get; set; }
    public long? ProjetoId { get; set; }
    public int AutorId { get; set; }
    public DateTime EmUtc { get; set; }
    public string Documento { get; set; } = "";
}
public sealed record RespostaMatrix<T>(int Status, T? Valor = default, string Erro = "");
public interface IMatrizes
{
    Task<RespostaMatrix<CabecalhoMatrix[]>> ListarAsync(EscopoDoDesign escopo, CancellationToken ct);
    Task<RespostaMatrix<RevisaoMatrix[]>> HistoricoAsync(EscopoDoDesign escopo, string aplicacao, string tipo, string id, CancellationToken ct);
    Task<RespostaMatrix<ResultadoMatrix>> EditarAsync(EscopoDoDesign escopo, EdicaoMatrix edicao, bool aplicar, CancellationToken ct);
}
