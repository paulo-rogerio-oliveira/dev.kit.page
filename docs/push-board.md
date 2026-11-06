# Push para decisões pendentes

O Board usa Web Push (RFC 8291, `aes128gcm`, e VAPID) para avisar sobre perguntas e aprovações. O usuário ativa pelo controle **Notificações neste dispositivo**, acima das abas Chat/Board. A permissão é solicitada somente após o toque. **Desligar notificações** cancela a assinatura na API antes de atualizar a interface, remove a assinatura local e fecha avisos exibidos. A permissão do navegador continua sob controle do usuário nas configurações do sistema.

## Privacidade e abertura da decisão

O título e o corpo são fixos: “dev.kit · decisão pendente” e “Há uma decisão pendente. Abra o dev.kit para conferir.” O payload cifrado contém somente `entregaId`, um GUID opaco. Nunca contém pergunta, resposta, projeto, máquina, evidência, SHA ou JWT. O service worker ignora textos e URLs recebidos no payload e constrói um link da mesma origem para `/board?notificacao=<id>`.

Esse link exige login, preserva o destino durante o login e a troca obrigatória de senha, e só resolve para o dono da assinatura. A API relê a pendência e o snapshot. Pendência resolvida, removida ou alterada informa indisponibilidade. Perguntas recebem foco no Board da máquina correta; aprovações abrem a revisão de evidências. A notificação nunca registra resposta, aprova ato ou substitui o segundo fator.

Endpoints, chaves `auth`/`p256dh` e seus vínculos são protegidos com ASP.NET Data Protection no banco; somente o hash do endpoint é indexado. O cliente HTTP de push não registra URLs e não segue redirecionamentos. Só aceita HTTPS/443, sem credenciais/fragmento, nos provedores FCM, Mozilla Autopush, Apple Web Push e Windows Notification Service. Não permite usar a API para chamar URL arbitrária ou rede interna.

## Configuração pelo responsável pela publicação

Aplicar `api/scripts/sqlserver/Push.sql` ao atualizar uma base Azure SQL existente, pelo mesmo processo de revisão/publicação dos demais scripts. O SQLite aplica a migration `20261106020000_PushDoBoard` automaticamente. Esta task não executa o script no Azure.

Configurar `Push__ChavePublica` (ponto P-256 não comprimido, base64url), `Push__ChavePrivada` (escalar P-256, base64url) e `Push__Assunto` (contato `mailto:` ou HTTPS). Usar um par VAPID estável e manter a chave privada no mecanismo de segredos do ambiente; nunca no bundle web nem no repositório. O endpoint autenticado `GET /api/remoto/push` publica somente a chave pública e a disponibilidade. Sem configuração válida, o controle informa indisponibilidade e a API recusa novas assinaturas com 503; não simula entrega.

Persistir e compartilhar o key ring de Data Protection entre instâncias da API. Sua perda invalida as assinaturas protegidas, que são removidas e precisam de nova ativação. A rotação da chave VAPID também exige nova assinatura: o processador remove as associadas à chave anterior. A conta aceita até oito dispositivos, com expiração de assinatura após 30 dias sem nova ativação. O usuário reativa pelo mesmo controle.

Rotas autenticadas: `POST /api/remoto/push/assinaturas`, `GET`/`DELETE /api/remoto/push/assinaturas/{id}` e `GET /api/remoto/push/entregas/{id}`. Cancelamento é idempotente e filtrado pelo usuário do JWT. Um endpoint de outra conta não pode ser transferido por conhecer sua URL.

## Envio, repetição e cancelamento

A API varre assinaturas a cada dez segundos, em lotes de vinte dispositivos, priorizando os menos recentemente verificados. Somente máquinas com heartbeat válido produzem novos avisos. A fila persistente deduplica por assinatura, máquina, pendência e hash do snapshot. A retomada após reinício continua a fila; o aceite do provedor fica registrado como `aceita`, sem afirmar que o sistema exibiu ou que o usuário leu.

O envio reserva cada registro atomicamente por um minuto, relê assinatura, pendência e resposta, e faz no máximo três tentativas por cinco minutos. Falhas transitórias são reagendadas; 404/410 removem a assinatura. O TTL no provedor é de cinco minutos. A tag do aviso e o tópico no provedor usam o mesmo id para substituir repetições. Uma falha após o aceite externo e antes da confirmação local pode repetir a requisição: Web Push não garante entrega exatamente uma vez.

Desligar impede novos envios após o cancelamento confirmado. Uma requisição que já saiu ou foi aceita no provedor pode chegar até o fim do TTL; ela não concede autorização, e seu link cancelado não resolve. Sair explicitamente da conta também cancela neste dispositivo; sem rede, o navegador é desassinado e a API limpa o endpoint no próximo 404/410. O vencimento do JWT mantém os avisos, mas exige novo login para ler a decisão. Entregas e assinaturas antigas são removidas após 30 dias. `pushsubscriptionchange`, quando suportado, pede nova associação autenticada ao abrir o Board, sem guardar JWT no worker.

## Navegadores e sistemas

| Ambiente | Condição e limite |
| --- | --- |
| iPhone/iPad | iOS/iPadOS 16.4 ou superior, dev.kit adicionado à Tela de Início e aberto por esse ícone; permissão após toque. Uma aba comum não basta. |
| Safari no Mac | Safari 16/macOS Ventura ou superior, com permissão do usuário. |
| Chrome/Edge no Android e computador | Push API, service worker, contexto seguro e permissão; políticas corporativas, economia de energia e serviço do navegador podem impedir ou atrasar. |
| Firefox no Android e computador | Push API e serviço Mozilla Autopush habilitados, com as mesmas condições de contexto seguro/permissão. |
| Privado, versões antigas ou navegador sem Push API | Pode não permitir assinatura; o controle informa indisponibilidade e o Board online permanece disponível. |

O código detecta recursos presentes, sem prometer suporte só pelo nome do navegador. HTTPS é obrigatório, exceto a exceção de desenvolvimento dos navegadores para localhost. Modo Foco/Não Perturbe, notificações desativadas, navegador encerrado por política, dispositivo sem rede e economia de bateria podem silenciar, atrasar ou descartar o aviso. O Board autenticado é a fonte do estado atual.

Fontes primárias verificadas na implementação: [WebKit — iOS/iPadOS](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/), [Apple — Web Push](https://developer.apple.com/documentation/usernotifications/sending-web-push-notifications-in-web-apps-and-browsers), [MDN — subscribe](https://developer.mozilla.org/en-US/docs/Web/API/PushManager/subscribe) e [biblioteca do transporte](https://github.com/tpeczek/Lib.Net.Http.WebPush).

## Validação

Os testes `PushTests` do Core validam provedores, formato e chaves. Na infraestrutura cobrem migration/snapshot, script SQL Server, associação e cancelamento por dono, deduplicação, pergunta/aprovação, retry, pendência resolvida, snapshot alterado, assinatura expirada, reserva simultânea e transporte real com decifragem independente RFC 8291. `PushApiTests` exercitam as rotas HTTP, autenticação e recusas de corpo. No frontend, `NotificacoesDoBoard.test.tsx`, `boardWorker.test.ts` e os cenários de push de `Board.test.tsx` verificam opt-in, permissão negada, falhas, cancelamento, texto fixo, clique e destino autenticado.

As recapturas e a demonstração funcional pertencem à entrega da task, fora do repositório. Devem distinguir transporte externo real, provedor substituído para teste e evento de navegador injetado por automação; um aceite simulado não comprova recebimento num celular físico.
