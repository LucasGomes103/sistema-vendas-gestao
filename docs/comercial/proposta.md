# Proposta comercial: planos do sistema

Rascunho para o Lucas revisar. Primeira versão em 18/11/2026; **revisada em 19/11/2026** com as decisões do Lucas:

- O caminho é o **SaaS com autoatendimento** ([plano-saas.md](plano-saas.md)): o cliente escolhe o plano, paga e
  configura sozinho.
- O sistema serve **qualquer segmento** de varejo pequeno, não só aquarismo.
- **Hospedagem, banco, IA, mapas do frete e tudo o que é da plataforma fica na conta do Lucas** e o custo já entra
  na mensalidade. **WhatsApp, Mercado Pago, Pix e o que é do negócio vêm do cliente.**

Os preços são sugestão a partir da pesquisa de concorrentes no fim deste documento e dos custos de
[docs/producao/custos.md](../producao/custos.md). Nada foi combinado com cliente ainda.

## O que estamos vendendo

Um **vendedor no WhatsApp com IA ligado ao estoque da loja**, mais a gestão do dia a dia, num sistema só:

- Robô com IA que responde no WhatsApp oficial, consulta o estoque de verdade, calcula o frete pelo CEP, reserva os
  itens por 24 h, pede o endereço e manda a cobrança (Pix copia e cola e link de cartão).
- Vitrine online (catálogo) que mostra só o que tem saldo, com botão para pedir pelo WhatsApp.
- Painel: conversas e funil (kanban), alerta de cliente quente sem atendimento, pedidos, conferência de Pix, caixa
  do dia, entregas com papel de entregador, estoque por lote, métricas com exportação para Excel.

A maioria dos concorrentes vende **só o atendimento** e a loja paga um sistema de gestão à parte (Bling, Tiny e
outros, de R$ 39 a R$ 349 por mês). O nosso diferencial é o robô vender a partir do estoque e do frete reais, sem
integração para montar.

## Quem fornece o quê

| Fica na conta do Lucas (incluído na mensalidade) | Vem do cliente (conectado no assistente de configuração) |
| --- | --- |
| Servidor da API (Render) | **Conta do WhatsApp oficial** e o número da loja, conectados pelo botão "Conectar WhatsApp" da Meta. O cliente cadastra o cartão dele na conta do WhatsApp e a Meta cobra as mensagens direto dele. |
| Banco de dados e backups (Supabase) | **Conta do Mercado Pago**, conectada pelo botão "Conectar Mercado Pago". O dinheiro das vendas cai direto na conta do cliente, e as taxas também são descontadas lá. |
| Painel, vitrine e endereço `sistema/<loja>` (Cloudflare) | **Chave Pix** da loja (banco do cliente). |
| IA do robô (Gemini) | Catálogo, fotos, preços, horário e endereço da loja. |
| Mapas e cálculo do frete por CEP | CNPJ ou CPF da loja, exigido pela Meta e pelo Mercado Pago. |
| E-mails do sistema (cadastro, cobrança, avisos) | Domínio próprio, só se quiser a vitrine no endereço da loja (plano Completo). |
| Monitoramento, atualizações e suporte | |

O cliente nunca copia chave nem token: os dois botões abrem a tela oficial da Meta e do Mercado Pago, ele entra com a
própria conta e autoriza.

## Planos

"Conversa" = cada cliente diferente que escreveu para a loja no mês (o mesmo critério da Yavendió). As respostas do
robô dentro dessa conversa não têm limite.

