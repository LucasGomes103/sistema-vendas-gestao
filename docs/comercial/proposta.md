# Proposta comercial: planos do sistema

Rascunho para o Lucas revisar, feito em **18/11/2026**. Os preços são uma sugestão a partir da pesquisa de
concorrentes abaixo e dos custos de [docs/producao/custos.md](../producao/custos.md). Nada aqui foi combinado com
cliente ainda. O plano técnico para o cliente se cadastrar e configurar tudo sozinho está em
[plano-saas.md](plano-saas.md).

## O que estamos vendendo

Um **vendedor no WhatsApp com IA ligado ao estoque da loja**, mais a gestão do dia a dia, num sistema só:

- Robô com IA que responde no WhatsApp oficial, consulta o estoque de verdade, calcula o frete pelo CEP, separa
  (reserva) os itens por 24 h, pede o endereço e manda a cobrança (Pix copia e cola e link de cartão).
- Vitrine online (catálogo) que mostra só o que tem saldo, com botão para pedir pelo WhatsApp.
- Painel: conversas e funil (kanban), alerta de cliente quente sem atendimento, pedidos, conferência de Pix,
  caixa do dia, entregas com papel de entregador, estoque por lote, métricas com exportação para Excel.

A maioria dos concorrentes vende **só o atendimento** (robô e caixa de entrada) e a loja paga um sistema de gestão
à parte (Bling, Tiny e outros, de R$ 39 a R$ 349 por mês). O nosso diferencial é o robô vender a partir do estoque
e do frete reais, sem integração para montar.

## Planos sugeridos

"Conversa" = cada cliente diferente que escreveu para a loja no mês (o mesmo critério da Yavendió). As respostas do
robô dentro dessa conversa não têm limite.

| | **Essencial** | **Profissional** | **Completo** |
| --- | --- | --- | --- |
| Mensalidade | **R$ 297** | **R$ 497** | **R$ 797** |
| No plano anual (2 meses grátis) | R$ 247/mês | R$ 414/mês | R$ 664/mês |
| Conversas com o robô por mês | 500 | 1.500 | 4.000 |
| Usuários no painel | 2 | 5 | 15 |
| Robô com IA no WhatsApp oficial | ✓ | ✓ | ✓ |
| Vitrine online e catálogo pelo estoque | ✓ | ✓ | ✓ |
| Estoque, pedidos, Pix com a chave da loja, caixa | ✓ | ✓ | ✓ |
| Funil (kanban) e alerta de cliente quente | ✓ | ✓ | ✓ |
| Frete por distância a partir do CEP | — | ✓ | ✓ |
| Link de cartão (Mercado Pago) com baixa automática | — | ✓ | ✓ |
| Entregas e papel de entregador | — | ✓ | ✓ |
| Métricas e relatórios (CSV) | básico | ✓ | ✓ |
| Vitrine no domínio próprio da loja | — | — | ✓ |
| Suporte | e-mail, até 2 dias úteis | WhatsApp, horário comercial | WhatsApp prioritário + 1 reunião por mês |
| Conversas extras | R$ 49 por pacote de +500 | R$ 49 por pacote de +500 | R$ 39 por pacote de +500 |

Por que esses números:

- **Essencial (R$ 297)** fica na faixa de entrada das plataformas com IA (SocialHub a partir de R$ 299, FalaMais
  Start R$ 299 ainda sem IA), mas já com estoque e vitrine.
- **Profissional (R$ 497)** é o plano para empurrar: igual ao FalaMais Essencial com IA (R$ 499) e bem abaixo do
  Yavendió Pro (R$ 999 por 1.500 conversas), entregando também a gestão.
- **Completo (R$ 797)** fica abaixo do Yavendió Business (a partir de R$ 2.499) e perto do SocialHub Business
  (R$ 899), para lojas com mais volume e equipe.
- Nosso custo de IA por conversa é de centavos (≈ R$ 0,05 com 8 respostas, ver custos.md), então o pacote extra de
  R$ 49 por 500 conversas tem margem alta e ainda é barato perto do Yavendió (R$ 1.000 por 1.000 conversas).

## Implantação

A maioria dos concorrentes de pequeno porte **não cobra implantação** (Yavendió, FalaMais, Clint, Leadsales e
SocialHub não listam taxa). Cobrar R$ 1.500 a R$ 3.000, como estimado antes, só se justifica enquanto **o Lucas
faz a configuração à mão**. Sugestão em duas etapas:

| | Agora (implantação feita pelo Lucas) | Depois do autoatendimento ([plano-saas.md](plano-saas.md)) |
| --- | --- | --- |
| Essencial | R$ 990 | **grátis** (o assistente guia o cliente) |
| Profissional | R$ 1.490 | grátis, ou **implantação assistida R$ 490** |
| Completo | R$ 2.490 | grátis, ou implantação assistida R$ 990 |

