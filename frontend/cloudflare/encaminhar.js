// Repassa a requisição para a API no Render sem mudar o endereço no navegador.
// Assim painel e API ficam no mesmo domínio e o cookie de login (SameSite=Strict) continua valendo.
// Para trocar o endereço da API, defina a variável API_URL no projeto do Cloudflare Pages.
const API_PADRAO = 'https://sistema-vendas-api-0x0l.onrender.com';

export function encaminharParaApi({ request, env }) {
  const origem = new URL(request.url);
  const api = new URL(env.API_URL || API_PADRAO);
  const destino = new URL(origem.pathname + origem.search, api);

  const headers = new Headers(request.headers);
  headers.delete('host');
  // IP real do cliente (usado no limite de tentativas de login).
  const ip = request.headers.get('cf-connecting-ip');
  if (ip) headers.set('x-forwarded-for', ip);
  headers.set('x-forwarded-proto', 'https');
  headers.set('x-forwarded-host', origem.host);

  const temCorpo = !['GET', 'HEAD'].includes(request.method);
  return fetch(destino, {
    method: request.method,
    headers,
    body: temCorpo ? request.body : undefined,
    redirect: 'manual',
  });
}
