namespace SistemaVendas.Api.Endpoints;

/// <summary>Páginas públicas exigidas pela Meta para publicar o app e pela LGPD.</summary>
public static class PaginasPublicasEndpoints
{
    public static void MapPaginasPublicasEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/privacidade", () => Results.Content(Privacidade, "text/html; charset=utf-8"))
            .ExcludeFromDescription();
        app.MapGet("/termos", () => Results.Content(Termos, "text/html; charset=utf-8"))
            .ExcludeFromDescription();
    }

    private const string Estilo = """
        <style>
          body { margin: 0; background: #f4fbfa; color: #161d1d; font: 16px/1.6 system-ui, -apple-system, 'Segoe UI', sans-serif; }
          main { max-width: 760px; margin: 0 auto; padding: 32px 20px 64px; }
          h1 { color: #006a6a; font-size: 28px; margin: 0 0 4px; }
          h2 { font-size: 18px; margin: 28px 0 8px; }
          p, li { color: #3f4948; }
          .data { color: #6f7979; font-size: 14px; }
        </style>
        """;

    private const string Privacidade = $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Política de privacidade</title>{{Estilo}}</head>
        <body><main>
          <h1>Política de privacidade</h1>
          <p class="data">Última atualização: 17 de novembro de 2026</p>

          <p>Esta política explica como o sistema de vendas usado pela loja trata os dados pessoais de quem conversa
          com a loja pelo WhatsApp e compra pelo catálogo, conforme a Lei Geral de Proteção de Dados (Lei 13.709/2018).</p>

          <h2>Quais dados coletamos</h2>
          <ul>
            <li>Nome e número de telefone do WhatsApp, recebidos quando você envia uma mensagem para a loja.</li>
            <li>Conteúdo das mensagens trocadas com a loja.</li>
            <li>CEP e endereço de entrega (rua, número, complemento, bairro e cidade), pedidos na conversa somente quando o pedido é para entregar.</li>
            <li>Histórico de pedidos e pagamentos.</li>
          </ul>

          <h2>Para que usamos</h2>
          <ul>
            <li>Responder dúvidas, informar preço, disponibilidade e frete, e atender pedidos (execução de contrato).</li>
            <li>Enviar avisos do pedido, como confirmação, pagamento pendente e saída para entrega.</li>
            <li>Enviar novidades e ofertas, somente se você aceitar receber (consentimento).</li>
          </ul>

          <h2>Atendimento automático</h2>
          <p>Parte do atendimento é feita por um assistente automático com inteligência artificial. Nome, telefone e
          cadastro não são enviados ao serviço de inteligência artificial: ele recebe apenas o texto da conversa, que
          inclui o endereço de entrega quando você o escreve. O assistente nunca pede CPF, documentos ou dados de cartão.
          A qualquer momento você pode pedir para falar com uma pessoa.</p>

          <h2>Compartilhamento</h2>
          <p>Os dados são usados apenas pela loja. Eles passam pelos serviços necessários ao atendimento: WhatsApp (Meta),
          provedor de pagamento Pix e serviço de hospedagem. Não vendemos dados pessoais.</p>

          <h2>Por quanto tempo guardamos</h2>
          <p>As conversas são guardadas por até 12 meses. Dados de pedidos são mantidos pelo prazo exigido pela legislação fiscal.</p>

          <h2>Seus direitos</h2>
          <ul>
            <li>Para parar de receber mensagens de marketing, responda <strong>"parar"</strong> ou <strong>"sair"</strong> no WhatsApp.</li>
            <li>Você pode pedir acesso, correção ou exclusão dos seus dados pelo próprio WhatsApp da loja. Na exclusão,
            o histórico de vendas é mantido sem identificação.</li>
          </ul>

          <h2>Contato</h2>
          <p>Dúvidas sobre esta política podem ser enviadas pelo WhatsApp da loja.</p>
        </main></body>
        </html>
        """;

    private const string Termos = $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Termos de uso</title>{{Estilo}}</head>
        <body><main>
          <h1>Termos de uso</h1>
          <p class="data">Última atualização: 17 de novembro de 2026</p>
          <p>Ao conversar com a loja pelo WhatsApp ou usar o catálogo, você concorda que as informações de preço e
          disponibilidade mostradas são válidas no momento da consulta e podem mudar, já que o estoque é de animais vivos.</p>
          <p>Reservas ficam válidas por 24 horas até a confirmação do pagamento. Depois disso, a reserva é cancelada
          e o animal volta ao estoque.</p>
          <p>O tratamento dos seus dados segue a <a href="/privacidade">política de privacidade</a>.</p>
        </main></body>
        </html>
        """;
}
