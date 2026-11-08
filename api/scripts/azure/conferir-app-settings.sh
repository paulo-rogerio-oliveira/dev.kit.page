#!/usr/bin/env bash
# A pré-checagem das app settings do App Service da API ANTES do deploy (docs/publicacao-azure.md, "Pré-checagem das app
# settings"): o deploy não passa à frente da configuração. Confere o que a API e o worker da provisão (o WebJob do mesmo
# pacote) RECUSAM na subida em Production — a API parada em 403 "stopped" depois do deploy, com a configuração nova faltando,
# é o que esta checagem evita — e avisa, sem segurar, o que só degrada um recurso.
#
# Só LÊ (az webapp config appsettings list / connection-string list) e NUNCA imprime um valor: a saída tem só os nomes e o
# que falta. Os valores ficam na memória do processo.
#
# Fora do Azure (os testes e a conferência local), --settings lê um arquivo no mesmo formato do az (nome<TAB>valor por
# linha) e --conexoes um arquivo com um nome de connection string por linha; com eles o az não é chamado.
set -euo pipefail
export MSYS_NO_PATHCONV=1

uso() {
  cat <<'EOF'
Uso: conferir-app-settings.sh [--app <nome>] [--grupo <grupo>] [--settings <arquivo> [--conexoes <arquivo>]]

  --app <nome>           O App Service da API (e do WebJob da provisão).                     (padrão: devkit)
  --grupo <nome>         O grupo de recursos dele.                     (padrão: o grupo do App Service, lido do az)
  --settings <arquivo>   As app settings em "nome<TAB>valor" por linha, sem chamar o az (conferência local e testes).
  --conexoes <arquivo>   As connection strings do App Service, um nome por linha (só com --settings).

Saída 0: nada segura o deploy. Saída 1: falta configuração (cada FALTA diz o quê e por quê). Saída 2: uso errado.
EOF
}

APP="devkit" GRUPO="" ARQUIVO_SETTINGS="" ARQUIVO_CONEXOES=""
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --grupo) GRUPO="$2"; shift 2 ;;
    --settings) ARQUIVO_SETTINGS="$2"; shift 2 ;;
    --conexoes) ARQUIVO_CONEXOES="$2"; shift 2 ;;
    -h|--help) uso; exit 0 ;;
    *) echo "Opção desconhecida: $1" >&2; uso >&2; exit 2 ;;
  esac
done
if [ -n "$ARQUIVO_CONEXOES" ] && [ -z "$ARQUIVO_SETTINGS" ]; then
  echo "O --conexoes acompanha o --settings (sem ele, as duas listas vêm do az)." >&2; exit 2
fi

if [ -n "$ARQUIVO_SETTINGS" ]; then
  [ -f "$ARQUIVO_SETTINGS" ] || { echo "Arquivo de settings não encontrado: $ARQUIVO_SETTINGS" >&2; exit 2; }
  SETTINGS=$(cat "$ARQUIVO_SETTINGS")
  CONEXOES=$([ -n "$ARQUIVO_CONEXOES" ] && cat "$ARQUIVO_CONEXOES" || true)
  ORIGEM="arquivo $ARQUIVO_SETTINGS"
else
  command -v az >/dev/null || { echo "O az não está instalado." >&2; exit 2; }
  if [ -z "$GRUPO" ]; then
    GRUPO=$(az webapp list --query "[?name=='$APP'].resourceGroup | [0]" -o tsv --only-show-errors)
    GRUPO="${GRUPO%$'\r'}"
    [ -n "$GRUPO" ] || { echo "O App Service $APP não foi achado na assinatura do login (informe --grupo)." >&2; exit 2; }
  fi
  SETTINGS=$(az webapp config appsettings list -g "$GRUPO" -n "$APP" --query "[].[name, value]" -o tsv --only-show-errors)
  CONEXOES=$(az webapp config connection-string list -g "$GRUPO" -n "$APP" --query "[].name" -o tsv --only-show-errors)
  ORIGEM="App Service $APP (grupo $GRUPO)"
fi

