# Contrato v1 do controle remoto

O que o dev.kit publica de cada máquina e o que o site lê: a fase 1 do projeto `controle-remoto`. O serviço do Windows
(item 2) publica, e o Board web (item 3) lê. Os tipos estão em `api/src/DevKitPage.Contracts/V1/RemotoV1.cs`, e os JSON
canônicos, em [`api/tests/DevKitPage.Api.Tests/ContratoDoRemoto`](../api/tests/DevKitPage.Api.Tests/ContratoDoRemoto). JSON em
camelCase; datas em ISO 8601 com fuso.

**Regra de evolução:** o envio é FECHADO: campo fora do contrato é recusado com 400. Campo novo é opcional e entra primeiro
na API, publicada antes do pacote do dev.kit. Mudança incompatível é `v2`, com rota própria (`/api/remoto/v2`), porque há
máquinas instaladas falando v1. Situação, tipo e agente são códigos abertos: o valor desconhecido passa, se tiver o formato,
e o site o mostra neutro.

## Autenticação e dono

Todas as rotas exigem o **JWT do usuário**, sem troca de senha pendente, com o `Authorization: Bearer` do mesmo login do
site. O token vence (`Jwt:ExpiracaoMinutos`): quem publica o renova por `POST /api/auth/renovar`.

| Situação | Resposta |
|---|---|
| Sem token, token vencido ou assinado por outro segredo | **401** |
| Só o `X-Machine-Key` da telemetria | **401**: a telemetria é anônima, e isto tem dono |
| Troca de senha pendente | **403** |

O dono é o usuário do **token**, nunca do corpo. O mesmo GUID em dois usuários são duas máquinas que não se enxergam. A
máquina de outro é **404**, sem dizer se existe, para o colega de empresa, para o gestor e para o admin.

## 1. Heartbeat — `PUT /api/remoto/v1/heartbeat`

Publique a cada `proximoEmSegundos`, com o app aberto ou fechado.

```json
{
  "versao": "v1",
  "maquinaId": "3f2b1c9e5d7a4b60a1c2d3e4f5061728",
  "apelido": "Notebook",
  "versaoDevKit": "1.61.0",
  "devKitAberto": true,
  "agente": "ativo",
  "turnosEmAndamento": 1
}
```

| Campo | O que é |
|---|---|
| `maquinaId` | O GUID anônimo, com ou sem hífens. O hostname é recusado. A API devolve e guarda 32 dígitos minúsculos, sem hífens. |
| `apelido` | O nome que o usuário deu à máquina, até 100 caracteres. Vazio (nunca nulo), a API usa `máquina 3f2b1c9e`. Nunca o hostname. |
| `versaoDevKit` | A versão instalada, até 50 caracteres. |
| `devKitAberto` | O app está aberto. |
| `agente` | `ativo`, `pausado`, `limite` ou `desconhecido`. |
| `turnosEmAndamento` | Quantos turnos rodam agora: é o que o reinício seguro confere. |

Resposta **200**:

```json
{ "maquinaId": "3f2b1c9e5d7a4b60a1c2d3e4f5061728", "recebidoEm": "2026-11-02T08:52:30+00:00", "proximoEmSegundos": 60 }
```

A hora que vale é a do **servidor**: uma máquina com o relógio errado não parece conectada. Cada usuário tem até
`Remoto:MaximoDeMaquinas` máquinas; a nova acima disto é **409**, e a parada além da retenção libera a vaga.

## 2. Retrato — `PUT /api/remoto/v1/retrato`

A foto inteira de projetos, fluxos e pendências, publicada depois do primeiro heartbeat. Sem heartbeat, **404**. Cada envio
substitui o anterior, e lista vazia quer dizer que não há nada: mande as três sempre. Exemplo completo em
[`retrato.json`](../api/tests/DevKitPage.Api.Tests/ContratoDoRemoto/retrato.json), com os dados da tela `app-remoto` v4.

```json
{
  "versao": "v1",
  "maquinaId": "3f2b1c9e5d7a4b60a1c2d3e4f5061728",
  "geradoEm": "2026-11-02T08:52:00+00:00",
  "projetos": [
    { "slug": "projeto", "nome": "projeto", "situacao": "aprovado", "itens": 21, "integrados": 20, "rodando": 1, "onda": 10, "ondas": 10 }
  ],
  "fluxos": [
    { "id": "a7d04c1e", "titulo": "Item 20 · layout do plano", "fluxo": "Fluxo simples · padrão", "etapa": "Implementação",
      "situacao": "rodando", "projeto": "projeto", "item": 20, "desde": "2026-11-02T08:40:00+00:00" }
  ],
  "pendencias": [
    { "id": "integracao-item-20", "tipo": "aprovacao", "titulo": "Integrar o item 20 no projeto?", "resumo": "Nota 100 · regressão sem falhas",
      "opcoes": ["Revisar e aprovar", "Pedir ajuste"], "projeto": "projeto", "item": 20, "desde": "2026-11-02T08:50:00+00:00" }
  ]
}
```

