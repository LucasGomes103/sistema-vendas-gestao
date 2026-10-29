#!/usr/bin/env bash
# Instala o sistema num servidor Ubuntu novo (ex.: Google Cloud e2-micro).
# Uso, dentro da pasta do projeto clonado:  bash infra/producao/instalar.sh
# As senhas e chaves são digitadas por você aqui e ficam só neste servidor (infra/producao/.env).
set -euo pipefail

PASTA="$(cd "$(dirname "$0")" && pwd)"
ENV="$PASTA/.env"

echo "== 1/5 Memória extra (swap de 2 GB) =="
if ! swapon --show | grep -q /swapfile; then
  sudo fallocate -l 2G /swapfile
  sudo chmod 600 /swapfile
  sudo mkswap /swapfile >/dev/null
  sudo swapon /swapfile
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab >/dev/null
fi
free -h | head -2

echo "== 2/5 Docker =="
if ! command -v docker >/dev/null; then
  curl -fsSL https://get.docker.com | sudo sh
  sudo usermod -aG docker "$USER"
fi
DOCKER="sudo docker"

echo "== 3/5 Configuração =="
if [ ! -f "$ENV" ]; then
  read -rp "Endereço do DuckDNS (ex.: ecsfish.duckdns.org): " DOMINIO
  read -rp "Token do DuckDNS (Enter para pular): " DUCKDNS_TOKEN
  read -rp "Nome da loja (ex.: ECS Fish): " LOJA_NOME
  read -rp "Identificador da loja no link da vitrine (ex.: ecsfish): " LOJA_SLUG
  read -rp "Seu nome: " DONO_NOME
  read -rp "Seu e-mail (login do painel): " DONO_EMAIL
  read -rsp "Senha do painel (não aparece ao digitar): " DONO_SENHA; echo
  read -rsp "Chave do Gemini (Enter para usar o robô sem IA): " GEMINI_CHAVE; echo
  read -rsp "Senha para a tela do QR do WhatsApp (usuário: admin): " QR_SENHA; echo

  QR_SENHA_HASH=$($DOCKER run --rm caddy:2-alpine caddy hash-password --plaintext "$QR_SENHA")
  umask 077
  cat > "$ENV" <<EOF
DOMINIO=$DOMINIO
DUCKDNS_DOMINIO=${DOMINIO%%.duckdns.org}
DUCKDNS_TOKEN=$DUCKDNS_TOKEN
LOJA_NOME=$LOJA_NOME
LOJA_SLUG=$LOJA_SLUG
DONO_NOME=$DONO_NOME
DONO_EMAIL=$DONO_EMAIL
DONO_SENHA=$DONO_SENHA
GEMINI_CHAVE=$GEMINI_CHAVE
QR_USUARIO=admin
QR_SENHA_HASH='$QR_SENHA_HASH'
POSTGRES_SENHA=$(openssl rand -hex 24)
PONTE_SEGREDO=$(openssl rand -hex 24)
WHATSAPP_PROVEDOR=Local
EOF
  echo "Configuração salva em $ENV (só o seu usuário consegue ler)."
fi

echo "== 4/5 Montando o sistema (pode levar 15 a 30 min neste servidor) =="
cd "$PASTA"
# Uma imagem por vez para não faltar memória.
for servico in api catalogo caddy whatsapp; do
  $DOCKER compose --env-file "$ENV" build "$servico"
done

echo "== 5/5 Subindo =="
$DOCKER compose --env-file "$ENV" up -d
sleep 20
$DOCKER compose --env-file "$ENV" ps

# A senha inicial só serve para criar o usuário na primeira subida; não fica guardada.
if $DOCKER compose --env-file "$ENV" logs api 2>/dev/null | grep -q "criada com o usuário dono"; then
  sed -i 's/^DONO_SENHA=.*/DONO_SENHA=/' "$ENV"
fi

DOMINIO=$(grep '^DOMINIO=' "$ENV" | cut -d= -f2)
SLUG=$(grep '^LOJA_SLUG=' "$ENV" | cut -d= -f2)
echo
echo "Pronto!"
echo "  Painel:              https://$DOMINIO/painel"
echo "  Vitrine da loja:     https://$DOMINIO/$SLUG"
echo "  Conectar WhatsApp:   https://$DOMINIO/whatsapp  (usuário admin)"
echo "Lembre de liberar os números de teste em Configuração > WhatsApp antes de conectar."
