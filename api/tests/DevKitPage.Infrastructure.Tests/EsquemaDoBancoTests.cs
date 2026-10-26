using DevKitPage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A verificação do esquema do Azure SQL na subida (bug do painel Uso, 26/10): o modelo comparado com as colunas que o
/// banco tem, e cada falta com o script de <c>api/scripts/sqlserver</c> que a traz — o "aplique o Us687.sql" que faltou.
/// </summary>
public sealed class EsquemaDoBancoTests
{
    private static List<(string Tabela, string Coluna)> ColunasDoModelo(IModel modelo)
        => modelo.GetEntityTypes().Where(e => e.GetTableName() is not null)
            .SelectMany(e => e.GetProperties().Select(p => (e.GetTableName()!, p.GetColumnName(StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema()))!)))
            .Distinct()
            .ToList();

    private static IModel Modelo()
    {
        using var db = new DevKitPageDb(new DbContextOptionsBuilder<DevKitPageDb>().UseSqlServer("Server=.;Database=modelo").Options);
        return db.Model;
    }

    [Fact]
    public void O_banco_completo_nao_tem_falta()
    {
        var modelo = Modelo();

        Assert.Empty(EsquemaDoBanco.Comparar(modelo, ColunasDoModelo(modelo)));
    }

    [Fact]
    public void O_banco_sem_o_Us687_diz_a_tabela_e_a_coluna_que_faltam_e_o_script()
    {
        // A base de produção antes do Us687.sql: sem FotosDeEstado e sem as colunas novas de Usuarios.
        var modelo = Modelo();
        var existentes = ColunasDoModelo(modelo)
            .Where(c => c.Tabela != "FotosDeEstado" && !(c.Tabela == "Usuarios" && c.Coluna == "LicencaTipo"))
            .Select(c => (c.Tabela.ToLowerInvariant(), c.Coluna)); // a caixa não importa

        var faltas = EsquemaDoBanco.Comparar(modelo, existentes);

        Assert.Equal(
            new[] { "a tabela FotosDeEstado (Us687.sql)", "a coluna Usuarios.LicencaTipo (Us687.sql)" },
            faltas.Select(f => f.ToString()));
    }

    [Fact]
    public void Cada_tabela_do_modelo_tem_o_script_que_a_cria()
    {
        // Uma tabela nova sem script do Azure SQL é exatamente o esquecimento que o painel pagou: a verificação não saberia o que mandar aplicar.
        // As tabelas que só o EnsureCreated cria (as da primeira versão) não têm script — o modelo inteiro nasce numa base vazia.
        string[] daPrimeiraVersao = ["EventosDeUso", "Maquinas", "TotaisDiarios", "Usuarios"];
        var semScript = Modelo().GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct()
            .Where(t => !daPrimeiraVersao.Contains(t) && EsquemaDoBanco.ScriptsQueTrazem(t, null).Count == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(semScript);
    }

    [Fact]
    public void Sem_falta_a_descricao_e_vazia()
    {
        Assert.Empty(new EsquemaDoBanco().Descrever());
    }
}
