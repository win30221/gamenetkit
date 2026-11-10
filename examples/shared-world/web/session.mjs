// Protocol state and connection lifetime; browser/Unity rendering is intentionally separate.
export class SharedWorldSession {
  constructor(createTransport, changed = () => {}) {
    this.createTransport = createTransport; this.changed = changed;
    this.generation = 0; this.transport = null; this.reset('disconnected');
  }
  reset(status, error = '') {
    this.status = status; this.error = error; this.playerID = null;
    this.tick = -1; this.players = []; this.seq = 0; this.changed(this);
  }
  disconnect(error = '') {
    ++this.generation;
    const old = this.transport; this.transport = null; old?.close();
    this.reset('disconnected', error);
  }
  connect(url) {
    this.disconnect(); const generation = this.generation;
    this.reset('connecting');
    const active = () => generation === this.generation;
    const fail = code => {
      if (!active()) return;
      ++this.generation; const old = this.transport; this.transport = null;
      old?.close(); this.reset('disconnected', code);
    };
    try {
      const transport = this.createTransport({kind:'ws', url,
        hello:JSON.stringify({type:'hello', protocol:1}),
        receive: data => { if (active()) { try { const message=JSON.parse(data); if(message?.type==='error')fail(message.code || 'server_error'); else this.apply(message); } catch { fail('invalid_server_message'); } } },
        closed: fail});
      if (!active()) { transport.close(); return; }
      this.transport = transport;
      transport.ready.catch(() => fail('connection_closed'));
    } catch { fail('connection_failed'); }
  }
  apply(message) {
    if (message?.type === 'error') throw Error(message.code);
    const welcome = message?.type === 'welcome';
    if ((!welcome && message?.type !== 'snapshot') || message.protocol !== 1 ||
        !Number.isSafeInteger(message.tick) || message.tick < 0 ||
        !Array.isArray(message.players) || message.players.length > 8) throw Error('invalid envelope');
    const ids = new Set();
    for (const p of message.players) {
      if (!Number.isSafeInteger(p.id) || p.id <= 0 || ids.has(p.id) ||
          !Number.isFinite(p.x) || !Number.isFinite(p.z) || Math.abs(p.x) > 8 || Math.abs(p.z) > 8 ||
          !Number.isSafeInteger(p.ack) || p.ack < 0) throw Error('invalid player');
      ids.add(p.id);
    }
    if (welcome) {
      if (this.status !== 'connecting' || !ids.has(message.playerID) ||
          message.tickRate !== 30 || message.snapshotRate !== 15) throw Error('invalid welcome');
      this.playerID = message.playerID; this.status = 'connected';
    } else {
      if (this.status !== 'connected' || !ids.has(this.playerID)) throw Error('snapshot before welcome or missing self');
      if (message.tick <= this.tick) return false;
    }
    this.tick = message.tick;
    this.players = message.players.map(p => ({...p}));
    this.changed(this); return true;
  }
  input(x, z) {
    if (this.status !== 'connected' || !this.transport) return Promise.resolve(false);
    if (![x,z].every(v => Number.isFinite(v) && Math.abs(v) <= 1)) throw Error('invalid input');
    const generation = this.generation;
    const transport = this.transport;
    const packet = JSON.stringify({type:'input', seq:++this.seq, x, z});
    return Promise.resolve().then(()=>generation === this.generation && transport.send(packet)).then(ok => generation === this.generation && !!ok)
      .catch(()=>{if(generation===this.generation)this.disconnect('send_failed');return false;});
  }
}
