using System.Text.Json.Serialization;

namespace DevKitPage.Contracts.V1;

/// <summary>
/// O contrato v1 do controle remoto (projeto controle-remoto, fase 1): o dev.kit publica o heartbeat e o retrato da máquina
/// com o JWT do usuário, e o site lê só o que é do dono. Os itens seguintes do projeto (o serviço do Windows e o Board web)
/// usam estes tipos, e os JSON de <c>tests/DevKitPage.Api.Tests/ContratoDoRemoto</c> travam os nomes. O envio é FECHADO: o
/// que está fora do contrato é recusado, para o retrato nunca virar um canal de texto livre. Campo novo é opcional e entra
/// primeiro na API, publicada antes do pacote do dev.kit; mudança incompatível é um <c>v2</c>, com rota própria.
/// </summary>
public static class RemotoV1
{
    /// <summary>O valor de <see cref="HeartbeatV1.Versao"/> e <see cref="RetratoV1.Versao"/> que as rotas aceitam.</summary>
    public const string Versao = "v1";

    /// <summary>A raiz das rotas deste contrato.</summary>
    public const string Rotas = "/api/remoto/v1";

    public const string AgenteAtivo = "ativo";
    public const string AgentePausado = "pausado";
    public const string AgenteEmLimite = "limite";
    public const string AgenteDesconhecido = "desconhecido";

    public const string FluxoRodando = "rodando";
    public const string FluxoNaFila = "na-fila";
    public const string FluxoPergunta = "pergunta";
    public const string FluxoFalhou = "falhou";

    public const string PendenciaPergunta = "pergunta";
    public const string PendenciaDecisao = "decisao";
    public const string PendenciaAprovacao = "aprovacao";

    /// <summary>A recusa de 422 por token, senha, chave ou connection string (a extensão <c>motivo</c> do problema).</summary>
    public const string MotivoSegredo = "segredo";

    /// <summary>A recusa de 422 por caminho, URL, e-mail, usuário, documento ou telefone (a extensão <c>motivo</c> do problema).</summary>
    public const string MotivoDadoDeCliente = "dado-de-cliente";

    public const int TamanhoMaximoDoApelido = 100;
    public const int TamanhoMaximoDaVersaoDoDevKit = 50;
    public const int TamanhoMaximoDoCodigo = 30;
    public const int TamanhoMaximoDoId = 100;
    public const int TamanhoMaximoDoTitulo = 200;
    public const int TamanhoMaximoDoResumo = 500;
    public const int MaximoDeProjetos = 100;
    public const int MaximoDeFluxos = 100;
    public const int MaximoDePendencias = 50;
    public const int MaximoDeOpcoes = 6;
}

/// <summary>O sinal de vida da máquina. O servidor marca a hora pelo relógio dele: é ela que diz se a máquina está conectada.</summary>
/// <param name="Versao">Sempre <see cref="RemotoV1.Versao"/>.</param>
/// <param name="MaquinaId">O GUID anônimo da máquina, com ou sem hífens — nunca o hostname.</param>
/// <param name="Apelido">O nome que o usuário deu à máquina; vazio, a API usa <c>máquina 3f2b1c9e</c>.</param>
/// <param name="VersaoDevKit">A versão instalada do dev.kit.</param>
/// <param name="DevKitAberto">O app está aberto; o serviço do Windows publica mesmo com ele fechado.</param>
/// <param name="Agente">Um de <c>ativo</c>, <c>pausado</c>, <c>limite</c> ou <c>desconhecido</c>.</param>
/// <param name="TurnosEmAndamento">Quantos turnos do agente rodam agora: o que o reinício seguro confere.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HeartbeatV1(
    string Versao, string MaquinaId, string Apelido, string VersaoDevKit, bool DevKitAberto, string Agente, int TurnosEmAndamento);

/// <summary>O heartbeat gravado.</summary>
/// <param name="MaquinaId">O id como a API o guarda: 32 dígitos hexadecimais minúsculos, sem hífens.</param>
/// <param name="RecebidoEm">A hora do servidor.</param>
/// <param name="ProximoEmSegundos">Daqui a quanto tempo o dev.kit publica de novo: quem dita o ritmo é a API.</param>
public sealed record HeartbeatRecebidoV1(string MaquinaId, DateTimeOffset RecebidoEm, int ProximoEmSegundos);

/// <summary>Um projeto no retrato: só o nome e os contadores, sem definição, repositórios nem anexos.</summary>
/// <param name="Slug">O identificador do projeto (<c>ProjetosV1.TamanhoMaximoDoSlug</c>).</param>
/// <param name="Nome">O nome que o Board mostra.</param>
/// <param name="Situacao">A situação do projeto, em código (<c>ProjetosV1.Situacoes</c>).</param>
/// <param name="Itens">Os itens do plano.</param>
/// <param name="Integrados">Os itens integrados.</param>
/// <param name="Rodando">Os itens rodando agora.</param>
/// <param name="Onda">A onda corrente; 0 antes da primeira.</param>
/// <param name="Ondas">As ondas do plano.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjetoDoRetratoV1(
    string Slug, string Nome, string Situacao, int Itens, int Integrados, int Rodando, int Onda, int Ondas);

