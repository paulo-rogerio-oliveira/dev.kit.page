using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras da conversa remota (<see cref="RegrasDaConversa"/>): a forma do comando e da resposta, o TTL dentro da
/// faixa, quem o TTL vence, o estado depois da resposta, o limite por hora e a SAS de serviço da fila — o texto assinado no
/// formato do Azure, só HTTPS e só a permissão pedida.
/// </summary>
public sealed class RegrasDaConversaTests
{
    private static readonly OpcoesDoRemoto Opcoes = new() { TtlMinimoSegundos = 60, TtlMaximoSegundos = 3600, TtlPadraoSegundos = 900, ComandosPorHora = 3 };

    private static ComandoNovoV1 Comando(string tipo = ConversaV1.ComandoMensagem, string? texto = "Como está o projeto?", int? ttl = null, string? id = null,
        string? dispositivo = null, string maquina = "3f2b9c0e5d7a")
        => new(id, maquina, tipo, texto, ttl, dispositivo);

    private static RespostaDoDevKitV1 Resposta(string tipo = ConversaV1.RespostaMensagem, string? comando = "c-1", string? texto = "O item 5 está rodando.")
        => new(RemotoV1.Contrato, "3f2b9c0e5d7a", "r-1", comando, tipo, texto, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void O_comando_valido_passa_e_ligar_e_reiniciar_nao_precisam_de_texto()
    {
        Assert.Empty(RegrasDaConversa.Validar(Comando(), Opcoes));
        Assert.Empty(RegrasDaConversa.Validar(Comando(ConversaV1.ComandoLigar, null), Opcoes));
        Assert.Empty(RegrasDaConversa.Validar(Comando(ConversaV1.ComandoReiniciar, ""), Opcoes));
        Assert.Empty(RegrasDaConversa.Validar(Comando(id: "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d", dispositivo: "Celular", ttl: 60), Opcoes));
    }

    [Theory]
    [InlineData("apagar-tudo", "Oi", null, null, "tipo")]
    [InlineData(ConversaV1.ComandoMensagem, "   ", null, null, "Escreva")]
    [InlineData(ConversaV1.ComandoMensagem, "Oi", 59, null, "ttlSegundos")]
    [InlineData(ConversaV1.ComandoMensagem, "Oi", 3601, null, "ttlSegundos")]
    [InlineData(ConversaV1.ComandoMensagem, "Oi", null, "tablet", "dispositivo")]
    public void O_comando_fora_da_forma_diz_o_campo(string tipo, string texto, int? ttl, string? dispositivo, string campo)
        => Assert.Contains(campo, RegrasDaConversa.Validar(Comando(tipo, texto, ttl, dispositivo: dispositivo), Opcoes));

    [Fact]
    public void O_id_e_a_maquina_seguem_a_regra_do_id_e_o_texto_tem_teto()
    {
        Assert.Contains("id", RegrasDaConversa.Validar(Comando(id: "../../fila"), Opcoes));
        Assert.Contains("maquinaId", RegrasDaConversa.Validar(Comando(maquina: "notebook do trabalho"), Opcoes));
        Assert.Contains("4000", RegrasDaConversa.Validar(Comando(texto: new string('a', ConversaV1.TamanhoMaximoDoTexto + 1)), Opcoes));
        Assert.Contains("Envie", RegrasDaConversa.Validar((ComandoNovoV1?)null, Opcoes));
    }

    private static FalaDoDevKitV1 Fala(string autor = ConversaV1.AutorDev, string origem = ConversaV1.OrigemDesktop, string? texto = "Como está o item 13?",
        string? comando = null, string id = "f-1")
        => new(RemotoV1.Contrato, "3f2b9c0e5d7a", id, autor, origem, texto, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), comando);

    [Fact]
    public void A_fala_valida_passa_com_cada_autor_e_origem_e_sem_diferenca_de_caixa()
    {
        Assert.Empty(RegrasDaConversa.Validar(Fala()));
        Assert.Empty(RegrasDaConversa.Validar(Fala(ConversaV1.AutorOrquestrador)));
        Assert.Empty(RegrasDaConversa.Validar(Fala("Orquestrador", " DESKTOP ")));
        Assert.Empty(RegrasDaConversa.Validar(Fala(origem: ConversaV1.OrigemRemoto, comando: "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d")));
        Assert.Empty(RegrasDaConversa.Validar(Fala(comando: "c-1")));
    }

