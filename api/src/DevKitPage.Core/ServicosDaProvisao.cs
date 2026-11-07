using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A provisão vista pela API PÚBLICA: o usuário pede, acompanha, repete e remove — e a API só GRAVA o pedido. Ela não tem
/// permissão nenhuma no Azure: quem executa é o worker (<see cref="IMotorDeProvisao"/>), noutro processo. O isolamento é o
/// <c>usuarioId</c> do token em toda operação: o recurso de outro usuário é inexistente (404).
/// </summary>
public interface IProvisao
{
    /// <summary>Se o usuário pode provisionar, e de onde vem a liberação (a permissão do admin ou o plano).</summary>
    Task<PermissaoDeProvisaoV1> PermissaoAsync(int usuarioId, CancellationToken ct);

    /// <summary>Os recursos do usuário, os mais novos primeiro (os removidos também: é o histórico do que custou).</summary>
    Task<IReadOnlyList<RecursoProvisionadoV1>> RecursosAsync(int usuarioId, CancellationToken ct);

    /// <summary>O recurso com a auditoria; nulo quando não é do usuário.</summary>
    Task<DetalheDoRecursoV1?> DetalheAsync(int usuarioId, long id, CancellationToken ct);

    /// <summary>
    /// Pede a fila na região (validada antes). IDEMPOTENTE: com a cota cheia, devolve a fila ativa que já existe, sem criar
    /// outra (o <c>Novo</c> falso) — o duplo clique e o reenvio não duplicam recurso pago.
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuarioId, string autor, string regiao, CancellationToken ct);

    /// <summary>O uso da cota de cada tipo de aplicativo e o custo estimado do mês, com o alerta e o bloqueio.</summary>
    Task<CotaDaProvisaoV1> CotaAsync(int usuarioId, CancellationToken ct);

    /// <summary>
    /// Pede o App Service ou o Static Web App (validado antes: <see cref="RegrasDaProvisao.ValidarAplicativo"/>). A cota do tipo
    /// e o teto do custo são conferidos NA MESMA transação que grava o pedido, com o usuário travado — dois pedidos simultâneos
    /// não passam juntos da cota nem do teto (409 com o código). IDEMPOTENTE pelo nome do usuário: o aplicativo ativo do
    /// mesmo tipo e nome volta como está (o <c>Novo</c> falso).
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> PedirAplicativoAsync(int usuarioId, string autor, AplicativoPedido pedido, CancellationToken ct);

    /// <summary>Devolve ao worker a operação que FALHOU, do começo das tentativas. Só no estado <c>falhou</c> (409 nos outros).</summary>
    Task<ResultadoDaGestao<PedidoAceito>> RepetirAsync(int usuarioId, string autor, long id, CancellationToken ct);

    /// <summary>
    /// Pede a remoção do recurso pronto (ou que falhou). Idempotente: a remoção já pedida, ou feita, volta como está. Com o
    /// recurso ainda em criação, 409 — remove-se depois que ele terminar. O APLICATIVO só se remove com o
    /// <paramref name="codigo"/> do segundo fator (403 com <see cref="ProvisaoV1.CodigoSegundoFator"/> sem ele ou errado).
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> RemoverAsync(int usuarioId, string autor, long id, string? codigo, CancellationToken ct);
}

/// <summary>O pedido gravado: o recurso e se ele nasceu agora (202) ou já existia (200).</summary>
public sealed record PedidoAceito(RecursoProvisionadoV1 Recurso, bool Novo);

/// <summary>O pedido de aplicativo JÁ validado: o tipo, o nome do usuário, a região aceita e o plano com o custo.</summary>
public sealed record AplicativoPedido(string Tipo, string Rotulo, string Regiao, PlanoDeAplicativoV1 Plano);

/// <summary>
/// O MOTOR da provisão, o lado do worker: reserva o próximo pedido, e conclui ou falha a execução. A reserva usa o token de
/// concorrência (<see cref="RecursoProvisionado.Versao"/>), então dois workers nunca executam o mesmo pedido; e vence
/// (<see cref="OpcoesDaProvisao.ReservaMinutos"/>), então o worker que caiu no meio não o prende.
/// </summary>
public interface IMotorDeProvisao
{
    /// <summary>O próximo pedido vencido, já reservado a <paramref name="trabalhador"/> (com a tentativa contada); nulo sem nenhum.</summary>
    Task<RecursoProvisionado?> ReservarAsync(string trabalhador, CancellationToken ct);

