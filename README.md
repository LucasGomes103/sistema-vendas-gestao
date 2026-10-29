# Sistema de Vendas e Gestão

Sistema de vendas pelo WhatsApp para lojas de peixes ornamentais: catálogo vivo, estoque por tanque,
atendimento com IA, funil (kanban) com regras de tempo, pedidos e Pix. O piloto é a ECS Fish; depois de
validado, o sistema atende outras lojas (multi-loja desde o primeiro dia).

Os documentos de origem estão em [docs/iniciacao](docs/iniciacao).

## Stack (fase 1, custo zero)

| Camada | Tecnologia |
| --- | --- |
| Front | Angular 22 + Angular Material 3 (painel SPA e catálogo público com SSR) |
| Back | ASP.NET Core / .NET 10 LTS, monólito modular |
| Dados | EF Core 10 + Npgsql, PostgreSQL 18 |
| Jobs | BackgroundService do .NET (motor do funil, envio de mensagens) |
| Infra | Docker Compose no Oracle Cloud Always Free (Arm) |

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
- Endpoints: `GET /health`, `GET /api/v1/public/{loja}/catalogo`, `GET/POST/PUT /api/v1/especies`, `GET /api/v1/tanques`.
- Painel com telas de Catálogo e Estoque; vitrine pública em `/{loja}`.
- Regra de horário comercial do funil, com testes.

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

## Próximos passos (fase 1)

1. Login com ASP.NET Core Identity (cookie) e papéis Dono, Vendedor e Entregador.
2. Entrada de lote, quarentena, baixa e transferência pelo celular.
3. Webhook do WhatsApp Cloud API, conversas e outbox de mensagens.
4. Cards do funil, motor de regras e kanban com SignalR.
5. IA do atendimento (Gemini Flash-Lite, camada gratuita) com function calling.
6. Deploy no Oracle Cloud Always Free com Caddy e backup diário.

Segredos (tokens da Meta, Pix, IA) ficam em variáveis de ambiente ou em `dotnet user-secrets`, nunca no repositório.
