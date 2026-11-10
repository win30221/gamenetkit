import {test} from 'node:test';
import assert from 'node:assert/strict';
import {SharedWorldSession} from '../web/session.mjs';

const player=(id,x=0)=>({id,x,z:0,ack:0});
const welcome={type:'welcome',protocol:1,playerID:1,tick:0,tickRate:30,snapshotRate:15,players:[player(1),player(2,2)]};
function harness(){const connections=[];const session=new SharedWorldSession(options=>{const c={options,ready:Promise.resolve(),sent:[],close(){this.closed=true;},send(data){this.sent.push(JSON.parse(data));return Promise.resolve(true);}};connections.push(c);return c;});return {session,connections};}
test('welcome and complete snapshots copy state, remove departures and reject old ticks',()=>{
  const {session}=harness();session.connect('ws://test');session.apply(welcome);
  const next={type:'snapshot',protocol:1,tick:2,players:[player(1,3)]};
  assert.equal(session.apply(next),true);next.players[0].x=7;
  assert.deepEqual(session.players,[player(1,3)]);
  assert.equal(session.apply({...next,tick:1}),false);assert.equal(session.players[0].x,3);
  assert.throws(()=>session.apply({...next,tick:3,players:[player(1),player(1)]}));
  assert.equal(session.tick,2);assert.throws(()=>session.apply({...next,tick:3,players:[player(2)]}));
});
test('replacement cancels deferred input and ignores every old callback',async()=>{
  const {session,connections}=harness();session.connect('ws://test');session.apply(welcome);
  const old=connections[0],pending=session.input(1,0);session.connect('ws://test');
  const fresh=connections[1];assert.equal(await pending,false);assert.deepEqual(fresh.sent,[]);assert.equal(old.closed,true);
  old.options.receive(JSON.stringify(welcome));old.options.closed('old_error');assert.equal(session.status,'connecting');
  fresh.options.receive(JSON.stringify(welcome));await session.input(0,0);
  assert.deepEqual(fresh.sent,[{type:'input',seq:1,x:0,z:0}]);
  session.disconnect();assert.equal(session.players.length,0);assert.equal(session.tick,-1);
});
test('server failures, malformed data and send failures clear the active state',async()=>{
  for(const data of ['bad JSON',JSON.stringify({...welcome,protocol:2}),JSON.stringify({type:'error',code:'world_full'})]){
    const {session,connections}=harness();session.connect('ws://test');connections[0].options.receive(data);
    assert.equal(session.status,'disconnected');assert.equal(session.players.length,0);
    if(data.includes('world_full'))assert.equal(session.error,'world_full');
  }
  const {session,connections}=harness();session.connect('ws://test');session.apply(welcome);
  connections[0].send=()=>{throw Error('write');};assert.equal(await session.input(1,0),false);
  assert.equal(session.error,'send_failed');assert.equal(session.players.length,0);
});
test('inputs require a welcome and reject nonfinite or out-of-range axes',async()=>{
  const {session,connections}=harness();session.connect('ws://test');assert.equal(await session.input(1,0),false);
  session.apply(welcome);for(const axis of [NaN,Infinity,2])assert.throws(()=>session.input(axis,0));
  await session.input(1,0);await session.input(0,0);assert.deepEqual(connections[0].sent.map(x=>x.seq),[1,2]);
});
