# Plano técnico: sistema multi-cliente com autoatendimento (SaaS)

Avaliação feita em **18/11/2026** sobre o código da `main` (depois do PR #26). Objetivo: o cliente entra no site,
escolhe o plano, paga a assinatura e um assistente coleta os dados dele (catálogo, WhatsApp oficial, Mercado Pago,
IA). Tudo fica no ar **sem o Lucas mexer no servidor**. Os planos e preços estão em [proposta.md](proposta.md).

Este documento é só o plano, para aprovação. Nada foi implementado.

## Resposta curta: é viável?

**Sim.** O sistema já nasceu com várias lojas no mesmo banco, então não é preciso reescrever: é preciso tirar da
configuração do servidor tudo o que hoje é "da ECS Fish" e passar para o cadastro de cada loja, e acrescentar
cadastro, cobrança e o assistente.

O que limita o prazo não é o código: é a **aprovação da Meta** para o Lucas virar "Tech Provider" (provedor de
tecnologia) do WhatsApp, que exige empresa verificada (CNPJ, domínio, site) e revisão do app com vídeos. Sem isso,
o cadastro do número pelo próprio cliente (Embedded Signup) não funciona para clientes de fora.

Esforço estimado: **10 a 12 semanas de desenvolvimento** em 6 fases, com o prazo de calendário de **3 a 4 meses**
por causa das aprovações da Meta, que correm em paralelo.

## O que o código já tem

| Peça | Situação hoje | Onde |
| --- | --- | --- |
| Várias lojas no mesmo banco | **Pronto.** Toda entidade de negócio herda `EntidadeDaLoja` (coluna `loja_id`) e o `AppDbContext` aplica filtro global por loja em todas as tabelas. | `Domain/Comum/Entidade.cs`, `Infrastructure/Dados/AppDbContext.cs` |
| Loja do usuário logado | **Pronto.** A loja vem da claim `loja_id` do login. | `Api/Comum/LojaAtualDaRequisicao.cs` |
| Vitrine por loja | **Pronto.** A vitrine é acessada pelo slug (`/ecsfish`). | `CatalogoEndpoints.cs` |
| Mensagem do WhatsApp chega na loja certa | **Pronto.** O webhook acha a loja pelo `phone_number_id`. | `WhatsApp/ProcessadorWebhook.cs` |
| Workers (robô, funil, avisos) | **Pronto.** Definem a loja de cada tarefa antes de processar. | `Workers/*.cs` |
| Usuários e papéis por loja | **Pronto** (Dono, Vendedor, Entregador). | `Domain/Lojas/Usuario.cs` |
| Criar uma loja nova | **Falta.** Só existe a `PrimeiraLoja`, que cria a loja uma vez quando o banco está vazio, a partir de variáveis do Render. | `Dados/PrimeiraLoja.cs` |
| Token do Mercado Pago | **Global.** `MercadoPago__AccessToken` é um só para o servidor: numa segunda loja, o dinheiro do cartão cairia na conta da ECS Fish. | `Pagamentos/MercadoPago.cs` |
| Token do WhatsApp oficial | **Global.** `WhatsApp__AccessToken` é um só; o `phone_number_id` já é por loja. | `WhatsApp/WhatsApp.cs` |
| Chave da IA | **Global.** `IA__Gemini__ApiKey` é uma só, sem medir o uso por loja. | `IA/GeminiAtendente.cs` |
| Ponte por QR (Baileys) | Uma sessão por servidor, presa a uma loja (`WhatsApp__Local__LojaSlug`). Não serve para vários clientes. | `whatsapp-local/` |
| Chaves dos cookies de login | Sem disco no Render: cada deploy desloga todo mundo. Com vários clientes, isso vira chamado de suporte. | `ChavesProtecao__Pasta` |
| Robô e textos | Falam de "peixes ornamentais", espécie e tanque. | `IA/GeminiAtendente.cs`, `IA/AtendentePorRegras.cs` |

**Atenção para hoje:** antes da fase 1, **não ligar o cartão para uma segunda loja**, pelo motivo do token global
do Mercado Pago. Uma segunda loja feita à mão (direto no banco) funcionaria para estoque, vitrine, robô e Pix.

## Como fica a arquitetura

Continua **um deploy só** para todas as lojas (mesma API, mesmo banco, mesmo front). Muda isto:

```
Site de vendas /planos ──► Cadastro (loja + dono) ──► Assinatura (Mercado Pago da plataforma, conta do Lucas)
                                    │
                                    ▼
                         Assistente de configuração (painel)
   1 Dados da loja ─ 2 Catálogo ─ 3 Pix ─ 4 WhatsApp (Embedded Signup) ─ 5 Mercado Pago (OAuth) ─ 6 IA ─ 7 Teste
                                    │
                                    ▼
            Tabela credencial_loja (criptografada): token Meta da loja, WABA, phone_number_id,
            token/refresh do Mercado Pago da loja, chave própria de IA (opcional)

API: WhatsAppCliente, MercadoPagoCliente e GeminiAtendente passam a pegar a credencial DA LOJA
     (hoje pegam das variáveis do servidor). Ficam no servidor só os segredos da plataforma:
     app da Meta (AppSecret, VerifyToken), app do Mercado Pago (client_id/secret), chave de IA da plataforma,
     chave mestra de criptografia.
```

### Dois "Mercado Pago" diferentes

1. **Cobrança da mensalidade** (loja paga o Lucas): conta do Lucas, API de **Assinaturas** (`/preapproval`). Taxa
   na página do Mercado Pago: crédito na hora 4,49%, em 30 dias 3,99%. Aceita cartão e saldo Mercado Pago; a página
   não fala em Pix nem boleto. A cobrança tenta de novo até 4 vezes em 10 dias e cancela depois de 3 parcelas
   recusadas ([Assinaturas](https://www.mercadopago.com.br/ferramentas-para-vender/assinaturas),
   [API](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/authorized-payments)).
   Se o Lucas quiser aceitar Pix ou boleto recorrente, avaliar Asaas ou similar (não pesquisado aqui).
2. **Venda da loja** (cliente final paga a loja): a loja clica em **"Conectar Mercado Pago"** e autoriza o app do
   Lucas por **OAuth**. O sistema recebe o token da loja (vale 180 dias, renovado pelo refresh token) e guarda
   criptografado. A loja nunca copia chave nenhuma
   ([OAuth](https://www.mercadopago.com.br/developers/pt/docs/security/oauth/creation?scope=prod)). O webhook do
   cartão continua um só (`/api/v1/webhooks/mercadopago`) e acha a loja pelo pedido, como já faz hoje.

### WhatsApp oficial pelo próprio cliente (Embedded Signup)

- O Lucas vira **Tech Provider** da Meta: empresa verificada, app com as permissões `whatsapp_business_messaging` e
  `whatsapp_business_management` em acesso avançado, aprovado na revisão com dois vídeos (enviar uma mensagem pelo
  sistema e criar um modelo de mensagem)
  ([Meta: Tech Provider](https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/get-started-for-tech-providers),
  [guia Kapso](https://docs.kapso.ai/docs/platform/become-tech-provider.md)).
- No assistente, um botão abre a janela da Meta; o cliente entra com o Facebook, cria ou escolhe a conta do
  WhatsApp e o número. O sistema recebe o **id da conta (WABA), o `phone_number_id` e um código** que troca por um
  token da loja; depois registra o número e assina os webhooks
  ([Meta: Embedded Signup](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/overview/)).
  Usar a versão 4 (a v2 foi descontinuada em 15/10/2026).
- **As mensagens são cobradas no cartão do cliente**, que ele cadastra na conta do WhatsApp dele. O Lucas não
  adianta dinheiro da Meta.
- Limite: antes das verificações, **10 clientes novos a cada 7 dias**; depois de verificação da empresa, revisão do
  app e verificação de acesso, **200 a cada 7 dias**. Para começar, sobra.
- A Meta também permite conectar um número que já usa o app WhatsApp Business ("Onboard WhatsApp Business app
  users"); confirmar na documentação se o cliente continua usando o app no celular ao mesmo tempo antes de prometer.

### IA

- Padrão: **IA da plataforma** (chave do Lucas, projeto pago do Gemini), incluída no plano. Cada chamada grava
  tokens e loja numa tabela de uso, que alimenta o limite de conversas e o relatório de custo.
- Opcional: a loja coloca a **própria chave** (Gemini); aí o uso de IA não conta no limite.

## Fases

| Fase | O que entrega | Esforço |
| --- | --- | --- |
| **0. Pré-requisitos (Lucas, em paralelo)** | CNPJ do produto, domínio e site com política de privacidade e termos; Meta Business verificada; app da Meta e pedido de revisão como Tech Provider; app no Mercado Pago com OAuth; conta paga do Gemini; contador (enquadramento no Simples) | 2 a 6 semanas de espera, pouco trabalho |
| **1. Fundação multi-cliente** | Tabela `credencial_loja` criptografada (chave mestra no Render); chaves dos cookies no banco (para de deslogar a cada deploy); clientes de WhatsApp, Mercado Pago e IA lendo a credencial da loja; cadastro público de loja + dono com confirmação de e-mail; slug único; testes automáticos de isolamento (uma loja nunca vê dado da outra, inclusive nos pontos com `IgnoreQueryFilters`); painel do administrador da plataforma (lista de lojas, status, uso, suspender) | 2 semanas |
| **2. Integrações pelo cliente** | Botão "Conectar Mercado Pago" (OAuth e renovação automática do token); botão "Conectar WhatsApp" (Embedded Signup v4, registro do número, webhooks); IA da plataforma com medição por loja e chave própria opcional; tirar a ponte por QR do produto (fica só o simulador para teste) | 2 a 3 semanas |
| **3. Planos e cobrança** | Tabelas de plano e assinatura; assinatura recorrente no Mercado Pago da plataforma com webhook; teste grátis de 7 dias; limites por plano (conversas, usuários, recursos como cartão, entregas e domínio próprio); carência e bloqueio por atraso (robô para, painel só leitura); e-mails de cobrança e aviso de limite (Resend ou similar na camada grátis) | 2 semanas |
| **4. Assistente de configuração** | Passo a passo no primeiro acesso, com barra de progresso e opção de pular e voltar: dados da loja, horário e endereço (para o frete); catálogo com importação por planilha e fotos; chave Pix; WhatsApp; Mercado Pago; IA; teste no simulador; publicar. Página pública de planos com botão de assinar | 2 semanas |
| **5. Piloto e endurecimento** | 2 ou 3 lojas piloto com implantação assistida; monitor e alertas por loja (webhook parado, token vencido, assinatura recusada); limite de requisições por loja; backup e exportação dos dados da loja no cancelamento; revisão de segurança e LGPD | 2 semanas |
| **6. Outros segmentos (opcional)** | Generalizar "espécie/tanque/peixe" para "produto/local/item" nos textos do painel e do robô, com o tipo de loja escolhido no cadastro; prompt do robô montado a partir do cadastro | 1 a 2 semanas |

Total das fases 1 a 5: **10 a 11 semanas**; com a 6, até 13. As fases 1 e 2 podem começar antes da aprovação da
Meta: em modo de desenvolvimento, o app da Meta funciona com as contas do próprio Lucas.

## Infraestrutura com várias lojas

| Item | 1 a 10 lojas | 10 a 50 lojas |
| --- | --- | --- |
| API (Render) | Starter US$ 7 | Standard US$ 25 (2 GB), depois 2 instâncias |
| Banco (Supabase) | Pro US$ 25 | Pro + instância Small/Medium |
| Front (Cloudflare) | Workers Paid US$ 5 (o kanban de várias lojas estoura a cota grátis) | igual |
| Mapas do frete | BrasilAPI/Nominatim/OSRM públicos com cache | Nominatim público não aguenta volume: serviço pago de mapas ou servidor próprio |
| E-mail transacional | camada grátis | plano básico |
| Total aproximado | **≈ R$ 250 a 300/mês** | **≈ R$ 450 a 700/mês** |

Custo que cresce por loja: IA (≈ R$ 0,05 por conversa) e espaço de fotos no banco. WhatsApp e Mercado Pago são
pagos pela própria loja.

## Riscos

- **Aprovação da Meta** pode demorar ou pedir ajustes (não há prazo publicado; há relatos de verificação parada por
  semanas no fórum da Meta). Mitigação: pedir já, antes de começar o código.
- **Um problema derruba todas as lojas** (um deploy só). Mitigação: CI com testes, rollback no Render, monitor.
- **Vazamento entre lojas** é o pior erro possível num sistema assim. Mitigação: testes de isolamento obrigatórios
  no CI e, como segunda barreira, Row Level Security no PostgreSQL.
- **Suporte**: com autoatendimento, o assistente precisa explicar bem as telas da Meta e do Mercado Pago, que mudam
  com frequência.
- **LGPD**: o Lucas passa a guardar dados de clientes de outras lojas. Precisa de termos, política, acordo de
  tratamento de dados e um jeito de apagar tudo de uma loja que cancela.

## Próximo passo sugerido

1. O Lucas aprova (ou ajusta) os planos da [proposta.md](proposta.md).
2. Começa a fase 0 (CNPJ, domínio e o pedido de Tech Provider na Meta), que é a que mais demora.
3. Em paralelo, a fase 1 em PRs pequenos, começando pela credencial por loja e pelas chaves dos cookies no banco
   (essa última já ajuda a ECS Fish hoje).
