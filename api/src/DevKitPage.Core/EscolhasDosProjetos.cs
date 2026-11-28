using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Campo opcional ausente preserva a escolha; texto/lista vazios removem explicitamente.</summary>
public static class EscolhasDosProjetos
{
    public static ItemDoProjetoV1 Preservar(ItemDoProjetoV1 envio, ItemDoProjetoV1? anterior) => envio with
    {
        Fluxo = envio.Fluxo ?? anterior?.Fluxo ?? "",
        Agente = envio.Agente ?? anterior?.Agente,
        Modelo = envio.Modelo ?? anterior?.Modelo,
        Design = envio.Design ?? anterior?.Design,
    };

    public static bool RebaixaReferencia(string? anterior, string? enviado)
        => anterior?.StartsWith("ref:", StringComparison.Ordinal) == true && !string.IsNullOrEmpty(enviado)
            && !enviado.StartsWith("ref:", StringComparison.Ordinal);

    public static EnvioDeProjetoV1 Preservar(EnvioDeProjetoV1 envio, ProjetoV1? anterior) => envio with
    {
        BaseDoBranch = envio.BaseDoBranch ?? anterior?.BaseDoBranch,
        FluxoPadrao = envio.FluxoPadrao ?? anterior?.FluxoPadrao,
        SkillDoPacote = envio.SkillDoPacote ?? anterior?.SkillDoPacote,
        SkillsDoPacote = envio.SkillsDoPacote ?? anterior?.SkillsDoPacote,
        Agente = envio.Agente ?? anterior?.Agente,
        Modelo = envio.Modelo ?? anterior?.Modelo,
        Modelos = envio.Modelos ?? anterior?.Modelos,
        PoliticaDeFinalizacao = envio.PoliticaDeFinalizacao ?? anterior?.PoliticaDeFinalizacao,
        NivelNoBoard = envio.NivelNoBoard ?? anterior?.NivelNoBoard,
        EstadosNoBoard = envio.EstadosNoBoard ?? anterior?.EstadosNoBoard,
        DisparadoEm = envio.DisparadoEm ?? anterior?.DisparadoEm,
        Itens = envio.Itens.Select(i => Preservar(i, anterior?.Itens.FirstOrDefault(a => a.Numero == i.Numero))).ToArray(),
    };

    public static string ValidarPolitica(EnvioDeProjetoV1 envio)
    {
        if (envio.PoliticaDeFinalizacao is not { } politica) return string.Empty;
        if (politica.Slug != envio.Slug || politica.Revisao < 1)
            return "A política da finalização exige o slug do projeto e revisão positiva.";
        if ((politica.Motivo?.Length ?? 0) > ProjetosV1.TamanhoMaximoDoTexto
            || (politica.IdentidadeDoFluxo?.Length ?? 0) > 200 || (politica.RevisaoDoFluxo?.Length ?? 0) > 128
            || (politica.FluxoDaFinalizacao?.Length ?? 0) > 200)
            return "A configuração da política excede os limites do contrato.";
        if (politica.Politica is not ("legado" or "finalizacao-unica"))
            return "Política da finalização desconhecida: atualize o cliente antes de alterar a política.";
        if (politica.Politica == "finalizacao-unica"
            && (string.IsNullOrWhiteSpace(politica.IdentidadeDoFluxo) || string.IsNullOrWhiteSpace(politica.RevisaoDoFluxo)
                || politica.ModeloDaFinalizacao is not { Agente.Length: > 0, Modelo.Length: > 0 }))
            return "A finalização única exige identidade/revisão do fluxo e seu próprio agente/modelo.";
        return string.Empty;
    }
}
