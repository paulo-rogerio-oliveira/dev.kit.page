using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Infrastructure;

public sealed partial class ProjetosDaEmpresa
{
    /// <summary>Primeiro compartilhamento, versão zero. Preserva o recibo local e audita separadamente o importador.</summary>
    public Task<ResultadoDaGestao<ProjetoGravado>> ImportarDecisoesLocaisAsync(EscopoDoDesign escopo,
        EnvioDeProjetoV1 envio, CancellationToken ct)
    {
        var problema = RegrasDosProjetos.Validar(envio);
        var itens = envio.Itens ?? [];
        var retirados = itens.Where(CicloDosItensV1.Retirado).ToArray();
        if (problema.Length > 0 || envio.Versao is not (null or 0) || retirados.Length == 0
            || itens.Any(i => i.ImportacaoDaDecisao is not null || i.ReservasDeEscrita?.Paradas.Length > 0)
            || retirados.Select(i => i.DecisaoHumana?.Pedido).Distinct().Count() != retirados.Length
            || retirados.Any(i => !ReciboLocalValido(i, envio)))
            return Task.FromResult(ResultadoDaGestao<ProjetoGravado>.Falhou(FalhaNaGestao.Recusada,
                "Importação inicial exige versão zero, recibos locais válidos e reservas sem provas de parada não autenticadas."));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envio)))).ToLowerInvariant();
        var origem = new ImportacaoDaDecisaoV1(escopo.UsuarioId.ToString(CultureInfo.InvariantCulture), relogio.GetUtcNow(), hash);
        var proposta = envio with { Itens = itens.Select(i => CicloDosItensV1.Retirado(i)
            ? i with { ImportacaoDaDecisao = origem } : i).ToArray() };
        return GravarInternoAsync(escopo, proposta, true, ct, hash);
    }

    private bool ReciboLocalValido(ItemDoProjetoV1 item, EnvioDeProjetoV1 envio)
    {
        if (item.DecisaoHumana is not { } d || !Guid.TryParse(d.Pedido, out _)
            || string.IsNullOrWhiteSpace(d.Autor) || d.Autor.Length > 256
            || string.IsNullOrWhiteSpace(d.Motivo) || d.Motivo.Length > 2000
            || string.IsNullOrWhiteSpace(d.Confirmacao) || d.Confirmacao.Length > 2000
            || string.IsNullOrWhiteSpace(d.Snapshot) || d.Snapshot.Length > 1_000_000
            || d.VersaoEsperada is not { Length: 64 } || !d.VersaoEsperada.All(Uri.IsHexDigit)
            || d.Em <= DateTimeOffset.UnixEpoch || d.Em > relogio.GetUtcNow().AddMinutes(5) || d.Geracao != 1
            || !ProjetosV1.SituacoesDoItem.Contains(d.EstadoAnterior)
            || (d.EstadoAnterior is ProjetosV1.ItemRodando or ProjetosV1.ItemPergunta or ProjetosV1.ItemSolicitado
                or ProjetosV1.ItemAguardandoConfirmacao) && !d.ParadaPendente
            || d.EstadoAnterior is ProjetosV1.ItemConcluidoManualmente or ProjetosV1.ItemCancelado
            || d.Acao is not (CicloDosItensV1.Concluir or CicloDosItensV1.Cancelar)
            || item.Situacao != (d.Acao == CicloDosItensV1.Concluir ? ProjetosV1.ItemConcluidoManualmente : ProjetosV1.ItemCancelado))
            return false;
        try
        {
            using var snapshot = JsonDocument.Parse(d.Snapshot);
            var raiz = snapshot.RootElement;
            return raiz.GetProperty("Item").GetProperty("Numero").GetInt32() == item.Numero
                && raiz.GetProperty("Item").GetProperty("Situacao").GetString() == d.EstadoAnterior
                && raiz.GetProperty("Anexos").EnumerateArray().Select(a => a.GetString()).SequenceEqual(envio.Anexos);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