    /// <summary>A operação deu certo: a criação fica <c>pronto</c>, a remoção <c>removido</c>. Falso quando a reserva não é mais dele.</summary>
    Task<bool> ConcluirAsync(long id, string trabalhador, RecursoNaNuvem? recurso, CancellationToken ct);

    /// <summary>
    /// Um passo da criação ficou pronto no Azure (o plano do App Service): ele é gravado no recurso, com a auditoria, e a
    /// repetição depois de uma falha retoma do passo seguinte. Falso quando a reserva não é mais dele.
    /// </summary>
    Task<bool> RegistrarPassoAsync(long id, string trabalhador, string passo, CancellationToken ct);

    /// <summary>
    /// A operação falhou: a TRANSITÓRIA volta a <c>pedido</c> com a espera (<see cref="RegrasDaProvisao.Espera"/>) até
    /// <see cref="OpcoesDaProvisao.MaximoDeTentativas"/>; a definitiva, ou a que esgotou as tentativas, fica <c>falhou</c>.
    /// </summary>
    Task<bool> FalharAsync(long id, string trabalhador, string mensagem, bool transitoria, CancellationToken ct);
}

/// <summary>
/// A nuvem que o worker comanda — o Azure Resource Manager em produção (<c>DevKitPage.Provisao.ProvedorDoAzure</c>), um falso
/// nos testes. As operações são IDEMPOTENTES (o PUT do ARM cria ou confirma; remover o que não existe é sucesso): o pedido
/// repetido depois de uma queda no meio chega ao mesmo resultado.
/// </summary>
public interface IProvedorDeNuvem
{
    /// <summary>Cria (ou confirma) o grupo do usuário, a conta de armazenamento e a fila.</summary>
    Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct);

    /// <summary>Remove a conta de armazenamento (e a fila com ela). O grupo do usuário fica: vazio, não custa.</summary>
    Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct);

    /// <summary>Cria (ou confirma) o grupo do usuário e o plano do App Service (Linux, no SKU pedido) — o 1º passo do App Service.</summary>
    Task CriarPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct);

    /// <summary>Cria (ou confirma) o grupo e o aplicativo — o site no plano, ou o Static Web App — e devolve o id e o endereço.</summary>
    Task<RecursoNaNuvem> CriarAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct);

    /// <summary>Remove o aplicativo (o que não existe é sucesso).</summary>
    Task RemoverAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct);

    /// <summary>Remove o plano do App Service, DEPOIS do aplicativo (o que não existe é sucesso).</summary>
    Task RemoverPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct);
}

/// <summary>O que o worker pede à nuvem: os nomes JÁ decididos no pedido (o repetido usa os mesmos).</summary>
public sealed record FilaNaNuvem(long RecursoId, int UsuarioId, string GrupoDeRecursos, string Conta, string Fila, string Regiao);

/// <summary>O aplicativo que o worker pede à nuvem, com os nomes JÁ decididos no pedido.</summary>
/// <param name="Tipo"><see cref="ProvisaoV1.TipoAppService"/> ou <see cref="ProvisaoV1.TipoStaticWebApp"/>.</param>
/// <param name="NomeDoPlano">O plano do App Service; vazio no Static Web App.</param>
/// <param name="Plano">O SKU (<c>B1</c>, <c>Free</c>).</param>
public sealed record AplicativoNaNuvem(long RecursoId, int UsuarioId, string Tipo, string GrupoDeRecursos, string Nome, string NomeDoPlano, string Regiao, string Plano);

/// <summary>O recurso criado: o id no Azure e o endereço da fila.</summary>
public sealed record RecursoNaNuvem(string IdNoAzure, string Endpoint);

