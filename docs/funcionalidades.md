# Funcionalidades por fase

Registro do que já foi entregue, em que PR, e como usar. Atualizado a cada PR que muda o comportamento do sistema.

O sistema atual é um **projeto teste**: serve para validar o fluxo com a ECS Fish antes da versão de produção definitiva.

## Onde está publicado

| Parte | Endereço | Deploy |
| --- | --- | --- |
| Painel | https://sistema-vendas-gestao.pages.dev/painel | Cloudflare Pages, automático a cada push na `main` |
| Vitrine | https://sistema-vendas-gestao.pages.dev/ecsfish | Cloudflare Pages |
| API + ponte do WhatsApp | https://sistema-vendas-api-0x0l.onrender.com | Render (plano grátis), automático a cada push na `main` |
| Banco | Supabase (PostgreSQL, East US) | migrações aplicadas sozinhas quando a API sobe |

Toda mudança entra por pull request; o merge na `main` publica.

## Roteiro

| Fase | Conteúdo | Situação |
| --- | --- | --- |
| 0 | Gerenciamento de usuários | Pronta |
| 1 | Robô vendedor: frete, reserva, fotos, alerta de cliente quente, kanban ao vivo | Pronta |
| 2 | Pedidos, Pix com chave comum, caixa e entregas | Pronta |
| 3 | Métricas e relatórios | Pronta |
| 4 | Itens restantes do plano em [iniciacao](iniciacao) | A fazer |

## Base (antes das fases)

- Multi-loja: toda entidade tem `loja_id`, com filtro global no EF Core.
- Login por cookie com papéis Dono, Vendedor e Entregador.
- Catálogo de espécies e estoque por tanque (lotes, baixa, transferência, ajuste, quarentena). O estoque é a fonte:
  espécie e tanque podem ser cadastrados na entrada de lote, e catálogo e vitrine só mostram o que tem saldo.
- Vitrine pública em `/{loja}` com o botão "Pedir pelo WhatsApp", que monta o pedido em texto.
- Conversas do WhatsApp pela Meta (Cloud API) ou pela ponte por QR code (`whatsapp-local`), com lista de números
  permitidos em **Configuração → WhatsApp**.
- Robô de atendimento com Gemini (camada grátis) ou, sem chave, por regras.
- Funil (kanban) com regras de tempo e horário comercial.

## Fase 0: usuários

**PR #1, #2.** Menu da conta → **Usuários** (só o dono): criar, editar nome, e-mail e papel, bloquear, definir senha
nova e excluir. Cada um troca a própria senha em "Trocar minha senha". A loja nunca fica sem um dono ativo; quem é
excluído, bloqueado ou tem a senha trocada perde a sessão em até 1 minuto.

## Fase 1: robô vendedor

### Frete por km a partir da loja (PR #10, #11, #12)

Substitui o frete por faixa de CEP do PR #3 (a tabela `zona_entrega` ficou sem uso).

**Configuração → Frete por km**:

- **CEP de onde sai a entrega** (a loja). Ao salvar, o sistema mostra o endereço que achou para ele.
- **Taxa base** e até quantos km ela cobre (ex.: R$ 10 até 2 km).
- **Valor por km** depois disso (ex.: R$ 2/km).
- **Atendo até X km**: acima disso o robô não dá preço, diz que um atendente vai ver a entrega e chama o vendedor.
- Prazo em dias úteis e o botão de ligar e desligar.
- Campo para testar um CEP: mostra o endereço achado, a distância, se veio da rota de carro e o valor.

Conta: `taxa base + (km − km inclusos) × valor por km`, com a distância arredondada para cima de 0,1 em 0,1 km.
Exemplo com R$ 10 até 2 km e R$ 2/km: 10,5 km dá 10 + 8,5 × 2 = **R$ 27,00**.

Como a distância é medida (tudo grátis, sem chave):

1. A **BrasilAPI** dá a rua, o bairro e a cidade do CEP.
2. O **Nominatim (OpenStreetMap)** acha o ponto da rua; se não achar, o do bairro. A coordenada da BrasilAPI só entra
   como último recurso.
3. Um ponto a menos de 300 m do centro da cidade é recusado (é o "chute" dos serviços quando não sabem o CEP). Nesse caso
   o CEP conta como não achado e o robô chama o atendente.
