# Sistema de Vendas e Gestão

Sistema de vendas pelo WhatsApp para lojas de peixes ornamentais: catálogo vivo, estoque por tanque,
atendimento com IA, funil (kanban) com regras de tempo, pedidos e Pix. O piloto é a ECS Fish; depois de
validado, o sistema atende outras lojas (multi-loja desde o primeiro dia).

Os documentos de origem estão em [docs/iniciacao](docs/iniciacao). O que cada fase entregou, e como usar, está em
[docs/funcionalidades.md](docs/funcionalidades.md).

**Publicado:** painel em https://sistema-vendas-gestao.pages.dev/painel, vitrine em
https://sistema-vendas-gestao.pages.dev/ecsfish e API em https://sistema-vendas-api-0x0l.onrender.com.
O merge na `main` publica sozinho (Cloudflare Pages para o front, Render para a API, migrações aplicadas ao subir).

## Stack (fase 1, custo zero)

| Camada | Tecnologia |
| --- | --- |
| Front | Angular 22 + Angular Material 3 (painel SPA e catálogo público com SSR) |
| Back | ASP.NET Core / .NET 10 LTS, monólito modular |
| Dados | EF Core 10 + Npgsql, PostgreSQL 18 |
| Jobs | BackgroundService do .NET (motor do funil, envio de mensagens) |
| Infra | Render (API + ponte do WhatsApp), Cloudflare Pages (painel e vitrine), Supabase (PostgreSQL), todos no plano grátis |
| Mapas (frete) | BrasilAPI, Nominatim (OpenStreetMap) e OSRM, grátis e sem chave |

## Estrutura

```
backend/
  src/SistemaVendas.Api             Minimal APIs, /health, OpenAPI (só em desenvolvimento)
  src/SistemaVendas.Application     casos de uso, DTOs, validação (FluentValidation)
  src/SistemaVendas.Domain          entidades, enums, regras do funil (sem dependência externa)
  src/SistemaVendas.Infrastructure  EF Core + PostgreSQL, migrações, integrações
  src/SistemaVendas.Workers         BackgroundServices (FunilWorker)
  tests/                            xUnit
frontend/
  projects/painel                   painel da loja (lojista e vendedor)
  projects/catalogo                 catálogo público com SSR (/{loja})
  projects/ui                       biblioteca de componentes e tema compartilhados
infra/docker-compose.yml            PostgreSQL 18 local
```

## O que já funciona

- Modelo multi-loja: toda entidade de negócio tem `loja_id`, com filtro global no EF Core.
- Catálogo (espécies) e estoque (tanques, fornecedores, lotes, movimentos); o saldo é a soma dos movimentos.
- Migração inicial e dados de exemplo (loja `demo`) criados ao subir em desenvolvimento.
- Login por cookie (HttpOnly, SameSite=Strict) com papéis Dono, Vendedor e Entregador; limite de tentativas no login.
- Usuários (menu da conta → Usuários, só o dono): criar, editar nome, e-mail e papel, bloquear o acesso, definir senha
  nova e excluir. Qualquer usuário troca a própria senha em "Trocar minha senha". A loja nunca fica sem um dono ativo,
  e quem é excluído, bloqueado ou tem a senha trocada perde a sessão em até 1 minuto.
- Endpoints: `GET /health`, `POST /api/v1/auth/login|logout`, `GET /api/v1/auth/eu`, `GET /api/v1/public/{loja}/catalogo`,
  `GET/POST/PUT /api/v1/especies`, `GET /api/v1/tanques` (saldo por tanque e baixas dos últimos 7 dias).
- Painel no padrão Material 3 das telas de referência: login, Estoque (resumo, alertas, tanques) e Catálogo
  (busca, filtros, cadastro e edição de espécies), com menu lateral no desktop e barra inferior no celular.
- Vitrine pública em `/{loja}`, renderizada no servidor, só com o que está disponível.
- Estoque com entrada de lote, baixa, transferência, ajuste e quarentena, com histórico por tanque.
- Conversas do WhatsApp: webhook da Meta com assinatura validada, fila de processamento, envio por outbox com
  novas tentativas, janela de 24 h e simulador de cliente para desenvolver sem número conectado.
