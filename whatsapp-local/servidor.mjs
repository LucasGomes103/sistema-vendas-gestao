// Ponte NÃO OFICIAL com o WhatsApp, para testes e demonstrações até haver um cliente.
// Conecta lendo um QR code (como o WhatsApp Web). Use um número reserva: o WhatsApp pode banir números
// que usam conexões não oficiais. Em produção, a loja usa a WhatsApp Cloud API oficial da Meta.
//
// Fluxo: mensagem recebida -> POST na API (/api/v1/webhooks/local) -> mesma fila das mensagens da Meta.
//        API quer enviar  -> POST aqui (/enviar) -> WhatsApp.

import http from 'node:http';
import makeWASocket, { DisconnectReason, fetchLatestBaileysVersion, useMultiFileAuthState } from 'baileys';
import pino from 'pino';
import QRCode from 'qrcode';

const PORTA = Number(process.env.PORTA ?? 5050);
const API_URL = process.env.API_URL ?? 'http://localhost:5007';
const SEGREDO = process.env.WHATSAPP_LOCAL_SEGREDO;

if (!SEGREDO) {
  console.error('Defina WHATSAPP_LOCAL_SEGREDO (o mesmo valor de WhatsApp:Local:Segredo na API).');
  process.exit(1);
}

const logger = pino({ level: 'warn' });
let sock = null;
let qrAtual = null;
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
  const { version } = await fetchLatestBaileysVersion();

  sock = makeWASocket({ version, auth: state, logger, markOnlineOnConnect: false, syncFullHistory: false });
  sock.ev.on('creds.update', saveCreds);

  sock.ev.on('connection.update', ({ connection, lastDisconnect, qr }) => {
    if (qr) {
      qrAtual = qr;
      status = 'aguardando QR code';
      console.log(`Abra http://localhost:${PORTA}/qr e leia o QR code com o WhatsApp do número reserva.`);
    }
    if (connection === 'open') {
      qrAtual = null;
      status = 'conectado';
      console.log('WhatsApp conectado.');
    }
    if (connection === 'close') {
      const codigo = lastDisconnect?.error?.output?.statusCode;
      if (codigo === DisconnectReason.loggedOut) {
        status = 'desconectado (apague a pasta "sessao" e leia o QR de novo)';
        console.error(status);
      } else {
        status = 'reconectando';
        setTimeout(conectar, 3000);
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

function responder(res, codigo, corpo, tipo = 'application/json') {
  res.writeHead(codigo, { 'content-type': tipo });
  res.end(tipo === 'application/json' ? JSON.stringify(corpo) : corpo);
}

http
  .createServer(async (req, res) => {
    try {
      if (req.method === 'GET' && req.url === '/status') return responder(res, 200, { status });

      if (req.method === 'GET' && req.url === '/qr') {
        const conteudo = qrAtual
          ? `<img src="${await QRCode.toDataURL(qrAtual, { width: 320 })}" alt="QR code"><p>No celular: WhatsApp &gt; Aparelhos conectados &gt; Conectar um aparelho.</p>`
          : `<p>Status: <strong>${status}</strong></p>`;
        return responder(
          res,
          200,
          `<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="5"><title>WhatsApp local</title>
           <body style="font-family:system-ui;display:grid;place-items:center;min-height:90vh;text-align:center">
           <div><h2>WhatsApp local (não oficial)</h2>${conteudo}<p style="color:#a00">Use um número reserva, não o da loja.</p></div></body>`,
          'text/html; charset=utf-8',
        );
      }

      if (req.method === 'POST' && req.url === '/enviar') {
        if (req.headers['x-segredo'] !== SEGREDO) return responder(res, 401, { erro: 'segredo inválido' });
        if (status !== 'conectado') return responder(res, 503, { erro: `WhatsApp ${status}` });

        let dados = '';
        for await (const parte of req) dados += parte;
        const { para, texto } = JSON.parse(dados);
        const enviada = await sock.sendMessage(paraJid(para), { text: texto });
        return responder(res, 200, { id: enviada?.key?.id });
      }

      responder(res, 404, { erro: 'não encontrado' });
    } catch (e) {
      responder(res, 500, { erro: e.message });
    }
  })
  .listen(PORTA, '127.0.0.1', () => console.log(`Ponte WhatsApp local em http://localhost:${PORTA} (QR em /qr)`));

conectar();
