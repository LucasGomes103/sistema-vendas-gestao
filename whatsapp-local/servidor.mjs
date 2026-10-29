// Ponte NÃO OFICIAL com o WhatsApp, para testes e demonstrações até haver um cliente.
// Conecta lendo um QR code (como o WhatsApp Web). Use um número reserva: o WhatsApp pode banir números
// que usam conexões não oficiais. Em produção, a loja usa a WhatsApp Cloud API oficial da Meta.
//
// Fluxo: mensagem recebida -> POST na API (/api/v1/webhooks/local) -> mesma fila das mensagens da Meta.
//        API quer enviar  -> POST aqui (/enviar) -> WhatsApp.

import http from 'node:http';
import makeWASocket, { Browsers, DisconnectReason, fetchLatestBaileysVersion, useMultiFileAuthState } from 'baileys';
import pino from 'pino';
import QRCode from 'qrcode';

const PORTA = Number(process.env.PORTA ?? 5050);
const API_URL = process.env.API_URL ?? 'http://localhost:5007';
const SEGREDO = process.env.WHATSAPP_LOCAL_SEGREDO;

if (!SEGREDO) {
  console.error('Defina WHATSAPP_LOCAL_SEGREDO (o mesmo valor de WhatsApp:Local:Segredo na API).');
  process.exit(1);
}

// Trava de segurança: por padrão a ponte só lê e responde os números desta lista (ex.: 5511987654321,5511912345678).
// Para atender qualquer pessoa (número dedicado só à loja), defina ATENDER_TODOS=sim.
const ATENDER_TODOS = (process.env.ATENDER_TODOS ?? '').toLowerCase() === 'sim';
const PERMITIDOS = new Set(
  (process.env.NUMEROS_PERMITIDOS ?? '')
    .split(',')
    .map((n) => n.replace(/\D/g, ''))
    .filter(Boolean),
);

function permitido(identificador) {
  return ATENDER_TODOS || PERMITIDOS.has(identificador);
}

const logger = pino({ level: 'warn' });
let sock = null;
let qrAtual = null;
let codigoAtual = null;
let status = 'iniciando';

/** "5511987654321@s.whatsapp.net" -> "5511987654321"; "123@lid" -> "lid:123". */
function paraIdentificador(jid) {
  const [usuario, dominio] = jid.split('@');
  const semDispositivo = usuario.split(':')[0];
  return dominio === 'lid' ? `lid:${semDispositivo}` : semDispositivo;
}

function paraJid(identificador) {
  return identificador.startsWith('lid:') ? `${identificador.slice(4)}@lid` : `${identificador}@s.whatsapp.net`;
}

function textoDa(mensagem) {
  const m = mensagem.message ?? {};
  return (
    m.conversation ??
    m.extendedTextMessage?.text ??
    m.imageMessage?.caption ??
    m.buttonsResponseMessage?.selectedDisplayText ??
    (m.imageMessage ? '[imagem]' : m.audioMessage ? '[áudio]' : m.stickerMessage ? '[figurinha]' : null)
  );
}

async function avisarApi(mensagem) {
  const remoto = mensagem.key.remoteJidAlt?.endsWith('@s.whatsapp.net') ? mensagem.key.remoteJidAlt : mensagem.key.remoteJid;
  const corpo = {
    de: paraIdentificador(remoto),
    nome: mensagem.pushName ?? null,
    id: mensagem.key.id,
    texto: textoDa(mensagem),
    timestamp: Number(mensagem.messageTimestamp ?? Math.floor(Date.now() / 1000)),
  };
  if (!corpo.texto) return;

  // Fora da lista: a mensagem fica só no celular, o sistema nem chega a ver.
  if (!permitido(corpo.de)) return;

  try {
    const r = await fetch(`${API_URL}/api/v1/webhooks/local`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-segredo': SEGREDO },
      body: JSON.stringify(corpo),
    });
    if (!r.ok) console.warn(`API respondeu ${r.status} ao receber mensagem`);
  } catch (e) {
    console.warn('API fora do ar, mensagem não entregue:', e.message);
  }
}

async function conectar() {
  const { state, saveCreds } = await useMultiFileAuthState('sessao');
  const { version, isLatest } = await fetchLatestBaileysVersion();
  console.log(`Versão do WhatsApp Web: ${version.join('.')}${isLatest ? '' : ' (não foi possível buscar a mais recente)'}`);

  sock = makeWASocket({
    version,
    auth: state,
    logger,
    // Identificação de navegador aceita tanto pelo QR quanto pelo código de pareamento.
    browser: Browsers.ubuntu('Chrome'),
    markOnlineOnConnect: false,
    syncFullHistory: false,
  });
  sock.ev.on('creds.update', saveCreds);

  sock.ev.on('connection.update', ({ connection, lastDisconnect, qr }) => {
    if (qr) {
      if (!qrAtual) console.log(`Abra http://localhost:${PORTA}/qr e conecte o número reserva (QR code ou código).`);
      qrAtual = qr;
      status = 'aguardando conexão';
    }
    if (connection === 'open') {
      qrAtual = null;
      codigoAtual = null;
      status = 'conectado';
      console.log('WhatsApp conectado.');
    }
    if (connection === 'close') {
      const codigo = lastDisconnect?.error?.output?.statusCode;
      const motivo = lastDisconnect?.error?.message ?? 'sem detalhe';
      console.warn(`Conexão fechada (código ${codigo}: ${motivo}).`);
      if (codigo === DisconnectReason.loggedOut) {
        status = 'desconectado: apague a pasta "sessao" e conecte de novo';
        console.error(status);
      } else {
        // 515 (restartRequired) é normal logo depois de ler o QR: reconecta com a sessão salva.
        status = codigo === DisconnectReason.restartRequired ? 'finalizando conexão' : 'reconectando';
        setTimeout(conectar, codigo === DisconnectReason.restartRequired ? 500 : 3000);
      }
    }
  });

  sock.ev.on('messages.upsert', async ({ messages, type }) => {
    if (type !== 'notify') return;
    for (const m of messages) {
      const jid = m.key.remoteJid ?? '';
      // Só conversas individuais, sem grupos, status ou mensagens enviadas por nós.
      if (m.key.fromMe || jid.endsWith('@g.us') || jid === 'status@broadcast') continue;
      await avisarApi(m);
    }
  });
}

