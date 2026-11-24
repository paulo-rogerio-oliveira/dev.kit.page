namespace DevKitPage.Contracts.V1;

/// <summary>O pacote imutavel conferido pelo CI. Nao transporta rascunhos do editor.</summary>
public sealed record PublicacaoGitV1(string Sha, string Referencia, string Motor, string Sha256, int Versao, byte[] Pacote);

/// <summary>Recibo duravel: identidade da origem e os enderecos da release exata.</summary>
public sealed record ReciboPublicacaoGitV1(string Sha, string Referencia, string Motor, string Sha256, int Versao,
    DateTimeOffset PublicadaEm, string Manifesto, string Download);

public sealed record ConferenciaGitV1(bool Simulacao, bool Repeticao, ReciboPublicacaoGitV1 Recibo);
