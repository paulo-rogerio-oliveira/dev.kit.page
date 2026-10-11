using System.Text.RegularExpressions;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O envio de e-mail falso (US #425): guarda as mensagens, e o teste lê o link de definir a senha do texto — o mesmo
/// caminho do dev que abre o e-mail.
/// </summary>
public sealed partial class EmailsDeTeste : IEnvioDeEmail
{
    private readonly List<MensagemDeEmail> _mensagens = [];

    public IReadOnlyList<MensagemDeEmail> Mensagens
    {
        get
        {
            lock (_mensagens)
                return _mensagens.ToArray();
        }
    }

    public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct)
    {
        lock (_mensagens)
            _mensagens.Add(mensagem);
        return Task.CompletedTask;
    }

    /// <summary>O token do ÚLTIMO link mandado ao e-mail.</summary>
    public string TokenPara(string email)
    {
        var mensagem = Mensagens.Last(m => string.Equals(m.Para, email, StringComparison.OrdinalIgnoreCase));
        return Uri.UnescapeDataString(Token().Match(mensagem.Texto).Groups[1].Value);
    }

    [GeneratedRegex(@"/definir-senha\?token=(\S+)")]
    private static partial Regex Token();
}
