namespace DevKitPage.Contracts.V1;

/// <summary>Contrato aditivo: os downloads individuais e os registros anteriores continuam em /artefatos.</summary>
public sealed record PedidoDeArtefatosV1(string Id, long ProjetoId, DateTimeOffset CriadoEm,
    IReadOnlyList<ResultadoDoPedidoDeArtefatosV1> Resultados)
{
    public string Estado
    {
        get
        {
            var ativos = Resultados.Where(a => a.Estado != "nao-aplicavel").ToArray();
            if (ativos.Length == 0) return "nao-aplicavel";
            var estados = ativos.Select(a => a.ValidoAte <= DateTimeOffset.UtcNow ? "expirado" : a.Estado).ToArray();
            if (estados.Contains("falhou")) return estados.All(e => e == "falhou") ? "falhou" : "parcial";
            if (estados.Contains("expirado")) return "expirado";
            if (estados.Any(e => e is "envio-pendente" or "registro-pendente")) return "publicacao-pendente";
            if (estados.All(e => e == "publicado")) return "publicado";
            if (estados.All(e => e is "publicado" or "local-pronto")) return "local-pronto";
            return "em-andamento";
        }
    }
}

public sealed record ResultadoDoPedidoDeArtefatosV1(long Id, long ProjetoId, string Tipo, string Repositorio, string Sha,
    string Situacao, string Link, DateTimeOffset? ValidoAte, string Hash, long Tamanho, string Ambiente, string Detalhe,
    DateTimeOffset CriadoEm, DateTimeOffset? TerminadoEm = null, long IdRemoto = 0, CicloDoResultadoV1? Ciclo = null)
{
    public string Estado => Ciclo?.Estado ?? (Situacao == "falhou" ? "falhou" : Situacao == "gerando" ? "gerando"
        : IdRemoto > 0 ? "publicado" : "local-pronto");
}

public sealed record CicloDoResultadoV1(string Estado, string Etapa, string Recibo = "", long ComandoId = 0,
    string Branch = "", string Formato = "", string PedidoId = "");
