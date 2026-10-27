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
| `REPO_KEY` | **Segredo** (US #405): o PAT do GitHub com leitura do repositório do dev.kit (fine-grained, *Contents: Read-only* no `git.kit`), para a API ler as Releases e fazer o proxy do zip. Referência ao Key Vault (`repo-key`). É lido SÓ desta variável — um `Atualizacao:Token` no appsettings é ignorado. Sem ele, `GET /api/versoes/ultima` e o download respondem **503** (e a landing mostra "Download indisponível") |
| `Atualizacao__Repositorio` | `owner/nome` das releases — padrão `paulo-rogerio-oliveira/git.kit` |
| `Atualizacao__CacheMinutos` | 5 — por quanto tempo a lista de releases fica em memória (o GitHub limita as chamadas por token) |
| `DEVKIT_LOGIN_OBRIGATORIO` | `true` (ou `1`) liga a política de login do app (US #405), devolvida em `GET /api/auth/politica` e no `loginObrigatorio` da última versão; ausente, desligada |
| `Licencas__ChavePrivada` | **Segredo** (US #425): a semente Ed25519 (32 bytes em base64) que assina as licenças, gravada DIRETO na app setting do App Service `devkit` (o dev.kit.page não usa Key Vault; uma referência `@Microsoft.KeyVault(...)` também funciona, se um dia houver cofre). Confira o valor antes de gravar: a pública derivada dele tem de ser a do git.kit (comando abaixo). A PÚBLICA correspondente está embutida no git.kit (`LicencaAssinada.ChavePublicaDeProducao`): trocar a chave exige um dev.kit novo. Sem ela, a renovação responde 503 (dizendo que falta a chave), o `/health` fica `Degraded` (check `licencas`) e o registro da máquina sai sem o trial assinado (o app conta o trial local) |
| `Conta__UrlDoSite` | A URL da web, para o link de definir a senha (US #425) |
| `Email__Host`, `Email__Porta`, `Email__Usuario`, `Email__Remetente` | O SMTP dos e-mails da conta (US #425). Sem `Email__Host`, o e-mail vai só para o log — serve para teste, não para produção |
| `Email__Senha` | **Segredo**: a senha do SMTP. Referência ao Key Vault (`Email--Senha`) |
| `Pagamentos__SegredoDoWebhook` | **Segredo** (US #425): o segredo HMAC do webhook do gateway. Referência ao Key Vault (`Pagamentos--SegredoDoWebhook`) |
| `Pagamentos__UrlDoCheckout`, `Pagamentos__UrlDoPortal` | O checkout e o portal do gateway. Sem o checkout, a compra é SIMULADA (o fluxo conclui na própria página) |
| `Atualizacao__DownloadsPorHora` | 20 — downloads anônimos do dev.kit por hora por IP (US #425; 429 acima) |
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

### Plano empresarial (US #381)

Rode também `api/scripts/sqlserver/Empresas.sql` (idempotente): cria `Empresas` e `AcessosAosDados` e
acrescenta as colunas de papel/empresa em `Usuarios` e de adesão em `Maquinas`. O limite das
exportações por usuário é `Painel__ExportacoesPorMinuto` (padrão 10).

### ROI por work item (US #387)

Rode também `api/scripts/sqlserver/RoiDeWorkItem.sql` (idempotente): cria `RoisDeWorkItem`, a foto do
ROI por (máquina, work item). Sem a tabela, o lote que traz um `RoiCalculado` com o campo `roi` falha
com 500 — e o dev.kit o reenvia até ela existir, sem perder nada da fila local.

### Feedback das entregas (US #417)

Rode também `api/scripts/sqlserver/AvaliacoesDeEntrega.sql` (idempotente): cria `AvaliacoesDeEntrega`, o
joinha mais recente por (máquina, sessão, turno). Sem a tabela, o lote que traz um `EntregaAvaliada` com o
campo `avaliacao` falha com 500 — e o dev.kit o reenvia até ela existir, sem perder nada da fila local.

### Gestão de usuários e atualização do dev.kit (US #405)

Rode também `api/scripts/sqlserver/Usuarios.sql` (idempotente): acrescenta a `Usuarios` o nome de exibição
(`Nome`) e o bloqueio pelo admin (`BloqueadoPeloAdmin`). Sem as colunas, o login falha com 500. O papel
`dev` não precisa de nada no banco (a coluna `Papel` já é texto).

Para o "Baixar o dev.kit" e a atualização do app, configure o `REPO_KEY` (tabela acima). O zip passa pela
API em streaming (sem bufferizar a memória), então o tempo do download conta no *timeout* de requisição do
proxy da plataforma — confira o da sua opção (App Service ou Container Apps) para o tamanho do pacote.
O workflow `release.yml` do git.kit publica o zip e o `<zip>.sha256` em cada release; prerelease e
rascunho não são oferecidos.

### Catálogo, conta do dev e licença assinada (US #425)

Rode também `api/scripts/sqlserver/Us425.sql` (idempotente): cria as tabelas do catálogo da empresa, dos
links de senha, dos trials por impressão de máquina, das licenças emitidas e dos eventos de pagamento, e
acrescenta as colunas novas a `Usuarios` (`Email`), `Empresas` (política de login, individual, assinatura
externa e cancelamento) e `Maquinas` (`Impressao`). Sem elas, o login e o registro de máquina falham com 500.

Antes de publicar, configure os segredos `Email--Senha` e `Pagamentos--SegredoDoWebhook` e as variáveis da tabela
acima. O download do dev.kit passa a ser anônimo: confira o `Atualizacao__DownloadsPorHora`.

**A chave das licenças (`Licencas__ChavePrivada`) vai DIRETO na app setting, sem Key Vault** (o dev.kit.page não usa
cofre; bug de 27/10). É a semente Ed25519 com que o desenvolvedor assina as licenças — gerada uma vez, guardada fora do
repositório e NUNCA versionada. Antes de gravar, confira no Git Bash que ela é o par da pública embutida no git.kit
(imprime só `confere` ou `não confere`, sem ecoar a semente):

```bash
read -s -p "Semente (base64): " CANDIDATA; echo
P=$( (printf '\x30\x2e\x02\x01\x00\x30\x05\x06\x03\x2b\x65\x70\x04\x22\x04\x20'; printf %s "$CANDIDATA" | base64 -d 2>/dev/null) \
  | openssl pkey -inform DER -pubout -outform DER 2>/dev/null | tail -c 32 | base64)
[ "$P" = "kf1FN8DD7HQC1V6JgctluuP5ZfDbgbIW4YPOokrpR/c=" ] && echo "confere" || echo "não confere"
```

Conferida, grave e reinicie (o valor entra pelo prompt acima, fora do histórico do shell):

```bash
az webapp config appsettings set -g <grupo> -n devkit --settings "Licencas__ChavePrivada=$CANDIDATA" --query "[?name=='Licencas__ChavePrivada'].name" -o tsv
unset CANDIDATA P
az webapp restart -g <grupo> -n devkit
```

(Pelo portal: App Service `devkit` → *Settings → Environment variables* → *+ Add* `Licencas__ChavePrivada` → *Apply* →
*Restart*.) Para conferir: com o login de um dev, `POST /api/licencas/renovar` sem a `X-Machine-Key` passa de **503**
(falta a chave) a **401** (a API pede a chave da máquina), e o `/health` deixa de ficar `Degraded`. Trocar a semente
exige um par novo, a pública nova no `LicencaAssinada.ChavePublicaDeProducao` do git.kit e um pacote novo do dev.kit.

### Catálogos nomeados (US #461)

Rode também `api/scripts/sqlserver/Us461.sql` (idempotente): cria `Catalogos` (com o registro do básico),
`VinculosDeCatalogo`, `ItensEditaveisDeCatalogo`, `VersoesDoCatalogo` e `PacotesDoCatalogo` (o zip de cada
release, em `varbinary(max)`). Sem elas, a vitrine, o `/api/catalogo/base` e a sincronia das máquinas
respondem 500.

Depois da publicação, o admin abre **Catálogo → Catálogos → Básico** e usa **Importar do GitHub** UMA vez: a
última release `catalogo-vN` (o mesmo zip, o mesmo manifesto) passa a ser servida pelo site, e a próxima
publicação é a `catalogo-v(N+1)`. Até lá, o básico continua saindo do GitHub (o `REPO_KEY`), como antes. A
partir daí o git.kit não publica mais o básico: o `catalogo.yml` dele só confere o manifesto, e o
`build/catalogo.ps1 -Baixar` traz o básico publicado no site para o pacote do instalador.

### Catálogo de repositórios e convenções da empresa (US #491)

Rode também `api/scripts/sqlserver/Us491.sql` (idempotente): cria `RepositoriosDaEmpresa` e `ConvencoesDaEmpresa` —
e, numa base que já rodou a primeira versão dele, acrescenta a coluna `UsuarioId` e troca o índice único.
Sem elas, `/api/empresa/repositorios` e `/api/empresa/convencoes` respondem 500 — e o dev.kit, que trata a falha
como "a empresa ainda não compartilha isto", segue com o que a máquina já sabe. Nenhuma configuração nova; a página
`/empresa/conhecimento` do site usa as mesmas tabelas.

### Contexto por nível (US #550)

Rode também `api/scripts/sqlserver/Us550.sql` (idempotente, depois do `Us491.sql`): a coluna `Valor` de
`ConvencoesDaEmpresa` passa a `nvarchar(4000)`, o teto do texto do contexto (`contexto.<tema>`). Sem ele, gravar um
contexto com mais de 200 caracteres responde 500. Nenhuma tabela nem configuração nova.

### Regra do usuário × Sugestão do agente (US #663)

Rode também `api/scripts/sqlserver/Us663.sql` (idempotente, depois do `Us550.sql`): `ConvencoesDaEmpresa` ganha
`Origem` (`usuario`, a regra; `agente`, a sugestão) e `Versao`, e o índice único passa a incluir a origem. Só
acrescenta: o que já existe vira regra na versão 1. Sem ele, `/api/empresa/contexto/*` e a leitura com
`?origem=todas` respondem 500. A rota antiga (`GET /api/empresa/convencoes` sem `origem`) continua devolvendo só as
regras — o dev.kit instalado não vê as sugestões como regra. Nenhuma configuração nova.

### Design compartilhado (US #669)

Rode também `api/scripts/sqlserver/Us669.sql` (idempotente): cria `ProjetosDeDesign`, `TelasDeDesign` e
`VersoesDeDesign`. As IMAGENS não vão para o banco: crie uma Storage Account (ou use a existente) e configure na API
`Design__Armazem=blob` e o segredo `Design__ConexaoDoBlob` (a connection string, do Key Vault); o contêiner
`Design__Conteiner` (padrão `design`) é criado privado na primeira gravação. O teto por plano é ajustável sem código
(`Design__Tetos__Empresarial__Versoes`, `Design__Tetos__Empresarial__MegaBytes`, `Design__TetoPadrao__*`). Sem o
`Design__Armazem=blob`, as imagens vão para a pasta `Design__Pasta` do contêiner da API — e se perdem a cada nova revisão.

### Painel de utilidade, licença por usuário, empresa e editor de artefato (US #687)

Rode também `api/scripts/sqlserver/Us687.sql` (idempotente) **antes de publicar a API** desta versão — e a API antes de
distribuir o pacote do dev.kit que manda os eventos novos. Ele acrescenta a `Usuarios` as colunas da licença definida
pelo admin (`LicencaValidaAteUtc`, `LicencaSemPrazo`, `LicencaTipo`, `LicencaRevogadaEmUtc`, `LicencaAlteradaEmUtc`), a
`Empresas` os dados da empresa (`Cnpj`, `Segmento`, `Cidade`, `Uf`, `Contato`, `Telefone`, `Site`, `ContratoAteUtc`,
`AlteradaEmUtc`, `AlteradaPor`) e cria `HistoricosDeLicenca`, `FotosDeEstado`, `HistoricosDeItemDoCatalogo` e
`AnexosDeItemDoCatalogo`. Só acrescenta: usuário sem licença definida segue a renovação de antes (30 dias corridos). Sem
ele, `/api/usuarios`, `/api/empresas`, a ingestão da telemetria e `/api/catalogos/itens` respondem 500. Nenhuma
configuração nova.

### Projetos compartilhados (US #693)

Rode também `api/scripts/sqlserver/Us693.sql` (idempotente): cria `ProjetosCompartilhados` e
`ItensDeProjetoCompartilhado`. Publique a API com as rotas `/api/projetos` ANTES do pacote do dev.kit que as usa (a
Rule de impacto 5): o dev.kit trata o 404 da API antiga como "o projeto fica só nesta máquina", mas o compartilhamento
só existe com a rota publicada. Nenhuma configuração nova.

### Como saber que falta um script (bug do painel Uso, 26/10)

Sem o `Us687.sql`, o painel Uso do admin mostrava só "An error occurred while processing your request" (o
`/api/dashboard/utilidade` lê `FotosDeEstado`). Desde esta versão a API confere o esquema na subida e diz o que falta:

- **`GET /health` responde `Degraded`** (status 200) quando o banco não tem uma tabela ou coluna do modelo — no caso da
  US #687, é o `Us687.sql` não aplicado. `Healthy` é o esquema completo.
- O **log da subida** traz `Esquema do banco incompleto: O banco não tem a tabela FotosDeEstado (Us687.sql); …`, com o
  script de cada falta.
- O **500** de qualquer rota diz, ao admin, a rota, o que falta e o script, com o código que acha a exceção no log; aos
  outros usuários, a rota e o código.

A API não roda os scripts sozinha: aplique o indicado (são idempotentes) e reinicie a API — o `/health` volta a
`Healthy`.

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
