# Checklist de ida para produção

Ordem sugerida para levar o sistema da versão teste para uma loja em operação. Arquitetura e variáveis em
[implantacao.md](implantacao.md); custos em [custos.md](custos.md).

## 1. Contas e contratos (antes de tudo)

- [ ] CNPJ da loja em mãos; contas no nome da empresa (Meta Business, Mercado Pago, Render, Supabase, Cloudflare, Google AI Studio).
- [ ] Cartão da empresa cadastrado nos serviços pagos; alertas de gasto ligados (Supabase: spend cap; Google Cloud: orçamento do projeto do Gemini).
- [ ] Domínio `.com.br` registrado no Registro.br e DNS no Cloudflare.
- [ ] E-mail no domínio (ex.: `contato@<dominio>`), usado nas contas acima.

## 2. Infraestrutura sem dormir e sem pausar

- [ ] Render: serviço `sistema-vendas-api` de **Free para Starter (ou Standard)**. Atualizar `plan` no `render.yaml`.
- [ ] Supabase: projeto para o plano **Pro**. Conferir backup diário ativo.
- [ ] Cloudflare Pages: domínio próprio em Custom domains; previews de branch desligados; avaliar Workers Paid (cota diária das Functions).
- [ ] `Vitrine__UrlBase=https://<dominio>` no Render.
- [ ] Monitor externo em `/health` com alerta.
- [ ] Teste de restauração de um backup.

## 3. Segurança (pendências da versão teste)

- [ ] **Trocar a senha do banco do Supabase** (vazou no chat) e atualizar `ConnectionStrings__Banco` no Render.
- [ ] **Revogar os tokens antigos da Meta** e gerar um token permanente de usuário do sistema (Meta Business → Usuários do sistema).
- [ ] **Remover o login de demonstração `dono@demo.local`** (tela Usuários): criar o dono real com senha forte, depois excluir o demo.
- [ ] Apagar `PrimeiraLoja__DonoSenha` do Render depois da primeira subida.
- [ ] `Simulador__Habilitado=false`.
- [ ] Cada pessoa da loja com o próprio usuário e o papel certo (Dono, Vendedor, Entregador); ninguém compartilha senha.
- [ ] Autenticação em dois fatores nas contas de GitHub, Render, Supabase, Cloudflare, Meta e Mercado Pago.

## 4. WhatsApp oficial (sai a ponte por QR)

- [ ] Número dedicado da loja (chip próprio, não o pessoal).
- [ ] Verificação da empresa na Meta Business (exige site no domínio próprio; a vitrine e as páginas `/privacidade` e `/termos` servem).
- [ ] App na Meta com o produto WhatsApp; número registrado na Cloud API; forma de pagamento cadastrada (faturamento em reais).
- [ ] No Render: `WhatsApp__Provedor=Meta` (ou remover), `WhatsApp__AccessToken`, `WhatsApp__AppSecret`, `WhatsApp__VerifyToken`; remover `WhatsApp__Local__*`.
- [ ] Webhook da Meta em `https://<dominio>/api/v1/webhooks/whatsapp` (campo `messages`).
- [ ] `phone_number_id` em **Configuração → WhatsApp** no painel.
- [ ] Desconectar a ponte por QR do número pessoal (Configuração → Conexão do WhatsApp) e confirmar que nenhuma ponte local roda no PC.
- [ ] Teste ponta a ponta com um celular de fora: pergunta, catálogo, frete, reserva, pedido, Pix, chamada do vendedor.

## 5. Pagamentos

- [ ] Mercado Pago: credenciais de **produção** (`APP_USR-...`) no lugar das de teste; `MercadoPago__ModoTeste=false`.
- [ ] Webhook de produção (evento Order) em `https://<dominio>/api/v1/webhooks/mercadopago` com a assinatura secreta em `MercadoPago__WebhookSecret`.
- [ ] Uma compra real de valor baixo no cartão, conferindo que o pedido confirma sozinho; estornar depois.
- [ ] Prazo de recebimento do cartão escolhido na conta (na hora 4,98% × 30 dias 2,95%) e regra de parcelamento.
- [ ] Chave Pix da loja (CNPJ) cadastrada no painel; tarifa de Pix do banco conferida.

## 6. IA

- [ ] Chave do Gemini de um projeto **com faturamento ativo** (na camada grátis os dados são usados pelo Google).
- [ ] Fixar o modelo em `IA__Gemini__Modelo`.
- [ ] Revisar as respostas do robô com o catálogo real (preços, espécies, frete) antes de abrir para todos.

## 7. LGPD

- [ ] Páginas `/privacidade` e `/termos` revisadas com os dados reais da empresa (razão social, CNPJ, contato do encarregado).
- [ ] Nomear o encarregado (pode ser o dono) e publicar o contato.
- [ ] Mensagem de boas-vindas do robô informando que o atendimento usa automação e linkando a política.
- [ ] Mensagens de marketing só para quem aceitou receber (opt-in), com forma de sair.
- [ ] Atender pedido de exclusão/correção de dados do cliente (hoje manual pelo banco; ver próximos passos).
- [ ] Operadores (Render, Supabase, Cloudflare, Google, Meta, Mercado Pago) listados na política, com a informação de que há transferência internacional (servidores nos EUA).
- [ ] Acesso ao painel e ao banco só para quem precisa.

## 8. Operação

- [ ] Treinar a equipe: Conversas, kanban, pedidos, conferir pagamento, caixa, entregas.
- [ ] Combinar quem responde quando o robô chama o vendedor, e em que horário.
- [ ] Período assistido de 1 a 2 semanas acompanhando as conversas do robô.
- [ ] Rever custos no fim do primeiro mês (Render, Supabase, Meta, Gemini) e ajustar planos.

## Próximos passos de código para produção

Itens que melhoram a operação real e ainda não existem:

1. **Chaves dos cookies no banco**: hoje, no Render, cada deploy desloga todo mundo (sem disco para `ChavesProtecao__Pasta`). Guardar as chaves no PostgreSQL resolve.
2. **Modelos de mensagem da Meta**: lembrete de pagamento em 12 h, pedido confirmado e saiu para entrega fora da janela de 24 h (utilidade, R$ 0,035 cada).
3. **Tirar a ponte por QR da imagem de produção** (e o auto-ping), deixando-a só para desenvolvimento.
4. **Exclusão e exportação dos dados de um cliente** pelo painel (LGPD).
5. **Backup semanal fora do Supabase** por GitHub Actions.
6. **Aviso automático de Pix** pelo Mercado Pago (0,99%), se a loja quiser parar de conferir comprovante.
7. Alerta de entrega atrasada há 48 h e QR do Pix na tela (pendências da fase 2).
8. Levar API e banco juntos para mais perto do Brasil se a latência incomodar (o Render não tem região no Brasil; o Supabase tem São Paulo, `sa-east-1`). Mudar só um dos dois piora.
