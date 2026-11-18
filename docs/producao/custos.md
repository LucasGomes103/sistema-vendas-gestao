# Custos mensais para produção

Levantamento feito em **18/11/2026** para levar o sistema da versão teste (tudo grátis) para uma loja em operação.
Preços mudam: confira nos links antes de contratar. Câmbio usado: **US$ 1 = R$ 5,56** (fechamento de 11/11/2026,
[Brasil 61](https://brasil61.com/n/dolar-fecha-cotado-a-r-5-56-com-mercado-atento-a-inflacao-dos-estados-unidos-bras2617456.amp))
mais IOF de cartão em compra internacional, o que dá **cerca de R$ 5,75 por dólar** no que é cobrado em dólar.

## Resumo

| | Enxuta | Recomendada |
| --- | --- | --- |
| Custo fixo + uso (sem taxas de venda) | **≈ R$ 200 a 240/mês** | **≈ R$ 500 a 680/mês** |
| Taxas do Mercado Pago | % sobre o que vender no cartão ou Pix pelo link | igual |
| Pagamento único | domínio R$ 40/ano | domínio R$ 40/ano |

A enxuta já resolve os dois problemas da versão teste: **nada dorme e nada pausa** (API paga no Render e banco pago
no Supabase) e o WhatsApp passa a ser o **oficial da Meta**. A recomendada dá folga de memória, cota do Cloudflare sem
limite diário, e-mail profissional e monitor com alerta a cada minuto.

## Item por item

| Item | Hoje (teste) | Enxuta | Recomendada | Observação |
| --- | --- | --- | --- | --- |
| API (Render) | Free, dorme | Starter US$ 7 (512 MB) ≈ **R$ 40** | Standard US$ 25 (1 CPU, 2 GB) ≈ **R$ 144** | Workspace Hobby fica US$ 0 (inclui 5 GB de tráfego, excedente US$ 0,15/GB). Sem a ponte do QR, a API cabe na Starter. |
| Banco (Supabase) | Free, pausa após 7 dias parado | Pro US$ 25 ≈ **R$ 144** | Pro US$ 25 ≈ **R$ 144** (+ Small US$ 5 se precisar de 2 GB de RAM) | Pro inclui crédito para a instância Micro, 8 GB de banco, 250 GB de tráfego e backup diário guardado por 7 dias. Não pausa. |
| Front (Cloudflare Pages) | Free | **R$ 0** | Workers Paid US$ 5 ≈ **R$ 29** | No grátis, as Functions (que repassam `/api` ao Render) têm 100 mil pedidos por dia. O kanban consulta a cada 5 s: cada aba aberta o dia todo gasta ~17 mil/dia. Com 5 ou mais abas abertas, a cota acaba. O pago dá 10 milhões/mês. |
| Domínio .com.br (Registro.br) | sem (pages.dev) | R$ 40/ano ≈ **R$ 3** | ≈ **R$ 3** | Necessário para verificar a empresa na Meta, para o e-mail e para o endereço da vitrine. |
| WhatsApp oficial (Meta Cloud API) | ponte por QR (grátis, risco de banimento) | **R$ 0 a 35** | **R$ 70 a 200** | Ver cálculo abaixo. A Meta não cobra mensalidade, só por mensagem enviada. |
| IA (Gemini pago) | camada grátis | **R$ 5 a 15** | **R$ 20 a 60** | Ver cálculo abaixo. Na camada grátis o Google usa o conteúdo para melhorar os produtos dele: com dados de clientes reais (LGPD), use o pago. |
| E-mail | — | **R$ 0** (Cloudflare Email Routing encaminha contato@dominio para um Gmail) | Google Workspace Starter ≈ **R$ 33 por usuário** | |
| Monitoramento | — | **R$ 0** (UptimeRobot grátis, 5 min, ou cron-job.org) | UptimeRobot Solo ≈ €9 a 10 ≈ **R$ 60** (a cada 60 s) | O grátis do UptimeRobot é anunciado para "projetos pessoais". |
| Backups | — | **R$ 0** (incluso no Supabase Pro) | **R$ 0** + cópia semanal fora do Supabase (GitHub Actions + armazenamento grátis) | Backup que nunca foi restaurado não conta: testar a restauração uma vez. |
| **Total aproximado** | R$ 0 | **≈ R$ 200 a 240** | **≈ R$ 500 a 680** | |

### WhatsApp: como a Meta cobra no Brasil (desde 01/10/2026)

| Tipo de mensagem | Preço por mensagem enviada |
| --- | --- |
| Mensagem do cliente para a loja | grátis |
| Resposta dentro da janela de 24 h (atendimento) | **1.000 grátis por mês por número**, depois R$ 0,035 |
| Utilidade (modelo aprovado: pedido confirmado, lembrete de pagamento) | R$ 0,035 |
| Autenticação | R$ 0,035 |
| Marketing (promoção, chegada de peixes) | R$ 0,32 |

Exemplos: 100 atendimentos de 8 respostas = 800 mensagens, **R$ 0**. 500 atendimentos de 10 respostas = 5.000
mensagens, (5.000 − 1.000) × R$ 0,035 = **R$ 140**. Uma campanha de marketing para 500 clientes custa **R$ 160** a mais.
A Meta passou a faturar em reais (Facebook Brasil), sem IOF. Mensagem que falha não é cobrada.

### Gemini: quanto custa o robô

O código usa o modelo `gemini-flash-lite-latest`. O Flash-Lite atual (3.1) custa US$ 0,25 por milhão de tokens de
entrada e US$ 1,50 por milhão de saída. Uma resposta do robô gasta por volta de 3 mil tokens de entrada (instruções,
ferramentas e histórico) e 200 de saída, ou seja, **cerca de US$ 0,001 (R$ 0,006) por resposta**. Mil respostas
saem por ≈ R$ 6; cinco mil, por ≈ R$ 30. Fixar o modelo por configuração (`IA__Gemini__Modelo`) evita trocas de
preço quando o Google muda o apelido "latest".

### Mercado Pago (cobrado sobre cada venda, não é mensalidade)

| Forma | Taxa (Checkout Pro / link de pagamento) |
| --- | --- |
| Cartão de crédito, dinheiro na hora | 4,98% |
| Cartão, dinheiro em 14 dias | 4,48% |
| Cartão, dinheiro em 30 dias | 2,95% (o próprio site também fala em 3,98% a 4,98%: confirmar na conta) |
| Pix pelo link do Mercado Pago | 0,99% |
| Parcelado | a loja escolhe se o juro fica com o cliente ou soma na taxa (2x 2,53% … 12x 17,28%) |

O Pix com a chave da loja (o fluxo atual, confirmado à mão pelo comprovante) não passa pelo Mercado Pago; a tarifa
depende do banco da loja (para CNPJ, muitos bancos cobram por Pix recebido: consultar). Exemplo: R$ 20 mil/mês, 40%
no cartão = R$ 8 mil × 4,98% = **R$ 398** (ou R$ 236 recebendo em 30 dias). As taxas exatas da conta ficam em
"Seu negócio → Custos" no Mercado Pago.

## Alternativa mais barata, com mais trabalho: servidor próprio (VPS)

A pasta `infra/producao` já sobe tudo (PostgreSQL, API, vitrine, HTTPS e backup diário) num servidor só com Docker.
Uma VPS de 4 vCPU e 8 GB custa a partir de ≈ €8,49 (Hetzner CX33, na Europa, com estoque irregular em outubro/2026),
algo como R$ 55 a 120/mês dependendo do provedor e da região. Em troca, a loja (ou quem cuida do sistema) fica
responsável por atualizações de segurança, backups fora do servidor e por consertar quando cair. Para uma loja sem
equipe de TI, a enxuta gerenciada (Render + Supabase) costuma valer a diferença.

## O que não entra na conta

- CNPJ e conta Meta Business verificada (necessários para o WhatsApp oficial e para o Mercado Pago como empresa).
- Um chip/número dedicado para a loja no WhatsApp oficial (registrado na API, o número em geral deixa de funcionar no app do WhatsApp).
- Horas de quem mantém o sistema.
- Frete por mapa: BrasilAPI, Nominatim e OSRM públicos são grátis, mas com política de uso justo. Com o cache atual
  aguentam uma loja; com várias lojas, avaliar um serviço pago de mapas.

## Fontes

- Render: [preços oficiais](https://render.com/pricing.md); mudança de planos de 01/08/2026 em [jwatte.com](https://jwatte.com/blog/render-com-platform-review.md)
- Supabase: [Activepieces (12/11/2026)](https://www.activepieces.com/blog/supabase-pricing-free-tier-limits-pro-costs-egress.md), [Toolradar](https://toolradar.com/blog/supabase-pricing-2026); oficial: https://supabase.com/pricing
- Cloudflare Workers/Pages Functions: [preços oficiais](https://developers.cloudflare.com/workers/platform/pricing/)
- WhatsApp Brasil: [Vectalk](https://vectalk.com.br/blog/en/whatsapp-api-pricing-brazil/), [Whautomate](https://whautomate.com/whatsapp-business-api-pricing-brazil); oficial: https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing
- Gemini: [preços oficiais](https://ai.google.dev/gemini-api/docs/pricing)
- Mercado Pago: [Checkouts](https://www.mercadopago.com.br/ferramentas-para-vender/check-out?code=1H1A5)
- Domínio .br: [Teletime, 14/10/2026](https://teletime.com.br/14/10/2026/br-6-milhoes/) (R$ 40/ano, sem reajuste desde 2017)
- Google Workspace: [B2B Stack](https://www.b2bstack.com.br/product/google-workspace/planos-e-precos)
- UptimeRobot: [preços](https://uptimerobot.com/pricing.md)
- Hetzner: [Bitdoze (20/10/2026)](https://www.bitdoze.com/hetzner-cloud-cost-optimized-plans/), [WZ-IT](https://wz-it.com/en/blog/hetzner-price-increase-june-2026-cpx-ccx-alternatives/)
