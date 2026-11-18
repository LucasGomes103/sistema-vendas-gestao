# Arquitetura, deploy e variáveis de ambiente

Como o sistema roda hoje (versão teste) e como fica na produção de uma loja em operação.
Custos em [custos.md](custos.md); passo a passo da virada em [checklist.md](checklist.md).

## Arquitetura

```
Cliente no WhatsApp ──► Meta Cloud API ──webhook──► API (.NET 10, Render)
                                                     │  workers: atendimento, robô (Gemini), funil, outbox
Navegador ──► https://<dominio>  (Cloudflare Pages)  │
               ├─ /painel/*   painel Angular (SPA)    │
               ├─ /<loja>     vitrine Angular (SSR)   │
               └─ /api, /health, /privacidade, /termos ─► Functions repassam para a API
                                                     │
                                                     ├─► PostgreSQL (Supabase)
                                                     ├─► Gemini (IA do robô)
                                                     ├─► Mercado Pago (link de cartão + webhook /api/v1/webhooks/mercadopago)
                                                     └─► BrasilAPI / Nominatim / OSRM (frete por CEP, com cache)
```

- Um monólito modular: `Api` (endpoints), `Application` (casos de uso), `Domain` (regras), `Infrastructure`
  (EF Core, integrações), `Workers` (BackgroundServices). Multi-loja por `loja_id` com filtro global.
- Painel e API ficam **no mesmo domínio** (as Functions do Cloudflare repassam `/api`): o cookie de login
  `SameSite=Strict` funciona sem CORS.
- As migrações do banco rodam sozinhas quando a API sobe (`Banco__MigrarAoIniciar=true`).
- **Na versão teste**, a ponte do WhatsApp por QR (`whatsapp-local`, Baileys) roda dentro do mesmo serviço do
  Render e se visita a cada 10 min para não dormir. **Na produção ela sai**: o WhatsApp passa a ser a Cloud API
  oficial da Meta, que chama o webhook da API.

## Deploy

| Parte | Onde | Como publica |
| --- | --- | --- |
| API | Render, serviço `sistema-vendas-api` (Docker, `infra/render/Dockerfile`, região Virgínia) | sozinho a cada merge na `main` que mexe em `backend/`, `whatsapp-local/` ou `infra/render/` |
| Painel e vitrine | Cloudflare Pages (root `frontend`, build `bash build-publicacao.sh`, saída `publicar`) | sozinho a cada merge na `main` |
| Banco | Supabase (East US), pooler em modo sessão | migrações na subida da API |
| CI | GitHub Actions (`.github/workflows/ci.yml`): build e testes do backend e do front | em todo PR |

Toda mudança entra por pull request; o merge publica. Para voltar uma versão: reverter o PR (ou, no Render,
"Rollback" para o deploy anterior). Migração de banco não volta sozinha: mudanças de esquema devem ser compatíveis
com a versão anterior da API.

Alternativa sem Render/Supabase: `infra/producao` sobe tudo numa VPS com Docker Compose (PostgreSQL, API, vitrine,
Caddy com HTTPS e backup diário). Instalação: `bash infra/producao/instalar.sh`; atualização: `infra/producao/atualizar.sh`.

### Domínio próprio

1. Registrar o domínio no [Registro.br](https://registro.br) e apontar os DNS para o Cloudflare (plano grátis).
2. No Cloudflare Pages → Custom domains, adicionar `www.<dominio>` e/ou `<dominio>`.
3. No Render, trocar `Vitrine__UrlBase` para `https://<dominio>` (é o link que o robô manda ao cliente).
4. Na Meta e no Mercado Pago, cadastrar os webhooks com o domínio novo (abaixo). O endereço `onrender.com` continua
   funcionando, mas o domínio próprio sobrevive a uma troca de hospedagem.

## Variáveis de ambiente (Render)

Segredos nunca vão para o repositório: ficam no painel do Render (ou em `dotnet user-secrets` no desenvolvimento).
Em .NET, `__` separa seções (`IA__Gemini__ApiKey` = `IA:Gemini:ApiKey`).

| Variável | Obrigatória | Produção | O que é |
| --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | sim | `Production` | |
| `PORT` | sim | `8080` | |
| `ConnectionStrings__Banco` | sim | segredo | `Host=...pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.xxxx;Password=...;SSL Mode=Require` |
| `Banco__MigrarAoIniciar` | sim | `true` | aplica migrações ao subir |
| `Vitrine__UrlBase` | sim | `https://<dominio>` | link da vitrine que o robô envia |
| `ChavesProtecao__Pasta` | não | — | pasta das chaves dos cookies; no Render não há disco persistente, então cada deploy desloga todos (ver próximos passos) |
| `Simulador__Habilitado` | não | `false` | desliga o simulador de cliente na produção |
| `WhatsApp__Provedor` | sim | `Meta` (ou remover: o padrão é Meta) | `Local` = ponte por QR, só para teste |
| `WhatsApp__AccessToken` | sim (Meta) | segredo | token **permanente** de usuário do sistema da Meta Business |
| `WhatsApp__AppSecret` | sim (Meta) | segredo | chave secreta do app: valida a assinatura do webhook |
| `WhatsApp__VerifyToken` | sim (Meta) | segredo | texto escolhido por você, o mesmo cadastrado no webhook da Meta |
| `WhatsApp__ApiVersion` | não | `v25.0` | versão da Graph API |
| `WhatsApp__Local__Segredo`, `WhatsApp__Local__LojaSlug`, `WhatsApp__Local__Url` | só teste | remover | ponte por QR |
| `IA__Gemini__ApiKey` | não | segredo (projeto com faturamento ativo) | sem ela, o robô atende por regras |
| `IA__Gemini__Modelo` | não | fixar um modelo, ex. `gemini-3.1-flash-lite` | padrão `gemini-flash-lite-latest` |
| `MercadoPago__AccessToken` | não | segredo `APP_USR-...` de produção | sem ele, só Pix |
| `MercadoPago__WebhookSecret` | recomendado | segredo | assinatura dos avisos do Mercado Pago |
| `MercadoPago__ModoTeste` | não | `false` | |
| `MercadoPago__UrlPublicaApi` | não | `https://<dominio>` | só para o painel mostrar a URL do webhook |
| `MercadoPago__MaxParcelas` | não | `12` | |
| `Geo__RespeitarLimite` | não | `true` | respeita o limite de uso do Nominatim público |
| `PrimeiraLoja__Nome`, `__Slug`, `__DonoNome`, `__DonoEmail`, `__DonoSenha` | só na 1ª subida | apagar `__DonoSenha` depois | cria a loja e o dono num banco vazio |

O número de WhatsApp de cada loja (`phone_number_id` da Meta) não é segredo: fica no banco, preenchido no painel em
**Configuração → WhatsApp**.

### Webhooks a cadastrar

| Serviço | URL | Eventos |
| --- | --- | --- |
| Meta (WhatsApp) | `https://<dominio>/api/v1/webhooks/whatsapp` | `messages` |
| Mercado Pago | `https://<dominio>/api/v1/webhooks/mercadopago` | Order |

## Monitoramento e backups

- **Monitor externo** (UptimeRobot ou cron-job.org) chamando `https://<dominio>/health` a cada 1 a 5 min, com alerta
  por e-mail/Telegram. Mostra se a API, o Cloudflare e o banco estão respondendo.
- **Logs**: Render → Logs (Serilog). Erros do robô e do outbox aparecem lá.
- **Backups**: Supabase Pro guarda backup diário por 7 dias. Recomendado um `pg_dump` semanal guardado fora do
  Supabase e **um teste de restauração** num banco local antes de começar a operar.