function redirecionar(res) {
  res.writeHead(303, { location: '/qr' });
  res.end();
}

async function paginaConexao() {
  let conteudo;
  if (status === 'conectado') {
    conteudo = '<p style="font-size:20px">✅ <strong>Conectado.</strong> Pode fechar esta página.</p>';
  } else if (codigoAtual) {
    conteudo = `<p>No celular do número reserva: <b>WhatsApp &gt; Aparelhos conectados &gt; Conectar um aparelho &gt;
      Conectar com número de telefone</b> e digite:</p>
      <p style="font-size:40px;letter-spacing:4px;font-weight:700">${codigoAtual}</p>`;
  } else if (qrAtual) {
    conteudo = `<img src="${await QRCode.toDataURL(qrAtual, { width: 300 })}" alt="QR code">
      <p>No celular: <b>WhatsApp &gt; Aparelhos conectados &gt; Conectar um aparelho</b> e aponte para o QR.</p>
      <hr style="margin:24px 0"><p>QR não funcionou? Conecte com código:</p>
      <form method="post" action="/codigo">
        <input name="numero" placeholder="Número reserva: 5511987654321" style="padding:8px;width:260px">
        <button style="padding:8px 14px">Gerar código</button>
      </form>`;
  } else {
    conteudo = `<p>Status: <strong>${status}</strong></p>`;
  }

  // Só atualiza sozinha enquanto espera o QR; com o código na tela, não recarrega.
  const atualizar = codigoAtual || status === 'conectado' ? '' : '<meta http-equiv="refresh" content="15">';
  return `<!doctype html><meta charset="utf-8">${atualizar}<title>WhatsApp local</title>
    <body style="font-family:system-ui;display:grid;place-items:center;min-height:90vh;text-align:center;padding:16px">
    <div style="max-width:520px"><h2>WhatsApp local (não oficial)</h2>${conteudo}
    <p style="color:#a00;margin-top:24px">Use um número reserva, não o da loja.</p></div></body>`;
}

function responder(res, codigo, corpo, tipo = 'application/json') {
  res.writeHead(codigo, { 'content-type': tipo });
  res.end(tipo === 'application/json' ? JSON.stringify(corpo) : corpo);
}

http
  .createServer(async (req, res) => {
    try {
      if (req.method === 'GET' && req.url === '/status') return responder(res, 200, { status });

      if (req.method === 'GET' && req.url === '/qr') {
        return responder(res, 200, await paginaConexao(), 'text/html; charset=utf-8');
      }

      // Alternativa ao QR: código de 8 dígitos digitado no celular.
      if (req.method === 'POST' && req.url === '/codigo') {
        let dados = '';
        for await (const parte of req) dados += parte;
        const numero = new URLSearchParams(dados).get('numero')?.replace(/\D/g, '') ?? '';
        if (status === 'conectado') return redirecionar(res);
        if (numero.length < 12) {
          codigoAtual = 'Número inválido: use DDI + DDD + número, ex.: 5511987654321';
        } else {
          const codigo = await sock.requestPairingCode(numero);
          codigoAtual = codigo.match(/.{1,4}/g).join('-');
          console.log(`Código de pareamento: ${codigoAtual}`);
        }
        return redirecionar(res);
      }

      if (req.method === 'POST' && req.url === '/enviar') {
        if (req.headers['x-segredo'] !== SEGREDO) return responder(res, 401, { erro: 'segredo inválido' });
        if (status !== 'conectado') return responder(res, 503, { erro: `WhatsApp ${status}` });

        let dados = '';
        for await (const parte of req) dados += parte;
        const { para, texto } = JSON.parse(dados);
        if (!permitido(para)) return responder(res, 403, { erro: 'número fora da lista de permitidos' });
        const enviada = await sock.sendMessage(paraJid(para), { text: texto });
        return responder(res, 200, { id: enviada?.key?.id });
      }

      responder(res, 404, { erro: 'não encontrado' });
    } catch (e) {
      responder(res, 500, { erro: e.message });
    }
  })
  .listen(PORTA, '127.0.0.1', () => {
    console.log(`Ponte WhatsApp local em http://localhost:${PORTA} (QR em /qr)`);
    if (ATENDER_TODOS) console.warn('ATENÇÃO: atendendo TODOS os contatos deste número.');
    else if (PERMITIDOS.size === 0) console.warn('Nenhum número permitido: o sistema não vai ler nem responder ninguém (NUMEROS_PERMITIDOS).');
    else console.log(`Só atende: ${[...PERMITIDOS].join(', ')}`);
  });

conectar();
