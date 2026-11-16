using System.Net;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Somente a extensão local do retrato. Não relaxa destinos, HTTPS ou acesso dos artefatos publicados.</summary>
public static partial class RegrasDosArtefatosLocais
{
    public static string ValidarAcao(AcaoNoProjetoRemotoV1 acao)
    {
        if (acao.Acao != ArtefatosV1.AcaoPreviewLocal || !RegrasDosProjetos.SlugValido(acao.Projeto))
            return "Preview local: ação ou projeto inválido.";
        if (acao.Repositorio is not null || acao.Sha is not null)
            return "Preview local usa o snapshot capturado; não combine repositório/SHA de outra geração.";
        if (acao.Artefato is not > 0 || !Hex(acao.Receita, 64) || !Hex(acao.Conjunto, 64))
            return "Preview local: informe artefato, identidade da receita e conjunto capturados no retrato.";
        if (acao.Aplicacao is not { Length: > 0 and <= 48 } app || !char.IsAsciiLetterLower(app[0])
            || !app.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            return "Preview local: escolha a aplicação declarada na receita.";
        if (acao.Operacao is not ("iniciar" or "consultar" or "abrir" or "parar"))
            return "Preview local: use iniciar, consultar, abrir ou parar.";
        if (acao.Versao is < 0 || (acao.Operacao != "consultar" && acao.Versao is null))
            return "Preview local: informe a versão consultada antes de alterar o pedido.";
        return "";
    }

    public static string Validar(ArtefatosLocaisNoRetratoV1? conjunto)
    {
        if (conjunto is null) return "";
        if (conjunto.Contrato != 1 || conjunto.Artefatos is null || conjunto.Artefatos.Count > 5)
            return "Artefatos locais: contrato 1 e até cinco gerações no retrato.";
        if (conjunto.Artefatos.Any(a => a is null) || conjunto.Artefatos.Select(a => a.Id).Distinct().Count() != conjunto.Artefatos.Count)
            return "Artefatos locais: gerações não nulas e identidades únicas.";
        if (Longo(conjunto.Aviso)) return "Artefatos locais: aviso excede o contrato.";
        foreach (var a in conjunto.Artefatos)
        {
            if (a.Id <= 0 || a.Situacao is not ("gerando" or "pronto" or "falhou") || a.CriadoEm == default
                || a.TerminadoEm < a.CriadoEm || a.Tamanho < 0 || Longo(a.Diagnostico) || a.Hash is null
                || (a.Hash.Length > 0 && !Hex(a.Hash, 64)))
                return "Artefato local: identidade, estado, datas, tamanho, hash ou diagnóstico inválidos.";
            if (a.Fontes is null || a.Alvos is null || a.Previews is null || a.Fontes.Count > 100 || a.Alvos.Count > 100 || a.Previews.Count > 20)
                return "Artefato local: até 100 fontes, 100 alvos e 20 aplicações, sem listas nulas.";
            if ((a.Fontes.Count > 0 && !Hex(a.ConjuntoHash, 64)) || (a.Fontes.Count == 0 && a.ConjuntoHash is not null))
                return "Artefato local: o conjunto completo exige sua identidade; registro antigo não inventa snapshot.";
            if (a.Fontes.Any(f => f is null || !Hex(f.Id, 64) || NomeInvalido(f.Nome) || NomeInvalido(f.Branch)
                    || !Sha(f.Sha) || (f.BranchBase is not null && NomeInvalido(f.BranchBase))
                    || (f.ShaBase is not null && !Sha(f.ShaBase)))
                || a.Fontes.Select(f => f.Id).Distinct().Count() != a.Fontes.Count)
                return "Artefato local: fontes únicas com identidade, branch e SHA completos.";
            var fontes = a.Fontes.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            bool FonteDoResultado(string repo, string sha) => Hex(repo, 64) && Sha(sha)
                && (a.Fontes.Count == 0 || a.Fontes.Any(f => f.Id == repo && f.Sha == sha));
            if (a.Publicacao is { } pub && (pub.IdRemoto <= 0 || pub.Tipo is not ("pacote" or "preview")
                || !FonteDoResultado(pub.Repositorio, pub.Sha) || pub.Hash != a.Hash || pub.Tamanho != a.Tamanho
                || (pub.Tipo == "pacote" && (!Hex(pub.Hash, 64) || pub.Tamanho <= 0))
                || (pub.Tipo == "preview" && (pub.Hash != "" || pub.Tamanho != 0))))
                return "Artefato local: publicação não corresponde ao resultado ou à fonte capturada.";
            if (a.Pacote is { } pacote && (!FonteDoResultado(pacote.Repositorio, pacote.Sha)
                || NomeInvalido(pacote.Versao) || !Hex(pacote.Hash, 64) || pacote.Tamanho <= 0 || pacote.IdRemoto < 0
                || (pacote.IdRemoto == 0 && pacote.ValidoAte is not null)
                || (a.Hash.Length > 0 && (a.Hash != pacote.Hash || a.Tamanho != pacote.Tamanho))
                || (a.Publicacao is { } origem && (origem.Repositorio != pacote.Repositorio || origem.Sha != pacote.Sha))
                || (a.Publicacao is { Tipo: "pacote" } principal && (principal.IdRemoto != pacote.IdRemoto
                    || principal.Hash != pacote.Hash || principal.Tamanho != pacote.Tamanho))))
                return "Artefato local: pacote conferido sem versão/hash/tamanho/fonte ou com publicação divergente.";
            if (a.Alvos.Any(t => t is null || !fontes.Contains(t.Repositorio) || !Relativo(t.Nome) || NomeInvalido(t.Receita)
                || (t.Saida is not null && !Relativo(t.Saida)) || (t.Assembly is not null && NomeInvalido(t.Assembly))
                || (t.Framework is not null && NomeInvalido(t.Framework)) || (t.Runtime is not null && NomeInvalido(t.Runtime))))
                return "Artefato local: alvo não pertence ao conjunto ou tem descrição inválida.";
            var detalhes = ValidarDetalhes(a);
            if (detalhes.Length > 0) return detalhes;
            if (a.Comando is { } c && (c.Id is <= 0 || !Hex(c.Recibo, 64) || Longo(c.LogParcial)
                || c.Fim < c.Inicio || (c.MotivoTermino is not null && NomeInvalido(c.MotivoTermino))))
                return "Artefato local: correlação, datas ou log parcial do comando inválidos.";
            if (a.Previews.Any(p => p is null) || a.Previews.Select(p => (p.Receita, p.Aplicacao)).Distinct().Count() != a.Previews.Count)
                return "Artefato local: aplicações únicas por receita.";
            foreach (var p in a.Previews)
            {
                if (!fontes.Contains(p.Receita) || NomeInvalido(p.Aplicacao) || p.Versao < 0 || p.AtualizadoEm == default
                    || (p.Versao > 0 && !Hex(p.Ciclo, 32)) || (p.Versao == 0 && p.Ciclo != "")
                    || p.Pedido is not ("iniciar" or "parar" or "nenhum")
                    || p.Situacao is not ("nao-solicitado" or "iniciando" or "pronto" or "parando" or "parado" or "falhou" or "interrompido")
                    || Longo(p.Diagnostico) || p.Prontidao is null || p.Componentes is null || p.Componentes.Count > 20)
                    return "Preview local: identidade, versão, estado, pedido ou componentes inválidos.";
                if (p.Componentes.Any(x => x is null || NomeInvalido(x.Id) || !fontes.Contains(x.Repositorio)
                    || x.Porta is < 1 or > 65535 || NomeInvalido(x.Situacao) || Longo(x.Diagnostico) || Longo(x.LogParcial))
                    || p.Componentes.Select(x => x.Id).Distinct().Count() != p.Componentes.Count)
                    return "Preview local: componente inválido ou não pertencente ao conjunto.";
                if (p.Situacao == "pronto")
                {
                    if (a.Situacao != "pronto" || p.Pedido != "iniciar" || !p.NestaMaquina || !UrlLocal(p.Url)
                        || !p.Prontidao.Front || !p.Prontidao.Api || !p.Prontidao.Dependencias || !p.Prontidao.Jornada
                        || p.Componentes.Count < 2 || p.Componentes.Any(x => x.Situacao != "pronto" || x.ProntoEm is null || x.Porta is null))
                        return "Preview local: URL pronta exige front, API, dependências e jornada confirmados na máquina do host.";
                }
                else if (p.Url is not null || p.Prontidao.Front || p.Prontidao.Api || p.Prontidao.Dependencias || p.Prontidao.Jornada)
                    return "Preview local: estado não pronto não publica URL nem prontidão.";
                if (p.Abertura is { } abertura && (!Hex(abertura.Id, 32)
                    || abertura.Situacao is not ("solicitado" or "executando" or "concluido" or "falhou")
                    || Longo(abertura.Diagnostico) || abertura.AtualizadoEm == default))
                    return "Preview local: recibo de abertura inválido.";
            }
        }
        return "";
    }

    public static bool UrlLocal(string? texto) => Uri.TryCreate(texto, UriKind.Absolute, out var u)
        && u.Scheme == "http" && u.UserInfo.Length == 0 && u.Query.Length == 0 && u.Fragment.Length == 0
        && u.AbsolutePath == "/" && u.Port is > 0 and <= 65535
        && IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);

