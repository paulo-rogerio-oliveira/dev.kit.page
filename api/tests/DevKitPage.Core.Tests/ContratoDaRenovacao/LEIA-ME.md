# Contrato da renovação da licença

Conjunto CANÔNICO dos casos da renovação da licença do dev.kit (US #687, Rule de impacto "Licença"): a validade que o
admin define para um usuário no dev.kit.page tem de ser respeitada pela renovação que o dev.kit pede no login — e os dois
lados decidem a janela dos 7 dias com a MESMA regra.

Cada caso traz a licença que a máquina tem (`emitidaEm`, `validaAte`), o instante (`agora`), se houve login novo
(`forcar`), o que o admin definiu (`admin`: `automatica`, `data`, `sem-prazo` ou `revogada`, com a validade e a última
alteração) e o esperado:

- `clienteChama` — o dev.kit chama a API? (`LicencaDoDevKit.DeveChamarApi`, git.kit): só com login novo, a partir do
  7º dia da emissão, ou a menos de 7 dias do fim da licença;
- `apiRenova` / `recusa` — a API renova? (`RegrasDeLicenca.Avaliar`, dev.kit.page): a janela é a mesma, aberta também
  pela alteração do admin depois da emissão; a revogada e a vencida são recusadas;
- `validaAteEmitida` — até quando vai a licença emitida (`RegrasDeLicenca.ValidadeEmitida`): os 30 dias de sempre, sem
  passar da validade do admin.

Testes: `ContratoDaRenovacaoTests` no dev.kit.page (`api/tests/DevKitPage.Core.Tests`) e no git.kit
(`tests/GitKit.Core.Tests/Setup`). **O MESMO arquivo existe nos dois repositórios**: mudar a regra de um lado obriga a
atualizar os casos e copiar a pasta inteira para o outro no mesmo pedido de mudança.
