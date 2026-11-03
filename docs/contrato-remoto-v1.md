# Contrato v1 do controle remoto

O que o dev.kit publica e o site lê na fase 1 do controle remoto (projeto `controle-remoto-v2`): o **heartbeat** de cada
máquina e o **retrato** sanitizado dos projetos, fluxos e pendências. Os tipos estão em
`api/src/DevKitPage.Contracts/V1/RemotoV1.cs`; as regras, em `api/src/DevKitPage.Core/ServicosDoRemoto.cs`. JSON em
camelCase; datas em ISO 8601 (UTC).

**Regra de evolução:** campo novo é opcional e não muda a versão (a API ignora o campo que não conhece, e um dev.kit
antigo não o manda). Mudança incompatível é o **contrato 2**: o envio leva `contrato`, e a API recusa o que não fala com
**400** `codigo: contrato-desconhecido` e `contratoAceito: 1` — o dev.kit sabe que tem de se atualizar, em vez de gravar
um dado que a API leria errado.

## Autenticação e isolamento

- Todas as rotas exigem o **JWT do usuário** do dev.kit.page (o mesmo do login do dev.kit), sem troca de senha pendente.
  Sem token, **401**. A chave de máquina (`X-Machine-Key`) da telemetria **não** serve aqui.
- O dono é o usuário do token. A máquina é única por (usuário, `maquinaId`): o mesmo id anônimo enviado por outro
  usuário é outra máquina, e nenhum lê ou grava a do outro. A máquina de outro usuário é **404**, sem revelar que existe.
  Nem o admin lê a máquina alheia.
- Limite por usuário: `Remoto:EnviosPorMinuto` (padrão 60) envios por minuto, heartbeat e retrato somados; acima, **429**.

## 1. Heartbeat — `POST /api/remoto/heartbeat`

O serviço do Windows (`GitKit.Scheduler`) envia a cada `IntervaloDoHeartbeatSegundos` (60 s), também com o app fechado.

```json
{ "contrato": 1, "maquinaId": "3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d", "nome": "Notebook do trabalho",
  "versaoDevKit": "75", "origem": "servico", "appAberto": true, "estadoDoAgente": "ativo",
  "turnoEmAndamento": false, "enviadoEm": "2026-11-03T12:00:00Z" }
```

- `maquinaId`: o id anônimo do dev.kit (o da telemetria) — letras, dígitos e hífen, até 64. Nunca o hostname.
- `origem`: `servico` ou `app`. `estadoDoAgente`: `ativo`, `pausado`, `limite` ou `parado` (vazio é `parado`).
- `turnoEmAndamento`: o site avisa antes de reiniciar o dev.kit (fase 5).
- Resposta **200** `{ "maquinaId", "recebidoEm", "intervaloSegundos": 60 }`.
- A máquina aparece `conectada` enquanto o último heartbeat tem até `SegundosAteDesconectar` (180 s) pelo relógio da API.

## 2. Retrato — `PUT /api/remoto/retrato`

Uma FOTO inteira do estado, enviada quando ele muda. Substitui a anterior; a mais velha que chegar depois (pelo
`geradoEm`) é aceita com `aplicado: false` e descartada.

```json
{
  "contrato": 1, "maquinaId": "3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d", "geradoEm": "2026-11-03T12:00:00Z",
  "projetos": [
    { "slug": "controle-remoto-v2", "nome": "controle-remoto v2", "situacao": "aprovado", "ondaAtual": 1,
      "itens": [ { "numero": 1, "titulo": "API recebe heartbeat e retrato", "situacao": "rodando", "onda": 1 } ] }
  ],
  "fluxos": [
    { "id": "exec-812", "nome": "Fluxo 6", "etapa": "Implementação", "situacao": "executando",
      "projeto": "controle-remoto-v2", "item": 1, "agente": "claude", "desdeEm": "2026-11-03T11:40:00Z" }
  ],
  "pendencias": [
    { "id": "pergunta-77", "tipo": "pergunta", "titulo": "Qual fila usar?", "pergunta": "Storage Queue ou Service Bus?",
      "opcoes": ["Storage Queue", "Service Bus"], "jaTentei": "Li a proposta.", "projeto": "controle-remoto-v2",
      "item": 4, "fluxo": "exec-813", "criadaEm": "2026-11-03T11:55:00Z" }
  ]
}
```