    internal static IEnumerable<(string Campo, string? Texto)> Textos(ArtefatosLocaisNoRetratoV1 conjunto, string prefixo)
    {
        yield return ($"{prefixo}.aviso", conjunto.Aviso);
        foreach (var (a, i) in conjunto.Artefatos.Select((a, i) => (a, i)))
        {
            var campo = $"{prefixo}.artefatos[{i}]";
            yield return ($"{campo}.diagnostico", a.Diagnostico);
            if (a.Pacote is { } pacote) yield return ($"{campo}.pacote.versao", pacote.Versao);
            if (a.Detalhes is { } detalhes)
                foreach (var texto in TextosDosDetalhes(detalhes, campo + ".detalhes")) yield return texto;
            foreach (var (f, j) in a.Fontes.Select((f, j) => (f, j)))
            {
                yield return ($"{campo}.fontes[{j}].nome", f.Nome);
                yield return ($"{campo}.fontes[{j}].branch", f.Branch);
                yield return ($"{campo}.fontes[{j}].branchBase", f.BranchBase);
            }
            foreach (var (t, j) in a.Alvos.Select((t, j) => (t, j)))
                foreach (var (nome, valor) in new[] { ("nome", t.Nome), ("receita", t.Receita), ("framework", t.Framework),
                    ("runtime", t.Runtime), ("saida", t.Saida), ("assembly", t.Assembly) })
                    yield return ($"{campo}.alvos[{j}].{nome}", valor);
            if (a.Comando is { } c)
            {
                yield return ($"{campo}.comando.motivoTermino", c.MotivoTermino);
                yield return ($"{campo}.comando.logParcial", c.LogParcial);
            }
            foreach (var (p, j) in a.Previews.Select((p, j) => (p, j)))
            {
                yield return ($"{campo}.previews[{j}].aplicacao", p.Aplicacao);
                yield return ($"{campo}.previews[{j}].diagnostico", p.Diagnostico);
                if (p.Abertura is { } b) yield return ($"{campo}.previews[{j}].abertura.diagnostico", b.Diagnostico);
                foreach (var (x, k) in p.Componentes.Select((x, k) => (x, k)))
                {
                    yield return ($"{campo}.previews[{j}].componentes[{k}].id", x.Id);
                    yield return ($"{campo}.previews[{j}].componentes[{k}].situacao", x.Situacao);
                    yield return ($"{campo}.previews[{j}].componentes[{k}].diagnostico", x.Diagnostico);
                    yield return ($"{campo}.previews[{j}].componentes[{k}].logParcial", x.LogParcial);
                }
            }
        }
    }

    private static bool Hex(string? valor, int tamanho) => valor is not null && valor.Length == tamanho && valor.All(char.IsAsciiHexDigit);
    private static bool Sha(string? valor) => Hex(valor, 40) || Hex(valor, 64);
    private static bool NomeInvalido(string? valor) => string.IsNullOrWhiteSpace(valor) || valor.Length > RemotoV1.TamanhoMaximoDoNome;
    private static bool Longo(string? valor) => valor is null || valor.Length > RemotoV1.TamanhoMaximoDoTexto;
}
