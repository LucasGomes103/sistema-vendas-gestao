#!/bin/sh
# Sobe a ponte do WhatsApp em segundo plano (reinicia se cair) e a API em primeiro plano.
# A ponte usa as mesmas variáveis da API: nada novo para configurar no Render.

if [ "${WhatsApp__Provedor:-}" = "Local" ]; then
  (
    cd /ponte
    export WHATSAPP_LOCAL_SEGREDO="${WhatsApp__Local__Segredo}"
    export SESSAO_BANCO="${ConnectionStrings__Banco}"
    export LOJA_SLUG="${WhatsApp__Local__LojaSlug}"
    export API_URL="http://127.0.0.1:8080"
    export HOST=127.0.0.1 PORTA=5050
    export NODE_OPTIONS=--max-old-space-size=128
    # Endereço público dado pelo Render: visitar a si mesmo evita que o serviço durma e derrube o WhatsApp.
    [ -n "${RENDER_EXTERNAL_URL:-}" ] && export MANTER_ACORDADO_URL="${RENDER_EXTERNAL_URL}/health"
    # Espera a API subir (migrações) antes de ligar a ponte.
    sleep 20
    while true; do
      node servidor.mjs
      echo "Ponte do WhatsApp parou; reiniciando em 5 s."
      sleep 5
    done
  ) &
fi

exec dotnet SistemaVendas.Api.dll
