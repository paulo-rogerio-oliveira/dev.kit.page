# dev.kit.page

A página do **dev.kit**: uma landing page de **venda** do produto (com vídeos de cada recurso e o
pedido de demonstração), o **login** e um **dashboard**
com a quantidade e a qualidade de uso do dev.kit **por máquina**, alimentado pela telemetria que o
próprio dev.kit envia (repositório `git.kit`, opt-in em *Configurações → Telemetria de uso*).

| Pasta | O que tem |
|---|---|
| [`api/`](api) | A API ASP.NET Core (.NET 10): autenticação JWT, registro e ingestão das máquinas, consultas do dashboard e os pedidos de demonstração. Base SQLite embarcada, criada na primeira subida já com o usuário **admin**; scripts do Azure SQL em `api/scripts/sqlserver`. |
| [`web/`](web) | A web em React + Vite + TypeScript: landing (conteúdo em `src/conteudo`, mídias em `public/midia`, gravador em `midia/`), login (com a troca obrigatória de senha) e dashboard. |
| [`docs/`](docs) | [Arquitetura](docs/arquitetura.md), a [mensagem e o roteiro das mídias da landing](docs/landing-conteudo.md), o [contrato v1 da telemetria](docs/contrato-telemetria-v1.md) e a [publicação no Azure](docs/publicacao-azure.md). |

## Executar localmente

Pré-requisitos: **.NET SDK 10.0.203+** e **Node 22+**.

**1. A API** (porta 5080):

```bash
cd api
dotnet user-secrets set "Seed:AdminPassword" "suaSenhaInicial2026" --project src/DevKitPage.Api   # opcional
dotnet user-secrets set "Telemetria:CodigoDeRegistro" "seu-codigo" --project src/DevKitPage.Api     # para registrar máquinas
dotnet run --project src/DevKitPage.Api
```

Na primeira subida a API cria `api/src/DevKitPage.Api/dados/devkitpage.db` com **um** usuário
`admin`, marcado para trocar a senha. Sem `Seed:AdminPassword`, a senha inicial é gerada e escrita
**uma vez** no log dessa subida. Em Development, sem `Jwt:Segredo`, um segredo efêmero é gerado (os
tokens morrem ao reiniciar); em Production a API **não sobe** sem ele.

**2. A web** (porta 5173):

```bash
cd web
cp .env.example .env.local      # VITE_API_URL=http://localhost:5080
npm install
npm run dev
```

Abra http://localhost:5173 → **Entrar** → `admin` e a senha inicial → defina a nova senha → dashboard.

**3. O dev.kit enviando**: no dev.kit, *Configurações → Telemetria de uso*: ligue o opt-in, informe
`http://localhost:5080` e o código de registro, salve e clique **Enviar agora** (ou
`devcli telemetria --enviar`). A máquina aparece no filtro do dashboard.

## Testes

```bash
cd api && dotnet test DevKitPage.slnx         # unidade (Core), base real (Infrastructure) e integração (WebApplicationFactory)
cd web && npm test                            # componentes: Vitest + Testing Library + MSW
cd web && npx playwright install chromium     # uma vez
cd web && npm run test:e2e                    # ponta a ponta: base nova → login → troca → lote → dashboard; landing → pedido de demonstração → dashboard
```

## Regravar as mídias da landing

As mídias (`web/public/midia`: MP4, WebM e o poster JPG de cada recurso) e a imagem de
compartilhamento são geradas pelo roteiro de `web/midia/roteiro.mjs`, a partir das capturas do
dev.kit com dados de demonstração:

```bash
cd web
npx playwright install chromium               # uma vez
FFMPEG=/caminho/do/ffmpeg npm run midia       # ffmpeg com libx264 e libvpx-vp9 (ou no PATH)
```

