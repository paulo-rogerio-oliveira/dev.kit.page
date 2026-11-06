#!/usr/bin/env bash
# A infraestrutura do controle remoto no Azure (docs/publicacao-azure.md, "Ativação do controle remoto"): a identidade do worker
# da provisão e os papéis mínimos dele, o papel do listKeys da identidade da API e, opcionalmente, o key ring do DataProtection
# (o contêiner do blob e a chave do Key Vault). IDEMPOTENTE: confere antes de criar, e rodar de novo não duplica nada.
#
# --dry-run (ou --simular) NÃO chama o az: mostra, na ordem, as consultas (?) e as gravações (+) que faria, supondo que nada
# existe ainda. É o que se confere antes de rodar de verdade, com o az logado numa conta que pode atribuir papéis (Owner ou
# User Access Administrator na assinatura).
set -euo pipefail
# O Git Bash converteria /subscriptions/... num caminho do Windows.
export MSYS_NO_PATHCONV=1

uso() {
  cat <<'EOF'
Uso: infra-controle-remoto.sh --assinatura <id> --grupo <grupo do App Service> [opções] [--dry-run]

  --assinatura <id>          A assinatura do dev.kit (a dos grupos rg-devkit-u{id} dos usuários).       (obrigatória)
  --grupo <nome>             O grupo de recursos do App Service da API.                                  (obrigatória)
  --app <nome>               O App Service da API (que também hospeda o WebJob do worker).               (padrão: devkit)
  --regiao <região>          A região da identidade do worker.                                           (padrão: brazilsouth)
  --identidade <nome>        A identidade atribuída ao usuário do worker.                                (padrão: id-devkit-provisao)
  --conta-keyring <conta>    A conta de Storage do key ring do DataProtection (opcional).
  --grupo-keyring <nome>     O grupo da conta do key ring.                                               (padrão: o --grupo)
  --conteiner-keyring <nome> O contêiner do key ring.                                                    (padrão: protecao-de-dados)
  --cofre <nome>             O Key Vault (com RBAC) cuja chave protege o key ring (opcional; exige --conta-keyring).
  --chave-keyring <nome>     A chave RSA do key ring no cofre.                                           (padrão: devkit-keyring)
  --dry-run | --simular      Só mostra o que faria, sem chamar o az.
EOF
}

ASSINATURA="" GRUPO="" APP="devkit" REGIAO="brazilsouth" IDENTIDADE="id-devkit-provisao"
CONTA_KEYRING="" GRUPO_KEYRING="" CONTEINER_KEYRING="protecao-de-dados" COFRE="" CHAVE_KEYRING="devkit-keyring" SIMULAR=0
PAPEL_WORKER="devkit-provisao-worker" PAPEL_API="devkit-api-chave-da-fila"

while [ $# -gt 0 ]; do
  case "$1" in
    --assinatura) ASSINATURA="$2"; shift 2 ;;
    --grupo) GRUPO="$2"; shift 2 ;;
    --app) APP="$2"; shift 2 ;;
    --regiao) REGIAO="$2"; shift 2 ;;
    --identidade) IDENTIDADE="$2"; shift 2 ;;
    --conta-keyring) CONTA_KEYRING="$2"; shift 2 ;;
    --grupo-keyring) GRUPO_KEYRING="$2"; shift 2 ;;
    --conteiner-keyring) CONTEINER_KEYRING="$2"; shift 2 ;;
    --cofre) COFRE="$2"; shift 2 ;;
    --chave-keyring) CHAVE_KEYRING="$2"; shift 2 ;;
    --dry-run|--simular) SIMULAR=1; shift ;;
    -h|--help) uso; exit 0 ;;
    *) echo "Opção desconhecida: $1" >&2; uso >&2; exit 2 ;;
  esac
done

if [ -z "$ASSINATURA" ] || [ -z "$GRUPO" ]; then uso >&2; exit 2; fi
if [ -n "$COFRE" ] && [ -z "$CONTA_KEYRING" ]; then
  echo "O --cofre protege o key ring do blob: informe também a --conta-keyring." >&2; exit 2
fi
GRUPO_KEYRING="${GRUPO_KEYRING:-$GRUPO}"
ESCOPO="/subscriptions/$ASSINATURA"

if [ "$SIMULAR" = 1 ]; then
  echo "# SIMULAÇÃO (--dry-run): nada é enviado ao Azure. '?' é consulta, '+' é gravação; supõe-se que nada existe ainda."
else
  command -v az >/dev/null || { echo "O az não está instalado." >&2; exit 1; }
fi

