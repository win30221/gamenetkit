import assert from 'node:assert/strict';
import {setTimeout as delay} from 'node:timers/promises';
import {readFile} from 'node:fs/promises';
import {createTransport} from '../../../packages/unity/transport/Browser/transport.js';
import {SharedWorldSession} from '../web/session.mjs';

const at=process.argv.indexOf('--server');
const origin=at<0?'http://127.0.0.1:18092':process.argv[at+1];
const url=new URL('/ws',origin);url.protocol=url.protocol==='https:'?'wss:':'ws:';
for(const path of ['/','/healthz','/transport.js','/app.js','/session.mjs']) {
  const response=await fetch(new URL(path,origin));
  assert.equal(response.status,200,path);
  if (/\.m?js$/.test(path)) assert.match(response.headers.get('content-type') || '', /^(text|application)\/javascript(?:;|$)/i, `${path}: browser module requires JavaScript MIME`);
}
assert.equal(await (await fetch(new URL('/transport.js',origin))).text(),await readFile(new URL('../../../packages/unity/transport/Browser/transport.js',import.meta.url),'utf8'));
const first=new SharedWorldSession(createTransport),second=new SharedWorldSession(createTransport);
const find=(session,id)=>session.players.find(p=>p.id===id);
async function until(predicate,drive=()=>{}){
  const deadline=performance.now()+5000;
  while(!predicate()){
    assert.ok(performance.now()<deadline,`shared world timeout: ${first.error} ${second.error}`);
    await drive();await delay(20);
  }
}
try {
  first.connect(url.href);second.connect(url.href);
  await until(()=>first.players.length===2&&second.players.length===2);
  const id1=first.playerID,id2=second.playerID;
  assert.notEqual(id1,id2);assert.deepEqual(first.players.map(p=>p.id),second.players.map(p=>p.id));
  const x1=find(first,id1).x,x2=find(second,id2).x;
  await until(()=>find(second,id1).x>x1+.5,()=>first.input(1,0));
  assert.equal(find(second,id2).x,x2,'input moved other player');
  const stop=first.seq+1;await first.input(0,0);
  await until(()=>find(second,id1).ack>=stop);
  const stopped=find(second,id1).x,tick=second.tick;
  await until(()=>second.tick>=tick+6);assert.equal(find(second,id1).x,stopped);
  await first.input(1,0);const ttlTick=second.tick;
  await until(()=>second.tick>=ttlTick+24);const ttlPosition=find(second,id1).x;
  await until(()=>second.tick>=ttlTick+30);assert.equal(find(second,id1).x,ttlPosition,'expired input drifted');
  first.disconnect();await until(()=>second.players.length===1);
  const priorTick=second.tick,z=find(second,id2).z;
  await until(()=>find(second,id2).z>z+.3,()=>second.input(0,1));await second.input(0,0);
  assert.ok(second.tick>priorTick);first.connect(url.href);
  await until(()=>first.players.length===2&&second.players.length===2);
  assert.ok(first.playerID>Math.max(id1,id2),'reconnect reused old ID');
  console.log('RF_SHARED_WORLD PASS '+JSON.stringify({transport:'real loopback WS',clients:2,sharedRoster:true,inputOwnership:true,zeroStop:true,idleStop:true,leaveSurvivor:true,rejoinNewID:true,playerIDs:[first.playerID,second.playerID],tick:second.tick,note:'Production JS session and SDK under Node; no browser renderer or Unity Player'}));
} finally { first.disconnect();second.disconnect(); }