    [Theory]
    [InlineData("cliente", ConversaV1.OrigemDesktop, "Oi", null, "autor")]
    [InlineData("", ConversaV1.OrigemDesktop, "Oi", null, "autor")]
    [InlineData(ConversaV1.AutorDev, "celular", "Oi", null, "origem")]
    [InlineData(ConversaV1.AutorDev, ConversaV1.OrigemRemoto, "Oi", null, "comandoId")]
    [InlineData(ConversaV1.AutorDev, ConversaV1.OrigemDesktop, "Oi", "../c-1", "comandoId")]
    [InlineData(ConversaV1.AutorDev, ConversaV1.OrigemDesktop, "   ", null, "Escreva")]
    [InlineData(ConversaV1.AutorDev, ConversaV1.OrigemDesktop, null, null, "Escreva")]
    public void A_fala_fora_da_forma_diz_o_campo(string autor, string origem, string? texto, string? comando, string campo)
        => Assert.Contains(campo, RegrasDaConversa.Validar(Fala(autor, origem, texto, comando)));

    [Fact]
    public void A_fala_segue_a_regra_do_id_tem_teto_data_e_o_texto_sensivel_e_recusado()
    {
        Assert.Contains("id da fala", RegrasDaConversa.Validar(Fala(id: "fala 1")));
        Assert.Contains("maquinaId", RegrasDaConversa.Validar(Fala() with { MaquinaId = "notebook do trabalho" }));
        Assert.Contains("4000", RegrasDaConversa.Validar(Fala(texto: new string('a', ConversaV1.TamanhoMaximoDoTexto + 1))));
        Assert.Empty(RegrasDaConversa.Validar(Fala(texto: new string('a', ConversaV1.TamanhoMaximoDoTexto))));
        Assert.Contains("ditaEm", RegrasDaConversa.Validar(Fala() with { DitaEm = default }));
        Assert.Contains("Envie", RegrasDaConversa.Validar((FalaDoDevKitV1?)null));

        Assert.Equal([new CampoSensivelV1("texto", RemotoV1.SensivelSegredo)],
            RegrasDaConversa.CamposSensiveis(Fala(texto: "use o token ghp_abcdefghijklmnopqrstuvwxyz0123456789")));
        Assert.Empty(RegrasDaConversa.CamposSensiveis(Fala(texto: "O slug sprint-11-2026-2027 do carimbo 20261102-095333 está pronto.")));
    }

    [Fact]
    public void O_contrato_publica_o_sinal_das_falas_e_o_evento_novo_no_fim_das_listas()
    {
        Assert.Equal("falas-do-desktop", RemotoV1.RecursoFalasDoDesktop);
        Assert.Contains(RemotoV1.RecursoFalasDoDesktop, RemotoV1.RecursosDaApi);
        Assert.Equal([ConversaV1.EventoComando, ConversaV1.EventoResposta, ConversaV1.EventoFala], ConversaV1.TiposDeEvento);
        // (semana 7) O agente da task entra no fim: o site antigo não o conhece e segue com os dois de antes.
        Assert.Equal(["orquestrador", "dev", "agente"], ConversaV1.AutoresDaFala);
        Assert.Equal(["desktop", "remoto"], ConversaV1.OrigensDaFala);
        Assert.Equal("conversa-da-task", RemotoV1.RecursosDaApi[^1]);
    }