# O relatório vai pelo descritor 3 (a saída padrão): a consulta roda dentro de $(...), e o que ela mostra não pode
# misturar-se ao valor devolvido nem sair fora de ordem.
exec 3>&1

# Uma gravação: roda (ou, na simulação, só mostra), com os argumentos citados como o shell os recebe.
grava() {
  echo "+ az $(printf '%q ' "$@")" >&3
  if [ "$SIMULAR" = 0 ]; then az "$@" --only-show-errors --output none; fi
}

# Uma consulta: devolve a saída (vazia se não existe); na simulação mostra e devolve o valor suposto ($1).
consulta() {
  local suposto="$1"; shift
  if [ "$SIMULAR" = 1 ]; then
    echo "? az $(printf '%q ' "$@")" >&3
    printf '%s' "$suposto"
  else
    az "$@" --only-show-errors 2>/dev/null || true
  fi
}

# O papel recém-criado demora a propagar: a atribuição tenta de novo antes de desistir.
grava_com_espera() {
  local tentativa
  for tentativa in 1 2 3 4 5; do
    if grava "$@"; then return 0; fi
    echo "  (o Azure ainda não propagou; nova tentativa em 20 s)" >&2
    sleep 20
  done
  return 1
}

# Atribui o papel ao principal no escopo, se ainda não tiver.
atribui() {
  local principal="$1" papel="$2" escopo="$3"
  local existente
  existente=$(consulta "" role assignment list --assignee "$principal" --role "$papel" --scope "$escopo" --query "[0].id" -o tsv)
  if [ -n "$existente" ]; then
    echo "= o papel '$papel' já está atribuído em $escopo"
  else
    grava_com_espera role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal --role "$papel" --scope "$escopo"
  fi
}

# Cria ou atualiza a função personalizada (o JSON vai num arquivo temporário; na simulação, é mostrado).
funcao() {
  local nome="$1" descricao="$2" acoes="$3"
  local arquivo
  arquivo=$(mktemp)
  cat >"$arquivo" <<EOF
{
  "Name": "$nome",
  "IsCustom": true,
  "Description": "$descricao",
  "Actions": [$acoes],
  "NotActions": [],
  "AssignableScopes": ["$ESCOPO"]
}
EOF
  local existente
  existente=$(consulta "" role definition list --custom-role-only true --name "$nome" --scope "$ESCOPO" --query "[0].id" -o tsv)
  if [ "$SIMULAR" = 1 ]; then sed 's/^/    /' "$arquivo"; fi
  if [ -n "$existente" ]; then
    grava role definition update --role-definition "@$arquivo"
  else
    grava role definition create --role-definition "@$arquivo"
  fi
  rm -f "$arquivo"
}

grava account set --subscription "$ASSINATURA"

echo "## 1. A identidade do worker ($IDENTIDADE), exclusiva dele"
ID_WORKER=$(consulta "" identity show -g "$GRUPO" -n "$IDENTIDADE" --query id -o tsv)
if [ -z "$ID_WORKER" ]; then
  grava identity create -g "$GRUPO" -n "$IDENTIDADE" -l "$REGIAO"
fi
ID_WORKER=$(consulta "$ESCOPO/resourcegroups/$GRUPO/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$IDENTIDADE" identity show -g "$GRUPO" -n "$IDENTIDADE" --query id -o tsv)
CLIENT_WORKER=$(consulta "<clientId de $IDENTIDADE>" identity show -g "$GRUPO" -n "$IDENTIDADE" --query clientId -o tsv)
PRINCIPAL_WORKER=$(consulta "<principalId de $IDENTIDADE>" identity show -g "$GRUPO" -n "$IDENTIDADE" --query principalId -o tsv)

echo "## 2. A função do worker: criar e remover a fila e os sites nos grupos dos usuários, e nada mais"
funcao "$PAPEL_WORKER" "Worker da provisão do dev.kit: grupos, contas e filas de Storage, planos, sites e Static Web Apps dos usuários." \
  '"Microsoft.Resources/subscriptions/resourceGroups/read", "Microsoft.Resources/subscriptions/resourceGroups/write",
   "Microsoft.Storage/storageAccounts/read", "Microsoft.Storage/storageAccounts/write", "Microsoft.Storage/storageAccounts/delete",
   "Microsoft.Storage/storageAccounts/queueServices/queues/read", "Microsoft.Storage/storageAccounts/queueServices/queues/write",
   "Microsoft.Web/serverfarms/read", "Microsoft.Web/serverfarms/write", "Microsoft.Web/serverfarms/delete",
   "Microsoft.Web/sites/read", "Microsoft.Web/sites/write", "Microsoft.Web/sites/delete",
   "Microsoft.Web/staticSites/read", "Microsoft.Web/staticSites/write", "Microsoft.Web/staticSites/delete"'
