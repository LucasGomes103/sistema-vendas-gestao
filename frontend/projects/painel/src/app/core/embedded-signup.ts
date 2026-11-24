/**
 * Embedded Signup da Meta (botão "Conectar WhatsApp"). Carrega o SDK do Facebook só quando o dono clica, abre a
 * janela da Meta e junta as duas respostas: o código (do FB.login) e os ids da conta e do número (mensagem
 * WA_EMBEDDED_SIGNUP que a janela da Meta manda para esta página).
 */
export interface ResultadoSignup {
  code: string;
  phoneNumberId: string;
  wabaId: string;
}

interface FacebookSdk {
  init(opcoes: { appId: string; autoLogAppEvents: boolean; xfbml: boolean; version: string }): void;
  login(callback: (r: { authResponse?: { code?: string } | null }) => void, opcoes: Record<string, unknown>): void;
}

declare global {
  interface Window {
    FB?: FacebookSdk;
    fbAsyncInit?: () => void;
  }
}

let carregando: Promise<FacebookSdk> | null = null;

function carregarSdk(appId: string, versao: string): Promise<FacebookSdk> {
  carregando ??= new Promise<FacebookSdk>((resolve, reject) => {
    window.fbAsyncInit = () => {
      window.FB!.init({ appId, autoLogAppEvents: true, xfbml: false, version: versao });
      resolve(window.FB!);
    };
    const s = document.createElement('script');
    s.src = 'https://connect.facebook.net/pt_BR/sdk.js';
    s.async = true;
    s.defer = true;
    s.crossOrigin = 'anonymous';
    s.onerror = () => {
      carregando = null;
      reject(new Error('Não deu para abrir a janela da Meta. Confira se algum bloqueador de anúncios está ligado.'));
    };
    document.body.appendChild(s);
  });
  return carregando;
}

export async function abrirEmbeddedSignup(appId: string, configId: string, versao: string): Promise<ResultadoSignup> {
  const fb = await carregarSdk(appId, versao);
  return new Promise<ResultadoSignup>((resolve, reject) => {
    let code: string | null = null;
    let ids: { phoneNumberId: string; wabaId: string } | null = null;
    let terminou = false;

    const fechar = (erro?: string) => {
      if (terminou) return;
      if (erro) {
        terminou = true;
        window.removeEventListener('message', ouvir);
        reject(new Error(erro));
      } else if (code && ids) {
        terminou = true;
        window.removeEventListener('message', ouvir);
        resolve({ code, ...ids });
      }
    };

    const ouvir = (evento: MessageEvent) => {
      if (!/(^|\.)facebook\.com$/.test(new URL(evento.origin).hostname)) return;
      try {
        const dados = typeof evento.data === 'string' ? JSON.parse(evento.data) : evento.data;
        if (dados?.type !== 'WA_EMBEDDED_SIGNUP') return;
        if (dados.event === 'FINISH' || dados.event === 'FINISH_ONLY_WABA' || dados.event === 'FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING') {
          if (!dados.data?.phone_number_id) {
            fechar('A conta foi criada, mas nenhum número foi escolhido. Conecte de novo e escolha o número.');
            return;
          }
          ids = { phoneNumberId: String(dados.data.phone_number_id), wabaId: String(dados.data.waba_id) };
          fechar();
        } else if (dados.event === 'CANCEL') {
          fechar(dados.data?.error_message || 'Conexão cancelada antes do fim.');
        } else if (dados.event === 'ERROR') {
          fechar(dados.data?.error_message || 'A Meta informou um erro na conexão.');
        }
      } catch {
        // Mensagem de outro tipo vinda do Facebook: ignora.
      }
    };
    window.addEventListener('message', ouvir);

    fb.login(
      (r) => {
        if (r.authResponse?.code) {
          code = r.authResponse.code;
          fechar();
          // A mensagem com os ids chega logo depois; se não vier, a janela foi fechada sem terminar.
          setTimeout(() => fechar('A janela da Meta foi fechada antes de escolher o número.'), 15000);
        } else {
          fechar('Conexão cancelada antes do fim.');
        }
      },
      {
        config_id: configId,
        response_type: 'code',
        override_default_response_type: true,
        extras: { setup: {}, featureType: '', sessionInfoVersion: '3' },
      },
    );
  });
}
