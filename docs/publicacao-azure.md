# Publicação no Azure

O caminho previsto — nada aqui exige mudança de código, só configuração.

## API

**Opção A — Azure Container Apps** (recomendada): a imagem do [`api/Dockerfile`](../api/Dockerfile)
(`docker build -t devkitpage-api api/`), publicada num Azure Container Registry. Porta 8080, probe
de saúde em `/health` (confere o banco).

**Opção B — App Service (Linux, .NET 10)**: `dotnet publish api/src/DevKitPage.Api -c Release` e
deploy do pacote; health check do App Service em `/health`.

No App Service, além das variáveis da tabela abaixo:

- **Identidade**: *Identity → System assigned → On*, para resolver as referências ao Key Vault (a
  sintaxe `@Microsoft.KeyVault(...)` funciona direto nos Application Settings).
- **Configuration → General settings**: *Always On* ligado (sem ele o app dorme e o expurgo diário
  não roda — exige plano Basic ou acima) e *HTTPS Only*.
- **CORS do portal vazio**: quem responde o CORS é a API (`Cors__Origens__0`). O CORS da plataforma
  passa por cima do da aplicação.
- **Health check**: *Monitoring → Health check* → caminho `/health`.
- **SQLite no App Service**: o caminho padrão (`dados/devkitpage.db`) cai em `wwwroot`, que é
  apagado a cada deploy e fica **somente leitura** no deploy por pacote (a API não sobe). Use
  `ConnectionStrings__DevKitPage=Data Source=/home/dados/devkitpage.db` (`/home` é persistente;
  no Windows, `D:\home\dados\devkitpage.db`), com uma instância só. Para uso real, Azure SQL.
- **Deploy manual**:

  ```bash
  dotnet publish api/src/DevKitPage.Api -c Release -o publicar
  # compacte o CONTEÚDO de publicar/ em publicar.zip (a dll na raiz do zip)
  az webapp deploy -g <grupo> -n <app> --src-path publicar.zip --type zip
  ```

  Ou *Deployment Center → GitHub*, que gera o workflow de build e deploy (aponte o projeto
  `api/src/DevKitPage.Api`).
- **Log da primeira subida** (a senha do admin, se o `Seed__AdminPassword` não foi configurado):
  *Monitoring → Log stream*, com *App Service logs → Application logging* ligado.

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

A API não lê o Key Vault sozinha (não há provider do cofre no código): quem resolve a referência é a
plataforma, e a sintaxe muda conforme a opção:

- **App Service**: a referência `@Microsoft.KeyVault(SecretUri=...)` direto no valor do Application Setting.
- **Container Apps**: essa sintaxe **não** funciona (a variável chegaria com o texto literal e a API
  recusaria subir). Crie um *segredo* do Container App apontando para o cofre e use `secretref:` na
  variável:

  ```bash
  az containerapp secret set -n <api> -g <grupo> \
    --secrets jwt-segredo=keyvaultref:https://<cofre>.vault.azure.net/secrets/jwt-segredo,identityref:system
  az containerapp update -n <api> -g <grupo> --set-env-vars Jwt__Segredo=secretref:jwt-segredo
  ```

  O mesmo para `codigo-registro` e `admin-password`.

O `Jwt__Segredo` precisa de **32 caracteres ou mais** (ex.: `openssl rand -base64 48`). Sem o
`Seed__AdminPassword`, a senha inicial do admin é gerada e escrita **uma vez** no log da primeira
subida (*Log stream* / *Console logs*). O `Cors__Origens__0` é a origem exata da web, sem barra no fim.

### Banco

