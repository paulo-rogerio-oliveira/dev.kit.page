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

### Relatos dos usuários (US #602)

Rode também `api/scripts/sqlserver/Us602.sql` (idempotente): cria `Relatos`, os defeitos, sugestões e dúvidas que os
usuários mandam pelo dev.kit, com o estado e a resposta de quem administra. Sem ela, `/api/relatos` e a tela `/relatos`
respondem 500. Nenhuma configuração nova.

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

### Artefatos do projeto (controle-v4, item 2)

Rode também `api/scripts/sqlserver/ArtefatosDosProjetos.sql` (idempotente, o passo 9 da lista do controle remoto). O
`POST /api/projetos/{id}/artefatos` recebe o zip do pacote .NET (multipart, campo `arquivo`) ou a URL do preview (JSON), e
o `GET /api/projetos/artefatos` dá os últimos de cada projeto ao card do Board. **Nenhuma configuração obrigatória nova**:
sem a seção `Artefatos`, o armazém e a conexão são os do design (`Design__Armazem=blob`, `Design__ConexaoDoBlob`), num
contêiner PRIVADO `artefatos` criado na primeira gravação. Opcionais: `Artefatos__Conteiner`, `Artefatos__ConexaoDoBlob`
(outra conta), `Artefatos__TamanhoMaximoMb` (teto do zip, padrão 100; acima, 413), `Artefatos__RetencaoDias` (padrão 7),
`Artefatos__LinkHoras` (o prazo da SAS, padrão 24, nunca além da retenção) e `Artefatos__MaximoPorProjeto` (padrão 10).
O link é uma SAS de serviço do blob, só leitura, só HTTPS e com prazo — leva a assinatura, nunca a chave da conta; por
isso a connection string precisa da chave (`AccountKey`). Nada é publicado em produção: o site guarda o zip e a URL do
ambiente de **preview** do Static Web App. A retenção apaga o zip vencido a cada novo registro do projeto; para o zip de um
projeto apagado, configure no Storage a política de ciclo de vida (*Lifecycle management*) do contêiner `artefatos`
apagando blobs com mais de 7 dias. **App Service no Windows:** o IIS da plataforma corta o corpo da requisição em ~28,6 MB
(`requestFiltering`, `maxAllowedContentLength` padrão de 30.000.000 bytes) antes de chegar à API, que só então aplica o
teto dela; ali, use `Artefatos__TamanhoMaximoMb=28` ou menos, ou publique um `web.config` com o limite maior. No Linux e
no Container Apps vale o teto da API.

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

### Importação de catálogos (US #738)