- Situação do projeto e do item: as de `ProjetosV1` (`rascunho`, `aguardando`, `aprovado`, `pausado`, `fechando`,
  `concluido`; `planejado`, `rodando`, `pergunta`, `integrado`, `falhou`).
- Pendência: `tipo` é `pergunta`, `aprovacao` ou `falha`. `pergunta`, `opcoes` e `jaTentei` são os campos do
  `[GITKIT:PERGUNTA]` — as decisões remotas (fase 3) os mostram como botões. O `id` é estável: é por ele que a resposta volta.
- Tetos: 256 KB de corpo; 50 projetos de até 100 itens, 100 fluxos, 100 pendências de até 10 opções; nomes e títulos
  até 200 caracteres, `pergunta` e `jaTentei` até 2000.
- Resposta **200** `{ "maquinaId", "recebidoEm", "aplicado" }`.

## Recusas dos envios

| Resposta | Quando |
|---|---|
| **400** | JSON inválido ou vazio, forma fora do contrato (com a mensagem do campo), ou `contrato` diferente de 1 (`codigo: contrato-desconhecido`, `contratoAceito`). |
| **401** | Sem JWT, ou JWT vencido. |
| **413** | Corpo acima do teto (4 KB o heartbeat, 256 KB o retrato), com ou sem `Content-Length`. |
| **415** | O corpo não é `application/json`, ou o `charset` é desconhecido. |
| **422** | Token ou dado de cliente em algum texto: `codigo: dado-sensivel` e `campos: [{ "campo": "pendencias[0].titulo", "tipo": "cnpj" }]` — o valor nunca volta. |
| **429** | Acima do limite por usuário. |

O 415 é só do formato do corpo: a leitura (`CorpoJson`) não captura exceção genérica, então a recusa do conteúdo (400 e
422) nunca vira 415, e uma falha interna é 500 com o código do log.

### O que conta como dado sensível

Todo texto do envio é conferido, inclusive ids e slugs. Tipos: `segredo`, `email`, `cpf`, `cnpj`, `telefone`.

- **Segredo:** os mesmos padrões da máscara dos relatos (token do GitHub e do Azure DevOps, `Bearer`, JWT, `senha=`,
  connection string, chave privada).
- **CPF e CNPJ:** só com os dígitos verificadores certos. O CNPJ numérico vale formatado ou com os 14 dígitos; o
  alfanumérico (de 2026), só formatado (`AB.CDE.FGH/IJKL-DV`). O carimbo `20261102-095333` tem 14 dígitos, mas o hífen fora
  da posição do DV o tira do padrão: **não** é CNPJ.
- **Telefone:** com `+55`, com o DDD entre parênteses, ou com o DDD solto e um separador (o celular de 11 dígitos também
  colado); o DDD tem de existir, e o assinante segue a Anatel (celular com 9 na frente, fixo de 2 a 5). O celular sem DDD
  conta com o separador (`98765-4321`). **Não** é telefone: o número colado por hífen a uma palavra — o slug
  `sprint-11-2026-2027` —, a faixa de anos (`11 2026-2027`) e o fixo sem DDD, indistinguível de uma faixa de anos.

## 3. Leitura do site

- `GET /api/remoto/maquinas` → `MaquinaRemotaV1[]`: `maquinaId`, `nome`, `versaoDevKit`, `origem`, `appAberto`,
  `estadoDoAgente`, `turnoEmAndamento`, `conectada`, `ultimoHeartbeatEm`, `retratoGeradoEm`. As conectadas primeiro.
- `GET /api/remoto/maquinas/{maquinaId}/retrato` → `RetratoDaMaquinaV1`: `maquina`, `retrato` (o último aplicado) e
  `recebidoEm`. **404** para a máquina sem retrato ou de outro usuário.

## Retenção

A máquina sem heartbeat nem retrato há mais de `Remoto:RetencaoDias` (padrão 30) sai do banco com o retrato, no
expurgo diário. O retrato guardado é só o último: não há histórico.

## No Azure SQL

A tabela `MaquinasRemotas` vem de `api/scripts/sqlserver/Remoto.sql`, idempotente — rode-o antes de publicar a API.