/// <summary>
/// A falha da nuvem já CLASSIFICADA: <see cref="Transitoria"/> é a que vale tentar de novo (429, 5xx, rede); a outra é
/// definitiva (sem permissão, nome tomado, pedido inválido). A mensagem vai para o usuário: nunca leva token nem chave.
/// </summary>
public sealed class FalhaNaNuvem(string mensagem, bool transitoria, Exception? causa = null) : Exception(mensagem, causa)
{
    public bool Transitoria { get; } = transitoria;
}

/// <summary>As regras puras da provisão: a liberação, os nomes, a região, a espera entre as tentativas e as transições. Sem banco.</summary>
public static partial class RegrasDaProvisao
{
    /// <summary>As regiões quando a configuração não diz nenhuma.</summary>
    public static IReadOnlyList<string> RegioesPadrao { get; } = ["brazilsouth", "eastus2"];

    /// <summary>O teto do nome da conta de armazenamento no Azure (3 a 24, minúsculas e dígitos).</summary>
    public const int TamanhoMaximoDaConta = 24;

    /// <summary>O teto da mensagem de erro guardada.</summary>
    public const int TamanhoMaximoDoErro = 500;

    /// <summary>As regiões do Static Web App quando a configuração não diz nenhuma (ele existe em poucas regiões).</summary>
    public static IReadOnlyList<string> RegioesPadraoDoStaticWebApp { get; } = ["eastus2", "westus2", "centralus", "westeurope", "eastasia"];

    /// <summary>
    /// Os planos do App Service quando a configuração não diz nenhum. O custo é ESTIMADO, em reais: os preços do Azure não
    /// foram conferidos nesta versão — a configuração (<c>Provisao:PlanosDoAppService</c>) os corrige sem publicar código.
    /// </summary>
    public static IReadOnlyList<PlanoDeAplicativoV1> PlanosPadraoDoAppService { get; } =
        [new("F1", "F1 · Linux gratuito", 0m), new("B1", "B1 · Linux", 61.70m)];

    /// <summary>Os planos do Static Web App quando a configuração não diz nenhum (o custo, estimado, em reais).</summary>
    public static IReadOnlyList<PlanoDeAplicativoV1> PlanosPadraoDoStaticWebApp { get; } =
        [new("Free", "Free", 0m), new("Standard", "Standard", 48.00m)];

    /// <summary>O teto do nome do aplicativo que o usuário escolhe.</summary>
    public const int TamanhoMaximoDoRotulo = 20;

    /// <summary>O teto do nome no Azure guardado (o App Service aceita 60; o Static Web App, 40 — os nomes gerados ficam abaixo de 40).</summary>
    public const int TamanhoMaximoDoNome = 60;

