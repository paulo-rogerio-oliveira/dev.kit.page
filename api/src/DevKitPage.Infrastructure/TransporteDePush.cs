using System.Net;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>RFC 8291/aes128gcm e VAPID pela biblioteca; o payload contém somente o id opaco da entrega.</summary>
public sealed class TransporteDePush(IHttpClientFactory clientes, IOptions<OpcoesDoPush> opcoes) : ITransporteDePush
{
    public const string NomeDoCliente = "push-do-board";
    public async Task<ResultadoDoEnvioDePush> EnviarAsync(AssinaturaDePushV1 assinatura, string entrega, CancellationToken ct)
    {
        if (!opcoes.Value.Disponivel || RegrasDoPush.Validar(assinatura) is not null || !RegrasDoPush.IdValido(entrega))
            return ResultadoDoEnvioDePush.AssinaturaInvalida;
        using var http = clientes.CreateClient(NomeDoCliente);
        using var autenticacao = new VapidAuthentication(opcoes.Value.ChavePublica, opcoes.Value.ChavePrivada) { Subject = opcoes.Value.Assunto };
        var cliente = new PushServiceClient(http) { AutoRetryAfter = false, DefaultAuthentication = autenticacao };
        var destino = new PushSubscription { Endpoint = assinatura.Endpoint };
        destino.SetKey(PushEncryptionKeyName.P256DH, assinatura.Keys.P256dh);
        destino.SetKey(PushEncryptionKeyName.Auth, assinatura.Keys.Auth);
        var mensagem = new PushMessage(JsonSerializer.Serialize(new { entregaId = entrega }))
        {
            TimeToLive = RegrasDoPush.TtlSegundos, Topic = entrega, Urgency = PushMessageUrgency.Normal,
        };
        try
        {
            await cliente.RequestPushMessageDeliveryAsync(destino, mensagem, ct);
            return ResultadoDoEnvioDePush.Aceito;
        }
        catch (PushServiceClientException e)
        {
            return e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                ? ResultadoDoEnvioDePush.AssinaturaInvalida : ResultadoDoEnvioDePush.Repetir;
        }
        catch (HttpRequestException) { return ResultadoDoEnvioDePush.Repetir; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ResultadoDoEnvioDePush.Repetir; }
    }
}