- **Azure SQL**: `Banco__Provider=SqlServer`. Na primeira subida o esquema nasce do modelo e o admin
  é semeado. Antes de evoluir o esquema em produção, gere as migrations do SQL Server num projeto de
  migrations próprio (ver [arquitetura](arquitetura.md)).
  Com identidade gerenciada, crie o usuário dela na base (o servidor precisa de um admin do Entra ID)
  — o `db_ddladmin` é porque a primeira subida cria o esquema:

  ```sql
  CREATE USER [<nome-da-api>] FROM EXTERNAL PROVIDER;
  ALTER ROLE db_datareader ADD MEMBER [<nome-da-api>];
  ALTER ROLE db_datawriter ADD MEMBER [<nome-da-api>];
  ALTER ROLE db_ddladmin ADD MEMBER [<nome-da-api>];
  ```

  E libere o acesso da API no firewall do servidor (*Allow Azure services* ou a rede do ambiente).
- **SQLite em volume**: válido para uma instância só (Container Apps com réplica única e um volume
  Azure Files em `/app/dados`). As migrations (inclusive a dos pedidos de demonstração) são
  aplicadas sozinhas na subida. Atenção: o SQLite sobre o SMB do Azure Files sofre com travas de
  arquivo (`database is locked`) — serve para demonstração; para uso real, Azure SQL.

### Atualizar uma base Azure SQL que já existe (US #283)

O `EnsureCreated` não cria tabela nova numa base que já existe. **Antes** de publicar a versão da
API com o formulário de demonstração, rode o script idempotente (pode rodar mais de uma vez):

```bash
sqlcmd -S <servidor>.database.windows.net -d <base> --authentication-method ActiveDirectoryDefault \
  -i api/scripts/sqlserver/PedidosDeDemonstracao.sql
```

(ou cole o conteúdo do script no *Query editor* da base no portal). Sem a tabela, o `POST
/api/demonstracoes` responde 500 e o dashboard não lista os pedidos.

### Exceções não classificadas (US #381)

Antes de publicar a versão da API com o painel de exceções, rode também
`api/scripts/sqlserver/GruposDeErro.sql` (idempotente, como o anterior). Sem as tabelas, o lote que
traz um `ExcecaoNaoClassificada` falha com 500 — e o dev.kit o reenvia até elas existirem, sem perder
nada da fila local.

## Web

**Azure Static Web Apps**: `npm ci && npm run build` em `web/`, publicando `web/dist`, com as
variáveis de build `VITE_API_URL=https://<api>` e `VITE_SITE_URL=https://<site>` (a URL absoluta do
canonical e da imagem do Open Graph). O fallback de rotas para `index.html` (rotas do React Router:
`/login`, `/dashboard`) já vai no build, pelo [`web/public/staticwebapp.config.json`](../web/public/staticwebapp.config.json)
— sem ele, recarregar o `/dashboard` dá 404. As mídias da landing (`web/public/midia`) vão no próprio
build — estão no repositório, sem LFS.

O Node do build sai do `engines` do `web/package.json` (`>=22`, como o README e o CI): o Vite 7 não compila
em Node anterior ao 20.19.

As `VITE_*` são lidas no **build**, não em execução: as *Environment variables* do portal do Static
Web App não chegam a elas. Ponha-as no `env:` do passo de build do workflow que o Static Web Apps
gera no GitHub (`app_location: web`, `output_location: dist`):

```yaml
      - uses: Azure/static-web-apps-deploy@v1
        env:
          VITE_API_URL: https://<api>
          VITE_SITE_URL: https://<site>
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN }}
          action: upload
          app_location: web
          output_location: dist
```

## Depois de publicar

1. Abra a web → **Entrar** → `admin` e a senha do Key Vault → defina a nova senha.
2. O dev.kit vem com a URL da API e o código de registro preenchidos no build, e com o envio ligado
   (o usuário desliga em *Configurações → Telemetria de uso*). O `Telemetria__CodigoDeRegistro` da
   API tem de ser o mesmo do build do dev.kit.
3. `devcli telemetria --enviar` numa máquina e confira o dashboard.
4. Envie um pedido pelo formulário *Quero uma demonstração* da landing e confira-o em *Pedidos de
   demonstração* no dashboard (exclua-o depois).
