namespace DevKitPage.Contracts.V1;

public sealed record ConfiguracaoDePushV1(bool Disponivel, string? ChavePublica);
public sealed record ChavesDaAssinaturaV1(string P256dh, string Auth);
public sealed record AssinaturaDePushV1(string Endpoint, ChavesDaAssinaturaV1 Keys);
public sealed record AssinaturaRegistradaV1(string Id);
public sealed record DestinoDoPushV1(string MaquinaId, string PendenciaId, string SnapshotId);