declare -A VALOR=()
while IFS=$'\t' read -r nome valor || [ -n "$nome" ]; do
  nome="${nome%$'\r'}"; valor="${valor%$'\r'}"
  [ -n "$nome" ] && VALOR["$nome"]="$valor"
done <<<"$SETTINGS"
declare -A CONEXAO=()
while IFS= read -r nome || [ -n "$nome" ]; do
  nome="${nome%$'\r'}"
  [ -n "$nome" ] && CONEXAO["$nome"]=1
done <<<"$CONEXOES"

FALTAS=0 AVISOS=0
ok() { echo "ok     $1"; }
falta() {
  FALTAS=$((FALTAS + 1))
  echo "FALTA  $1: $2"
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo "::error title=App setting $1::$2"; fi
}
aviso() {
  AVISOS=$((AVISOS + 1))
  echo "aviso  $1: $2"
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo "::warning title=App setting $1::$2"; fi
}

# O valor aparado (vazio quando a setting não existe).
valor() { local v="${VALOR[$1]:-}"; v="${v#"${v%%[![:space:]]*}"}"; printf '%s' "${v%"${v##*[![:space:]]}"}"; }
tem() { [ -n "$(valor "$1")" ]; }
minusculo() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }
# A referência ao Key Vault é resolvida pela plataforma: o formato do valor não se confere daqui.
do_cofre() { case "$(valor "$1")" in @Microsoft.KeyVault\(*) return 0 ;; *) return 1 ;; esac; }
https() { case "$(valor "$1")" in https://?*) return 0 ;; *) return 1 ;; esac; }

# Um inteiro opcional numa faixa (a API não sobe fora dela).
faixa() {
  local nome="$1" minimo="$2" maximo="$3" v
  tem "$nome" || return 0
  do_cofre "$nome" && { ok "$nome"; return 0; }
  v=$(valor "$nome")
  if [[ "$v" =~ ^-?[0-9]+$ ]] && [ "$v" -ge "$minimo" ] && [ "$v" -le "$maximo" ]; then ok "$nome"
  else falta "$nome" "vai de $minimo a $maximo (a API não sobe fora da faixa)"; fi
}

echo "## Pré-checagem das app settings — $ORIGEM (só os nomes; nenhum valor é mostrado)"

# A API: o segredo do JWT (Seguranca.ValidarSegredo).
if ! tem Jwt__Segredo; then falta Jwt__Segredo "sem ele a API não sobe em Production (32 caracteres ou mais, ou a referência ao Key Vault)"
elif ! do_cofre Jwt__Segredo && [ "${#VALOR[Jwt__Segredo]}" -lt 32 ]; then falta Jwt__Segredo "tem menos de 32 caracteres: a API não sobe"
else ok Jwt__Segredo; fi

# A base: o provider explícito (sem ele vale o SQLite em wwwroot, somente leitura no deploy por pacote) e a conexão.
case "$(minusculo "$(valor Banco__Provider)")" in
  sqlserver|sqlite) ok Banco__Provider ;;
  "") falta Banco__Provider "sem ele a API usa o SQLite em wwwroot, somente leitura no deploy por pacote (use SqlServer)" ;;
  *) falta Banco__Provider "é SqlServer ou Sqlite (outro valor cai no SQLite sem aviso)" ;;
esac
if tem ConnectionStrings__DevKitPage || [ -n "${CONEXAO[DevKitPage]:-}" ]; then ok "ConnectionStrings__DevKitPage"
else falta "ConnectionStrings__DevKitPage" "nem a app setting nem a connection string DevKitPage do App Service: a API abriria a base padrão em wwwroot"; fi

# O CORS: sem a origem do site, o navegador recusa todas as chamadas da web.
if ! tem Cors__Origens__0; then falta Cors__Origens__0 "a URL do Static Web App: sem ela o site não chama a API"
elif [[ "$(valor Cors__Origens__0)" == */ ]]; then falta Cors__Origens__0 "a origem é sem barra no fim (com ela o CORS não casa)"
else ok Cors__Origens__0; fi

# O key ring do DataProtection (KeyRing.Validar): em Production, persistido e compartilhado.
if ! tem ProtecaoDeDados__Blob && ! tem ProtecaoDeDados__Pasta; then
  falta "ProtecaoDeDados__Blob | ProtecaoDeDados__Pasta" "sem um dos dois a API não sobe em Production ('ProtecaoDeDados não configurada')"
