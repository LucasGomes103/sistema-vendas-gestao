// Sessão do WhatsApp guardada no PostgreSQL: na nuvem o disco do contêiner some a cada reinício,
// e sem isso seria preciso ler o QR code de novo toda vez.
import { BufferJSON, initAuthCreds, proto } from 'baileys';
import pg from 'pg';

/** Converte a connection string do Npgsql (Host=...;Port=...;...) para a configuração do node-postgres. */
export function configuracaoDoBanco(connectionString) {
  const partes = Object.fromEntries(
    connectionString
      .split(';')
      .map((p) => p.trim())
      .filter(Boolean)
      .map((p) => {
        const i = p.indexOf('=');
        return [p.slice(0, i).trim().toLowerCase().replace(/\s+/g, ''), p.slice(i + 1).trim()];
      }),
  );
  const ssl = (partes.sslmode ?? '').toLowerCase();
  return {
    host: partes.host ?? partes.server,
    port: Number(partes.port ?? 5432),
    database: partes.database,
    user: partes.username ?? partes.userid ?? partes.user,
    password: partes.password,
    ssl: ssl && ssl !== 'disable' ? { rejectUnauthorized: false } : undefined,
    max: 3,
  };
}

/** Mesmo contrato do useMultiFileAuthState do Baileys, com uma linha por chave numa tabela. */
export async function useSessaoNoBanco(connectionString, prefixo) {
  const pool = new pg.Pool(configuracaoDoBanco(connectionString));
  await pool.query(`create table if not exists whatsapp_sessao (
    chave text primary key,
    valor text not null,
    atualizado_em timestamptz not null default now())`);

  const gravar = (chave, valor) =>
    pool.query(
      `insert into whatsapp_sessao (chave, valor) values ($1, $2)
       on conflict (chave) do update set valor = excluded.valor, atualizado_em = now()`,
      [prefixo + chave, JSON.stringify(valor, BufferJSON.replacer)],
    );
  const apagar = (chaves) => pool.query('delete from whatsapp_sessao where chave = any($1)', [chaves.map((c) => prefixo + c)]);

  const linha = await pool.query('select valor from whatsapp_sessao where chave = $1', [prefixo + 'creds']);
  const creds = linha.rows[0] ? JSON.parse(linha.rows[0].valor, BufferJSON.reviver) : initAuthCreds();

  return {
    state: {
      creds,
      keys: {
        get: async (tipo, ids) => {
          const r = await pool.query('select chave, valor from whatsapp_sessao where chave = any($1)', [
            ids.map((id) => `${prefixo}${tipo}-${id}`),
          ]);
          const achados = new Map(r.rows.map((l) => [l.chave, JSON.parse(l.valor, BufferJSON.reviver)]));
          const dados = {};
          for (const id of ids) {
            let valor = achados.get(`${prefixo}${tipo}-${id}`);
            if (tipo === 'app-state-sync-key' && valor) valor = proto.Message.AppStateSyncKeyData.fromObject(valor);
            dados[id] = valor;
          }
          return dados;
        },
        set: async (dados) => {
          const remover = [];
          const tarefas = [];
          for (const tipo in dados) {
            for (const id in dados[tipo]) {
              const valor = dados[tipo][id];
              if (valor) tarefas.push(gravar(`${tipo}-${id}`, valor));
              else remover.push(`${tipo}-${id}`);
            }
          }
          if (remover.length) tarefas.push(apagar(remover));
          await Promise.all(tarefas);
        },
      },
    },
    saveCreds: () => gravar('creds', creds),
    /** Desconectado pelo celular: apaga tudo para gerar um QR novo. */
    limpar: () => pool.query('delete from whatsapp_sessao where chave like $1', [prefixo + '%']),
  };
}