O que a implantação feita pelo Lucas inclui: acompanhar a verificação da empresa na Meta e a conexão do número,
cadastrar o catálogo inicial (até 100 itens com foto, a partir de planilha), configurar frete, Pix e Mercado Pago,
ajustar o jeito de falar do robô e treinar a equipe (1 h por vídeo). Uma opção comercial é **descontar a implantação
para quem fechar o plano anual**.

## O que é cobrado à parte (pago pela loja direto ao fornecedor)

| Item | Quem cobra | Quanto (nov/2026) | Observação para o contrato |
| --- | --- | --- | --- |
| Mensagens do WhatsApp oficial | Meta, no cartão da própria loja cadastrado na conta do WhatsApp dela | 1.000 respostas de atendimento grátis por mês por número, depois R$ 0,035; aviso de pedido (utilidade) R$ 0,035; marketing R$ 0,32 | Igual aos concorrentes (Yavendió, SleekFlow e Respond.io também repassam). Exemplo: 500 atendimentos de 10 respostas ≈ R$ 140/mês. |
| Taxas do Mercado Pago | Mercado Pago, descontadas de cada venda na conta da loja | cartão na hora 4,98%; em 30 dias 2,95%; Pix pelo link 0,99% | O dinheiro cai direto na conta da loja; o sistema não toca no dinheiro. |
| Pix com a chave da loja | banco da loja | depende do banco (para CNPJ muitos cobram por Pix recebido) | |
| Domínio próprio (opcional) | Registro.br | R$ 40/ano | Só no Completo a vitrine usa o domínio da loja. |
| Número de celular dedicado | operadora | chip pré-pago | Recomendado: o número usado no robô deve ser só da loja. |

A IA (Gemini) **está incluída** em todos os planos, dentro do limite de conversas.

## Conta de margem (exemplo)

Com a hospedagem compartilhada (todas as lojas no mesmo servidor, como no [plano-saas.md](plano-saas.md)):

| | 10 lojas Profissional | 30 lojas (10 de cada plano) |
| --- | --- | --- |
| Receita mensal | R$ 4.970 | R$ 15.910 |
| Infraestrutura compartilhada (Render Standard, Supabase Pro, Cloudflare, monitor, e-mail) | ≈ R$ 450 | ≈ R$ 650 |
| IA (≈ R$ 0,05 por conversa, uso médio de 60% do limite) | ≈ R$ 450 | ≈ R$ 1.800 |
| Taxa da cobrança recorrente (Mercado Pago Assinaturas, cartão ≈ 4,49%) | ≈ R$ 225 | ≈ R$ 715 |
| Imposto (Simples Nacional, estimativa de 6% sobre serviço) | ≈ R$ 300 | ≈ R$ 955 |
| **Sobra antes do tempo de suporte** | **≈ R$ 3.500** | **≈ R$ 11.800** |

Já no modelo de **um servidor por cliente** (como está hoje), cada loja custaria R$ 200 a 240 por mês só de
infraestrutura, e o Essencial a R$ 297 quase não pagaria a conta. Por isso o caminho multi-cliente do plano SaaS
é o que torna esses preços viáveis.

O imposto é estimativa: conferir com um contador o anexo do Simples (software como serviço pode cair no Anexo III
ou V, conforme o fator R).

## Condições sugeridas

- Teste grátis de 7 dias (com o simulador de cliente; o WhatsApp oficial entra quando a loja conectar o número).
- Sem fidelidade no mensal; plano anual com 2 meses grátis (pago à vista ou parcelado no cartão).
- Atraso no pagamento: aviso por e-mail, 7 dias de carência, depois o robô para de responder e o painel fica só
  leitura. Os dados ficam guardados por 90 dias após o cancelamento, com exportação em CSV.
- Reajuste anual pelo IPCA.
- Contrato com termos de uso e acordo de tratamento de dados (LGPD): a loja é a controladora dos dados dos clientes
  dela e nós somos operadores.

## Pesquisa de concorrentes (preços de 2026)

Preços tirados de páginas de fornecedores e comparativos; vários comparativos são escritos por concorrentes.
Confirmar na página de preços de cada um antes de usar em material de venda.

