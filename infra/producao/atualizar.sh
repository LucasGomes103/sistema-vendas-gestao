#!/usr/bin/env bash
# Atualiza o servidor com a última versão do GitHub.  Uso: bash infra/producao/atualizar.sh
set -euo pipefail
PASTA="$(cd "$(dirname "$0")" && pwd)"
cd "$PASTA/../.."
git pull --ff-only
cd "$PASTA"
for servico in api catalogo caddy whatsapp; do
  sudo docker compose --env-file .env build "$servico"
done
sudo docker compose --env-file .env up -d
sudo docker image prune -f >/dev/null
sudo docker compose --env-file .env ps