- Regra de horário comercial do funil, com testes.
- Fase 1 (robô vendedor): frete por km a partir da loja com raio máximo, reserva de 24 h, fotos das espécies,
  alerta de cliente quente sem dono, kanban atualizando a cada 5 s e simulador com pedido pelo catálogo.
  Detalhes em [docs/funcionalidades.md](docs/funcionalidades.md).

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 24 LTS](https://nodejs.org) (inclui o npm)
- Um PostgreSQL 18: [Docker Desktop](https://www.docker.com/products/docker-desktop/) (gratuito para uso pessoal e empresas pequenas)
  ou o instalador do [PostgreSQL para Windows](https://www.postgresql.org/download/windows/)

## Rodando localmente

1. Banco (com Docker):

   ```bash
   docker compose -f infra/docker-compose.yml up -d
   ```

   Sem Docker, instale o PostgreSQL 18 (`winget install PostgreSQL.PostgreSQL.18`) e crie o usuário e o banco
   com o superusuário `postgres`:

   ```sql
   CREATE ROLE sistema LOGIN PASSWORD 'sistema_dev';
   CREATE DATABASE sistema_vendas OWNER sistema;
   \c sistema_vendas
   CREATE EXTENSION IF NOT EXISTS pg_trgm;
   ```

   Outra conexão? Ajuste `ConnectionStrings:Banco` em `backend/src/SistemaVendas.Api/appsettings.Development.json`.

2. API (aplica as migrações e cria a loja `demo` na primeira vez):

   ```bash
   cd backend
   dotnet run --project src/SistemaVendas.Api
   ```

   A API sobe em http://localhost:5007. Teste em http://localhost:5007/health e
   http://localhost:5007/api/v1/public/demo/catalogo. O Swagger abre em http://localhost:5007/swagger (só em desenvolvimento).

3. Front:

   ```bash
   cd frontend
   npm install
   npx ng serve painel      # http://localhost:4200
   npx ng serve catalogo    # http://localhost:4300/demo
   ```

   No PowerShell do Windows, se `npx` for bloqueado pela política de scripts, use `npx.cmd`.

   Login de desenvolvimento (criado junto com a loja `demo`): `dono@demo.local` / `Demo@123`.

   O `proxy.conf.json` encaminha `/api` para a API local.

## Testes

```bash
cd backend && dotnet test
cd frontend && npx ng test painel --watch=false
```

## Migrações

```bash
cd backend
dotnet tool restore
dotnet ef migrations add NomeDaMigracao -p src/SistemaVendas.Infrastructure -s src/SistemaVendas.Api -o Dados/Migracoes
```

## WhatsApp

Sem configuração, o envio é **simulado**: a tela de Conversas mostra um simulador para fazer o papel do cliente,
e as mensagens passam pelo mesmo caminho de uma mensagem real (fila de webhooks, worker, outbox).

Para ligar o número de teste da Meta, guarde os segredos com `dotnet user-secrets` (ficam fora do repositório):

```bash
cd backend/src/SistemaVendas.Api
dotnet user-secrets set "WhatsApp:AccessToken" "<token do app na Meta>"
dotnet user-secrets set "WhatsApp:AppSecret" "<chave secreta do app>"
dotnet user-secrets set "WhatsApp:VerifyToken" "<texto qualquer, o mesmo cadastrado no webhook da Meta>"
```

O webhook da Meta aponta para `https://<seu-domínio>/api/v1/webhooks/whatsapp`. Em desenvolvimento, use um túnel
gratuito (Cloudflare Tunnel ou ngrok) para expor a API local.

### Modo local por QR code (não oficial, só para testes e demonstrações)

A pasta `whatsapp-local` tem uma ponte gratuita que conecta um número lendo um QR code, como o WhatsApp Web.
**Use um número reserva, nunca o da loja**: conexões não oficiais violam os termos do WhatsApp e o número pode ser
banido. Para atender clientes de verdade, use a Cloud API da Meta (acima); a troca é só de configuração.

```bash
# 1. Segredo compartilhado entre API e ponte (o mesmo valor nos dois lugares)
cd backend/src/SistemaVendas.Api
dotnet user-secrets set "WhatsApp:Provedor" "Local"
dotnet user-secrets set "WhatsApp:Local:Segredo" "<um texto aleatório>"

# 2. Ponte: crie whatsapp-local/.env com WHATSAPP_LOCAL_SEGREDO=<o mesmo texto>
cd whatsapp-local
npm install
npm start          # abra http://localhost:5050/qr e leia com o celular do número reserva
```

**Trava de segurança:** por padrão a ponte **não lê nem responde ninguém**. Os números que podem conversar com o robô
são configurados no painel (**Configuração → WhatsApp**). Só ligue "Atender todos os contatos" num número dedicado à loja,
nunca num número pessoal.

**Na nuvem (Render):** a imagem `infra/render/Dockerfile` roda a API e a ponte no mesmo serviço. A ponte guarda a
sessão no PostgreSQL (tabela `whatsapp_sessao`), então reiniciar não pede o QR de novo, e se visita a cada 10 min
para o serviço grátis não dormir. O número é conectado pelo painel, em **Configuração → Conexão do WhatsApp**.

Para voltar à Meta: `dotnet user-secrets remove "WhatsApp:Provedor"`.

## Robô de atendimento (IA)

Com a chave do Gemini (camada gratuita), o robô responde usando só dados das ferramentas do backend
(buscar espécie, consultar estoque, calcular frete, criar reserva). Ele separa o pedido por 24 h, pede o CEP,
responde o frete e então chama o vendedor; também chama quando o cliente quer falar com uma pessoa. Nome, telefone e endereço não vão para o modelo. Sem chave, ou se o Gemini falhar,
entra um atendente por regras.

```bash
cd backend/src/SistemaVendas.Api
dotnet user-secrets set "IA:Gemini:ApiKey" "<sua chave do AI Studio>"
```

## Próximos passos

1. Fase 2: pedidos, Pix com chave comum, caixa e entregas.
2. Fase 3: métricas e relatórios.
3. Fase 4: itens restantes do plano em [docs/iniciacao](docs/iniciacao).

Segredos (tokens da Meta, Pix, IA) ficam em variáveis de ambiente ou em `dotnet user-secrets`, nunca no repositório.
