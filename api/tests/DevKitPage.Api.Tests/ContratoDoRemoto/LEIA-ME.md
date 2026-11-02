# Contrato do controle remoto

Conjunto CANÔNICO do contrato v1 do controle remoto (`docs/contrato-remoto-v1.md`): o JSON exato que o dev.kit publica e o
que o site lê. Os itens seguintes do projeto `controle-remoto` partem daqui: quem publica é o serviço do git.kit (item 2) e
quem lê é o Board web (item 3).

- `heartbeat.json` — o corpo de `PUT /api/remoto/v1/heartbeat`.
- `retrato.json` — o corpo de `PUT /api/remoto/v1/retrato`, com o exemplo da tela `app-remoto` v4.
- `painel.json` — a resposta de `GET /api/remoto/v1/maquinas/{maquinaId}`, com os nomes e os tipos de cada campo.
- `textos.json` — o que a API aceita num texto do retrato e o que ela recusa com 422, por motivo (`segredo` ou
  `dado-de-cliente`). O sanitizador do dev.kit tem de passar por estes casos antes de enviar.

Teste: `ContratoDoRemotoApiTests`, em `api/tests/DevKitPage.Api.Tests`, que manda estes arquivos pela rota de verdade.
Mudar um nome aqui quebra as máquinas instaladas: campo novo é opcional e entra primeiro na API, publicada antes do pacote
do dev.kit; mudança incompatível é um `v2`, com rota própria. **Copie a pasta inteira para o git.kit** quando o item 2
implementar o envio, e mantenha as duas cópias iguais a cada mudança.
