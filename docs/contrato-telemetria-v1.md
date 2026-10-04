# Contrato v1 da telemetria

O que o dev.kit (`git.kit`, `TelemetrySyncService`) envia e a API aceita. Os tipos estão em
`api/src/DevKitPage.Contracts/V1`. JSON em camelCase; datas em ISO 8601 (UTC).

**Regra de evolução:** campo novo é opcional e não quebra ninguém; mudança incompatível é `v2`, com
rota própria — há máquinas instaladas falando v1.

## 1. Registrar a máquina — `POST /api/maquinas/registrar`

```json
{ "maquinaId": "3f2b…(GUID anônimo, sem hífens)", "versaoDevKit": "1.4.0", "codigoRegistro": "…" }
```

- Aceita quem apresenta o `Telemetria:CodigoDeRegistro` configurado, **ou** um JWT de admin (sem
  troca de senha pendente). Sem nenhum dos dois: **401**.
- Resposta **200** `{ "chave": "…" }` — a chave é devolvida UMA vez; a API guarda só o hash.
- Registrar de novo a mesma máquina **gira** a chave (o dev.kit faz isso quando a dele deixa de valer).

## 2. Enviar um lote — `POST /api/telemetria/lote`

Cabeçalho obrigatório: `X-Machine-Key: <chave>`. O JWT de usuário **não** serve aqui.

```json
{
  "versao": "v1",
  "maquinaId": "3f2b…",
  "versaoDevKit": "1.4.0",
  "eventos": [
    { "eventId": "9c1e…", "tipo": "TurnoExecutado", "sessaoId": "a7d0…", "quantidade": 1,
      "valor": 42500, "detalhe": "claude", "em": "2026-10-03T13:01:07Z" }
  ]
}
```

| Resposta | Quando |
|---|---|
| **202** `{ recebidos, novos, duplicados, ignorados }` | Aceito. Reenviar o mesmo lote dá `novos: 0` e não muda nenhum total. |
| **400** | Versão diferente de `v1`, `maquinaId` diferente da máquina da chave, evento sem `eventId`, quantidade negativa, JSON inválido. |
| **401** | Sem chave, ou chave desconhecida (o dev.kit esquece a dele e se registra de novo). |
| **413** | Mais de `Telemetria:MaxEventosPorLote` eventos ou corpo acima de `MaxBytesPorLote`. O dev.kit envia 200 por lote. |

Um `tipo` que a API não conhece é **ignorado** (contado em `ignorados`), não recusado: um dev.kit mais
novo não quebra uma API mais velha.

## Os tipos de evento

| Tipo | `detalhe` | `valor` | Mede |
|---|---|---|---|
| `SessaoIniciada` | versão do dev.kit | — | sessões (tasks) |
| `TurnoExecutado` | CLI (claude, kiro, kimi, glm) | duração em ms | turnos e duração |
| `TurnoFalhou` | causa (`prompt-perdido`, `sessao-ausente`, `aborto-de-encoding`, `sem-autenticacao`, `limite-de-uso`, `nao-classificada`) | — | taxa de falha por causa |
| `FluxoExecutado` | id do fluxo | — | fluxos executados |
| `FerramentaAcionada` | nome da ferramenta | — | ferramentas (quantidade = chamadas) |
| `ComandoDelegado` | executável (sem argumentos) | — | comandos delegados |
| `ArquivoAlterado` | — | — | arquivos (quantidade = arquivos) |
| `ObjetivoAvaliado` | — | nota 0–100 | nota média dos avaliadores |
| `ObjetivoCumprido` / `ObjetivoRecusado` | — | — | objetivos aceitos × recusados |

## O que NÃO é coletado

Caminhos de pasta, nomes de repositório ou de cliente, argumentos de comando, o texto do prompt e
das respostas, código, e-mail e o usuário do Windows. A máquina é um GUID gerado no dev.kit, nunca o
hostname. A sessão é o id interno da task no dev.kit.

## No dev.kit

- **Ligado por padrão** (opt-out): a URL da API e o código de registro vêm preenchidos no build do
  dev.kit, e o usuário desliga em *Configurações → Telemetria de uso*. Só o que for registrado com o
  envio ligado é enviado.
- O código de registro embutido no instalador é, na prática, **público** (dá para extraí-lo do
  binário). Ele separa as máquinas do dev.kit de chamadas anônimas, mas não autentica ninguém. Se
  vazar ou for abusado, troque o `Telemetria__CodigoDeRegistro` na API e publique um dev.kit novo:
  as máquinas já registradas continuam enviando com a chave delas.
- **Fila local**: a própria tabela de telemetria; o que não foi aceito espera o próximo envio.
- **Espera exponencial** depois de falha (1, 2, 4… minutos, até 6 h); uma falha nunca afeta o turno.
- **Enviar agora**: o botão das Configurações ou `devcli telemetria --enviar`.