4. A distância é a **rota de carro** do OSRM; se ele não responder, linha reta × 1,3.
5. Cada CEP achado fica guardado na tabela `local_cep`, então só a primeira consulta sai para a internet.

A distância de carro costuma ser diferente da que um aplicativo de mapa mostra, porque a rota escolhida pode ser outra.

**No atendimento**: quando o robô pede o CEP, ele continua na conversa até o cliente mandar. Então responde o frete e
passa a conversa para o vendedor (robô pausado). Enquanto espera, a conversa mostra "O robô está esperando o CEP para
dar o frete" e o botão **Assumir conversa**.

### Reserva de 24 h (PR #5)

- Quando chega um pedido montado na vitrine, ou o cliente pede para reservar, o sistema separa as quantidades por 24 h,
  dos lotes mais antigos primeiro, e o robô confirma o que separou e o que faltou.
- Enquanto a reserva está ativa, essas unidades não aparecem na vitrine nem para o robô.
- Na conversa: **Confirmar venda** (registra a venda, card vai para Pago) ou **Cancelar** (o peixe volta).
- Reserva vencida volta ao estoque sozinha; se o card estava em "Aguardando pagamento", vai para Perdido.

### Fotos das espécies (PR #6)

Botão de câmera no card da espécie (só o dono). A foto é reduzida no navegador (lado maior de 1000 px) e guardada no
próprio banco (tabela `arquivo_foto`), sem custo de armazenamento. Aceita JPG, PNG e WebP até 1,5 MB.

### Cliente quente sem dono e kanban (PR #7)

