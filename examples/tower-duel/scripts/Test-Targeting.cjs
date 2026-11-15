// Real public WebSocket matches. Uses ordinary create/join/ready/deploy/leave
// messages and canonical stats; no test endpoints, injected positions or damage.
const fs = require('node:fs');
const path = require('node:path');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const assert = (value, message) => { if (!value) throw Error(message); };

async function until(test, peers, timeout = 20000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const failure = peers.flatMap(peer => peer.errors)[0];
    if (failure) throw Error(failure);
    if (test()) return;
    await pause(25);
  }
  const states = peers.map(peer => ({ phase: peer.state?.phase, tick: peer.state?.tick,
    players: peer.state?.players.map(player => ({ connected: player.connected, ready: player.ready })) }));
  throw Error('Timed out waiting for authoritative targeting evidence: ' + JSON.stringify(states));
}

async function run(origin, output) {
  origin = origin.replace(/\/$/, '');
  fs.mkdirSync(output, { recursive: true });
  const response = await fetch(origin + '/api/config');
  assert(response.ok, 'Config request failed');
  const config = await response.json(), results = [];
  async function connect(peers, mode, room = '') {
    const ws = new WebSocket(origin.replace(/^http/, 'ws') + '/ws');
    const peer = { ws, errors: [], actions: new Map(), send: value => ws.send(JSON.stringify(value)) };
    peers.push(peer);
    ws.addEventListener('error', () => peer.errors.push('WebSocket connection error'));
    ws.addEventListener('close', event => {
      if (!peer.closing) peer.errors.push(`WebSocket closed during verification: ${event.code} ${event.reason}`);
    });
    ws.addEventListener('message', event => {
      const message = JSON.parse(String(event.data));
      if (message.type === 'welcome' || message.type === 'snapshot') {
        peer.state = message;
        peer.onSnapshot?.(message);
      } else if (message.type === 'action') peer.actions.set(message.action, message);
      else if (message.type === 'error') peer.errors.push(message.code);
    });
    await until(() => ws.readyState === WebSocket.OPEN, peers);
    peer.send({ type: 'hello', protocol: config.protocol, mode, room, name: 'Targeting regression',
      mapHash: config.mapHash, rulesHash: config.rulesHash, token: '' });
    await until(() => peer.state, peers);
    return peer;
  }
  async function deploy(peer, peers, card, x, z) {
    peer.send({ type: 'deploy', match: peer.state.match, action: 1, card, x, z });
    await until(() => peer.actions.has(1), peers);
    const ack = peer.actions.get(1);
    assert(ack.accepted, 'Deploy rejected: ' + ack.reason);
    return ack.unitID;
  }

  const cases = [
    ...[0, 1].flatMap(seat => [-5, 5].map(lane => ({ scenario: 'behind', seat, lane }))),
    { scenario: 'front', seat: 0, lane: -5 }
  ];
  for (const { scenario, seat, lane } of cases) {
    const peers = [], trace = [], forward = seat === 0 ? 1 : -1;
    let attackerID, defenderID, tower;
    try {
      const a = await connect(peers, 'create');
      const b = await connect(peers, 'join', a.state.room);
      const attacker = peers[seat], defender = peers[1 - seat];
      tower = a.state.towers.find(t => t.seat !== seat && t.kind === 'tower' && t.x === lane);
      assert(tower, 'Missing lane tower');
      a.onSnapshot = state => {
        const u = state.units.find(unit => unit.id === attackerID);
        const enemy = state.units.find(unit => unit.id === defenderID);
        if (!u || !enemy) return;
        trace.push({ tick: state.tick, x: u.x, z: u.z, target: u.target,
          lockedTarget: u.lockedTarget || 0, hitTick: u.hitTick || 0, hitTarget: u.hitTarget || 0,
          enemyDistance: Math.hypot(u.x - enemy.x, u.z - enemy.z),
          enemyEdgeDistance: Math.hypot(u.x - enemy.x, u.z - enemy.z) - u.radius - enemy.radius,
          towerEdgeDistance: Math.hypot(u.x - tower.x, u.z - tower.z) - u.radius - tower.radius,
          towerHP: state.towers.find(t => t.id === tower.id).hp });
      };
      await until(() => peers.every(peer => peer.state.players.every(player => player.connected)), peers);
      for (const peer of peers) peer.send({ type: 'ready', match: peer.state.match });
      await until(() => peers.every(peer => peer.state.phase === 'playing'), peers);
      attackerID = await deploy(attacker, peers, 'guard', lane + Math.sign(lane) * .5, -2.5 * forward);
      await until(() => {
        const u = a.state.units.find(unit => unit.id === attackerID);
        return u && u.z * forward > 4.7;
      }, peers);
      const x = lane + Math.sign(lane) * (scenario === 'behind' ? .5 : 1.5);
      const z = tower.z + (scenario === 'behind' ? 1.8 : -1.2) * forward;
      defenderID = await deploy(defender, peers, 'archer', x, z);
      await until(() => trace.some(frame => frame.hitTick), peers);
      const firstHit = trace.find(frame => frame.hitTick);
      const aggro = config.cards.find(card => card.id === 'guard').aggro;
      const approach = trace.find(frame => !frame.lockedTarget && frame.enemyDistance <= aggro);
      assert(approach, 'Did not observe an unlocked target decision with an enemy inside aggro');
      const want = scenario === 'behind' ? tower.id : defenderID;
      assert(approach.target === want, `Approach target ${approach.target}, expected ${want}`);
      assert(firstHit.hitTarget === want, `First hit ${firstHit.hitTarget}, expected ${want}`);
      if (scenario === 'behind') {
        assert(approach.towerEdgeDistance < approach.enemyEdgeDistance, 'Rear defender was not farther than the tower');
        assert(firstHit.towerHP < tower.hp, 'First tower attack dealt no damage');
        assert(trace.every(frame => Math.abs(frame.x - lane) < .65), 'Rear defender caused a tower detour');
      } else {
        assert(approach.enemyEdgeDistance < approach.towerEdgeDistance, 'Front defender was not the nearest target');
        assert(firstHit.towerHP === tower.hp, 'Front defender was skipped for a tower attack');
      }
      results.push({ scenario, seat, lane, passed: true, towerID: tower.id, attackerID, defenderID,
        approach, firstHit, trace });
      console.log(`PASS ${scenario} seat=${seat} lane=${lane}: approach=${approach.target}, hit=${firstHit.hitTarget}, towerHP=${firstHit.towerHP}`);
    } catch (error) {
      results.push({ scenario, seat, lane, passed: false, error: error.message, trace });
      throw error;
    } finally {
      for (const peer of peers) {
        peer.closing = true;
        if (peer.ws.readyState === WebSocket.OPEN && peer.state) peer.send({ type: 'leave', match: peer.state.match });
      }
      await pause(150);
      for (const peer of peers) peer.ws.close();
      fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ origin, protocol: config.protocol,
        mapHash: config.mapHash, rulesHash: config.rulesHash,
        passed: results.length === cases.length && results.every(result => result.passed), results }, null, 2));
    }
  }
}

if (require.main === module) {
  const origin = process.argv[2], output = process.argv[3];
  if (!origin || !output) throw Error('Usage: node Test-Targeting.cjs ORIGIN OUTPUT');
  run(origin, output).catch(error => { console.error(error.stack); process.exitCode = 1; });
}
module.exports = { run };
