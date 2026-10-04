namespace DevKitPage.Contracts.V1;

/// <summary>
/// O pedido de demonstração que o visitante envia pelo formulário da landing (rota pública). Só o
/// que ele mesmo informou — nada de IP, navegador ou origem é gravado com o pedido.
/// </summary>
/// <param name="Nome">Obrigatório.</param>
/// <param name="Email">Obrigatório: é por ele que o contato acontece.</param>
/// <param name="Empresa">Opcional.</param>
/// <param name="Mensagem">Opcional: o que o visitante quer ver na demonstração.</param>
/// <param name="Consentimento">O aceite (LGPD) do uso dos dados para o contato — sem ele o pedido é recusado.</param>
public sealed record PedidoDeDemonstracaoV1(string? Nome, string? Email, string? Empresa, string? Mensagem, bool Consentimento);

/// <summary>A resposta do pedido gravado (201).</summary>
public sealed record PedidoDeDemonstracaoCriadoV1(long Id, DateTimeOffset RecebidoEm);

/// <summary>Um pedido de demonstração na lista do dashboard (rota autenticada).</summary>
public sealed record DemonstracaoResumo(
    long Id, string Nome, string Email, string Empresa, string Mensagem, DateTimeOffset RecebidoEm, DateTimeOffset ConsentimentoEm);