- "Sem dono" é o cliente que pediu vendedor e que ninguém assumiu nem respondeu.
- **15 min**: o painel avisa de novo. **1 h**: aviso urgente ao dono e mensagem ao cliente ("um atendente fala com você
  em instantes"), uma vez só e dentro da janela de 24 h.
- **Sino** no menu com a contagem, bipe, aviso na tela e notificação do navegador.
- O kanban atualiza a cada 5 s.

### Simulador (PR #4, #9)

Em **Conversas → Simular cliente chegando**, inclusive no painel publicado. O botão **Pedido pelo catálogo** monta o
mesmo texto da vitrine, para testar vitrine → robô → reserva → frete sem WhatsApp. As respostas do simulador nunca saem
pelo WhatsApp. Para desligar: `Simulador__Habilitado=false` no Render.

## Fase 2: pedidos, Pix, caixa e entregas

### Pedidos e Pix com a chave da loja (PR #14)

Tudo sem custo: não há provedor de pagamento. O sistema gera o **Pix copia e cola** (o mesmo código que vira QR code)
com a chave comum da loja, o valor do pedido e o número do pedido como identificador.

**Configuração → Pix da loja** (só o dono): chave (CPF, CNPJ, celular com +55, e-mail ou aleatória), nome de quem
recebe (até 25 letras) e cidade (até 15). Ao salvar, o sistema mostra como leu a chave.

**Na conversa**, a reserva de 24 h ganha o botão **Gerar pedido**:

- Entrega ou retirada na loja. Para entrega, o CEP vem do que o cliente mandou na conversa e o frete por km já vem
  calculado quando a loja usa o frete por km; o vendedor pode corrigir. Endereço é obrigatório para entrega.
- Com a opção marcada, o pedido vai para o cliente em duas mensagens: o resumo (itens, frete, total, até quando o peixe
  fica separado) e o código Pix sozinho, para ele copiar com um toque.
- O card vai para **Aguardando pagamento** e o peixe continua reservado.

Depois disso o pedido aparece no lugar da reserva, com **Confirmar pagamento**, **Enviar Pix** (reenviar), copiar o
código e cancelar.

- Quando o cliente escreve "paguei", "segue o comprovante", "fiz o Pix" ou manda foto ou PDF, o pedido vai para
  **Conferir pagamento**, o card também, e o vendedor recebe o aviso de cliente quente (sino, 15 min, 1 h).
- Não há aviso automático de Pix pago: **a confirmação é sempre manual**. A cobrança já pede o comprovante, e o robô
  responde sozinho: se o cliente só escreveu "paguei", pede o comprovante (print ou PDF); quando chega a foto ou o PDF,
  diz que recebeu e que um atendente vai conferir. Essa resposta sai uma vez só por pedido.
- **Confirmar pagamento**: escolhe Pix, dinheiro ou cartão. O peixe sai do estoque (a reserva vira venda), o card vai
  para **Pago** e, se marcado, o cliente recebe "Pagamento do pedido #N confirmado".
- **Cancelar**: o peixe volta ao estoque e o card vai para Perdido ("desistiu").
- Reserva que vence (24 h), é cancelada ou trocada por outra cancela o pedido dela junto.
- Fora da janela de 24 h do WhatsApp a mensagem não sai (regra da Meta); o painel avisa e o código pode ser copiado.

Com pedido, a venda é confirmada pelo pagamento do pedido; o antigo "Confirmar venda" da reserva deu lugar ao
"Gerar pedido".

**Menu → Pedidos**: contagem do que está para conferir e aguardando, valor em aberto e a lista com filtros
(em aberto, pagos, cancelados, todos). Atualiza a cada 10 s.

### Caixa do dia (PR #15)

**Menu → Caixa** (só o dono).

- Todo pedido pago entra sozinho como **Venda**, no dia do pagamento (fuso da loja) e na forma em que foi pago. Os
  pedidos pagos antes do caixa existir entraram na migração.
- **Despesa** (compra de peixes, ração e insumos, energia e água, aluguel, embalagem e entrega, manutenção, outras) e
  **Entrada** (venda no balcão, outras), com Pix, dinheiro ou cartão.
- Totais do dia: entradas, saídas, saldo, dinheiro que deveria estar na gaveta e saldo por forma.
- **Fechar o caixa do dia**: informa o dinheiro contado e o sistema mostra sobra ou falta. Dia fechado não aceita
  lançamento manual nem exclusão; venda paga continua entrando e o painel avisa. Dá para **Reabrir**.
- Setas para ver os dias anteriores.

### Entregas e retiradas

**Menu → Entregas** (equipe e entregador). Todo pedido pago entra na agenda como "a agendar" (os pagos antes da
agenda existir entraram na migração).

- Grupos: a agendar, atrasadas, hoje, próximos dias e as feitas nos últimos 7 dias. Atualiza a cada 15 s.
- **Agendar / Remarcar**: dia, horário (texto livre, com atalhos como "14h às 18h") e, para entrega, quem leva.
  O cliente recebe "A entrega do pedido #N ficou para..." no WhatsApp.
- **Saiu para entrega**: o cliente recebe o aviso com a dica de aclimatação (saquinho boiando 20 min).
- **Entregue / Retirado**: o card vai para **Entregue** e o cliente recebe o agradecimento.
- Endereço abre no Google Maps; telefone liga direto.
- Fora da janela de 24 h o aviso ao cliente não sai e o painel diz isso.

**Entregador**: entra direto em Entregas e só vê as entregas (não retiradas) de hoje ou atrasadas que são dele ou
estão sem ninguém. Pode marcar saída e entrega; não agenda. Não vê conversas nem os alertas de cliente quente.

No celular a barra inferior tem Funil, Conversas, Pedidos, Entregas e Estoque; Caixa e Catálogo ficam no menu da conta.

## Fase 3: métricas e relatórios

**Menu → Métricas** (só o dono). Período: 7 dias, 30 dias, este mês, mês anterior, 90 dias ou datas escolhidas (até
um ano). Os números comparam com o período anterior de mesmo tamanho.

- **Resumo**: conversas novas, vendas pagas, conversão (vendas ÷ conversas), receita, ticket médio, peixes vendidos e
  pedidos cancelados.
- **Receita no tempo**: por dia até 31 dias, por semana até ~4 meses, por mês acima disso. Passe o mouse na coluna para
  ver valor e pedidos.
- **Funil das conversas novas**: quantas chegaram a robô atendeu, quente, recebeu o Pix, pago e entregue (sai do
  histórico de etapas dos cards).
- **Motivos de perda**, **de onde vieram as vendas** (pedido pela vitrine, recuperada por follow-up, atendimento
  direto) e **formas de pagamento**.
- **Mais vendidos e margem por espécie**: receita, custo e margem. O custo vem do lote de onde o peixe saiu; se algum
  lote não tem custo cadastrado, a margem fica em branco.
- **Giro do estoque por tanque**: saldo no início, entradas, vendidos, mortes, giro (vendidos ÷ estoque médio) e o
  saldo de hoje.
- **Mortalidade por fornecedor**: dos lotes recebidos no período, quanto morreu até hoje, e as mortes por espécie.
- **Caixa do período** (entradas, saídas, despesas por categoria, sobra ou falta na gaveta) e **entregas** (feitas,
  retiradas, tempo médio do pagamento à entrega, em aberto e atrasadas).

Conversas e pedidos do simulador ficam de fora; a opção **Incluir simulações** traz de volta. Estoque e caixa sempre
mostram tudo o que foi lançado.

**Exportar** baixa planilhas CSV que abrem direto no Excel (separador ";", vírgula decimal): pedidos pagos, vendas por
período, espécies, estoque por tanque, mortalidade por fornecedor e lançamentos do caixa. A API é
`GET /api/v1/metricas?de=&ate=` e `GET /api/v1/metricas/exportar/{relatorio}?de=&ate=`.

Ficou de fora: custo do WhatsApp (a ponte por QR não cobra; volta quando usar a API oficial da Meta).

## Cartão pelo Mercado Pago

Opcional e sem mensalidade: o Mercado Pago cobra uma taxa por venda no cartão. Liga sozinho quando a variável
`MercadoPago__AccessToken` existe no Render; sem ela, tudo segue só com Pix, como antes.

- Ao criar o pedido (ou tocar em **Enviar cobrança**), o sistema cria uma order do **Checkout Pro** (API de Orders,
  `POST /v1/orders`) com o total do pedido e manda o `checkout_url`. A cobrança no WhatsApp passa a oferecer as duas formas: o Pix copia e cola (comprovante e confirmação
  manual, sem mudança) e o link do cartão de crédito (até 12 parcelas) ou débito. Boleto e o Pix do Mercado Pago
  ficam fora do link: o Pix continua pela chave da loja, sem taxa.
- O link vence junto com a reserva de 24 h. **Reenviar cobrança** sempre gera um link novo (o do Mercado Pago não aceita outra tentativa
  depois de uma que falhou). Se um link antigo for pago depois do pedido já estar pago, o vendedor é chamado para estornar.
- Quando o cliente paga pelo link, o Mercado Pago avisa a API em `POST /api/v1/webhooks/mercadopago` (evento **Order**,
  cadastrado no painel do Mercado Pago em Configuração de webhooks). A API busca a order com o próprio token (o aviso
  sozinho não confirma nada) e, com status `processed` e o valor certo,
  confirma o pedido sozinho: o peixe sai do estoque, entra no caixa como **Cartão**, o card vai para **Pago**, entra
  na agenda de entregas e o cliente recebe a confirmação. Aviso repetido é ignorado.
- Se o pagamento chega depois que a reserva venceu (ou o valor não bate), o estoque não é mexido: o pedido vai para
  **Conferir pagamento** e o vendedor é chamado para separar de novo ou estornar no Mercado Pago.
- No pedido, o ícone de cartão copia o link; pedido pago assim mostra "Mercado Pago, confirmado sozinho".
- **Configuração → Cartão (Mercado Pago)** mostra se está ligado, se está em modo de teste e o endereço do aviso.
- O robô (Gemini) passa a dizer que a loja aceita Pix e cartão, sem mandar link por conta própria.

**Por loja (SaaS fase 1):** cada loja usa o Mercado Pago dela. O dono cola o Access Token e a assinatura secreta
em **Configuração → Cartão**; o servidor guarda cifrado (tabela `credencial_loja`, chave `Plataforma__ChaveMestra`) e
nunca devolve o token, só o final dele. O aviso de pagamento acha a loja pelo pedido que guardou a order e busca a order
com o token dessa loja; order que aponta para pedido de outra loja é ignorada. As variáveis abaixo valem só para a
loja original (a mais antiga do banco, a ECS Fish), que segue funcionando sem mudar nada. Uma loja nova sem token
próprio fica sem cartão, nunca com o token do servidor.

Variáveis no Render (só a loja original): `MercadoPago__AccessToken`, `MercadoPago__WebhookSecret` (assinatura secreta dos
avisos; com ela, aviso com assinatura errada é recusado), `MercadoPago__ModoTeste=true` enquanto usar a conta de
teste (só muda o selo no painel). O endereço público vem do `RENDER_EXTERNAL_URL`, que o Render preenche sozinho
(ou `MercadoPago__UrlPublicaApi`); serve só para o painel mostrar a URL a cadastrar no Mercado Pago.

## WhatsApp oficial por loja (SaaS fase 1)

- **Configuração → WhatsApp oficial (Meta)**, só o dono: Phone Number ID e token de acesso da conta do WhatsApp da
  loja. Antes de salvar, o sistema pergunta à Meta se o token enxerga o número; o token fica cifrado em
  `credencial_loja` e o painel só mostra o final dele.
- Um número só pode estar ligado a uma loja (índice único em `loja.phone_number_id`), senão as mensagens dele
  iriam para a loja errada.
- Envio: a loja da ponte por QR (`WhatsApp__Local__LojaSlug`, quando `WhatsApp__Provedor=Local`) continua saindo
  pela ponte; todas as outras saem pela Cloud API com o token delas, ou ficam simuladas sem token. A ponte da ECS Fish
  nunca envia mensagem de outra loja. `WhatsApp__AccessToken` no Render vale só para a loja original.
- Na fase 2 isto vira o botão "Conectar WhatsApp" (Embedded Signup da Meta).

## Cadastro de loja pelo site (SaaS fase 1)

- `/painel/cadastro` (link "Cadastre a sua loja" na tela de entrar): nome da loja, endereço da vitrine (sugerido a
  partir do nome e conferido na hora), tipo de loja, nome do dono, e-mail, senha e aceite dos termos.
- A loja nasce **aguardando confirmação**. O dono recebe um e-mail com um link que vale 48 h; ao abrir, a loja fica
  ativa e ele já entra no painel. Antes disso o login responde "Confirme o seu e-mail", com o botão de reenviar o link.
- Endereço da vitrine único e com palavras reservadas (painel, api, admin, termos...); e-mail único entre todas as lojas.
- A loja nova começa vazia: não vê nada das outras (coberto pelos testes de isolamento).
- E-mail por SMTP (Gmail com senha de app, grátis). Sem SMTP configurado, o link vai para o log do Render.

## Administrador da plataforma (SaaS fase 1)

- Quem tem o e-mail em `Plataforma__Admins` (no Render, separados por vírgula) vê **Lojas da plataforma** no menu
  da conta (`/painel/admin`).
- Lista todas as lojas com status, dono, tipo, e números dos últimos 30 dias (conversas, pedidos, vendido), produtos,
  usuários, última mensagem e se WhatsApp e Mercado Pago são próprios. Só números: não abre conversa nem cliente.
- **Suspender** (com motivo): ninguém da loja entra, quem está dentro sai em até 1 minuto, a vitrine sai do ar e as
  mensagens que chegam são ignoradas. **Reativar** devolve tudo, sem perder nada. **Ativar sem e-mail** libera uma
  loja cujo e-mail de confirmação não chegou. O administrador não suspende a própria loja.
- **Editar**: nome, endereço da vitrine (o link antigo para de funcionar), tipo de loja e nome/e-mail de login do dono
  (endereço e e-mail não podem repetir os de outra loja ou usuário).
- **Excluir**: apaga a loja e todas as linhas dela (produtos, estoque, conversas, clientes, pedidos, caixa, usuários,
  credenciais), numa transação só. Pede para digitar o endereço da loja. Não aparece para a própria loja do
  administrador nem para a loja original (a que usa as chaves do servidor, a ECS Fish), e a API também recusa.

## Equipe: quem atendeu e quem vendeu

- **Responsável pela conversa:** quem assume ou responde primeiro fica com a conversa e segue o atendimento. Todos veem
  todas as conversas, mas os outros vendedores só acompanham (o campo de mensagem fica bloqueado e aparece "Conversa com
  Ana"). O dono pode responder em qualquer conversa sem tirar o responsável.
- **Transferir (só o dono):** menu ⋮ da conversa → Transferir atendimento. Passa para outra pessoa (os pedidos em aberto
  vão junto) ou deixa sem responsável, e a conversa volta a ser cliente quente sem dono. "Assumir eu" passa para o dono.
  Devolver ao robô também tira o responsável. Quem é bloqueado, excluído ou vira entregador deixa as conversas livres.
- **Autor de cada mensagem:** as mensagens da equipe mostram o nome de quem enviou. Na lista, filtros Todas, Minhas e
  Sem responsável; no funil, o card mostra com quem está.
- **Vendedor do pedido:** o pedido fica com o responsável da conversa (ou com quem criou). O card do pedido mostra o
  vendedor, quem fez o pedido e quem confirmou o pagamento.
- **Métricas:** quadro "Por vendedor" (conversas atendidas, mensagens, vendas pagas, receita, ticket médio, cancelados e
  conversas abertas com a pessoa agora), filtro por vendedor no topo e o relatório CSV "Desempenho por vendedor". Com o
  filtro, vendas, funil e perdas mostram só o que é da pessoa; estoque, caixa e entregas continuam da loja toda.
- Na migração, pedidos antigos ficaram com quem os criou e conversas pausadas com quem respondeu por último.

## Pendências conhecidas

Para a ida à produção (custos, variáveis, segurança, LGPD), ver [producao/checklist.md](producao/checklist.md).

- Fase 2 sem: aviso automático de Pix pago (dá para fazer pelo Mercado Pago, se quiser, com taxa por Pix), lembrete de pagamento em 12 h e mensagens fora da
  janela de 24 h (exigem modelos aprovados na Meta), alerta de entrega atrasada há 48 h e QR code na tela.

- Trocar a senha do banco do Supabase (vazou no chat) e atualizar `ConnectionStrings__Banco` no Render.
- Gerar tokens novos da Meta quando for usá-la.
- Trocar o login `dono@demo.local` em produção (pela tela Usuários).
- O Supabase grátis pausa após dias sem uso; recomendado um monitor externo chamando `/health` a cada 5 min.

## Qualquer segmento (SaaS fase 4)

- O **tipo de loja** (escolhido no cadastro ou editado pelo administrador) define as palavras do sistema. Se o tipo
  fala de aquário ou peixe, a loja é de **aquarismo** e tudo continua como na ECS Fish (espécie, tanque, quarentena,
  nome científico, dica do saquinho na entrega). Qualquer outro tipo usa **produto**, **local**, "marca ou modelo" e
  "separado (não vende)".
- As lojas criadas antes do cadastro público (ECS Fish e a demo) ficaram com o tipo "Aquarismo".
- O **robô** se apresenta conforme o tipo ("uma loja do ramo de roupas"), busca produtos pelo nome ou marca e não fala
  de peixe nem de quarentena em loja que não é de aquário. As mensagens de pedido, pagamento e entrega usam termos
  neutros ("seu pedido fica separado até..."); a dica de aclimatar o peixe só vai para loja de aquário.
- Loja que não é de aquário começa com o follow-up "ficou alguma dúvida?" (sem peixe) e com "Compra de mercadoria" no
  lugar de "Compra de peixes" nas categorias do caixa.
- Por dentro (banco e código) o produto continua se chamando espécie e o local, tanque: nada foi renomeado no banco,
  então os dados da ECS Fish não mudam.
- **Painel e vitrine** usam as palavras da loja: "Novo produto", "Estoque por local", "Buscar produto", ícones de
  caixa e prateleira em vez de peixe e água. Tamanho em cm, litros e dias de quarentena só aparecem no aquarismo;
  "mortes" vira "perdas" nas métricas e no CSV.
- **Cadastro** sugere o tipo de loja (Aquarismo, Pet shop, Roupas e acessórios, Autopeças...) e o campo passa a ser
  obrigatório.
- **Detalhes do produto (opcional):** até 8 campos livres por produto (ex.: Tamanho: M, Cor: preto, Marca: Bosch,
  Aplicação: Gol 2015). O cadastro sugere os campos do ramo da loja (roupas: tamanho, cor, tecido; autopeças: marca,
  aplicação, código; pet: marca, peso, porte...). Aparecem no catálogo e na vitrine, entram na busca da vitrine e o
  robô recebe os detalhes ao buscar o produto (e acha o produto pelo valor, ex.: "tem em preto?").

