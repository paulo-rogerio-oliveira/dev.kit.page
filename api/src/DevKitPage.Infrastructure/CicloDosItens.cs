using System.Globalization;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Infrastructure;

public sealed partial class ProjetosDaEmpresa
{
    public async Task<ResultadoDaDecisaoDoItem> DecidirItemAsync(EscopoDoDesign escopo, long id, int numero,
        PedidoDeDecisaoDoItemV1 pedido, CancellationToken ct)
    {
        pedido = pedido with { Motivo = pedido.Motivo ?? "", Confirmacao = pedido.Confirmacao ?? "" };
        var projeto = await ObterAsync(escopo, id, ct);
        if (projeto is null) return new(404, Mensagem: "Projeto não encontrado.");
        if (projeto.Papel is not ("editor" or "dono")) return new(403, Mensagem: "Decidir exige editor ou dono.");
        if (pedido.Acao is not (CicloDosItensV1.Concluir or CicloDosItensV1.Cancelar) || !Guid.TryParse(pedido.Pedido, out _))
            return new(400, Mensagem: "Informe pedido único e ação concluir-manualmente ou cancelar-item.");
        var item = projeto.Itens.FirstOrDefault(i => i.Numero == numero);
        if (item is null) return new(404, Mensagem: "Item não encontrado.");
        if (projeto.Itens.Any(i => i.Numero != numero && i.DecisaoHumana?.Pedido == pedido.Pedido))
            return new(409, Mensagem: "O pedido já pertence a outro item.");
        if (item.DecisaoHumana is { } anterior)
        {
            if (anterior.Pedido == pedido.Pedido && anterior.Acao == pedido.Acao && anterior.Motivo == pedido.Motivo.Trim()
                && anterior.Confirmacao == pedido.Confirmacao && anterior.Autor == escopo.UsuarioId.ToString(CultureInfo.InvariantCulture)
                && anterior.VersaoEsperada == pedido.VersaoEsperada.ToString(CultureInfo.InvariantCulture) && pedido.Confirmar)
                return new(200, Impacto(projeto, numero, !pedido.Aplicar));
            return new(409, Mensagem: "O item já possui outra decisão terminal. Consulte seu recibo.");
        }
        if (projeto.Situacao == ProjetosV1.Concluido) return new(409, Mensagem: "Projeto concluído não recebe decisão de item.");
        if (projeto.Versao != pedido.VersaoEsperada) return new(409, Mensagem: "O projeto mudou. Simule e confirme novamente.");
        if (pedido.Aplicar && (!pedido.Confirmar || string.IsNullOrWhiteSpace(pedido.Motivo) || string.IsNullOrWhiteSpace(pedido.Confirmacao)))
            return new(400, Mensagem: "Aplicar exige confirmação humana, motivo e origem da confirmação.");
        if (pedido.Motivo.Length > 2000 || pedido.Confirmacao.Length > 2000) return new(400, Mensagem: "Motivo ou confirmação excede 2000 caracteres.");
        var agora = relogio.GetUtcNow();
        var decisao = new DecisaoHumanaDoItemV1(pedido.Pedido, pedido.Acao, escopo.UsuarioId.ToString(CultureInfo.InvariantCulture),
            pedido.Confirmacao, pedido.Motivo.Trim(), agora, pedido.VersaoEsperada.ToString(CultureInfo.InvariantCulture),
            item.Situacao, JsonSerializer.Serialize(new { projeto.Anexos, Item = item }),
            item.Situacao is ProjetosV1.ItemRodando or ProjetosV1.ItemPergunta or "solicitado" or "aguardando-confirmacao"
                || item.ReservasDeEscrita?.Reservas.Length > 0);
        var retirado = item with { Situacao = pedido.Acao == CicloDosItensV1.Concluir
            ? ProjetosV1.ItemConcluidoManualmente : ProjetosV1.ItemCancelado, DecisaoHumana = decisao, AtualizadoEm = agora };
        var proposta = projeto with { Itens = projeto.Itens.Select(i => i.Numero == numero ? retirado : i).ToArray() };
        if (!pedido.Aplicar) return new(200, Impacto(proposta, numero, true));
        var envio = new EnvioDeProjetoV1(projeto.Slug, projeto.Nome, projeto.Nivel, projeto.Time, projeto.Repositorios,
            projeto.Branch, projeto.SemanaConvertida, projeto.Definicao, projeto.Anexos, projeto.Autonomia, projeto.Paralelismo,
            projeto.DestinoDaPr, projeto.Situacao, projeto.Versao, proposta.Itens, projeto.FeatureId, projeto.EpicId,
            projeto.Iteracao, projeto.Board, projeto.BranchesDasTasks, agora);
        var resultado = await GravarInternoAsync(escopo, envio, true, ct, idDoProjeto: projeto.Id);
        return resultado.Valor is { } gravado ? new(200, Impacto(gravado.Projeto, numero, false))
            : new(resultado.Falha == FalhaNaGestao.Conflito ? 409 : resultado.Falha == FalhaNaGestao.Proibida ? 403
                : resultado.Falha == FalhaNaGestao.NaoEncontrado ? 404 : 400,
                Mensagem: resultado.Mensagem);
    }

