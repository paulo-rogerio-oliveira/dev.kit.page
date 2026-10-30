using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Infrastructure;

/// <summary>As rotas de sincronização reutilizam a persistência e a autorização já existentes.</summary>
public sealed class ProjetosCompartilhados(IProjetosDaEmpresa projetos) : IProjetosCompartilhados
{
    public Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct)
        => projetos.ListarAsync(escopo, time, desde, ct);

    public Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
        => projetos.ObterAsync(escopo, id, ct);

    public async Task<GravacaoDoProjeto> GravarAsync(EscopoDoDesign escopo, long? id, EnvioDeProjetoV1 envio, CancellationToken ct)
    {
        var erro = RegrasDosProjetos.Validar(envio);
        if (erro.Length > 0) return new(400, null, erro);
        ProjetoV1? atual;
        if (id is { } numero)
        {
            atual = await projetos.ObterAsync(escopo, numero, ct);
            if (atual is null) return new(404, null, "Projeto não encontrado.");
            var nivel = RegrasDoDesign.NivelEfetivo(escopo, envio.Nivel);
            var time = nivel == NiveisDeConvencao.Time ? RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time) : "";
            if (atual.Slug != envio.Slug || atual.Nivel != nivel || atual.Time != time)
                return new(400, null, "A identidade do projeto não pode mudar durante a sincronização.");
        }
        else atual = null;
        var resultado = await projetos.GravarAsync(escopo, envio, ct);
        if (resultado.Valor is { } gravado) return new(gravado.Novo ? 201 : 200, gravado.Projeto);
        if (resultado.Falha == FalhaNaGestao.Conflito)
        {
            atual = id is { } existente ? await projetos.ObterAsync(escopo, existente, ct)
                : (await projetos.ListarAsync(escopo, null, null, ct)).FirstOrDefault(p => p.Slug == envio.Slug);
            return new(409, atual, "O projeto mudou em outra máquina. Mescle antes de reenviar.");
        }
        return new(resultado.Falha == FalhaNaGestao.NaoEncontrado ? 404 : 400, null, "Projeto inválido.");
    }
}