| | **Essencial** | **Profissional** | **Completo** |
| --- | --- | --- | --- |
| Mensalidade | **R$ 297** | **R$ 497** | **R$ 797** |
| No plano anual (2 meses grátis) | R$ 247/mês | R$ 414/mês | R$ 664/mês |
| Conversas com o robô por mês (IA incluída) | 500 | 1.500 | 4.000 |
| Usuários no painel | 2 | 5 | 15 |
| Produtos com foto | até 300 | até 2.000 | sem limite |
| Robô com IA no WhatsApp oficial | ✓ | ✓ | ✓ |
| Vitrine online e catálogo pelo estoque | ✓ | ✓ | ✓ |
| Estoque, pedidos, Pix com a chave da loja, caixa | ✓ | ✓ | ✓ |
| Funil (kanban) e alerta de cliente quente | ✓ | ✓ | ✓ |
| Frete por distância a partir do CEP (mapas incluídos) | ✓ | ✓ | ✓ |
| Link de cartão (Mercado Pago) com baixa automática | — | ✓ | ✓ |
| Entregas e papel de entregador | — | ✓ | ✓ |
| Métricas e relatórios (CSV) | básico | ✓ | ✓ |
| Vitrine no domínio próprio da loja | — | — | ✓ |
| Suporte | e-mail, até 2 dias úteis | WhatsApp, horário comercial | WhatsApp prioritário + 1 reunião por mês |
| Conversas extras | R$ 49 por +500 | R$ 49 por +500 | R$ 39 por +500 |
| Implantação | **grátis** (assistente) | grátis, ou assistida **R$ 490** | grátis, ou assistida **R$ 990** |

O frete por CEP passou para todos os planos porque a maioria dos segmentos entrega, e o custo dos mapas é baixo
(ver a conta abaixo).

### Quanto custa cada plano para o Lucas

Conta no pior caso, com a loja usando **todas** as conversas do plano e o Lucas com 10 a 30 lojas no ar. Hospedagem
rateada a partir do [plano-saas.md](plano-saas.md) (≈ R$ 450 a 700 por mês no total para 10 a 50 lojas, com e-mail e
monitor; os mapas vão numa linha à parte). IA a ≈ R$ 0,05 por conversa (8 respostas de ≈ R$ 0,006, ver custos.md).

| Custo por loja, por mês | Essencial | Profissional | Completo |
| --- | --- | --- | --- |
| Hospedagem, banco, front, monitor e e-mail (rateio) | R$ 30 | R$ 30 | R$ 40 |
| IA (Gemini), com o limite todo usado | R$ 25 | R$ 75 | R$ 200 |
| Mapas do frete (estimativa, com cache de CEP) | R$ 5 | R$ 10 | R$ 20 |
| Taxa da cobrança da mensalidade (Mercado Pago Assinaturas, cartão ≈ 4,49%) | R$ 13 | R$ 22 | R$ 36 |
| Imposto (Simples Nacional, estimativa de 6%) | R$ 18 | R$ 30 | R$ 48 |
| **Custo total** | **≈ R$ 91** | **≈ R$ 167** | **≈ R$ 344** |
| **Sobra por loja (antes do tempo de suporte)** | **≈ R$ 206** | **≈ R$ 330** | **≈ R$ 453** |

Na média, uma loja usa bem menos que o limite, então a sobra real tende a ser maior. O pacote extra de 500 conversas
custa ≈ R$ 25 de IA e é vendido por R$ 49.

Com **10 lojas de cada plano** (30 lojas): receita de R$ 15.910 e custo de ≈ R$ 6.000 no pior caso, sobrando
≈ R$ 9.900 por mês. Com uso médio de 60% das conversas, a sobra passa de R$ 11.000.

Pontos a confirmar antes de fechar o preço: o custo dos mapas (o serviço pago ainda não foi escolhido; os públicos
de hoje não aguentam muitas lojas) e o anexo do Simples com um contador (software como serviço pode cair no Anexo
III ou V, conforme o fator R).

### Proteções na conta do Lucas

Como a IA e os mapas ficam na conta do Lucas, o custo cresce com o uso dos clientes. Por isso o sistema precisa:

- Contar conversas por loja e parar o robô (passando para atendimento humano) quando o limite acabar, com aviso
  para comprar o pacote extra.
- Limitar respostas por conversa (ex.: 40) para um cliente insistente ou um robô de spam não gastar IA à toa.
- Alerta de gasto no Google Cloud (orçamento do projeto do Gemini) e no Supabase.

## O que o cliente paga à parte, direto ao fornecedor

