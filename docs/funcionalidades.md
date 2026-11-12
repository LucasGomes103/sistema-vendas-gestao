# Funcionalidades por fase

Registro do que já foi entregue, em que PR, e como usar. Atualizado a cada PR que muda o comportamento do sistema.

O sistema atual é um **projeto teste**: serve para validar o fluxo com a ECS Fish antes da versão de produção definitiva.

## Onde está publicado

| Parte | Endereço | Deploy |
| --- | --- | --- |
| Painel | https://subtle-faun-9fb444.netlify.app/painel | Netlify, automático a cada push na `main` |
| Vitrine | https://subtle-faun-9fb444.netlify.app/ecsfish | Netlify |
| API + ponte do WhatsApp | https://sistema-vendas-api-0x0l.onrender.com | Render (plano grátis), automático a cada push na `main` |
| Banco | Supabase (PostgreSQL, East US) | migrações aplicadas sozinhas quando a API sobe |

Toda mudança entra por pull request; o merge na `main` publica.

## Roteiro

| Fase | Conteúdo | Situação |
| --- | --- | --- |
| 0 | Gerenciamento de usuários | Pronta |
| 1 | Robô vendedor: frete, reserva, fotos, alerta de cliente quente, kanban ao vivo | Pronta |
| 2 | Pedidos, Pix com chave comum, caixa e entregas | Em andamento |
| 3 | Métricas e relatórios | A fazer |
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

### Pedidos e Pix com a chave da loja

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
- **Confirmar pagamento**: escolhe Pix, dinheiro ou cartão. O peixe sai do estoque (a reserva vira venda), o card vai
  para **Pago** e, se marcado, o cliente recebe "Pagamento do pedido #N confirmado".
- **Cancelar**: o peixe volta ao estoque e o card vai para Perdido ("desistiu").
- Reserva que vence (24 h), é cancelada ou trocada por outra cancela o pedido dela junto.
- Fora da janela de 24 h do WhatsApp a mensagem não sai (regra da Meta); o painel avisa e o código pode ser copiado.

Com pedido, a venda é confirmada pelo pagamento do pedido; o antigo "Confirmar venda" da reserva deu lugar ao
"Gerar pedido".

**Menu → Pedidos**: contagem do que está para conferir e aguardando, valor em aberto e a lista com filtros
(em aberto, pagos, cancelados, todos). Atualiza a cada 10 s.

## Pendências conhecidas

- Trocar a senha do banco do Supabase (vazou no chat) e atualizar `ConnectionStrings__Banco` no Render.
- Gerar tokens novos da Meta quando for usá-la.
- Trocar o login `dono@demo.local` em produção (pela tela Usuários).
- O Supabase grátis pausa após dias sem uso; recomendado um monitor externo chamando `/health` a cada 5 min.