    [Fact]
    public void A_criacao_de_projeto_e_de_design_entra_no_fim_dos_tipos_e_o_formulario_e_conferido()
    {
        // O tipo novo entra no fim: o site e o dev.kit antigos leem a lista como antes (item 4 do remote-control-v3).
        Assert.Equal(["mensagem", "ligar", "reiniciar", "catalogo", "projeto", "design"], ConversaV1.TiposDeComando);
        Assert.Equal([ConversaV1.ComandoProjeto, ConversaV1.ComandoDesign], ConversaV1.TiposDeCriacao);

        Assert.Empty(RegrasDaConversa.Validar(Comando(ConversaV1.ComandoProjeto,
            """{"nome":"Projetos pelo site","repositorios":["github.com/org/app"],"definicao":"Ver e criar.","nivel":"Time"}"""), Opcoes));
        Assert.Empty(RegrasDaConversa.Validar(Comando(ConversaV1.ComandoDesign, """{"repositorio":"github.com/org/app"}"""), Opcoes));

        Assert.Contains("Envie o formulário", RegrasDaConversa.Validar(Comando(ConversaV1.ComandoDesign, ""), Opcoes));
        Assert.Contains("JSON", RegrasDaConversa.Validar(Comando(ConversaV1.ComandoProjeto, "criar projeto"), Opcoes));
        Assert.Contains("Envie o novo projeto", RegrasDaConversa.Validar(Comando(ConversaV1.ComandoProjeto, "null"), Opcoes));
        var muitos = string.Join(",", Enumerable.Range(1, CriacaoRemotaV1.MaximoDeRepositorios + 1).Select(i => $"\"github.com/org/app{i}\""));
        Assert.Contains("até 20 repositórios", RegrasDaConversa.Validar(Comando(ConversaV1.ComandoProjeto, $$"""{"nome":"Novo","repositorios":[{{muitos}}]}"""), Opcoes));
        Assert.Contains("definição", RegrasDaCriacaoRemota.Validar(new NovoProjetoRemotoV1("Novo", ["github.com/org/app"],
            new string('a', CriacaoRemotaV1.TamanhoMaximoDaDefinicao + 1))));
        Assert.Contains("Cada repositório", RegrasDaCriacaoRemota.Validar(new NovoProjetoRemotoV1("Novo", ["github.com/org/app", " "])));
    }

    [Fact]
    public void A_resposta_valida_passa_e_a_que_conclui_diz_o_comando()
    {
        Assert.Empty(RegrasDaConversa.Validar(Resposta()));
        Assert.Empty(RegrasDaConversa.Validar(Resposta(comando: null)));
        Assert.Empty(RegrasDaConversa.Validar(Resposta(ConversaV1.RespostaConcluido, texto: null)));
        Assert.Contains("comandoId", RegrasDaConversa.Validar(Resposta(ConversaV1.RespostaConcluido, comando: null)));
        Assert.Contains("tipo", RegrasDaConversa.Validar(Resposta("aprovado")));
        Assert.Contains("Escreva", RegrasDaConversa.Validar(Resposta(texto: "")));
        Assert.Contains("geradaEm", RegrasDaConversa.Validar(Resposta() with { GeradaEm = default }));
    }

    [Fact]
    public void O_texto_com_token_ou_dado_de_cliente_e_barrado_sem_falso_positivo()
    {
        Assert.Equal([new CampoSensivelV1("texto", RemotoV1.SensivelEmail)], RegrasDaConversa.CamposSensiveis(Comando(texto: "mande para ana@cliente.com.br")));
        Assert.Equal([new CampoSensivelV1("texto", RemotoV1.SensivelCnpj)], RegrasDaConversa.CamposSensiveis(Resposta(texto: "CNPJ 11.222.333/0001-81")));
        Assert.Empty(RegrasDaConversa.CamposSensiveis(Comando(texto: "Como está o sprint-11-2026-2027 de 20261102-095333?")));
    }

    [Fact]
    public void O_ttl_e_o_pedido_ou_o_padrao_sempre_dentro_da_faixa()
    {
        Assert.Equal(TimeSpan.FromSeconds(900), RegrasDaConversa.Ttl(null, Opcoes));
        Assert.Equal(TimeSpan.FromSeconds(120), RegrasDaConversa.Ttl(120, Opcoes));
        Assert.Equal(TimeSpan.FromSeconds(3600), RegrasDaConversa.Ttl(99999, Opcoes));
        Assert.Equal(TimeSpan.FromSeconds(60), RegrasDaConversa.Ttl(null, new OpcoesDoRemoto { TtlMinimoSegundos = 60, TtlPadraoSegundos = 5 }));
        Assert.Equal(TimeSpan.FromMinutes(60), RegrasDaConversa.ValidadeDaSas(new OpcoesDoRemoto { SasMinutos = 600 }));
        Assert.Equal(TimeSpan.FromMinutes(1), RegrasDaConversa.ValidadeDaSas(new OpcoesDoRemoto { SasMinutos = 0 }));
    }

