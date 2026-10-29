# Ponte não oficial do WhatsApp (QR code). Só use com número dedicado e com a trava de números ligada no painel.
FROM node:24-alpine
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund
COPY servidor.mjs ./
ENV NODE_OPTIONS=--max-old-space-size=128
EXPOSE 5050
CMD ["node", "servidor.mjs"]
