namespace DevKitPage.Contracts.V1;

/// <summary>
/// Os papéis do usuário (US #381, US #405) — o valor da claim <c>role</c> do JWT. O admin vê tudo e
/// administra as empresas e os usuários; o gestor vê só a empresa dele; o dev é o usuário do dev.kit:
/// entra no app (e baixa a versão), mas NÃO no painel.
/// </summary>
public static class Papeis
{
    public const string Admin = "admin";
    public const string Gestor = "gestor";
    public const string Dev = "dev";

    /// <summary>Os papéis que o admin atribui na gestão de usuários.</summary>
    public static IReadOnlyList<string> Todos { get; } = [Admin, Gestor, Dev];

    /// <summary>Quem entra no painel (<c>/api/dashboard</c>): o admin e o gestor — o dev, não.</summary>
    public static bool EntraNoPainel(string? papel) => papel is Admin or Gestor;
}

/// <summary>Uma empresa nova (só o admin cria): o nome, o plano contratado e os assentos.</summary>
public sealed record EmpresaNova(string Nome, string Plano, int Assentos);

/// <summary>Uma empresa na lista do admin.</summary>
/// <param name="CodigoDeAdesao">O código que o colaborador informa no dev.kit para aderir — entregue pelo gestor ao time.</param>
/// <param name="Colaboradores">Máquinas vinculadas (com consentimento) à telemetria da empresa.</param>
/// <param name="Gestor">O gestor da empresa (o nome, ou o login sem nome); nulo quando ela ainda não tem gestor (US #461).</param>
/// <param name="AssentosEmUso">Os assentos ocupados: os devs ativos da empresa, a mesma conta da adesão e da licença (US #461).</param>
/// <param name="Dados">Os dados básicos e o contrato (US #687); nulos só numa API anterior a ela.</param>
public sealed record EmpresaResumo(
    int Id, string Nome, string Plano, int Assentos, string CodigoDeAdesao, int Colaboradores, DateTimeOffset CriadaEm,
    string? Gestor = null, int AssentosEmUso = 0, DadosDaEmpresaV1? Dados = null);

/// <summary>
/// Os dados básicos e o contrato de uma empresa (US #687) — o que o painel "Editar" mostra além do nome, do plano e dos
/// assentos. O CNPJ vai só com os dígitos; a tela o formata.
/// </summary>
/// <param name="ContratoAte">Até quando vai o contrato: a validade da licença dos usuários dela (sem a definida um a um).</param>
/// <param name="AlteradaEm">A última edição pelo admin; nula na empresa nunca editada.</param>
public sealed record DadosDaEmpresaV1(
    string Cnpj, string Segmento, string Cidade, string Uf, string Contato, string Telefone, string Site, DateOnly? ContratoAte,
    DateTimeOffset? AlteradaEm, string AlteradaPor);

/// <summary>
/// A edição de uma empresa pelo admin (US #687): os dados básicos, o plano, o contrato e os ASSENTOS — que não descem
/// abaixo dos que estão em uso (quem já está dentro não perde o assento por uma edição).
/// </summary>
public sealed record EmpresaEditadaV1(
    string Nome, string Plano, int Assentos, string? Cnpj = null, string? Segmento = null, string? Cidade = null, string? Uf = null,
    string? Contato = null, string? Telefone = null, string? Site = null, DateOnly? ContratoAte = null);

/// <summary>Os segmentos que o admin escolhe (a lista do painel; o texto livre de antes da lista continua aceito).</summary>
public static class SegmentosDeEmpresa
{
    public static IReadOnlyList<string> Todos { get; } =
        ["Software / ERP", "Construção civil", "Financeiro", "Indústria", "Varejo", "Saúde", "Educação", "Governo", "Serviços", "Outro"];
}

/// <summary>As 27 UFs, na ordem do seletor.</summary>
public static class Ufs
{
    public static IReadOnlyList<string> Todas { get; } =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS",
        "SC", "SE", "SP", "TO",
    ];
}

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
