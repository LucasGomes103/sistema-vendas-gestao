# Front: painel (arquivos estáticos servidos pelo Caddy) e vitrine pública (Node com SSR).
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
# Servidor pequeno: limita a memória do build (com swap ligado).
ENV NODE_OPTIONS=--max-old-space-size=900
RUN npx ng build painel --base-href /painel/ && npx ng build catalogo

# Caddy com o painel embutido.
FROM caddy:2-alpine AS caddy
COPY --from=build /app/dist/painel/browser /srv/painel

# Vitrine pública renderizada no servidor.
FROM node:24-alpine AS catalogo
WORKDIR /app
COPY --from=build /app/dist/catalogo ./dist/catalogo
COPY --from=build /app/node_modules ./node_modules
ENV PORT=4000 NODE_ENV=production NODE_OPTIONS=--max-old-space-size=128
EXPOSE 4000
CMD ["node", "dist/catalogo/server/server.mjs"]