Para a sincronização completa de projetos (US #747), execute também `api/scripts/sqlserver/Us747.sql`,
depois de `Us693.sql`. A integração acrescenta `Documento` à tabela existente; os projetos e itens antigos
continuam legíveis. A versão mais recente preserva itens ausentes no reenvio e mantém a exclusão explícita
restrita ao autor ou gestor. SQLite aplica essa alteração automaticamente pela migration da US #747.

Antes de publicar a API em uma base Azure SQL existente, execute
`api/scripts/sqlserver/us738-origem-catalogo.sql` (idempotente, depois de `Us461.sql`). Ele acrescenta
`Catalogos.Origem` e `Catalogos.MinDevKitDaOrigem`, sem alterar os catálogos existentes. SQLite aplica a migration
automaticamente. A importação do GitHub usa somente repositórios públicos, sem `REPO_KEY` e sem configuração nova.

### Último login do usuário

Rode `api/scripts/sqlserver/UltimoLogin.sql` (idempotente) **antes de publicar a API** desta versão: ele acrescenta
`Usuarios.UltimoLoginEmUtc`, que o login grava. Sem a coluna, todo login falha. Os usuários que já existem aparecem como
"nunca" nas listas de `/usuarios` e `/empresa/usuarios` até entrarem de novo. SQLite aplica a migration automaticamente.

### Provisão no Azure e o worker (controle remoto v2, item 4)

A API pública só GRAVA o pedido; quem cria e remove no Azure é o **worker** `DevKitPage.Provisao`, outro processo, com a
identidade gerenciada dele. **Nada fica no escopo da assinatura**: a fila, os planos, os sites e os Static Web Apps de todos
os usuários vivem num grupo de recursos FIXO, já criado (`Provisao__Grupo`, ex.: `devkit-clients`), com nomes únicos por
usuário (`dkq{usuário}{sorteio}`, `app-u{usuário}-…`, `asp-u{usuário}-…`, `swa-u{usuário}-…`) e as tags de dono
`devkit=provisao`, `devkit-usuario` e `devkit-recurso`. O worker não cria nem apaga grupo; antes de REMOVER, ele lê o recurso
e só apaga se as tags disserem que é do usuário do pedido (o que já não existe é sucesso; o de outro dono é recusado).

1. **Banco.** Rode `api/scripts/sqlserver/Provisao.sql` (idempotente) antes de publicar a API desta versão: cria
   `PermissoesDeUsuario`, `RecursosProvisionados` e `EventosDaProvisao`. Sem ele, o `/health` fica `Degraded` e aponta o script.
2. **Grupo e identidade do worker.** Crie o grupo da provisão (ex.: `az group create -n devkit-clients -l brazilsouth`) e uma
   identidade gerenciada atribuída ao usuário (ex.: `id-devkit-provisao`), e dê a ela, **no grupo** (não na assinatura), só o
   que a provisão usa — a função personalizada `devkit-provisao-worker`, atribuível só no grupo, com
   `Microsoft.Storage/storageAccounts/read`, `/write` e `/delete`, `Microsoft.Storage/storageAccounts/queueServices/queues/read`
   e `/write`, `Microsoft.Web/serverfarms`, `Microsoft.Web/sites` e `Microsoft.Web/staticSites` (`/read`, `/write` e
   `/delete`) — sem `resourceGroups/write`. O script de infraestrutura (abaixo) faz tudo isso.
3. **Banco para o worker, sem senha.** No Azure SQL, crie o usuário da identidade (`CREATE USER [id-devkit-provisao] FROM
   EXTERNAL PROVIDER`) com leitura e escrita, e use na connection string do worker
   `Authentication=Active Directory Managed Identity;User Id=<client id da identidade>`.
4. **Publicar.** O caminho padrão é o **WebJob contínuo** no App Service da API (`devkit`): o workflow
   `.github/workflows/main_devkit.yml` publica o worker no job `worker` e o põe em `App_Data/jobs/continuous/provisao` no
   mesmo pacote da API, com o `run.cmd` (Windows), o `run.sh` (Linux) e o `settings.job` (`is_singleton`: uma instância só)
   de `api/src/DevKitPage.Provisao/webjob/`. O WebJob contínuo exige o *Always On* (já pedido acima). A alternativa é
   `dotnet publish api/src/DevKitPage.Provisao -c Release` num Container App próprio, **sem ingress** — o worker não recebe
   chamada nenhuma —, com a identidade do passo 2 e uma réplica (mais de uma também funciona: a reserva do pedido é
   concorrente-segura). Ver "O limite da identidade no WebJob", abaixo.

**O WebJob e a identidade.** O WebJob herda as app settings do site: a mesma `ConnectionStrings__DevKitPage` e o
`Banco__Provider` da API (e, com isso, o acesso ao banco da identidade do site — o passo 3 é só para o Container App). O
`Provisao__IdentidadeClientId` diz ao worker qual identidade usar no Azure Resource Manager (`ManagedIdentityCredential`
com o client id), e a API nunca pede essa identidade: ela usa a do sistema (ou a do `Remoto__IdentidadeClientId`). O
ambiente do worker é `Production` sem configuração nenhuma (o host genérico lê o `DOTNET_ENVIRONMENT`, e não o
`ASPNETCORE_ENVIRONMENT`), e o worker recusa subir nele sem o `Provisao__AssinaturaId` e sem o `Provisao__Grupo` (a mensagem diz
`Provisao:Grupo não configurado`). No Linux, confira em *WebJobs* que o
job aparece como *Running*: se o `run.sh` perder o bit de execução no pacote, o Kudu não o inicia.

**O limite da identidade no WebJob.** O WebJob roda no MESMO sandbox do site: toda identidade atribuída ao App Service fica
disponível a todos os processos dele (o `IDENTITY_ENDPOINT` é um só). A separação entre a identidade da API e a do worker
é, portanto, de CONFIGURAÇÃO — o papel de criar recursos está só na identidade do worker, e a API não a pede —, e não uma
fronteira de segurança: um código hostil rodando na API conseguiria pedir o token do worker. Para a separação de verdade,
a alternativa mínima é tirar o worker do App Service: um **Container App** (ou um *Container Apps job* agendado) só com a
identidade do worker, sem ingress — o mesmo `Program.cs`, as mesmas variáveis, nenhum código novo. Não está implementada:
fica como o próximo passo se a política de segurança pedir.

| Variável do worker | O que é |
|---|---|
| `ConnectionStrings__DevKitPage` · `Banco__Provider` | A MESMA base da API (`SqlServer`), com a autenticação da identidade (passo 3). |
| `Provisao__Provedor` | `azure` (padrão). O `simulado` é de desenvolvimento: o worker se recusa a subir com ele em Production. |
| `Provisao__AssinaturaId` | A assinatura do dev.kit onde fica o grupo da provisão (obrigatória). |
| `Provisao__Grupo` | O grupo de recursos FIXO da provisão, já criado (ex.: `devkit-clients`) — obrigatório; vale também para a API, que o grava em cada pedido. O worker recusa o pedido de outro grupo (um antigo, de `rg-devkit-u{id}`): remova esse pelo portal. |
| `Provisao__IdentidadeClientId` | O client id da identidade do passo 2 (não é segredo). Vazio: a identidade do sistema. |
| `Provisao__MaximoDeTentativas` · `EsperaInicialSegundos` · `EsperaMaximaSegundos` · `ReservaMinutos` | 5 · 30 · 900 · 10: a repetição da falha transitória (recuo exponencial) e a validade da reserva de um pedido. |

| Variável da API | O que é |
|---|---|
| `Provisao__Regioes__0`, `__1`… · `Provisao__RegiaoPadrao` | As regiões aceitas (padrão `brazilsouth` e `eastus2`) e a sugerida. |
| `Provisao__MaximoDeFilasPorUsuario` | A cota de filas ativas por usuário (1). |
| `Provisao__PlanosQueLiberam__0`… | Os planos da empresa que liberam a provisão sem a permissão do admin — o tier de assinatura; vazio hoje. |
| `Provisao__PedidosPorHora` | Pedidos (criar, repetir, remover) por usuário por hora (20); acima, 429. |
| `Provisao__MaximoDeAppServicesPorUsuario` · `Provisao__MaximoDeStaticWebAppsPorUsuario` | A cota de App Services (2) e de Static Web Apps (3) ativos por usuário (item 12). |
| `Provisao__LimiteDeCustoMensal` · `Provisao__AlertaDeCustoPercentual` | O teto do custo estimado do mês dos aplicativos, em reais (150; 0 desliga), e o alerta (80%). |
| `Provisao__PlanosDoAppService__0__Id`, `__Rotulo`, `__CustoMensal`… · `Provisao__PlanosDoStaticWebApp__…` · `Provisao__RegioesDoStaticWebApp__0`… | Os planos com o custo estimado (padrão F1/B1 e Free/Standard, sem os preços conferidos) e as regiões do Static Web App. |

**App Service e Static Web App (item 12).** Rode de novo o `Provisao.sql` (as colunas novas e o índice da vaga) antes de
publicar a API desta versão. A identidade do worker passa a precisar também de `Microsoft.Web/serverfarms/read`, `/write` e
`/delete`, `Microsoft.Web/sites/read`, `/write` e `/delete` e `Microsoft.Web/staticSites/read`, `/write` e `/delete`.

**Liberar a um usuário**: `/usuarios` → a licença do usuário → **Recursos liberados** → Liberar. Retirar não apaga o que ele
criou (só impede pedidos novos). **Custo e dono**: tudo no grupo da provisão, com as tags `devkit=provisao`, `devkit-usuario` e
`devkit-recurso` — o que é de um usuário se lista com `az resource list -g devkit-clients --tag devkit-usuario=<id>`; crie o
orçamento com alerta no grupo (ou filtrado pela tag `devkit=provisao`) (os preços e a
disponibilidade das regiões não foram conferidos nesta entrega). A conta de armazenamento aceita só HTTPS e TLS 1.2, sem
acesso público a blob; a chave compartilhada continua ligada porque a fila não aceita SAS de delegação de usuário, e a SAS
curta que o serviço do dev.kit vai pedir à API (fase 2) é assinada com ela.

### Conversa e comandos remotos (controle remoto v2, item 5)

A API põe o comando do site na fila do usuário e entrega ao serviço da máquina a SAS curta de consumo, as duas assinadas com
a chave da conta de armazenamento do usuário. A chave é lida do Resource Manager (`listKeys`) pela **identidade gerenciada
da API** e fica só na memória dela — nunca no banco, no log ou na configuração.

1. **Banco.** Rode `api/scripts/sqlserver/Conversa.sql` (idempotente; depois de `Remoto.sql` e `Provisao.sql`) antes de
   publicar a API desta versão: cria `ComandosRemotos`, `RespostasRemotas`, `EventosDaConversa` e `FalasRemotas` (item 13 —
   numa base que já tem as outras, rodar de novo só cria esta). Sem ele, o `/health` fica `Degraded` e aponta o script.
2. **Identidade da API.** Ligue uma identidade gerenciada à API (a do sistema, ou uma atribuída ao usuário com o client id em
   `Remoto__IdentidadeClientId`) e dê a ela, **no grupo da provisão** (`devkit-clients`), SÓ a função personalizada
   `devkit-api-chave-da-fila`, atribuível só no grupo, com `Microsoft.Storage/storageAccounts/listkeys/action`. Ela não cria,
   não remove e não lê mais nada, e não alcança conta de Storage fora do grupo: quem provisiona continua sendo o worker, com a
   identidade dele.
3. **Rede.** A API sai para `management.azure.com` e `*.queue.core.windows.net` (HTTPS); a máquina do usuário só faz chamadas
   de saída à API e à fila dele.
4. **SSE.** O `GET /api/remoto/conversa/eventos` é uma conexão longa (até `Remoto:DuracaoDoSseSegundos`, 300 s): o ingress do
   Container Apps a aceita com o tempo ocioso padrão, porque a API manda um `ping` a cada 15 s sem evento. Com mais de uma
   réplica, o evento gravado por outra chega na releitura do SQL (`Remoto:IntervaloDoSseSegundos`).

| Variável da API | O que é |
|---|---|
| `Remoto__Fila` | `azure` (padrão). O `simulado` é de desenvolvimento: a API se recusa a subir com ele em Production. |
| `Remoto__IdentidadeClientId` | O client id da identidade atribuída ao usuário (não é segredo). Vazio: a identidade do sistema. |
| `Remoto__TtlPadraoSegundos` · `TtlMinimoSegundos` · `TtlMaximoSegundos` | 900 · 60 · 86400: o TTL do comando; o vencido antes da entrega volta `expirado`. |
| `Remoto__SasMinutos` | 15 (de 1 a 60): a validade da SAS de consumo do serviço. |
| `Remoto__ComandosPorHora` · `EnviosDaConversaPorMinuto` | 30 · 60: o limite de comandos por usuário (no SQL, 429 com `Retry-After`) e o de escritas por minuto. |
| `Remoto__FalasPorMinuto` | 60: as falas do chat do dev.kit por usuário por minuto (item 13), separado das escritas da conversa. |
| `Remoto__RetencaoDaConversaDias` | 30: comandos, respostas, falas e eventos mais velhos saem no expurgo diário. |

Para testar local sem o Azure: `Remoto__Fila=simulado` na API e `Provisao__Provedor=simulado` no worker, na mesma base.

### Acesso aos projetos de design (dono, leitor e editor)

Rode `api/scripts/sqlserver/DesignAcesso.sql` (idempotente, depois de `Us669.sql`) **antes de publicar a API** desta
versão: ele acrescenta `ProjetosDeDesign.DonoId` e a tabela `AcessosAoDesign`, e reconhece o dono dos projetos que já
existem (o do nível usuário, ou quem enviou a primeira versão), deixando editor quem já tinha enviado versões. Sem ele,
todo o design falha. Depois dele, quem não é dono nem tem acesso deixa de ver o projeto: o dono concede pelo painel
Acesso do site ou por `devcli design --conceder <login> --papel leitor|editor --aplicar`. SQLite aplica a migration
`DesignAcesso` automaticamente.

Para o acesso também aos projetos em ondas, execute depois
`api/scripts/sqlserver/AcessoAosProjetos.sql`, antes da API nova. Ele preserva o dono
atual de cada projeto (`UsuarioId` no nível usuário; `AutorId` nos demais), copia
as concessões de design para `AcessosAosProjetos` e usa uma única tabela para os
dois recursos. A cópia e a retirada da tabela antiga ocorrem na mesma transação.
Em uma base já atualizada, execute somente o script novo ao reaplicar esta versão;
não recrie a tabela antiga com `DesignAcesso.sql`.
O SQLite aplica também `AcessoAosProjetos` automaticamente.
Os projetos em ondas passam a ser visíveis somente ao dono e aos usuários com
acesso explícito. Leitores veem os itens e o andamento; editores também enviam
alterações, aprovação e disparo. A gestão de acessos é exclusiva do dono.

### Scripts SQL do controle remoto, na ordem

Numa base Azure SQL que já existe, o controle remoto precisa de nove scripts de `api/scripts/sqlserver/`, todos
idempotentes, NESTA ordem (o `Conversa.sql` depende das tabelas dos dois primeiros; o `CatalogoRemoto.sql` e o
`ClienteDaMaquina.sql`, das `Maquinas` e dos `Usuarios` que já existem; o `BoardRemoto.sql`, do `Conversa.sql` e do
`Decisoes.sql`; o `ArtefatosDosProjetos.sql`, dos `ProjetosCompartilhados` do `Us693.sql`):

1. `Remoto.sql` — as máquinas e os retratos (o heartbeat).
2. `Provisao.sql` — as permissões, os recursos provisionados e os eventos (rode de novo a cada versão: só acrescenta).
3. `Conversa.sql` — os comandos, as respostas, os eventos e as falas.
4. `Decisoes.sql` — as decisões remotas e o segundo fator.
5. `Push.sql` — as assinaturas e as entregas do push do board.
6. `CatalogoRemoto.sql` — o último catálogo de cada máquina (o handshake dos atalhos do chat; remote-control-v3, item 1).
7. `ClienteDaMaquina.sql` — o client credentials da máquina (o cliente e os jti gastos das asserções; remote-control-v3,
   item 9).
8. `BoardRemoto.sql` — as correções do board remoto (07/11/2026): a tabela `PendenciasDispensadas` (o × dos cards) e as
   colunas `ComandosRemotos.Contexto` (o fluxo, o agente e os parâmetros do chat), `FalasRemotas.Anexos` (as imagens das
   falas) e `RespostasDasDecisoes.Dispositivo` (o celular ou o navegador da decisão; controle-v4, item 3). Sem ele, a API
   nova não grava a fala, o comando nem a decisão nessas tabelas, e a verificação do esquema na subida o aponta.
9. `ArtefatosDosProjetos.sql` — os artefatos do projeto (controle-v4, item 2): a tabela `ArtefatosDosProjetos` (o zip do
   pacote .NET no Blob, com o hash e a retenção, e a URL do preview). Sem ele, `POST /api/projetos/{id}/artefatos` e
   `GET /api/projetos/artefatos` respondem 500, e a verificação do esquema na subida o aponta.

**O que o remote-control-v3 acrescenta ao banco** são só os passos 6 e 7 (as tabelas `CatalogosRemotos`,
`ClientesDaMaquina` e `AssercoesDoCliente`). Os itens 4 (ver e criar projeto e design pelo site) e 5 (a fila criada pela
liberação do admin e os Sites no Azure fora do site) **não têm script**: os comandos novos (`catalogo`, `projeto`,
`design`) cabem na coluna `ComandosRemotos.Tipo` (`nvarchar(20)`, do `Conversa.sql`), e a fila pedida na liberação é a
mesma linha de `RecursosProvisionados` do `Provisao.sql`. Os testes `PublicacaoDoControleRemotoTests` conferem que esta
lista e o job `sql` do workflow têm os mesmos scripts, na mesma ordem, e que as tabelas e colunas novas vêm deles.

**Todo script NOVO da pasta vai no job `sql`.** Os vinte anteriores ao controle remoto (`PedidosDeDemonstracao.sql` a
`AcessoAosProjetos.sql`, cada um na seção da sua US, acima) ficam fora: foram aplicados uma vez, à mão, e reaplicá-los a
cada deploy não é seguro (o `AcessoAosProjetos.sql` retira a tabela que o `DesignAcesso.sql` cria). Essa lista é
congelada no teste `Todo_script_da_pasta_vai_no_job_sql_salvo_os_anteriores_ao_controle_remoto`: um `.sql` novo em
`api/scripts/sqlserver` que não esteja no job reprova o teste — e o CI — com o nome dele, antes de chegar à `main`.

**O que o controle-v4 acrescenta ao banco** são os passos 8 e 9: o `BoardRemoto.sql` (as correções de 07/11 e, do item 3,
`RespostasDasDecisoes.Dispositivo`) e o `ArtefatosDosProjetos.sql` (item 2). Os itens 1 (testes) e 6 (as variáveis de
ambiente nas Configurações) são do git.kit e não mudam o banco nem a API.

```bash
for s in Remoto Provisao Conversa Decisoes Push CatalogoRemoto ClienteDaMaquina BoardRemoto ArtefatosDosProjetos; do
  sqlcmd -S <servidor>.database.windows.net -d <base> --authentication-method ActiveDirectoryDefault -i api/scripts/sqlserver/$s.sql || break
done
```

O workflow da `main` também os aplica, **desligado por padrão**: *Actions → Build and deploy… → Run workflow* com
**aplicar_sql** marcado roda o job `sql` (um passo por script, na ordem acima) depois da pré-checagem das app settings
(seção seguinte) e antes do deploy, e um script que falha segura o deploy. Ele usa o segredo do repositório `AZURE_SQL_CONNECTION_STRING`
(`Server=tcp:<servidor>.database.windows.net;Database=<base>;Authentication=Active Directory Default;Encrypt=True`) e o
login do `azure/login` — a identidade do workflow precisa de um usuário na base com `db_ddladmin`, `db_datareader` e
`db_datawriter`. O `/health` sem `Degraded` confirma que nenhum falta.

### Pré-checagem das app settings (o deploy não passa à frente da configuração)

Publicar código que exige configuração nova num App Service sem ela derruba a API (o 403 "stopped" com `ProtecaoDeDados
não configurada` no log de arranque). Por isso o workflow da `main` tem o job **`configuracao`**: ele faz o `azure/login`,
lê as app settings e as connection strings do App Service `devkit` e roda `api/scripts/azure/conferir-app-settings.sh`.
Com uma **FALTA**, o job falha e **nem o `sql` nem o `deploy` rodam**; corrigida a setting, *Re-run* do workflow. O
script só lê, e o log tem só os **nomes** (nenhum valor, nem o das settings que não são segredo). A identidade do
`azure/login` já pode lê-las: é a mesma que publica (`Microsoft.Web/sites/config/list/action`).

O que **segura** o deploy (a API ou o WebJob da provisão recusam subir em Production, ou sobem quebrados):

| Setting | A falta |
|---|---|
| `Jwt__Segredo` | Ausente, ou com menos de 32 caracteres (a referência `@Microsoft.KeyVault(...)` vale). |
| `Banco__Provider` | Ausente ou diferente de `SqlServer`/`Sqlite`: a API cairia no SQLite em `wwwroot`, somente leitura. |
| `ConnectionStrings__DevKitPage` | Nem a app setting nem a connection string `DevKitPage` do App Service. |
| `Cors__Origens__0` | Ausente, ou com a barra no fim (o CORS não casa). |
| `ProtecaoDeDados__Blob` ou `ProtecaoDeDados__Pasta` | Nenhum dos dois; o `Blob` ou a `ChaveDoCofre` que não são `https://`. |
| `Remoto__Fila` · `Provisao__Provedor` | `simulado` (ou outro valor que não `azure`). Ausentes valem `azure`. |
| `Provisao__AssinaturaId` · `Provisao__Grupo` | Ausentes com o provedor `azure`: o WebJob não sobe. |
| `ClienteDaMaquina__*` | Presentes e fora da faixa: `Habilitado` true/false; `ValidadeDoTokenMinutos` 1–60; `ValidadeMaximaDaAssercaoSegundos` 30–600; `ToleranciaDoRelogioSegundos` 0–300. |
| `Artefatos__Armazem` (ou, sem ele, `Design__Armazem`) | Outro valor que não `blob` ou `disco`: o zip do artefato iria para o disco sem aviso. |
| `Artefatos__ConexaoDoBlob` (ou, sem ela, `Design__ConexaoDoBlob`) | Com o armazém `blob`: ausente, ou sem a `AccountKey` (a SAS do link de download é assinada com ela). A referência ao Key Vault vale (o conteúdo não se confere daqui). |
| `Artefatos__TamanhoMaximoMb` · `RetencaoDias` · `LinkHoras` · `MaximoPorProjeto` | Presentes e não inteiros: as rotas dos artefatos respondem 500 ao ler a configuração. |

O que só **avisa** (o deploy segue; o recurso fica degradado): `Licencas__ChavePrivada` (o `/health` fica `Degraded`),
as três `Push__*`, o `REPO_KEY`, o `Provisao__IdentidadeClientId` vazio (o worker usa a identidade do sistema), o
`ASPNETCORE_ENVIRONMENT` diferente de `Production`, o armazém dos artefatos em **disco** sem um `Artefatos__Pasta`
absoluto (a pasta padrão cai em `wwwroot`, somente leitura no deploy por pacote) e os números `Artefatos__*` abaixo de 1
(a API usa 1).

**App settings novas do remote-control-v3:** só a seção opcional `ClienteDaMaquina__*` (item 9, tabela da seção "Client
credentials da máquina"), com padrões seguros. Os itens 1, 4 e 5 não pedem configuração nova.

**App settings novas do controle-v4:** só a seção opcional `Artefatos__*` (item 2, seção "Artefatos do projeto"), que
herda do design o armazém e a conexão. Para o recurso funcionar em produção basta o que o design já pede —
`Design__Armazem=blob` e `Design__ConexaoDoBlob` com a `AccountKey` —, e a pré-checagem acima segura o deploy quando o
blob está declarado sem a conexão que assina o link. As correções de 07/11 e o item 3 não pedem configuração nova.

Antes de mesclar na `main`, rode a mesma conferência da sua máquina (o `az login` na assinatura do dev.kit):

```bash
bash api/scripts/azure/conferir-app-settings.sh --app devkit [--grupo <grupo do App Service>]
```

Saída 0 com `Resultado: configuração conferida … — o deploy segue.`; saída 1 com uma linha `FALTA  <setting>: <o que
fazer>` por pendência. Sem o `az` (ou para conferir um arquivo), `--settings <arquivo>` lê `nome<TAB>valor` por linha e
`--conexoes <arquivo>` um nome de connection string por linha.

O **Static Web App** também confere antes de publicar: o passo *Pré-checagem do build* do workflow
`azure-static-web-apps-black-forest-00e9a2410.yml` falha sem a variável do repositório `VITE_API_URL` (`https://…`), em
vez de publicar uma web que não chama a API.

### Push do board (as chaves VAPID)

O push precisa de um par VAPID (P-256) e de um contato. Gere o par UMA vez por ambiente, numa máquina com o SDK:

```bash
dotnet run --project api/src/DevKitPage.Api -- --gerar-chaves-vapid
```

A API imprime as três app settings e sai, sem subir o site nem ler o banco. Grave-as no App Service da API (*Settings →
Environment variables*, ou o `az webapp config appsettings set` que o gerador também imprime) e reinicie:

| Variável da API | O que é |
|---|---|
| `Push__ChavePublica` | A pública, 65 bytes em base64url (87 caracteres). O site a entrega ao navegador. |
| `Push__ChavePrivada` | **Segredo**: o escalar privado, 32 bytes em base64url (43 caracteres). Nunca em commit nem chamado. |
| `Push__Assunto` | O contato do VAPID: `mailto:<e-mail>` ou `https://<site>` (o serviço de push o usa para avisar de abuso). |

Sem as três válidas, `GET /api/remoto/push` (com o login) responde `disponivel: false` e o board não oferece o aviso. Trocar o par
invalida todas as assinaturas (o navegador assina de novo na próxima visita ao board).

### Key ring do DataProtection (persistido e compartilhado)

O push do board e o segundo fator das decisões guardam dados protegidos pelo DataProtection. Sem o key ring persistido e
compartilhado, um reinício (ou outra instância) gera uma chave nova e o que foi protegido antes deixa de abrir. Por isso a
API **não sobe em Production sem ele** (a mensagem diz `ProtecaoDeDados não configurada`). Fora de Production, sem nada,
vale o padrão do ASP.NET (a pasta do usuário que roda).

| Variável da API | O que é |
|---|---|
| `ProtecaoDeDados__Blob` | O blob do key ring: `https://<conta>.blob.core.windows.net/protecao-de-dados/keys.xml`. Lido e gravado com a identidade da API (`Storage Blob Data Contributor` no contêiner, que já precisa existir). |
| `ProtecaoDeDados__ChaveDoCofre` | Opcional: a chave RSA do Key Vault que protege o key ring (`https://<cofre>.vault.azure.net/keys/devkit-keyring`). A identidade da API precisa do `Key Vault Crypto Service Encryption User` nela. Exige o `Blob` (ou a `Pasta`). |
| `ProtecaoDeDados__Pasta` | A alternativa ao blob: uma pasta num disco compartilhado pelas instâncias (no App Service, `/home/dados/protecao`). Sem cofre, as chaves ficam em claro nesse disco. |
| `ProtecaoDeDados__IdentidadeClientId` | O client id de uma identidade atribuída ao usuário para o blob e o cofre (não é segredo). Vazio: a do sistema. |

O nome da aplicação no DataProtection é fixo (`DevKitPage.Api`): a primeira publicação desta versão invalida o que estava
protegido com o padrão anterior — as assinaturas do push são refeitas na próxima visita ao board, e quem tinha o segundo
fator preparado prepara de novo.

### Renovação do login pela máquina (o serviço do dev.kit com o app fechado)

O token do usuário vence em `Jwt__ExpiracaoMinutos` (60) e só o app aberto o renovava. Agora o app, logado, pede ao site
uma **renovação presa à máquina** (`POST /api/auth/sessao-da-maquina`, com o JWT e a `X-Machine-Key`), e o serviço do
dev.kit a troca por um token novo sozinho (`POST /api/auth/renovar-pela-maquina`, só com a `X-Machine-Key`) — sem a senha.
A renovação não tem estado no banco: é um JWT com outra audiência (não abre rota nenhuma como Bearer), vale só naquela
máquina e para aquele usuário, por `Jwt__RenovacaoDaMaquinaDias` (30) a partir do último uso, e cai com a troca de senha,
com o bloqueio e com a falta de assento. Nenhuma configuração nova nem script.

### Client credentials da máquina (o serviço do dev.kit só no remoto)

Desde o item 9 do remote-control-v3, o serviço do dev.kit não depende mais do token do usuário: no login do app, o dev.kit
registra a máquina como CLIENTE (`POST /api/auth/cliente-da-maquina`, com o JWT e a `X-Machine-Key`), mandando a chave
PÚBLICA de um par ECDSA P-256 que ele gerou (a privada fica no DPAPI do usuário na máquina). O serviço pede o token com
`grant_type=client_credentials` e uma asserção assinada (`private_key_jwt`, RFC 7523) em `POST /api/auth/token`: curta, de
uso único (`jti`), e o token que volta vale só para o controle remoto daquela máquina e daquele usuário — as demais rotas o
recusam. O "Desvincular" do Board revoga a máquina; a troca de senha e o bloqueio do usuário revogam todas as dele. A
renovação pela máquina (seção anterior) continua aceita: o dev.kit cai nela quando a API não anuncia o client credentials.

1. **Banco.** Rode `api/scripts/sqlserver/ClienteDaMaquina.sql` (idempotente; é também o passo 7 da lista do controle remoto) antes de publicar a API.
2. **Configuração (opcional).** A seção `ClienteDaMaquina` tem padrões seguros, e a API **não sobe** com um valor fora da faixa
   (a mensagem diz qual):

| Variável da API | Padrão | O que é |
|---|---|---|
| `ClienteDaMaquina__Habilitado` | `true` | `false` faz o registro e o token responderem 404: o dev.kit segue na renovação pela máquina. |
| `ClienteDaMaquina__ValidadeDoTokenMinutos` | `15` | A validade do token da máquina (1 a 60). |
| `ClienteDaMaquina__ValidadeMaximaDaAssercaoSegundos` | `300` | O maior intervalo entre `iat` e `exp` da asserção (30 a 600). |
| `ClienteDaMaquina__ToleranciaDoRelogioSegundos` | `60` | A tolerância do relógio da máquina (0 a 300). |

### Script de infraestrutura

`api/scripts/azure/infra-controle-remoto.sh` faz, de forma idempotente e **sem nada no escopo da assinatura**, o que as seções
acima pedem no Azure: confere que o grupo da provisão (`--grupo-clientes`) existe (não o cria); a identidade do worker
(`id-devkit-provisao`) e a função `devkit-provisao-worker` atribuível e atribuída só nesse grupo (as contas e filas de Storage, os
planos, os sites e os Static Web Apps — sem `resourceGroups/write`); a identidade no App Service, ao lado da do sistema, com o
`Provisao__AssinaturaId`, o `Provisao__Grupo` e o `Provisao__IdentidadeClientId`; a função `devkit-api-chave-da-fila` (só o
`listKeys`) para a identidade da API, também só no grupo; e, com `--conta-keyring` (e `--cofre`), o contêiner do key ring, a
chave RSA do cofre, os papéis da API neles e as app settings `ProtecaoDeDados__*`. Rode primeiro com `--dry-run`, que não
chama o `az` e lista as consultas e gravações na ordem:

```bash
api/scripts/azure/infra-controle-remoto.sh --dry-run --assinatura 82ba9ad4-40bb-4bfe-91e3-1a4ba1edf95a \
  --grupo <grupo do App Service> --app devkit --grupo-clientes devkit-clients [--conta-keyring <conta> --cofre <cofre>]
```

Conferido, rode sem o `--dry-run`, com o `az login` numa conta que cria funções e atribui papéis no grupo (Owner ou User
Access Administrator nele). Com o key ring em `ProtecaoDeDados__Pasta` (o caso de hoje, numa instância B1), omita
`--conta-keyring` e `--cofre`. Os testes do script (`InfraDoControleRemotoTests`) rodam a simulação e conferem que nada fica
no escopo da assinatura.

### Ativação do controle remoto (remote-control-v3) — roteiro e checklist

O roteiro é do desenvolvedor, no Azure real; nada dele roda pelo agente. **Na ordem**, com o branch do projeto mesclado
na `main`:

1. **Settings**: `bash api/scripts/azure/conferir-app-settings.sh --app devkit` até sair `o deploy segue` (seção
   "Pré-checagem das app settings"). A variável do repositório `VITE_API_URL` definida (o Static Web App).
2. **Azure**: `infra-controle-remoto.sh --dry-run … --grupo-clientes devkit-clients`, conferir, e rodar de verdade (grava o
   `Provisao__Grupo`; sem ele o WebJob não sobe). Numa base que já tem o controle remoto v2, é só conferir: rodar de novo
   não muda nada.
3. **Push**: `--gerar-chaves-vapid` e as três `Push__*` no App Service (se ainda não estão).
4. **Banco e API**: *Actions → Build and deploy… → Run workflow* com **aplicar_sql** marcado — `configuracao` → `sql` (os
   nove scripts, na ordem da seção "Scripts SQL do controle remoto") → `deploy` (a API e o WebJob `provisao` no mesmo
   pacote). O push na `main` já publica; o disparo manual é o que aplica o SQL. Se os scripts já foram aplicados à mão
   (`sqlcmd`), o push basta. **Não** reinicie nem mude settings durante o deploy (as DLLs corrompidas do 500 `Bad IL
   range`).
5. **Site**: o workflow do Static Web App roda no mesmo push da `main` (ou *Run workflow* nele, para republicar sem
   commit): *Pré-checagem do build* → *Build And Deploy*.
6. **dev.kit**: o pacote com o handshake (item 1), a criação de projeto e design (item 4) e o client credentials (item 9);
   em cada máquina, entrar uma vez no app (o login registra o cliente da máquina) e reinstalar o serviço na conta do
   desenvolvedor (Configurações → Serviço de agendamento).

Checklist verificável (`<api>` é a URL da API, `<site>` a do Static Web App, `$TOKEN` o JWT de um dev logado — o
`token` do `POST /api/auth/login` — e `<maquinaId>` o de `GET /api/remoto/maquinas`):

- [ ] **`/health` Healthy.** `curl -s https://<api>/health` responde `Healthy`. `Degraded` é um script ou uma setting
      faltando: o log da subida diz qual (`Esquema do banco incompleto: … (CatalogoRemoto.sql)`), e o check `licencas`
      aponta o `Licencas__ChavePrivada`.
- [ ] **O handshake respondendo.** Com o dev.kit ABERTO na máquina, no `/board` → chat → um atalho (`/`, `#` ou `@`) →
      **Atualizar**: a lista de work items, repositórios e fluxos se preenche e a data do catálogo passa a ser a de agora.
      Pela API: `curl -s -X POST https://<api>/api/remoto/comandos -H "Authorization: Bearer $TOKEN" -H "Content-Type:
      application/json" -d '{"maquinaId":"<maquinaId>","tipo":"catalogo"}'` responde **202** (ou **200**, o pedido que já
      estava na fila) — a API antiga responderia **400** —, e em segundos `curl -s
      https://<api>/api/remoto/maquinas/<maquinaId>/catalogo -H "Authorization: Bearer $TOKEN"` traz o `recebidoEm` de
      agora. Sem o dev.kit aberto o pedido fica `na-fila` (espera o app abrir), e o site mostra o último catálogo com a data.
- [ ] **A fila criada ao liberar o usuário.** O admin, em `/usuarios`, menu ⋯ do usuário → **Liberar a provisão**: a
      mensagem diz "a fila do controle remoto (conta dkq…) foi pedida" e o selo **Provisão** aparece na linha, sem o
      usuário fazer nada. Em poucos minutos (o WebJob varre a cada 10 s; o Azure cria a conta de Storage) `GET /api/usuarios` (admin) traz a `fila` dele com
      `estado: "pronto"`; `az storage account list -g devkit-clients --query "[?tags.\"devkit-usuario\"=='<id do usuário>'].name" -o tsv`
      lista a conta; a `/conta` do usuário mostra só a situação da **Fila do controle remoto** (sem região, sem Criar a
      fila, sem Remover); nenhum grupo novo apareceu na assinatura.
- [ ] **O artefato do projeto (controle-v4, item 2).** `curl -s https://<api>/api/projetos/artefatos -H "Authorization:
      Bearer $TOKEN"` responde **200** com uma lista (vazia antes do primeiro artefato) — o 500 é o
      `ArtefatosDosProjetos.sql` faltando. No `/board`, o card do projeto tem **Gerar artefato**; o clique vira o comando
      `projeto` com `acao: artefato` na fila, o dev.kit responde com o número do artefato, e o link aparece no card quando o
      executor termina: **Baixar** (o zip no contêiner `artefatos`, link SAS com prazo) ou **Abrir preview** (o ambiente de
      preview do Static Web App, nunca o de produção).
- [ ] **O chat sem aprovação.** Com uma decisão pendente na máquina, no `/board`: a decisão aparece só no painel à direita
      (Decisões, com o segundo fator); o chat mostra só a conversa — nenhum cartão nem botão Aprovar/Recusar dentro dele,
      e os turnos automáticos do Orquestrador (avisos, retratos, gatilhos) não entram.
- [ ] **Sites no Azure fora do site.** A `/conta` não tem a seção "Sites e APIs no Azure" (nem em lugar nenhum do site);
      os motores ficam: `POST /api/provisao/aplicativos` e o worker de aplicativos seguem na API para os fluxos e as skills
      do dev.kit (`docs/arquitetura.md`).
- [ ] **O site novo publicado.** O workflow do Static Web App do commit mesclado está verde, e
      `curl -sI https://<site>/board` traz `Cache-Control: no-cache` (o navegador revalida a página a cada visita e pega o
      `index.html` novo); um `/assets/*.js` do HTML traz `max-age=31536000, immutable`. Um recarregamento comum no `/board`
      já mostra o painel à direita, o filtro de dia e os atalhos com Atualizar.
- [ ] **O worker.** O WebJob `provisao` aparece *Running* em *WebJobs*, e o log dele (*WebJobs → Logs*) mostra a varredura
      sem erro de identidade ou de assinatura.
- [ ] **O serviço sem o app.** Com o dev.kit FECHADO, a máquina continua *online* no site, o cartão **Vínculo do serviço**
      do `/board` diz **Serviço vinculado**, e o Ligar chega.
- [ ] `GET /api/remoto/push` (com o login) responde `disponivel: true`; o board assinado recebe o aviso de uma pendência nova.

### Ativação do controle-v4 — roteiro e checklist

O roteiro é do desenvolvedor, no Azure real; nada dele roda pelo agente. Ele parte do remote-control-v3 já ativo (seção
anterior) e traz ao ar as correções do board remoto de 07/11, o "Gerar artefato" (item 2) e a autorização pelo site com o
dispositivo e o cartão de arquivar tasks (item 3). **Na ordem**, com o branch do projeto mesclado na `main`:

1. **Settings**: `bash api/scripts/azure/conferir-app-settings.sh --app devkit` até sair `o deploy segue`, **sem** o aviso
   `Design__Armazem`/`Artefatos__Armazem` (o armazém dos artefatos em `blob`, com a conexão que tem a `AccountKey`). No
   App Service Windows, `Artefatos__TamanhoMaximoMb=28` ou menos (seção "Artefatos do projeto").
2. **Storage**: nada a criar à mão — o contêiner PRIVADO `artefatos` nasce na primeira gravação, na conta do design (ou na
   do `Artefatos__ConexaoDoBlob`). Configure o ciclo de vida dele (*Lifecycle management*: blobs com mais de 7 dias, ou do
   `Artefatos__RetencaoDias`).
3. **Banco e API**: *Actions → Build and deploy… → Run workflow* com **aplicar_sql** — `configuracao` → `sql` (os nove
   scripts; os do controle-v4 são o passo 8, `BoardRemoto.sql`, e o 9, `ArtefatosDosProjetos.sql`) → `deploy`. À mão, o
   mesmo com `sqlcmd … -i api/scripts/sqlserver/BoardRemoto.sql` e depois `-i api/scripts/sqlserver/ArtefatosDosProjetos.sql`,
   **antes** do deploy da API (o segundo recusa sem o `Us693.sql`, com a mensagem dizendo qual rodar).
4. **Site**: o workflow do Static Web App do mesmo push (o card do projeto com **Gerar artefato**, o cartão de arquivar tasks
   e a decisão que manda o dispositivo).
5. **dev.kit**: só depois da API (a Rule de impacto 5), o pacote do dev.kit do branch do projeto no git.kit — o que executa a
   ação `artefato` e oferece a autorização `arquivar-tasks`. O dev.kit antigo recusa a ação com o motivo, sem quebrar.

Checklist verificável (`<api>`, `<site>`, `$TOKEN` e `<maquinaId>` como na seção anterior; `<projetoId>` é o `id` de um
projeto de TESTE em que o dev é editor, de `GET /api/projetos`):

- [ ] **O esquema completo.** `curl -s https://<api>/health` responde `Healthy`, e no *Query editor* da base
      `SELECT OBJECT_ID(N'dbo.PendenciasDispensadas') AS Dispensadas, OBJECT_ID(N'dbo.ArtefatosDosProjetos') AS Artefatos,
      COL_LENGTH(N'dbo.ComandosRemotos', N'Contexto') AS Contexto, COL_LENGTH(N'dbo.FalasRemotas', N'Anexos') AS Anexos,
      COL_LENGTH(N'dbo.RespostasDasDecisoes', N'Dispositivo') AS Dispositivo` não traz nenhum `NULL`.
- [ ] **Os scripts idempotentes.** No job `sql`, os passos `BoardRemoto.sql` e `ArtefatosDosProjetos.sql` verdes; um
      segundo *Run workflow* com **aplicar_sql** fica verde de novo e a consulta acima devolve os mesmos valores.
- [ ] **Os artefatos listados.** `curl -s https://<api>/api/projetos/artefatos -H "Authorization: Bearer $TOKEN"` responde
      **200** com uma lista (vazia antes do primeiro artefato); o **500** é o `ArtefatosDosProjetos.sql` faltando.
- [ ] **O preview guardado, nunca produção.** `curl -s -X POST https://<api>/api/projetos/<projetoId>/artefatos -H
      "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"repositorio":"dev.kit.page","sha":"0f3c534",
      "url":"https://<preview>.azurestaticapps.net/","ambiente":"preview"}'` responde **201** com o `link` igual à `url`; com
      `"ambiente":"production"`, **400**.
- [ ] **O pacote com o link assinado.** Com um `teste.zip` pequeno: `curl -s -X POST
      https://<api>/api/projetos/<projetoId>/artefatos -H "Authorization: Bearer $TOKEN" -F arquivo=@teste.zip -F
      repositorio=dev.kit.page -F sha=0f3c534 -F hash=$(sha256sum teste.zip | cut -d' ' -f1)` responde **201** com o `link`
      `https://<conta>.blob.core.windows.net/artefatos/projetos/<projetoId>/….zip?…&sig=…` e o `expiraEm`; `curl -sI "<link>"`
      responde **200** com `Content-Disposition: attachment`, e o mesmo endereço **sem** a consulta (`?…`) é recusado (o
      contêiner é privado). Com outro `hash`, **400**; com um zip acima do teto, **413**.
- [ ] **Gerar artefato pelo site.** No `/board` do desktop e do celular, com o dev.kit novo aberto, o card do projeto →
      **Gerar artefato**: o comando `projeto` com `acao: artefato` vai à fila, e o card mostra o link quando o dev.kit termina
      (**Baixar**, com o prazo, ou **Abrir preview**).
- [ ] **O dispositivo na decisão.** Uma decisão aprovada pelo celular aparece no registro do Board como "pelo celular";
      `GET https://<api>/api/remoto/maquinas/<maquinaId>/decisoes/respostas` traz `"dispositivo":"celular"`, e o valor fora da
      lista no `POST …/decisoes/<pendencia>` é **400**.
- [ ] **O cartão de arquivar tasks.** O pedido do Orquestrador `devcli sessao --acao arquivar --alvo <a>,<b> --aplicar`
      vira, no painel de Decisões do `/board`, o cartão com as tasks e o **Autorizar** com o segundo fator; autorizado, as tasks
      aparecem arquivadas no dev.kit (nenhuma pasta apagada), com a origem site e o dispositivo na conversa.
- [ ] **As correções de 07/11.** O **×** de um card de decisão o tira do painel (e do push) e o filtro das dispensadas o
      traz de volta (`GET …/decisoes?dispensadas=true` com `dispensada: true`); um comando do chat com fluxo e parâmetros
      grava o `Contexto` (o reenvio do que falhou leva o mesmo fluxo); a fala do Orquestrador com imagem mostra a miniatura e
      abre o modal.

## Web

**Azure Static Web Apps**: `npm ci && npm run build` em `web/`, publicando `web/dist`, com as
variáveis de build `VITE_API_URL=https://<api>` e `VITE_SITE_URL=https://<site>` (a URL absoluta do
canonical e da imagem do Open Graph). O fallback de rotas para `index.html` (rotas do React Router:
`/login`, `/dashboard`) já vai no build, pelo [`web/public/staticwebapp.config.json`](../web/public/staticwebapp.config.json)
— sem ele, recarregar o `/dashboard` dá 404. As mídias da landing (`web/public/midia`) vão no próprio
build — estão no repositório, sem LFS.

**O site novo chega ao navegador** pelo mesmo arquivo: a página e as rotas do React saem com `Cache-Control: no-cache`
(`globalHeaders`: o navegador revalida o `index.html` a cada visita, e uma publicação nova aparece no primeiro
recarregamento, sem o index antigo apontando para assets que a publicação apagou), e os `/assets/*` — com o hash no nome
— com `max-age=31536000, immutable`. O `board-sw.js` continua `no-cache` e não guarda nada no cache. O teste
`web/src/staticWebApp.test.ts` confere os três.

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