| Item | Quem cobra | Quanto (nov/2026) | Observação para o contrato |
| --- | --- | --- | --- |
| Mensagens do WhatsApp oficial | Meta, no cartão do cliente cadastrado na conta do WhatsApp dele | 1.000 respostas de atendimento grátis por mês por número, depois R$ 0,035; aviso de pedido (utilidade) R$ 0,035; marketing R$ 0,32 | Igual aos concorrentes (Yavendió, SleekFlow e Respond.io também repassam). Exemplo: 500 atendimentos de 10 respostas ≈ R$ 140/mês. A maioria das lojas pequenas fica nas 1.000 grátis. |
| Taxas do Mercado Pago | Mercado Pago, descontadas de cada venda na conta do cliente | cartão na hora 4,98%; em 30 dias 2,95%; Pix pelo link 0,99% | O sistema não toca no dinheiro. |
| Pix com a chave da loja | banco do cliente | depende do banco (para CNPJ muitos cobram por Pix recebido) | |
| Domínio próprio (opcional, Completo) | Registro.br | R$ 40/ano | |
| Número de celular dedicado | operadora | chip pré-pago | Recomendado: o número do robô deve ser só da loja. |

## Implantação

Com o autoatendimento, a implantação é **grátis**: o assistente guia o cliente em cada passo. Isso segue o mercado:
Yavendió, FalaMais, Clint, Leadsales e SocialHub não listam taxa de implantação.

A **implantação assistida** é opcional, para quem não quer fazer sozinho: uma chamada de vídeo em que o Lucas faz o
assistente junto com o cliente, importa o catálogo da planilha dele (até 300 itens no Profissional, sem limite no
Completo), ajusta o jeito de falar do robô e treina a equipe por 1 h. Pode sair de graça para quem fechar o anual.

Enquanto o autoatendimento não fica pronto, as primeiras lojas (piloto) entram com a configuração feita pelo Lucas,
com desconto na mensalidade em troca de opinião e depoimento.

## Condições sugeridas

- Teste grátis de 7 dias com o simulador de cliente (o WhatsApp oficial entra quando a loja conectar o número).
- Sem fidelidade no mensal; anual com 2 meses grátis (à vista ou parcelado no cartão).
- Atraso no pagamento: aviso por e-mail, 7 dias de carência, depois o robô para de responder e o painel fica só
  leitura. Os dados ficam guardados por 90 dias após o cancelamento, com exportação em CSV.
- Reajuste anual pelo IPCA.
- Contrato com termos de uso e acordo de tratamento de dados (LGPD): a loja é a controladora dos dados dos clientes
  dela e o Lucas é o operador.

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
responde dúvidas, mas não conhece o estoque, o frete nem os pedidos da loja: é mais um motivo para vender o sistema
pela parte de **gestão + venda**, não só pelo robô.

**Conclusão da pesquisa:** a faixa de R$ 297 a 797 com IA incluída fica no meio do mercado (FalaMais R$ 499,
SocialHub R$ 299 a 899) e bem abaixo do Yavendió (R$ 999 por 1.500 conversas), entregando também a gestão. Taxa de
implantação não é praxe no pequeno varejo, por isso ela sai no autoatendimento.

## Segmentos

O sistema vai ser **generalizado para qualquer varejo pequeno que vende pelo WhatsApp** (fase 4 do
[plano-saas.md](plano-saas.md)): no cadastro o cliente escolhe o tipo de loja (ex.: pet e aquarismo, plantas e
jardinagem, moda, cosméticos, alimentos, peças) e o painel e o robô passam a falar "produto" em vez de "peixe" e
"espécie". A ECS Fish continua como primeiro caso de sucesso.

## Decisões para o Lucas

1. Os três planos e os preços acima, ou outro desenho?
2. Limites de produtos por plano (300, 2.000, sem limite) fazem sentido para os segmentos que você quer atingir?
3. Abrir CNPJ próprio para o produto (necessário para a Meta, o Mercado Pago, a nota fiscal e o contrato).