elif tem ProtecaoDeDados__Blob && ! do_cofre ProtecaoDeDados__Blob && ! https ProtecaoDeDados__Blob; then
  falta ProtecaoDeDados__Blob "não é um endereço https de blob: a API não sobe"
else ok "ProtecaoDeDados (Blob ou Pasta)"; fi
if tem ProtecaoDeDados__ChaveDoCofre && ! do_cofre ProtecaoDeDados__ChaveDoCofre && ! https ProtecaoDeDados__ChaveDoCofre; then
  falta ProtecaoDeDados__ChaveDoCofre "não é o endereço https de uma chave do Key Vault: a API não sobe"
fi

# A fila do controle remoto (FilaDeComandos.Validar): só a do Azure em Production.
case "$(minusculo "$(valor Remoto__Fila)")" in
  ""|azure) ok "Remoto__Fila (azure)" ;;
  simulado) falta Remoto__Fila "a fila simulada não sobe em Production (apague a setting ou use azure)" ;;
  *) falta Remoto__Fila "é azure ou simulado: outro valor derruba a API" ;;
esac

# O worker da provisão (ComposicaoDoWorker.Validar): o WebJob vai no mesmo pacote e herda estas settings.
case "$(minusculo "$(valor Provisao__Provedor)")" in
  ""|azure)
    ok "Provisao__Provedor (azure)"
    for nome in Provisao__AssinaturaId Provisao__Grupo; do
      if tem "$nome"; then ok "$nome"; else falta "$nome" "o WebJob da provisão não sobe sem ele (infra-controle-remoto.sh o grava)"; fi
    done
    tem Provisao__IdentidadeClientId || aviso Provisao__IdentidadeClientId "vazio: o worker usa a identidade do SISTEMA, a mesma da API"
    ;;
  simulado) falta Provisao__Provedor "o provedor simulado não sobe em Production" ;;
  *) falta Provisao__Provedor "é azure ou simulado: outro valor derruba o WebJob" ;;
esac

# O client credentials da máquina (OpcoesDoClienteDaMaquina.Validar): opcional, mas fora da faixa a API não sobe.
if tem ClienteDaMaquina__Habilitado; then
  case "$(minusculo "$(valor ClienteDaMaquina__Habilitado)")" in
    true|false) ok ClienteDaMaquina__Habilitado ;;
    *) do_cofre ClienteDaMaquina__Habilitado && ok ClienteDaMaquina__Habilitado \
         || falta ClienteDaMaquina__Habilitado "é true ou false: outro valor derruba a API na subida" ;;
  esac
fi
faixa ClienteDaMaquina__ValidadeDoTokenMinutos 1 60
faixa ClienteDaMaquina__ValidadeMaximaDaAssercaoSegundos 30 600
faixa ClienteDaMaquina__ToleranciaDoRelogioSegundos 0 300

# O que só degrada um recurso: avisa, sem segurar o deploy.
case "$(valor ASPNETCORE_ENVIRONMENT)" in
  ""|Production) ;;
  *) aviso ASPNETCORE_ENVIRONMENT "não é Production: as recusas acima (fila simulada, key ring) deixam de valer na API" ;;
esac
tem Licencas__ChavePrivada || aviso Licencas__ChavePrivada "sem ela o /health fica Degraded (check licencas) e a renovação da licença responde 503"
for nome in Push__ChavePublica Push__ChavePrivada Push__Assunto; do
  tem "$nome" || aviso "$nome" "sem as três Push__*, o board não oferece o aviso (GET /api/remoto/push com disponivel: false)"
done
tem REPO_KEY || aviso REPO_KEY "sem ele o download do dev.kit responde 503"

if [ "$FALTAS" -gt 0 ]; then
  echo "Resultado: $FALTAS pendência(s) de configuração e $AVISOS aviso(s) — o deploy NÃO segue. Grave as settings no App Service e rode de novo."
  exit 1
fi
echo "Resultado: configuração conferida ($AVISOS aviso(s)) — o deploy segue."
