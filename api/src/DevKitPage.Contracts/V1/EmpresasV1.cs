namespace DevKitPage.Contracts.V1;

/// <summary>
/// Os papéis do usuário do dashboard (US #381) — o valor da claim <c>role</c> do JWT. O admin vê tudo e
/// administra as empresas; o gestor vê só a empresa dele.
/// </summary>
public static class Papeis
{
    public const string Admin = "admin";
    public const string Gestor = "gestor";
}

/// <summary>Uma empresa nova (só o admin cria): o nome, o plano contratado e os assentos.</summary>
public sealed record EmpresaNova(string Nome, string Plano, int Assentos);

/// <summary>Uma empresa na lista do admin.</summary>
/// <param name="CodigoDeAdesao">O código que o colaborador informa no dev.kit para aderir — entregue pelo gestor ao time.</param>
/// <param name="Colaboradores">Máquinas vinculadas (com consentimento) — os assentos ocupados.</param>
public sealed record EmpresaResumo(
    int Id, string Nome, string Plano, int Assentos, string CodigoDeAdesao, int Colaboradores, DateTimeOffset CriadaEm);

/// <summary>O convite de um gestor para a empresa: o login dele.</summary>
public sealed record GestorNovo(string Login);

/// <summary>
/// O gestor criado: a senha inicial é devolvida UMA vez (o admin a repassa), e o primeiro acesso exige
/// a troca — a mesma regra do admin semeado.
/// </summary>
public sealed record GestorCriado(int Id, string Login, string SenhaInicial);

/// <summary>Um colaborador que CONSENTIU em compartilhar o uso com o gestor da empresa (uma máquina).</summary>
/// <param name="MaquinaId">O id interno da máquina — o filtro das consultas.</param>
/// <param name="Colaborador">O nome que o próprio colaborador informou no dev.kit (vazio: a tela mostra o apelido).</param>
public sealed record ColaboradorResumo(
    int MaquinaId, string Colaborador, string Apelido, string Empresa, string VersaoDevKit, DateTimeOffset ConsentiuEm, DateTimeOffset? UltimoEnvioEm);

/// <summary>Uma linha da exportação: o uso de um colaborador (máquina) num dia.</summary>
public sealed record LinhaExportada(
    string Colaborador, string Apelido, string Empresa, DateOnly Dia, long Sessoes, long Turnos, long TurnosComFalha,
    long Ferramentas, long ComandosDelegados, long ArquivosAlterados, long ObjetivosCumpridos, long ObjetivosRecusados);