O gravador falha acima do orçamento (1,5 MB por arquivo, 25 MB no total). O passo a passo — inclusive
como gerar telas novas sem tocar no banco real do dev.kit — está em
[docs/landing-conteudo.md](docs/landing-conteudo.md#regravar-as-mídias-a-cada-versão-do-produto).

O pipeline [`.github/workflows/ci.yml`](.github/workflows/ci.yml) roda os três a cada push e PR e
publica os resultados.

## Configuração (por ambiente — nada de segredo no repositório)

| Chave | Padrão | O que é |
|---|---|---|
| `ConnectionStrings:DevKitPage` | `Data Source=dados/devkitpage.db` | A base. |
| `Banco:Provider` | `Sqlite` | `Sqlite` ou `SqlServer` (Azure SQL) — troca sem mudar código. |
| `Jwt:Segredo` | — | **Segredo**, 32+ caracteres (Key Vault / `Jwt__Segredo`). Obrigatório em Production. |
| `Jwt:ExpiracaoMinutos` · `MaxFalhas` · `BloqueioMinutos` | 60 · 5 · 15 | Validade do token e bloqueio após falhas seguidas. |
| `Seed:AdminLogin` · `Seed:AdminPassword` | `admin` · gerada | O admin da primeira subida (troca obrigatória). |
| `Telemetria:CodigoDeRegistro` | — | **Segredo**: o código com que uma máquina se registra (vazio: só o admin registra). |
| `Telemetria:RetencaoDias` | 180 | Por quanto tempo o evento bruto é guardado (os totais diários ficam). |
| `Telemetria:MaxEventosPorLote` · `MaxBytesPorLote` | 1000 · 1 MB | Acima disto, 413. |
| `Demonstracoes:RetencaoDias` | 365 | Por quanto tempo o pedido de demonstração é guardado (LGPD); o expurgo diário apaga os vencidos. |
| `Demonstracoes:LimitePorMinuto` | 5 | Pedidos por minuto por IP de cliente no formulário; acima disto, 429. |
| `Proxy:RedesConfiaveis` | — (só loopback) | As redes (CIDR) dos proxies cujo `X-Forwarded-For` vale — no Azure, a do Container Apps. |
| `Cors:Origens` | (Development: Vite) | As origens da web. |
| `VITE_SITE_URL` (build da web) | — | A URL pública do site, para o canonical e o Open Graph. |

## Como verificar cada critério da US #270

1. **Repositório com /web, /api, testes e README** — esta árvore; `dotnet test` e `npm test`.
2. **A landing apresenta os recursos e leva ao login** — abra `/`; `Landing.test.tsx`.
3. **Login JWT, rotas exigem token, admin criado na primeira subida** — passo 1 acima; `ApiTests` (`Rota_sem_token_e_401`, `Admin_semeado_so_acessa_a_troca…`) e `InfraestruturaTests` (`Base_nova_tem_exatamente_um_admin…`).
4. **Quantidade e qualidade por máquina e período, mais os eventos** — o dashboard; `Dashboard.test.tsx` e o cenário Playwright.
5. **O dev.kit envia com opt-in, fila local e retry, sem dado sensível** — passo 3; os testes `TelemetrySyncServiceTests` do git.kit e o [contrato](docs/contrato-telemetria-v1.md).
6. **Configuração por ambiente para o Azure** — a tabela acima e [docs/publicacao-azure.md](docs/publicacao-azure.md); `Api_recusa_subir_em_production_sem_segredo_jwt`.
7. **Testes automatizados da API, do front e da instrumentação** — as três suítes acima mais as do git.kit.

## Como verificar cada critério da US #283

1. **A landing, nesta ordem: hero com mídia em movimento, benefícios, como funciona, recursos,
   integrações, segurança, contato, FAQ e CTA final** — abra `/`; `Landing.test.tsx` (`abre sem login
   com as seções na ordem do critério 1`) e `conteudo/landing.test.ts`; o cenário Playwright
   `landing.spec.ts`.
2. **Cada recurso principal com mídia animada, poster, texto alternativo e fallback para a captura** —
   `landing.test.ts` (todo recurso tem vídeo, poster e alt, e os arquivos existem em `public/midia`)
   e `Midia.test.tsx` (`quando o vídeo falha, cai na captura estática…`).
3. **Vídeos muted, em loop, playsinline e sob demanda; com prefers-reduced-motion, o poster** —
   `Midia.test.tsx` (atributos, `só carrega as fontes… quando chega à tela`, `com prefers-reduced-motion
   mostra só o poster`); no navegador, ligue *Reduzir movimento* no sistema e recarregue.
4. **Formulário que valida e grava pela API com limite por IP (certo atrás do proxy); pedidos listados
   e excluídos no dashboard; expurgo além da retenção; a tabela no SQLite e no Azure SQL** —
   `FormularioDeDemonstracao.test.tsx`, `Dashboard.test.tsx`, `ValidadorDeDemonstracaoTests`,
   `DemonstracoesTests` (Infrastructure: gravação, paginação, exclusão, expurgo e o script do Azure SQL
   contra o modelo) e `DemonstracoesApiTests` (201/400/401/404, 429, `X-Forwarded-For` confiável e não
   confiável); a migration `PedidosDeDemonstracao` e `api/scripts/sqlserver/PedidosDeDemonstracao.sql`.
5. **"Entrar" leva ao login e o dashboard segue sem regressão** — `Landing.test.tsx` (`o link de entrar
   leva ao login`), `Dashboard.test.tsx` e o cenário Playwright `fluxo.spec.ts`, que continua igual.
6. **SEO e Open Graph; Lighthouse ≥ 90 em Performance e Acessibilidade (desktop)** — `web/index.html`;
   rode `npx lighthouse http://localhost:4173 --preset=desktop` com `npm run build && npm run preview`.
7. **Testes em todas as camadas e CI verde** — as suítes acima no [`ci.yml`](.github/workflows/ci.yml).
8. **README, arquitetura e publicação atualizados** — esta seção, a tabela de configuração,
   [docs/arquitetura.md](docs/arquitetura.md#a-landing-us-283), [docs/landing-conteudo.md](docs/landing-conteudo.md)
   e [docs/publicacao-azure.md](docs/publicacao-azure.md#atualizar-uma-base-azure-sql-que-já-existe-us-283).
