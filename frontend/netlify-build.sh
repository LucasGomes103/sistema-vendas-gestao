#!/usr/bin/env bash
# Build do Netlify: monta painel (/painel) e vitrine (raiz) numa pasta só e gera os redirecionamentos.
set -euo pipefail

if [ -z "${API_URL:-}" ]; then
  echo "Defina a variável API_URL no Netlify (endereço da API no Render)." >&2
  exit 1
fi
API_URL="${API_URL%/}"

npx ng build painel -c publicado
npx ng build catalogo -c estatico

rm -rf publicar
mkdir -p publicar/painel
cp -r dist/catalogo/browser/. publicar/
cp -r dist/painel/browser/. publicar/painel/

# Regras em ordem: a primeira que casar vale. "200" = encaminha sem mudar o endereço no navegador.
cat > publicar/_redirects <<EOF
/api/*        ${API_URL}/api/:splat   200
/health       ${API_URL}/health       200
/privacidade  ${API_URL}/privacidade  200
/termos       ${API_URL}/termos       200
/painel       /painel/                301
/painel/*     /painel/index.html      200
/*            /index.html             200
EOF

echo "Publicação montada em publicar/ (API: ${API_URL})"
