// Ponte NÃO OFICIAL com o WhatsApp, para testes e demonstrações até haver um cliente.
// Conecta lendo um QR code (como o WhatsApp Web). Use um número reserva: o WhatsApp pode banir números
// que usam conexões não oficiais. Em produção, a loja usa a WhatsApp Cloud API oficial da Meta.
//
// Fluxo: mensagem recebida -> POST na API (/api/v1/webhooks/local) -> mesma fila das mensagens da Meta.
//        API quer enviar  -> POST aqui (/enviar) -> WhatsApp.

import http from 'node:http';
import { rm } from 'node:fs/promises';
import makeWASocket, { Browsers, DisconnectReason, fetchLatestBaileysVersion, useMultiFileAuthState } from 'baileys';
import pino from 'pino';
import QRCode from 'qrcode';
import { useSessaoNoBanco } from './sessao-banco.mjs';

const PORTA = Number(process.env.PORTA ?? 5050);
// No servidor (Docker) a ponte escuta na rede interna; no PC, só na própria máquina.
const HOST = process.env.HOST ?? '127.0.0.1';
const PASTA_SESSAO = process.env.PASTA_SESSAO ?? 'sessao';
const API_URL = process.env.API_URL ?? 'http://localhost:5007';
const SEGREDO = process.env.WHATSAPP_LOCAL_SEGREDO;
// Na nuvem a sessão fica no banco (connection string no formato do Npgsql); no PC, na pasta "sessao".
const SESSAO_BANCO = process.env.SESSAO_BANCO;
const PREFIXO_SESSAO = `${process.env.LOJA_SLUG ?? 'loja'}:`;
// Render grátis dorme após 15 min sem acessos; a ponte precisa ficar conectada, então se visita sozinha.
const MANTER_ACORDADO_URL = process.env.MANTER_ACORDADO_URL;

if (!SEGREDO) {
  console.error('Defina WHATSAPP_LOCAL_SEGREDO (o mesmo valor de WhatsApp:Local:Segredo na API).');
  process.exit(1);
}

// Trava de segurança: a lista de quem pode conversar com o robô é configurada no painel
// (Configuração > WhatsApp) e buscada na API a cada 30 s. Sem resposta da API, ninguém é atendido.
let atenderTodos = false;
let permitidos = new Set();
let travaCarregada = false;

async function atualizarTrava() {
  try {
    const r = await fetch(`${API_URL}/api/v1/webhooks/local/permitidos`, { headers: { 'x-segredo': SEGREDO } });
    if (!r.ok) throw new Error(`API respondeu ${r.status}`);
    const trava = await r.json();
    const antes = `${atenderTodos}|${[...permitidos].join(',')}`;
    atenderTodos = trava.atenderTodos === true;
    permitidos = new Set(trava.numeros ?? []);
    const depois = `${atenderTodos}|${[...permitidos].join(',')}`;
    if (!travaCarregada || antes !== depois) {
      if (atenderTodos) console.warn('ATENÇÃO: atendendo TODOS os contatos deste número.');
      else if (permitidos.size === 0) console.log('Nenhum número permitido no painel: o sistema não lê nem responde ninguém.');
      else console.log(`Só atende: ${[...permitidos].join(', ')}`);
    }
    travaCarregada = true;
  } catch (e) {
    // API fora do ar: fecha a trava por segurança.
    atenderTodos = false;
    permitidos = new Set();
    if (travaCarregada) console.warn('Não foi possível ler a trava na API; ninguém será atendido até voltar:', e.message);
    travaCarregada = false;
  }
}

function permitido(identificador) {
  return atenderTodos || permitidos.has(identificador);
}

await atualizarTrava();
setInterval(atualizarTrava, 30_000);

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

async function abrirSessao() {
  if (SESSAO_BANCO) return useSessaoNoBanco(SESSAO_BANCO, PREFIXO_SESSAO);
  const sessao = await useMultiFileAuthState(PASTA_SESSAO);
  return { ...sessao, limpar: () => rm(PASTA_SESSAO, { recursive: true, force: true }) };
}

async function conectar() {
  const { state, saveCreds, limpar } = await abrirSessao();
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
        // Desconectado pelo celular (ou pelo painel): apaga a sessão e volta a mostrar um QR novo.
        status = 'desconectado';
        qrAtual = null;
        codigoAtual = null;
        console.warn('Sessão encerrada; gerando um QR code novo.');
        limpar().then(() => setTimeout(conectar, 1000));
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
  res.writeHead(303, { location: 'qr' });
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
      <form method="post" action="codigo">
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

      // Usado pelo painel (através da API) para conectar o número sem acessar esta máquina.
      if (req.url === '/estado' || req.url === '/estado/codigo' || req.url === '/estado/desconectar') {
        if (req.headers['x-segredo'] !== SEGREDO) return responder(res, 401, { erro: 'segredo inválido' });

        if (req.method === 'POST' && req.url === '/estado/codigo') {
          let dados = '';
          for await (const parte of req) dados += parte;
          const numero = String(JSON.parse(dados || '{}').numero ?? '').replace(/D/g, '');
          if (status === 'conectado') return responder(res, 409, { erro: 'já está conectado' });
          if (numero.length < 12) return responder(res, 400, { erro: 'Use DDI + DDD + número, ex.: 5511987654321' });
          const codigo = await sock.requestPairingCode(numero);
          codigoAtual = codigo.match(/.{1,4}/g).join('-');
        } else if (req.method === 'POST' && req.url === '/estado/desconectar') {
          if (status === 'conectado') await sock.logout();
        } else if (req.method !== 'GET') {
          return responder(res, 405, { erro: 'método não permitido' });
        }

        return responder(res, 200, {
          status,
          conectado: status === 'conectado',
          numero: status === 'conectado' ? paraIdentificador(sock?.user?.id ?? '') : null,
          qr: qrAtual && !codigoAtual ? await QRCode.toDataURL(qrAtual, { width: 280, margin: 1 }) : null,
          codigo: codigoAtual,
        });
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
  .listen(PORTA, HOST, () => console.log(`Ponte WhatsApp local em http://localhost:${PORTA} (QR em /qr)`));

conectar();

if (MANTER_ACORDADO_URL) {
  setInterval(() => fetch(MANTER_ACORDADO_URL).catch(() => {}), 10 * 60_000);
  console.log(`Mantendo o serviço acordado: visita ${MANTER_ACORDADO_URL} a cada 10 min.`);
}
