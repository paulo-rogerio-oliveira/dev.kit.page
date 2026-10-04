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
| `Demonstracoes__RetencaoDias` | 365 — por quanto tempo o pedido de demonstração fica guardado (LGPD) |
| `Demonstracoes__LimitePorMinuto` | 5 — pedidos por minuto por IP de cliente no formulário (429 acima) |
| `Proxy__RedesConfiaveis__0` | A rede (CIDR) do proxy na frente da API — no Container Apps, a sub-rede do ambiente (ex.: `100.100.0.0/16`, confira em *Networking* do ambiente). Sem ela o `X-Forwarded-For` é ignorado e todos os visitantes dividem a cota do IP do proxy |

Dê à identidade gerenciada da API o papel **Key Vault Secrets User** no cofre.

### Banco

- **Azure SQL**: `Banco__Provider=SqlServer`. Na primeira subida o esquema nasce do modelo e o admin
  é semeado. Antes de evoluir o esquema em produção, gere as migrations do SQL Server num projeto de
  migrations próprio (ver [arquitetura](arquitetura.md)).
- **SQLite em volume**: válido para uma instância só (Container Apps com réplica única e um volume
  Azure Files em `/app/dados`). As migrations (inclusive a dos pedidos de demonstração) são
  aplicadas sozinhas na subida.

### Atualizar uma base Azure SQL que já existe (US #283)

O `EnsureCreated` não cria tabela nova numa base que já existe. **Antes** de publicar a versão da
API com o formulário de demonstração, rode o script idempotente (pode rodar mais de uma vez):

```bash
sqlcmd -S <servidor>.database.windows.net -d <base> --authentication-method ActiveDirectoryDefault \
  -i api/scripts/sqlserver/PedidosDeDemonstracao.sql
```

(ou cole o conteúdo do script no *Query editor* da base no portal). Sem a tabela, o `POST
/api/demonstracoes` responde 500 e o dashboard não lista os pedidos.

## Web

**Azure Static Web Apps**: `npm ci && npm run build` em `web/`, publicando `web/dist`, com as
variáveis de build `VITE_API_URL=https://<api>` e `VITE_SITE_URL=https://<site>` (a URL absoluta do
canonical e da imagem do Open Graph). Configure o fallback de rotas para `index.html` (rotas do
React Router: `/login`, `/dashboard`). As mídias da landing (`web/public/midia`) vão no próprio build
— estão no repositório, sem LFS.

## Depois de publicar

1. Abra a web → **Entrar** → `admin` e a senha do Key Vault → defina a nova senha.
2. No dev.kit de cada máquina: *Configurações → Telemetria de uso* → URL da API e código de registro.
3. `devcli telemetria --enviar` numa máquina e confira o dashboard.
4. Envie um pedido pelo formulário *Quero uma demonstração* da landing e confira-o em *Pedidos de
   demonstração* no dashboard (exclua-o depois).