/// <summary>Uma execução de fluxo em andamento na máquina: a lista Executando do Board.</summary>
/// <param name="Id">O id interno da execução, sem pasta nem nome: não muda entre um retrato e o seguinte.</param>
/// <param name="Titulo">O que a linha mostra (<c>Item 20 · layout do plano</c>).</param>
/// <param name="Fluxo">O nome do fluxo; vazio na task sem fluxo.</param>
/// <param name="Etapa">A etapa corrente; vazia quando não se aplica.</param>
/// <param name="Situacao">Em código: <c>rodando</c>, <c>na-fila</c>, <c>pergunta</c>, <c>falhou</c>; o desconhecido o site mostra neutro.</param>
/// <param name="Projeto">O slug do projeto, quando a execução é de um item dele.</param>
/// <param name="Item">O número do item no projeto.</param>
/// <param name="Desde">Desde quando está nesta situação.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FluxoDoRetratoV1(
    string Id, string Titulo, string Fluxo, string Etapa, string Situacao, string? Projeto, int? Item, DateTimeOffset? Desde);

/// <summary>O que espera o usuário: a lista "Precisa de você" do Board.</summary>
/// <param name="Id">O id estável da pendência: é por ele que uma decisão futura a referencia.</param>
/// <param name="Tipo">Em código: <c>pergunta</c>, <c>decisao</c>, <c>aprovacao</c>.</param>
/// <param name="Titulo">A pergunta, já sanitizada.</param>
/// <param name="Resumo">O contexto curto (<c>Nota 100 · regressão sem falhas</c>); vazio quando não há.</param>
/// <param name="Opcoes">Os botões, os campos de <c>[GITKIT:PERGUNTA]</c>; vazia quando não há.</param>
/// <param name="Projeto">O slug do projeto, quando a pendência é de um item dele.</param>
/// <param name="Item">O número do item no projeto.</param>
/// <param name="Desde">Desde quando espera.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PendenciaDoRetratoV1(
    string Id, string Tipo, string Titulo, string Resumo, IReadOnlyList<string> Opcoes, string? Projeto, int? Item, DateTimeOffset? Desde);

/// <summary>
/// A foto do estado da máquina, sempre inteira: cada envio substitui o anterior, e lista vazia quer dizer que não há nada.
/// Vale a de <see cref="GeradoEm"/> mais recente; a que chega atrasada é ignorada.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RetratoV1(
    string Versao, string MaquinaId, DateTimeOffset GeradoEm, IReadOnlyList<ProjetoDoRetratoV1> Projetos,
    IReadOnlyList<FluxoDoRetratoV1> Fluxos, IReadOnlyList<PendenciaDoRetratoV1> Pendencias);

/// <summary>O retrato recebido.</summary>
/// <param name="MaquinaId">O id como a API o guarda.</param>
/// <param name="Aplicado">Falso quando o gravado é igual ou mais recente: reenvio ou entrega fora de ordem, sem erro.</param>
/// <param name="GeradoEm">A data do retrato que vale agora.</param>
/// <param name="RecebidoEm">Quando o retrato que vale agora chegou (a hora do servidor).</param>
public sealed record RetratoRecebidoV1(string MaquinaId, bool Aplicado, DateTimeOffset GeradoEm, DateTimeOffset RecebidoEm);

/// <summary>Uma máquina do dono, como o site a lê.</summary>
/// <param name="MaquinaId">O id como a API o guarda.</param>
/// <param name="Apelido">O nome da máquina.</param>
/// <param name="VersaoDevKit">A versão do último heartbeat.</param>
/// <param name="Conectada">O último heartbeat está dentro da tolerância. Falso, o resto é o último estado conhecido.</param>
/// <param name="DevKitAberto">O app estava aberto no último heartbeat.</param>
/// <param name="Agente">O estado do agente no último heartbeat.</param>
/// <param name="TurnosEmAndamento">Os turnos em andamento no último heartbeat.</param>
/// <param name="UltimoHeartbeat">A hora do servidor no último heartbeat.</param>
/// <param name="RetratoRecebidoEm">Quando o retrato que vale agora chegou; nulo sem retrato.</param>
public sealed record MaquinaRemotaV1(
    string MaquinaId, string Apelido, string VersaoDevKit, bool Conectada, bool DevKitAberto, string Agente, int TurnosEmAndamento,
    DateTimeOffset UltimoHeartbeat, DateTimeOffset? RetratoRecebidoEm);

/// <summary>A leitura de uma máquina pelo site: o estado dela e o último retrato.</summary>
/// <param name="Maquina">A máquina.</param>
/// <param name="Retrato">O último retrato; nulo quando ela só publicou heartbeat.</param>
public sealed record PainelRemotoV1(MaquinaRemotaV1 Maquina, RetratoV1? Retrato);