| Lista | Teto | Campos |
|---|---|---|
| `projetos` | 100 | `slug`, `nome` (até 200), `situacao`, `itens`, `integrados`, `rodando`, `onda`, `ondas`. Os contadores têm de fechar: `integrados + rodando <= itens` e `onda <= ondas`. |
| `fluxos` | 100 | `id` interno e estável, sem pasta nem nome; `titulo` (até 200); `fluxo` e `etapa` (até 200, vazios quando não se aplicam, nunca nulos); `situacao` (`rodando`, `na-fila`, `pergunta`, `falhou`); `projeto` e `item` (nulos fora de projeto); `desde`. |
| `pendencias` | 50 | `id` estável, o que uma decisão futura referencia; `tipo` (`pergunta`, `decisao`, `aprovacao`); `titulo` (até 200); `resumo` (até 500, vazio quando não há, nunca nulo); `opcoes` (até 6 botões, vazia quando não há); `projeto`, `item`, `desde`. |

Resposta **200**:

```json
{ "maquinaId": "3f2b1c9e5d7a4b60a1c2d3e4f5061728", "aplicado": true, "geradoEm": "2026-11-02T08:52:00+00:00", "recebidoEm": "2026-11-02T08:52:01+00:00" }
```

Vale o retrato de `geradoEm` mais recente. O reenvio e o que chega fora de ordem respondem **200** com `aplicado: false` e os
dados do que continua valendo: não há o que repetir. O relógio adiantado não congela a foto: o retrato "do futuro" vale como
gerado na hora do servidor.

## O que o retrato não aceita

A sanitização é do dev.kit (`TraceSanitizado`). A API é a defesa em dobro: ela **recusa inteiro, sem gravar nada**, e o
retrato anterior continua valendo.

| Resposta | Quando |
|---|---|
| **422** `motivo: segredo` | Token, senha, chave, `Bearer`, JWT ou connection string em qualquer texto, identificador ou código: `id`, `slug`, `situacao`, `tipo` e `agente` também passam por aqui. |
| **422** `motivo: dado-de-cliente` | Caminho, URL, e-mail, usuário de domínio, GUID, CPF, CNPJ ou telefone em qualquer texto livre; CPF, CNPJ ou telefone em identificador ou código. O `id` que é um GUID inteiro é legítimo. |
| **400** | Campo fora do contrato, versão diferente de `v1`, id que não é GUID, texto acima do teto ou com quebra de linha (nas pontas também), campo de texto nulo, contadores que não fecham, id repetido, JSON inválido. |
| **415** | `Content-Type` ausente, que não é JSON ou com um charset desconhecido. |

O 422 traz o campo, e nunca o valor. Os casos canônicos, para o sanitizador conferir antes de enviar, estão em
[`textos.json`](../api/tests/DevKitPage.Api.Tests/ContratoDoRemoto/textos.json).

```json
{ "status": 422, "detail": "fluxos[0].titulo parece ter um segredo (token, senha, chave ou connection string): nada foi gravado.", "campo": "fluxos[0].titulo", "motivo": "segredo" }
```

**O limite da defesa.** Um nome de cliente escrito num título não tem formato para reconhecer. O que o protege é o contrato:
não há campo de descrição, definição, prompt, conversa, repositório, branch nem caminho, e cada texto é uma linha curta. Quem
publica monta o título a partir do que já é rótulo, e nunca do texto do work item.

**Limites.** Corpo acima de `Remoto:MaxBytesPorEnvio`: **413**. Acima de `Remoto:EnviosPorMinuto` por usuário: **429** com
`Retry-After`. Nada se perde: o envio seguinte substitui o que ficou para trás, então não há fila para reenviar.

## 3. Leitura do site

`GET /api/remoto/v1/maquinas`: as máquinas do dono, da que falou por último à mais antiga.

```json
[
  {
    "maquinaId": "3f2b1c9e5d7a4b60a1c2d3e4f5061728",
    "apelido": "Notebook",
    "versaoDevKit": "1.61.0",
    "conectada": true,
    "devKitAberto": true,
    "agente": "ativo",
    "turnosEmAndamento": 1,
    "ultimoHeartbeat": "2026-11-02T08:52:30+00:00",
    "retratoRecebidoEm": "2026-11-02T08:52:01+00:00"
  }
]
```

`GET /api/remoto/v1/maquinas/{maquinaId}`: `{ "maquina": { … }, "retrato": { … } }`, com a mesma máquina e o último retrato, no
formato em que foi publicado ([`painel.json`](../api/tests/DevKitPage.Api.Tests/ContratoDoRemoto/painel.json)). Com a máquina
que só publicou heartbeat, `retrato` é nulo.

`conectada` é falso quando o último heartbeat passou de `Remoto:ToleranciaDoHeartbeatSegundos`. Aí `devKitAberto`, `agente` e
`turnosEmAndamento` são o **último estado conhecido**, e não o de agora: o site mostra a máquina desconectada, e não finge que
entregou nada.

## Retenção

A máquina sem heartbeat há mais de `Remoto:RetencaoDias` dias sai no expurgo diário, com o retrato. Guarda-se só o último
heartbeat e o último retrato de cada máquina: não há histórico.
