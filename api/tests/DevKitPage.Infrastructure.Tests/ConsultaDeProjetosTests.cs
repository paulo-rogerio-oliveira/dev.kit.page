using System.Data.Common;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace DevKitPage.Infrastructure.Tests;

public sealed class ConsultaDeProjetosTests(ITestOutputHelper saida)
{
    private sealed class Comandos : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Listar_projeta_escalares_sem_materializar_planos_e_preserva_leitura_completa()
    {
        await using var banco = new BaseDeTeste();
        await banco.InicializarAsync();
        await banco.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var usuario = await db.Usuarios.Select(u => u.Id).SingleAsync();
            var escopo = new EscopoDoDesign(usuario, null, false);
            var escrita = sp.GetRequiredService<IProjetosDaEmpresa>();
            for (var p = 0; p < 8; p++)
            {
                var envio = new EnvioDeProjetoV1($"projeto-{p}", $"Projeto {p}", "usuario", "", ["repo"],
                    $"feature/projeto-{p}", false, new string('d', 2000), [], true, 2, "main", "rascunho", null,
                    Enumerable.Range(1, 12).Select(n => new ItemDoProjetoV1(n, $"Item {n}", new string('x', 1000),
                        "", [], [], [], 1, 0, n == 1 ? "integrado" : n == 2 ? "pergunta" : n == 3 ? "falhou" : "planejado")).ToArray());
                Assert.NotNull((await escrita.GravarAsync(escopo, envio, default)).Valor);
            }

            var comandos = new Comandos();
            await using var leitor = new DevKitPageDb(new DbContextOptionsBuilder<DevKitPageDb>()
                .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(comandos).Options);
            var completos = await new ProjetosDaEmpresa(leitor, banco.Relogio).ListarAsync(escopo, null, null, default);
            var consultasAntes = comandos.Sql.Count;
            comandos.Sql.Clear();
            var resumos = await new ConsultaDeProjetos(leitor).ListarResumosAsync(escopo, null, null, default);
            Assert.Equal(8, resumos.Count);
            Assert.All(resumos, r => Assert.Equal((12, 1, 1, 1, 8, true),
                (r.TotalDeItens, r.ItensConcluidos, r.ItensComPergunta, r.ItensFalhos, r.Progresso, r.TemPendencias)));
            var sql = Assert.Single(comandos.Sql);
            Assert.DoesNotContain("\"Documento\"", sql);
            Assert.DoesNotContain("\"Descricao\"", sql);
            Assert.DoesNotContain("\"Definicao\"", sql);
            Assert.Empty(leitor.ChangeTracker.Entries());
            var antes = JsonSerializer.SerializeToUtf8Bytes(completos).Length;
            var depois = JsonSerializer.SerializeToUtf8Bytes(resumos).Length;
            Assert.True(depois < antes / 10, $"antes={antes}; depois={depois}");
            saida.WriteLine($"8 projetos x 12 itens: consultas {consultasAntes} -> {comandos.Sql.Count}; objetos projeto/item {completos.Count + completos.Sum(p => p.Itens.Count)} -> {resumos.Count}; bytes JSON {antes} -> {depois}.");

            // A listagem nem interpreta um documento: um plano inválido fica restrito ao detalhe.
            var primeiro = await db.ProjetosCompartilhados.OrderBy(p => p.Id).FirstAsync();
            primeiro.Documento = "documento propositalmente inválido";
            await db.SaveChangesAsync();
            Assert.Equal(8, (await new ConsultaDeProjetos(leitor).ListarResumosAsync(escopo, null, null, default)).Count);
            Assert.Empty(await new ConsultaDeProjetos(leitor).ListarResumosAsync(escopo, null, banco.Relogio.Agora.AddSeconds(1), default));
            using var cancelado = new CancellationTokenSource();
            cancelado.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConsultaDeProjetos(leitor)
                .ListarResumosAsync(escopo, null, null, cancelado.Token));
            return true;
        });
    }
}
