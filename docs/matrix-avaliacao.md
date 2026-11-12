# Avaliação da Matrix e compatibilidade

O mesmo recibo imutável do host é consumido pelo serviço, desktop, devcli e Board. O contrato está em `api/src/DevKitPage.Contracts/V1/Matrix.cs`; produtor e conferência de notas ficam no repositório git.kit, documentados em `Documentacao/Matrix/avaliacao-e-cobertura.md`.

O heartbeat anuncia `matrix-avaliacao-v1`. Clientes atuais enviam `X-DevKit-Matrix-Avaliacao: 1` nas rotas `/api/matrix`. Sem o cabeçalho, a API conserva o formato matrix-v1, omite o stream `evento` do catálogo e os campos novos dos documentos retornados. A edição de uma definição sem `coberturas` preserva as associações já persistidas; use uma lista vazia para removê-las explicitamente.

O host registra documentos do tipo `evento`, com ID derivado do evento e da matriz. O recibo exige máquina do usuário, sessão avaliada no retrato dessa máquina, ACL de escrita na demanda e revisão existente. Não pode ser substituído. Avaliações novas apontam esse recibo e devem conservar avaliador, sessão, responsáveis, células, revisões, SHAs e manifesto da entrega. O registro confere novamente se obrigações ou atribuições do recorte mudaram durante a leitura; falha conserva o histórico e informa o motivo. Registros antigos continuam consultáveis e não completam a cobertura sem vínculo verificável.

As áreas existentes de avaliação/evidências exibem evento, avaliador, revisão efetiva, SHAs e motivo da obsolescência, também no celular. O implementador registra evidência separadamente. Uma nova evidência não substitui a avaliação, e uma revisão de outra célula não invalida resultados independentes. A reintrodução de uma definição antiga não recupera uma avaliação que atravessou uma mudança material.

`matriz.coberturas` associa relação do mapa a uma célula com dono explícito. O fechamento existente no host confere coordenadas analisadas, comprovação dos responsáveis, não aplicação justificada e relações pertinentes. A nota global permanece distinta do resultado por obrigação. Não há nova aprovação, publicação automática, regressão no avaliador ou nota obrigatória em fluxo sem objetivo.

Não há nova tabela nem script SQL no Page: documentos opcionais e recibos usam o journal existente. A ativação exige API/Board e host compatíveis, na etapa de publicação do projeto, sem envio automático para produção neste item.

Validação focada: `AvaliacaoMatrixApiTests`, `MatrixApiTests`, testes dos componentes Matrix e o cenário Playwright da avaliação em 1440 e 390 pixels, sempre com SQLite temporário e dados fictícios.