| Produto | Planos e preço por mês | IA | Implantação | Taxas da Meta | Fonte |
| --- | --- | --- | --- | --- | --- |
| Yavendió (agente de vendas com IA) | Pro R$ 999 (1.500 conversas); Business a partir de R$ 2.499 (5.000); Business Plus R$ 5.999 (16.000); +1.000 conversas R$ 1.000 | incluída | não lista; Business inclui implantação guiada | à parte | [yavendio.com](https://yavendio.com/br/chatbot-whatsapp-preco) (preços de 07/11/2026) |
| FalaMais.AI | Start R$ 299 (sem IA); Essencial R$ 499 (IA sem limite, 2 usuários); Expansão R$ 1.190; Scale R$ 2.490 | a partir do Essencial | não lista | pagas direto à Meta | [falamais.ai](https://falamais.ai/blog/chatbot-whatsapp-preco/) (abril/2026) |
| SocialHub | Essencial R$ 299 (3 usuários); Pro R$ 499; Business R$ 899 | chatbot com IA | configuração assistida incluída | não informa | [useclint.com](https://www.useclint.com/blog/melhor-plataforma-whatsapp-vendas-brasil-2026/), [socialhub.pro](https://www.socialhub.pro/blog/umbler-talk-preco-plano-comparativo-socialhub/) |
| Clint | Pro R$ 199 por usuário (R$ 149 no anual); agente de IA cobrado por conversa com teto | 1 agente incluído | não informa | não informa | [useclint.com](https://www.useclint.com/blog/melhor-plataforma-whatsapp-vendas-brasil-2026/) (set/2026) |
| Leadsales | US$ 97 / 133 / 247 (≈ R$ 570 / 780 / 1.450); agente de IA à parte de US$ 87 a 793 | à parte | ativação da API sem custo | saldo pré-pago | [leadsales.io](https://leadsales.io/pt/precos/) |
| SleekFlow | a partir de R$ 569 | incluída | não informa | repassadas | [sleekflow.io](https://sleekflow.io/pt-br/blog/quanto-custa-agente-IA.md) |
| BotConversa, chatPro, Zenvia, Kommo | R$ 97 a 397; R$ 299 a 499; R$ 100 a 700; R$ 85 a 280 por usuário | varia | grandes empresas: R$ 1.000 a 20.000+ | — | [falamais.ai](https://falamais.ai/blog/chatbot-whatsapp-preco/) |
| Sistemas de gestão (sem robô) | Bling R$ 60 a 120; Tiny R$ 59 a 849; Stoqui R$ 39 a 109; Nex R$ 39 a 169 | — | não cobram | — | [uai.com.br](https://www.uai.com.br/?p=174717), [gosmarter.com.br](https://gosmarter.com.br/tiny-erp-para-e-commerce/) |
| Montar sozinho (VPS + Evolution API + n8n + IA) | VPS de US$ 6 a 26 + tokens | — | — | Meta, se usar a API oficial | [horadecodar.com.br](https://horadecodar.com.br/custo-atendimento-ia-whatsapp/) |

Também a observar: a Meta lançou o **Business Agent** dentro do app WhatsApp Business, grátis por enquanto, com
assinatura paga anunciada ([Algoritmo Diário](https://algoritmodiario.com/artigos/ferramentas-ia-atendimento-whatsapp-pme.php),
[Forbes Brasil](https://forbes.com.br/forbes-tech/2026/02/whatsapp-business-lanca-ia-agentica-para-pmes/)). Ele
responde dúvidas, mas não conhece o estoque, o frete nem os pedidos da loja: é mais um motivo para vender o
sistema pela parte de **gestão + venda**, não só pelo robô.

**Conclusão da pesquisa:** a mensalidade de R$ 400 a 700 estimada antes está dentro do mercado (é a faixa do plano
do meio). A implantação de R$ 1.500 a 3.000 fica acima do que o pequeno varejo encontra nos concorrentes, que em
geral não cobram: faz sentido só enquanto a configuração for manual, e deve cair para zero com o autoatendimento.

## Público para começar

O robô, os textos e o cadastro hoje falam de **peixes, espécies e tanques**. Sugestão: começar vendendo para
**lojas de aquarismo e pet shops com peixes**, onde o sistema já encaixa sem mudança e a experiência da ECS Fish
serve de caso de sucesso. Abrir para outros segmentos (plantas, pet em geral, varejo pequeno) exige generalizar os
nomes e o jeito do robô falar, como descrito na fase 6 do [plano-saas.md](plano-saas.md).

## Decisões para o Lucas

1. Os três planos e os preços acima, ou outro desenho (ex.: só dois planos)?
2. Cobrar implantação agora (R$ 990 a 2.490) ou já entrar sem taxa para ganhar os primeiros clientes?
3. Começar pelo nicho de aquarismo ou já generalizar para outros segmentos?
4. Abrir CNPJ próprio para o produto (necessário para a Meta, o Mercado Pago, a nota fiscal e o contrato).
