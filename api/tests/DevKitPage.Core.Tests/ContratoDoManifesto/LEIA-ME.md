# Contrato do manifesto do catálogo

Conjunto CANÔNICO de entrada e saída do gerador do manifesto (`GeradorDoManifesto`, US #425), usado pelo teste
`GeradorDoManifestoTests.O_contrato_do_manifesto_gera_o_esperado_dos_fixtures_canonicos`.

- `anterior.json` — o manifesto anterior (versão 1, três skills).
- `base/` — o catálogo de agora: uma skill inalterada (com `{DEVCLI}` e `{MSBUILD}`, o `requer`), uma alterada, uma
  nova, um plan com `key` e um fluxo `devkit-fluxo` com etapas (`skillName`) e um plan embutido (`planos[]`). A skill
  `descrever-telas` do anterior sumiu: vira retirada.
- `esperado.json` — o `ManifestoJson.Serializar(GeradorDoManifesto.Gerar(anterior, Ler(base), 160).Manifesto)`,
  comparado texto a texto (com as quebras em LF).

**O MESMO conjunto existe no site**, em `dev.kit.page/api/tests/DevKitPage.Core.Tests/ContratoDoManifesto/`, onde o
porte do gerador (US #461, task #463) tem de gerar o mesmo `esperado.json`. É o que garante que o manifesto publicado
pelo site é o que o dev.kit sabe ler e o que o gerador daqui produziria.

Mudar o gerador de um lado obriga a atualizar os DOIS: regenere o `esperado.json` com o gerador alterado, confira à mão
o que mudou, e copie a pasta inteira para o outro repositório no mesmo pedido de mudança.
