# Serviço único no Render grátis: API (.NET) + ponte do WhatsApp (Node) no mesmo contêiner.
# Contexto do build: raiz do repositório. No Render: Root Directory vazio e Dockerfile Path infra/render/Dockerfile.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/ .
RUN dotnet publish src/SistemaVendas.Api -c Release -o /app --nologo

FROM node:24-bookworm-slim AS ponte
WORKDIR /ponte
COPY whatsapp-local/package.json whatsapp-local/package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund
COPY whatsapp-local/servidor.mjs whatsapp-local/sessao-banco.mjs ./

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=ponte /usr/local/bin/node /usr/local/bin/node
COPY --from=ponte /ponte /ponte
COPY --from=api /app /app
COPY infra/render/iniciar.sh /iniciar.sh
WORKDIR /app
# 512 MB no total: teto para o heap do .NET e para o Node.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimit=0x10000000 \
    TZ=America/Sao_Paulo
EXPOSE 8080
CMD ["sh", "/iniciar.sh"]