    public async Task<ResultadoDaGestao<ProjetoV1>> ConfirmarParadaAsync(EscopoDoDesign escopo, long id, int numero,
        string maquinaAutenticada, ParadaDeEscritaDoItemV1 parada, CancellationToken ct)
    {
        var projeto = await ObterAsync(escopo, id, ct);
        if (projeto is null) return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        if (projeto.Papel is not ("dono" or "editor"))
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.Proibida, "Parada exige editor ou dono.");
        var item = projeto.Itens.FirstOrDefault(i => i.Numero == numero);
        if (item?.DecisaoHumana is not { } decisao || item.ReservasDeEscrita is not { } reservas)
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.Recusada, "Decisão e reserva esperadas indisponíveis.");
        if (parada.Reserva is null || parada.Reserva.Maquina != maquinaAutenticada || !reservas.Reservas.Contains(parada.Reserva)
            || parada.Pedido != decisao.Pedido || parada.Recibo <= 0 || parada.Em < decisao.Em || parada.Em > relogio.GetUtcNow().AddMinutes(5))
            return ResultadoDaGestao<ProjetoV1>.Falhou(FalhaNaGestao.Recusada, "Recibo não corresponde à decisão, máquina e tentativa reservadas.");
        if (reservas.Paradas.Contains(parada)) return new(projeto);
        var atualizado = item with { ReservasDeEscrita = reservas with { Paradas = reservas.Paradas.Append(parada).ToArray() } };
        var envio = new EnvioDeProjetoV1(projeto.Slug, projeto.Nome, projeto.Nivel, projeto.Time, projeto.Repositorios,
            projeto.Branch, projeto.SemanaConvertida, projeto.Definicao, projeto.Anexos, projeto.Autonomia, projeto.Paralelismo,
            projeto.DestinoDaPr, projeto.Situacao, projeto.Versao, projeto.Itens.Select(i => i.Numero == numero ? atualizado : i).ToArray(),
            projeto.FeatureId, projeto.EpicId, projeto.Iteracao, projeto.Board, projeto.BranchesDasTasks, projeto.AlteradoEm);
        var gravado = await GravarInternoAsync(escopo, envio, true, ct, idDoProjeto: projeto.Id);
        return gravado.Valor is { } valor ? new(valor.Projeto)
            : ResultadoDaGestao<ProjetoV1>.Falhou(gravado.Falha, gravado.Mensagem);
    }

    private static ImpactoDaDecisaoDoItemV1 Impacto(ProjetoV1 projeto, int numero, bool simulacao)
    {
        var efetivos = OndasEfetivasDosItens.Projetar(projeto.Itens);
        return new(simulacao, projeto, numero, projeto.Itens.Where(i => i.Dependencias.Contains(numero)).Select(i => i.Numero).ToArray(),
            efetivos, CicloDosItensV1.Consequencia);
    }
}