    [GeneratedRegex("^[a-z0-9]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex Conta();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,18}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex Rotulo();

    /// <summary>
    /// A liberação: a permissão explícita do admin vence; sem ela, o plano da empresa do usuário quando está em
    /// <see cref="OpcoesDaProvisao.PlanosQueLiberam"/> (o tier de assinatura); senão, nada.
    /// </summary>
    public static string Origem(bool permissao, string? plano, IEnumerable<string> planosQueLiberam)
    {
        if (permissao)
            return ProvisaoV1.OrigemPermissao;
        return !string.IsNullOrWhiteSpace(plano) && planosQueLiberam.Any(p => string.Equals(p?.Trim(), plano.Trim(), StringComparison.OrdinalIgnoreCase))
            ? ProvisaoV1.OrigemPlano
            : ProvisaoV1.OrigemNenhuma;
    }

    /// <summary>As regiões aceitas, em minúsculas e sem repetição.</summary>
    public static IReadOnlyList<string> Regioes(OpcoesDaProvisao opcoes)
    {
        var lidas = (opcoes.Regioes ?? []).Select(Normalizar).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return lidas.Length > 0 ? lidas : RegioesPadrao;
    }

    /// <summary>A região padrão, sempre uma das aceitas.</summary>
    public static string RegiaoPadrao(OpcoesDaProvisao opcoes)
    {
        var regioes = Regioes(opcoes);
        var padrao = Normalizar(opcoes.RegiaoPadrao);
        return regioes.Contains(padrao) ? padrao : regioes[0];
    }

    /// <summary>A região do pedido (vazia é a padrão), ou nula quando não é aceita.</summary>
    public static string? Regiao(string? pedida, OpcoesDaProvisao opcoes)
    {
        var regiao = Normalizar(pedida);
        if (regiao.Length == 0)
            return RegiaoPadrao(opcoes);
        return Regioes(opcoes).Contains(regiao) ? regiao : null;
    }

    /// <summary>
    /// O grupo de recursos do pedido: o FIXO da provisão (<see cref="OpcoesDaProvisao.Grupo"/>), com o papel do worker limitado a
    /// ele; sem ele (o desenvolvimento), o do usuário pelo prefixo.
    /// </summary>
    public static string GrupoDoRecurso(OpcoesDaProvisao opcoes, int usuarioId)
        => string.IsNullOrWhiteSpace(opcoes.Grupo) ? NomeDoGrupo(opcoes.PrefixoDoGrupo, usuarioId) : opcoes.Grupo.Trim();

    /// <summary>O grupo de recursos de um usuário pelo prefixo (o desenvolvimento, sem <see cref="OpcoesDaProvisao.Grupo"/>).</summary>
    public static string NomeDoGrupo(string prefixo, int usuarioId)
        => $"{(string.IsNullOrWhiteSpace(prefixo) ? "rg-devkit-u" : prefixo.Trim())}{usuarioId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// O nome da conta de armazenamento: <c>dkq</c>, o usuário em base 36 e um sufixo ALEATÓRIO até 24 caracteres. O nome é
    /// global no Azure: o sufixo de 8 caracteres (36^8) torna a colisão improvável, e o índice único da base a barra aqui.
    /// </summary>
    public static string NomeDaConta(int usuarioId, string? sufixo = null)
    {
        var usuario = Base36(usuarioId);
        var aleatorio = sufixo ?? Aleatorio(8);
        var nome = $"dkq{usuario}{aleatorio}";
        return nome.Length <= TamanhoMaximoDaConta ? nome : nome[..TamanhoMaximoDaConta];
    }

    /// <summary>O nome é um nome de conta de armazenamento válido.</summary>
    public static bool ContaValida(string? nome) => nome is not null && Conta().IsMatch(nome);

    /// <summary>O nome que o usuário dá ao aplicativo: 3 a 20 caracteres, minúsculas, dígitos e hífen, começando por letra, sem hífen no fim nem duplo.</summary>
    public static bool RotuloValido(string? rotulo) => rotulo is not null && Rotulo().IsMatch(rotulo) && !rotulo.Contains("--", StringComparison.Ordinal);

    /// <summary>
    /// O nome do aplicativo no Azure: o prefixo do tipo (<c>app</c> ou <c>swa</c>), o usuário em base 36, o nome do usuário e um
    /// sufixo ALEATÓRIO de 4 caracteres — o nome do site é global no Azure (<c>*.azurewebsites.net</c>); o índice único da base
    /// barra o sorteio repetido aqui. Até 38 caracteres, abaixo do teto do Static Web App (40).
    /// </summary>
    public static string NomeDoAplicativo(string tipo, int usuarioId, string rotulo, string sufixo)
        => $"{(tipo == ProvisaoV1.TipoStaticWebApp ? "swa" : "app")}-u{Base36(usuarioId)}-{rotulo}-{sufixo}";

    /// <summary>O nome do plano do App Service no Azure: o do aplicativo com o prefixo <c>asp</c> (o mesmo sufixo).</summary>
    public static string NomeDoPlanoDoServico(int usuarioId, string rotulo, string sufixo) => $"asp-u{Base36(usuarioId)}-{rotulo}-{sufixo}";

    /// <summary>O sufixo aleatório dos nomes de aplicativo (minúsculas e dígitos).</summary>
    public static string Sufixo() => Aleatorio(4);

    /// <summary>O tipo é um aplicativo (App Service ou Static Web App): cota por tipo, custo e remoção com segundo fator.</summary>
    public static bool EhAplicativo(string tipo) => ProvisaoV1.TiposDeAplicativo.Contains(tipo);

    /// <summary>A remoção do tipo pede o segundo fator: a do aplicativo (o que custa e tem dados do usuário). A fila, não.</summary>
    public static bool RemocaoPedeSegundoFator(string tipo) => EhAplicativo(tipo);

    /// <summary>A cota de recursos ATIVOS do tipo por usuário (no mínimo 1).</summary>
    public static int Maximo(string tipo, OpcoesDaProvisao opcoes) => Math.Max(1, tipo switch
    {
        ProvisaoV1.TipoAppService => opcoes.MaximoDeAppServicesPorUsuario,
        ProvisaoV1.TipoStaticWebApp => opcoes.MaximoDeStaticWebAppsPorUsuario,
        _ => opcoes.MaximoDeFilasPorUsuario,
    });

    /// <summary>As regiões aceitas para o tipo: o Static Web App tem as dele; o resto, as da provisão.</summary>
    public static IReadOnlyList<string> RegioesDoTipo(string tipo, OpcoesDaProvisao opcoes)
    {
        if (tipo != ProvisaoV1.TipoStaticWebApp)
            return Regioes(opcoes);
        var lidas = (opcoes.RegioesDoStaticWebApp ?? []).Select(Normalizar).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return lidas.Length > 0 ? lidas : RegioesPadraoDoStaticWebApp;
    }

    /// <summary>A região sugerida para o tipo: a padrão da provisão quando o tipo a aceita; senão, a primeira dele.</summary>
    public static string RegiaoPadraoDoTipo(string tipo, OpcoesDaProvisao opcoes)
    {
        var regioes = RegioesDoTipo(tipo, opcoes);
        var padrao = RegiaoPadrao(opcoes);
        return regioes.Contains(padrao) ? padrao : regioes[0];
    }

    /// <summary>Os planos do tipo com o custo estimado do mês (os da configuração, ou os padrão).</summary>
    public static IReadOnlyList<PlanoDeAplicativoV1> Planos(string tipo, OpcoesDaProvisao opcoes)
    {
        var (configurados, padrao) = tipo == ProvisaoV1.TipoStaticWebApp
            ? (opcoes.PlanosDoStaticWebApp, PlanosPadraoDoStaticWebApp)
            : (opcoes.PlanosDoAppService, PlanosPadraoDoAppService);
        var lidos = (configurados ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .Select(p => new PlanoDeAplicativoV1(p.Id.Trim(), string.IsNullOrWhiteSpace(p.Rotulo) ? p.Id.Trim() : p.Rotulo.Trim(), Math.Max(0m, p.CustoMensal)))
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return lidos.Length > 0 ? lidos : padrao;
    }

    /// <summary>Os tipos de aplicativo como a permissão os publica: a cota, as regiões e os planos.</summary>
    public static IReadOnlyList<TipoDeAplicativoV1> Aplicativos(OpcoesDaProvisao opcoes)
        => ProvisaoV1.TiposDeAplicativo
            .Select(t => new TipoDeAplicativoV1(t, Maximo(t, opcoes), RegioesDoTipo(t, opcoes), RegiaoPadraoDoTipo(t, opcoes), Planos(t, opcoes)))
            .ToArray();

    /// <summary>
    /// Valida o pedido de aplicativo: o tipo, o nome, a região do tipo (vazia é a padrão) e o plano (vazio é o primeiro). Devolve
    /// o pedido pronto, ou os erros por campo (o 400 da API).
    /// </summary>
    public static (AplicativoPedido? Pedido, Dictionary<string, string[]> Erros) ValidarAplicativo(PedidoDeAplicativoV1? pedido, OpcoesDaProvisao opcoes)
    {
        var erros = new Dictionary<string, string[]>();
        var tipo = Normalizar(pedido?.Tipo);
        if (!EhAplicativo(tipo))
        {
            erros["tipo"] = [$"O tipo é um de: {string.Join(", ", ProvisaoV1.TiposDeAplicativo)}."];
            return (null, erros);
        }

        var rotulo = Normalizar(pedido?.Nome);
        if (!RotuloValido(rotulo))
            erros["nome"] = [$"O nome tem de 3 a {TamanhoMaximoDoRotulo} caracteres: letras minúsculas, dígitos e hífen, começando por letra e sem hífen no fim."];

        var regiao = Normalizar(pedido?.Regiao) is { Length: > 0 } pedida ? pedida : RegiaoPadraoDoTipo(tipo, opcoes);
        if (!RegioesDoTipo(tipo, opcoes).Contains(regiao))
            erros["regiao"] = [$"A região é uma de: {string.Join(", ", RegioesDoTipo(tipo, opcoes))}."];

        var planos = Planos(tipo, opcoes);
        var planoPedido = (pedido?.Plano ?? string.Empty).Trim();
        var plano = planoPedido.Length == 0
            ? planos[0]
            : planos.FirstOrDefault(p => string.Equals(p.Id, planoPedido, StringComparison.OrdinalIgnoreCase));
        if (plano is null)
            erros["plano"] = [$"O plano é um de: {string.Join(", ", planos.Select(p => p.Id))}."];

        return erros.Count > 0 ? (null, erros) : (new AplicativoPedido(tipo, rotulo, regiao, plano!), erros);
    }

    /// <summary>A primeira vaga livre da cota (1 até <paramref name="maximo"/>); nula com a cota cheia.</summary>
    public static int? Vaga(IEnumerable<int> ocupadas, int maximo)
    {
        var usadas = ocupadas.ToHashSet();
        for (var vaga = 1; vaga <= Math.Max(1, maximo); vaga++)
            if (!usadas.Contains(vaga))
                return vaga;
        return null;
    }

    /// <summary>O custo está no alerta: a partir do percentual do teto (sem teto, nunca).</summary>
    public static bool EmAlerta(decimal custo, OpcoesDaProvisao opcoes)
        => opcoes.LimiteDeCustoMensal > 0 && custo >= opcoes.LimiteDeCustoMensal * Math.Clamp(opcoes.AlertaDeCustoPercentual, 1, 100) / 100m;

    /// <summary>O custo chegou ao teto: novos pedidos param (o que existe continua).</summary>
    public static bool Bloqueado(decimal custo, OpcoesDaProvisao opcoes) => opcoes.LimiteDeCustoMensal > 0 && custo >= opcoes.LimiteDeCustoMensal;

    /// <summary>O pedido de <paramref name="novo"/> não cabe: o custo já está no teto, ou passaria dele.</summary>
    public static bool PassaDoTeto(decimal atual, decimal novo, OpcoesDaProvisao opcoes)
        => opcoes.LimiteDeCustoMensal > 0 && (atual >= opcoes.LimiteDeCustoMensal || atual + novo > opcoes.LimiteDeCustoMensal);

    /// <summary>O uso da cota de cada tipo de aplicativo e o custo do mês, pelos recursos ATIVOS do usuário (tipo e custo).</summary>
    public static CotaDaProvisaoV1 Cota(IEnumerable<(string Tipo, decimal Custo)> ativos, OpcoesDaProvisao opcoes)
    {
        var lista = ativos.Where(a => EhAplicativo(a.Tipo)).ToArray();
        var custo = lista.Sum(a => a.Custo);
        var tipos = ProvisaoV1.TiposDeAplicativo.Select(t => new UsoDoTipoV1(t, lista.Count(a => a.Tipo == t), Maximo(t, opcoes))).ToArray();
        return new CotaDaProvisaoV1(tipos, custo, Math.Max(0m, opcoes.LimiteDeCustoMensal), Math.Clamp(opcoes.AlertaDeCustoPercentual, 1, 100),
            EmAlerta(custo, opcoes), Bloqueado(custo, opcoes));
    }

    /// <summary>Os passos já feitos, lidos da coluna (separados por vírgula).</summary>
    public static IReadOnlyList<string> Passos(string? passos)
        => (passos ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A coluna com o <paramref name="passo"/> acrescentado (sem repetir).</summary>
    public static string ComPasso(string? passos, string passo)
        => string.Join(',', Passos(passos).Append(passo).Distinct(StringComparer.Ordinal));

    /// <summary>A falha PARCIAL: a criação falhou depois de um passo feito no Azure — algo ficou lá e o resto não.</summary>
    public static bool Parcial(string estado, string operacao, string? passos)
        => estado == ProvisaoV1.EstadoFalhou && operacao == ProvisaoV1.OperacaoCriar && Passos(passos).Count > 0;

    /// <summary>O tipo em palavras, para as mensagens e a auditoria.</summary>
    public static string RotuloDoTipo(string tipo) => tipo switch
    {
        ProvisaoV1.TipoAppService => "App Service",
        ProvisaoV1.TipoStaticWebApp => "Static Web App",
        _ => "fila",
    };

    /// <summary>
    /// A espera antes da tentativa seguinte à <paramref name="tentativa"/> (1, 2, ...): a inicial, dobrando, até a máxima — o
    /// recuo exponencial que não martela o Azure no 429.
    /// </summary>
    public static TimeSpan Espera(int tentativa, OpcoesDaProvisao opcoes)
    {
        var inicial = Math.Max(1, opcoes.EsperaInicialSegundos);
        var maxima = Math.Max(inicial, opcoes.EsperaMaximaSegundos);
        var expoente = Math.Clamp(tentativa - 1, 0, 20);
        return TimeSpan.FromSeconds(Math.Min(maxima, inicial * Math.Pow(2, expoente)));
    }

    /// <summary>A falha transitória ainda tem tentativa: volta a <c>pedido</c>; senão, <c>falhou</c>.</summary>
    public static bool TentaDeNovo(bool transitoria, int tentativas, OpcoesDaProvisao opcoes)
        => transitoria && tentativas < Math.Max(1, opcoes.MaximoDeTentativas);

    /// <summary>Só o que falhou se repete.</summary>
    public static bool PodeRepetir(string estado) => estado == ProvisaoV1.EstadoFalhou;

    /// <summary>
    /// O recurso ATIVO conta na cota: tudo que não foi removido. O que falhou na CRIAÇÃO conta também — os nomes estão
    /// reservados e algo pode ter ficado no Azure; ele se repete ou se remove.
    /// </summary>
    public static bool Ativo(string estado) => estado != ProvisaoV1.EstadoRemovido;

    /// <summary>O que acontece com o pedido de remoção, pelo estado e pela operação do recurso.</summary>
    public static Remocao DecidirRemocao(string estado, string operacao) => (estado, operacao) switch
    {
        (ProvisaoV1.EstadoRemovido, _) => Remocao.JaFeita,
        (ProvisaoV1.EstadoPedido or ProvisaoV1.EstadoEmProvisao, ProvisaoV1.OperacaoRemover) => Remocao.JaPedida,
        (ProvisaoV1.EstadoPedido or ProvisaoV1.EstadoEmProvisao, _) => Remocao.AguardeACriacao,
        _ => Remocao.Pedir,
    };

    /// <summary>
    /// A mensagem de erro (e o detalhe da auditoria) como fica guardada: uma linha, sem segredo, caminho, URL nem GUID (a
    /// máscara dos relatos, <see cref="RegrasDosRelatos.Mascarar(string?, int)"/> — a mensagem do Azure traz o id da
    /// assinatura) e cortada no teto.
    /// </summary>
    public static string Erro(string? mensagem)
        => RegrasDosRelatos.Mascarar((mensagem ?? string.Empty).ReplaceLineEndings(" ").Trim(), TamanhoMaximoDoErro);

    private static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    private static string Base36(int numero)
    {
        const string digitos = "0123456789abcdefghijklmnopqrstuvwxyz";
        var n = (uint)Math.Max(0, numero);
        if (n == 0)
            return "0";
        var texto = string.Empty;
        while (n > 0)
        {
            texto = digitos[(int)(n % 36)] + texto;
            n /= 36;
        }
        return texto;
    }

    private static string Aleatorio(int tamanho)
    {
        const string alfabeto = "abcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(tamanho, alfabeto, (destino, letras) =>
        {
            for (var i = 0; i < destino.Length; i++)
                destino[i] = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        });
    }
}

/// <summary>A decisão sobre o pedido de remoção (<see cref="RegrasDaProvisao.DecidirRemocao"/>).</summary>
public enum Remocao
{
    /// <summary>Pronto ou falhou: a remoção é pedida agora.</summary>
    Pedir,

    /// <summary>A remoção já está na fila ou em execução: nada muda.</summary>
    JaPedida,

    /// <summary>O recurso já foi removido: nada muda.</summary>
    JaFeita,

    /// <summary>A criação ainda não terminou: 409, remove-se depois.</summary>
    AguardeACriacao,
}