    [Theory]
    [InlineData(ConversaV1.EstadoPendente, true)]
    [InlineData(ConversaV1.EstadoNaFila, true)]
    [InlineData(ConversaV1.EstadoFalhou, true)]
    [InlineData(ConversaV1.EstadoEntregue, false)]
    [InlineData(ConversaV1.EstadoRespondido, false)]
    [InlineData(ConversaV1.EstadoExpirado, false)]
    public void O_ttl_vence_so_o_que_nao_chegou_a_maquina(string estado, bool vence)
    {
        var expira = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(vence, RegrasDaConversa.Vencido(estado, expira, expira));
        Assert.False(RegrasDaConversa.Vencido(estado, expira, expira.AddSeconds(-1)));
    }

    [Fact]
    public void A_resposta_leva_o_comando_ao_estado_dela_e_o_expirado_fica()
    {
        Assert.Equal(ConversaV1.EstadoRespondido, RegrasDaConversa.EstadoDepoisDaResposta(ConversaV1.EstadoEntregue, ConversaV1.RespostaMensagem));
        Assert.Equal(ConversaV1.EstadoConcluido, RegrasDaConversa.EstadoDepoisDaResposta(ConversaV1.EstadoEntregue, ConversaV1.RespostaConcluido));
        Assert.Equal(ConversaV1.EstadoRecusado, RegrasDaConversa.EstadoDepoisDaResposta(ConversaV1.EstadoRespondido, ConversaV1.RespostaRecusado));
        Assert.Equal(ConversaV1.EstadoExpirado, RegrasDaConversa.EstadoDepoisDaResposta(ConversaV1.EstadoExpirado, ConversaV1.RespostaMensagem));
    }

    [Fact]
    public void O_limite_enche_no_teto_e_volta_quando_o_mais_antigo_sai_da_janela()
    {
        Assert.False(RegrasDaConversa.LimiteCheio(2, Opcoes));
        Assert.True(RegrasDaConversa.LimiteCheio(3, Opcoes));
        var agora = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromMinutes(20), RegrasDaConversa.TenteEm(agora.AddMinutes(-40), agora));
        Assert.Equal(TimeSpan.FromSeconds(1), RegrasDaConversa.TenteEm(agora.AddHours(-2), agora));
    }

    [Fact]
    public void A_sas_assina_o_texto_da_fila_no_formato_do_azure_so_https_e_so_a_permissao_pedida()
    {
        var chave = Convert.ToBase64String(Encoding.UTF8.GetBytes("chave-de-teste-da-conta-de-armazenamento"));
        var inicio = new DateTimeOffset(2026, 11, 3, 11, 58, 0, TimeSpan.Zero);
        var fim = new DateTimeOffset(2026, 11, 3, 12, 15, 0, TimeSpan.Zero);

        // O texto da SAS de serviço da fila (versão 2015-04-05 em diante), linha a linha.
        Assert.Equal("p\n2026-11-03T11:58:00Z\n2026-11-03T12:15:00Z\n/queue/dkq16abcd1234/comandos\n\n\nhttps\n2021-12-02",
            RegrasDaConversa.TextoDaSas("dkq16abcd1234", "comandos", "p", inicio, fim));

        var sas = RegrasDaConversa.Sas("dkq16abcd1234", "comandos", chave, "p", inicio, fim);
        var campos = sas.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal(["sv", "st", "se", "sp", "spr", "sig"], campos.Keys);
        Assert.Equal(("2021-12-02", "2026-11-03T11:58:00Z", "2026-11-03T12:15:00Z", "p", "https"),
            (campos["sv"], campos["st"], campos["se"], campos["sp"], campos["spr"]));
        using var hmac = new HMACSHA256(Convert.FromBase64String(chave));
        var esperada = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(RegrasDaConversa.TextoDaSas("dkq16abcd1234", "comandos", "p", inicio, fim))));
        Assert.Equal(esperada, campos["sig"]);
        // A chave nunca vai na SAS: só a assinatura.
        Assert.DoesNotContain(chave, sas, StringComparison.Ordinal);
        // Outra fila, ou outra permissão, é outra assinatura.
        Assert.NotEqual(sas, RegrasDaConversa.Sas("dkq16abcd1234", "outra", chave, "p", inicio, fim));
        Assert.Contains("sp=a&", RegrasDaConversa.Sas("dkq16abcd1234", "comandos", chave, "a", inicio, fim), StringComparison.Ordinal);
    }
}
