using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>Confere origem e ACL antes de persistir/enfileirar. O host refaz a leitura antes de reservar.</summary>
public sealed class ProntidaoDoDisparoRemoto(DevKitPageDb db, TimeProvider relogio)
{
    public async Task<string?> PendenciaAsync(int usuario, string tipo, string texto, CancellationToken ct)
    {
        if (tipo != ConversaV1.ComandoProjeto) return null;
        try
        {
            var pedido = DisparoDeProjetoRemoto.Ler(texto);
            if (pedido is null) return null;
            var u = await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario, ct);
            var escopo = new EscopoDoDesign(usuario, u.EmpresaId, u.Papel is "admin" or "gestor");
            var projetos = new ProjetosDaEmpresa(db, relogio);
            var encontrados = await db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo)
                .Where(p => p.Slug == pedido.Projeto).Select(p => p.Id).Take(2).ToArrayAsync(ct);
            RegrasMatrix.Exigir(encontrados.Length == 1, "Projeto indisponível ou ambíguo neste escopo.");
            var projeto = await projetos.ObterAsync(escopo, encontrados[0], ct)
                ?? throw new ArgumentException("Projeto indisponível neste escopo.");
            RegrasMatrix.Exigir(RegrasDeAcesso.PodeEditar(projeto.Papel), "Disparo exige editor ou dono do projeto.");
            RegrasMatrix.Exigir(projeto.Situacao == (pedido.Acao == "retomar" ? ProjetosV1.Pausado : ProjetosV1.Aprovado),
                "Projeto não está disponível para esta ação.");
            RegrasMatrix.Exigir(pedido.Item is null || projeto.Itens.Any(i => i.Numero == pedido.Item), "Item não pertence ao projeto.");
            RegrasMatrix.Exigir(string.IsNullOrEmpty(projeto.PendenciaDoGrafo), "Grafo do projeto indisponível.");
            var loja = new MatrizesNoBanco(db, relogio);
            var lista = await loja.ListarAsync(escopo, ct);
            RegrasMatrix.Exigir(lista.Status == 200 && lista.Valor is not null, "Catálogo matrix indisponível.");
            var matrizes = lista.Valor.Where(c => c.Projeto == projeto.Slug && c.Tipo == "matriz").ToArray();
            RegrasMatrix.Exigir(matrizes.Length > 0, "Projeto sem matriz definida.");
            RegrasMatrix.Exigir(lista.Valor.Where(c => c.Projeto == projeto.Slug && c.Tipo == "vinculo")
                .All(v => matrizes.Any(m => m.Aplicacao == v.Aplicacao && m.Id == v.Id)), "Matriz vinculada indisponível.");
            var donos = new HashSet<int>();
            foreach (var c in matrizes)
            {
                var h = await loja.HistoricoAsync(escopo, c.Aplicacao, "matriz", c.Id, ct);
                RegrasMatrix.Exigir(h.Status == 200 && h.Valor is not null, "Leitura da matriz indisponível.");
                var d = h.Valor.Single(r => r.Versao == c.Versao).Documento;
                RegrasMatrix.Validar(d);
                RegrasMatrix.Exigir(d.Projeto == projeto.Slug, "Vínculo da matriz divergente.");
                var mapa = await loja.HistoricoAsync(escopo, c.Aplicacao, "mapa", c.Aplicacao, ct);
                RegrasMatrix.Exigir(mapa.Status == 200 && mapa.Valor is not null, "Revisão do mapa indisponível.");
                var m = mapa.Valor.Single(r => r.Versao == d.Matriz!.RevisaoDoMapa).Documento;
                RegrasMatrix.Validar(m);
                RegrasMatrix.ValidarReferencia(d, (_, _, v) => mapa.Valor.SingleOrDefault(r => r.Versao == v));
                RegrasProntidaoMatrix.Validar(d.Matriz!, m.Mapa!);
                foreach (var v in lista.Valor.Where(v => v.Tipo == "vinculo" && v.Aplicacao == c.Aplicacao && v.Id == c.Id))
                {
                    var vinculos = await loja.HistoricoAsync(escopo, v.Aplicacao, v.Tipo, v.Id, ct);
                    RegrasMatrix.Exigir(vinculos.Status == 200 && vinculos.Valor is not null, "Vínculo matrix indisponível.");
                    foreach (var vinculo in vinculos.Valor.GroupBy(r => r.Documento.Vinculo?.Responsavel).Select(g => g.Last()))
                    {
                        RegrasMatrix.Validar(vinculo.Documento);
                        RegrasMatrix.ValidarReferencia(vinculo.Documento, (_, _, _) => h.Valor.Single(r => r.Versao == c.Versao));
                    }
                }
                foreach (var dono in d.Matriz!.Celulas.SelectMany(x => x.Responsaveis).Where(r => r.Item is not null))
                {
                    RegrasMatrix.Exigir(projeto.Itens.Any(i => i.Numero == dono.Item), "Dono aponta item fora do projeto.");
                    donos.Add(dono.Item!.Value);
                }
            }
            foreach (var item in projeto.Itens.Where(i => projeto.ItensDoGrafo is null || projeto.ItensDoGrafo.Contains(i.Numero)))
                RegrasMatrix.Exigir(donos.Contains(item.Numero), $"Item {item.Numero} sem célula/responsável na matriz.");
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException ex) { return "Pendência matrix: " + ex.Message; }
        catch (Exception) { return "Pendência matrix: definição do projeto indisponível, incompleta ou inválida; nenhum comando enfileirado."; }
    }
}
