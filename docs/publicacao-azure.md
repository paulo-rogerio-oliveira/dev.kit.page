# Publicação no Azure

O caminho previsto — nada aqui exige mudança de código, só configuração.

## API

**Opção A — Azure Container Apps** (recomendada): a imagem do [`api/Dockerfile`](../api/Dockerfile)
(`docker build -t devkitpage-api api/`), publicada num Azure Container Registry. Porta 8080, probe
de saúde em `/health` (confere o banco).

**Opção B — App Service (Linux, .NET 10)**: `dotnet publish api/src/DevKitPage.Api -c Release` e
deploy do pacote; health check do App Service em `/health`.

### Configuração (Application Settings / variáveis do Container App)

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Jwt__Segredo` | Referência ao Key Vault: `@Microsoft.KeyVault(SecretUri=https://<cofre>.vault.azure.net/secrets/jwt-segredo/)` — **sem ele a API não sobe** |
| `Telemetria__CodigoDeRegistro` | Referência ao Key Vault (`codigo-registro`) |
| `Seed__AdminPassword` | Referência ao Key Vault — só para a primeira subida; depois pode sair |
| `Banco__Provider` | `SqlServer` (Azure SQL) — ou `Sqlite` com volume persistente montado em `/app/dados` |
| `ConnectionStrings__DevKitPage` | A connection string do Azure SQL (de preferência com identidade gerenciada: `Authentication=Active Directory Managed Identity`) |
| `Cors__Origens__0` | A URL da web (Static Web App) |
| `Telemetria__RetencaoDias` | 180 (ou o que a política pedir) |

Dê à identidade gerenciada da API o papel **Key Vault Secrets User** no cofre.

### Banco

- **Azure SQL**: `Banco__Provider=SqlServer`. Na primeira subida o esquema nasce do modelo e o admin
  é semeado. Antes de evoluir o esquema em produção, gere as migrations do SQL Server num projeto de
  migrations próprio (ver [arquitetura](arquitetura.md)).
- **SQLite em volume**: válido para uma instância só (Container Apps com réplica única e um volume
  Azure Files em `/app/dados`).

## Web

**Azure Static Web Apps**: `npm ci && npm run build` em `web/`, publicando `web/dist`, com a variável
de build `VITE_API_URL=https://<api>`. Configure o fallback de rotas para `index.html` (rotas do
React Router: `/login`, `/dashboard`).

## Depois de publicar

1. Abra a web → **Entrar** → `admin` e a senha do Key Vault → defina a nova senha.
2. No dev.kit de cada máquina: *Configurações → Telemetria de uso* → URL da API e código de registro.
3. `devcli telemetria --enviar` numa máquina e confira o dashboard.
