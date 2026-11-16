// Rotas do painel (Angular). Arquivo que existe (js, css, ícone) é servido como está;
// qualquer outra rota (/painel/pedidos, /painel/conversas...) recebe o index.html do painel.
export async function onRequest({ request, env }) {
  const url = new URL(request.url);
  if (url.pathname === '/painel') return Response.redirect(`${url.origin}/painel/${url.search}`, 301);

  const ultimo = url.pathname.split('/').pop() ?? '';
  if (ultimo.includes('.')) return env.ASSETS.fetch(request);

  return env.ASSETS.fetch(new Request(new URL('/painel/', url), request));
}