# O escopo é a assinatura porque o worker CRIA o grupo de cada usuário (rg-devkit-u{id}): não há grupo prévio onde atribuir.
atribui "$PRINCIPAL_WORKER" "$PAPEL_WORKER" "$ESCOPO"

echo "## 3. O App Service: a identidade do worker ao lado da do sistema (a da API), e as configurações do worker"
grava webapp identity assign -g "$GRUPO" -n "$APP" --identities "[system]" "$ID_WORKER"
grava webapp config appsettings set -g "$GRUPO" -n "$APP" --settings "Provisao__AssinaturaId=$ASSINATURA" "Provisao__IdentidadeClientId=$CLIENT_WORKER"
PRINCIPAL_API=$(consulta "<principalId da identidade do sistema de $APP>" webapp identity show -g "$GRUPO" -n "$APP" --query principalId -o tsv)
if [ -z "$PRINCIPAL_API" ]; then echo "O App Service $APP está sem a identidade do sistema." >&2; exit 1; fi

echo "## 4. A função da API: só o listKeys das contas das filas (assina a SAS curta do serviço da máquina)"
funcao "$PAPEL_API" "API do dev.kit.page: lê a chave das contas de Storage das filas dos usuários (listKeys), e nada mais." \
  '"Microsoft.Storage/storageAccounts/listkeys/action"'
atribui "$PRINCIPAL_API" "$PAPEL_API" "$ESCOPO"

if [ -n "$CONTA_KEYRING" ]; then
  echo "## 5. O key ring do DataProtection: o contêiner, o papel da API nele e a app setting"
  CONTA_ID=$(consulta "$ESCOPO/resourceGroups/$GRUPO_KEYRING/providers/Microsoft.Storage/storageAccounts/$CONTA_KEYRING" storage account show -g "$GRUPO_KEYRING" -n "$CONTA_KEYRING" --query id -o tsv)
  if [ -z "$CONTA_ID" ]; then echo "A conta $CONTA_KEYRING não existe no grupo $GRUPO_KEYRING." >&2; exit 1; fi
  EXISTE=$(consulta "false" storage container-rm exists -g "$GRUPO_KEYRING" --storage-account "$CONTA_KEYRING" -n "$CONTEINER_KEYRING" -o tsv)
  if [ "$EXISTE" != "true" ]; then
    grava storage container-rm create -g "$GRUPO_KEYRING" --storage-account "$CONTA_KEYRING" -n "$CONTEINER_KEYRING" --public-access off
  fi
  atribui "$PRINCIPAL_API" "Storage Blob Data Contributor" "$CONTA_ID/blobServices/default/containers/$CONTEINER_KEYRING"
  SETTINGS=("ProtecaoDeDados__Blob=https://$CONTA_KEYRING.blob.core.windows.net/$CONTEINER_KEYRING/keys.xml")

  if [ -n "$COFRE" ]; then
    echo "## 6. A chave do Key Vault que protege o key ring, e o papel da API nela"
    COFRE_ID=$(consulta "$ESCOPO/resourceGroups/<grupo do cofre>/providers/Microsoft.KeyVault/vaults/$COFRE" keyvault show -n "$COFRE" --query id -o tsv)
    if [ -z "$COFRE_ID" ]; then echo "O Key Vault $COFRE não existe." >&2; exit 1; fi
    CHAVE=$(consulta "" keyvault key show --vault-name "$COFRE" -n "$CHAVE_KEYRING" --query key.kid -o tsv)
    if [ -z "$CHAVE" ]; then
      grava keyvault key create --vault-name "$COFRE" -n "$CHAVE_KEYRING" --kty RSA --size 3072 --ops wrapKey unwrapKey
    fi
    atribui "$PRINCIPAL_API" "Key Vault Crypto Service Encryption User" "$COFRE_ID/keys/$CHAVE_KEYRING"
    SETTINGS+=("ProtecaoDeDados__ChaveDoCofre=https://$COFRE.vault.azure.net/keys/$CHAVE_KEYRING")
  fi
  grava webapp config appsettings set -g "$GRUPO" -n "$APP" --settings "${SETTINGS[@]}"
fi

echo "## Pronto. Reinicie o App Service para a API e o WebJob lerem as configurações:"
echo "   az webapp restart -g $GRUPO -n $APP"
