#!/usr/bin/env bash
# Build do Cloudflare Pages: monta painel (/painel) e vitrine (raiz) numa pasta só.
# Config no Cloudflare: diretório raiz "frontend", comando "bash build-publicacao.sh", saída "publicar".
# O encaminhamento de /api para o Render e as rotas do painel ficam nas Functions (frontend/functions).
set -euo pipefail

npx ng build painel -c publicado
npx ng build catalogo -c estatico

rm -rf publicar
mkdir -p publicar/painel
cp -r dist/catalogo/browser/. publicar/
cp -r dist/painel/browser/. publicar/painel/

# Só estes caminhos passam pelas Functions (contam na cota grátis de 100 mil/dia).
# O resto é arquivo estático; caminho sem arquivo cai no index.html da raiz (vitrine).
cat > publicar/_routes.json <<'JSON'
{
  "version": 1,
  "include": ["/api/*", "/health", "/privacidade", "/termos", "/painel", "/painel/*"],
  "exclude": []
}
JSON

# A raiz abre o login do painel; a vitrine de cada loja continua em /<loja> (ex.: /ecsfish).
cat > publicar/_redirects <<'TXT'
/  /painel/  302
TXT

echo "Publicação montada em publicar/"
