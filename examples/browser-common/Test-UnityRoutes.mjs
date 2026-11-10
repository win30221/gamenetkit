import assert from 'node:assert/strict';
import http from 'node:http';
import {setTimeout as delay} from 'node:timers/promises';

const origin=process.argv[process.argv.indexOf('--server')+1];
const runtime=await fetch(origin+'/transport.js');assert.equal(runtime.status,200);assert.match(runtime.headers.get('content-type'),/javascript/);
const source=await runtime.text();
const {createTransport}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
const redirect=await fetch(origin+'/unity',{redirect:'manual'});assert.equal(redirect.status,302);assert.equal(redirect.headers.get('location'),'/unity/');
assert.equal((await fetch(origin+'/unity/does-not-exist.wasm')).status,404);
assert.equal((await fetch(origin+'/unity/%2e%2e%2fProjectSettings%2fProjectSettings.asset')).status,404);
assert.equal((await fetch(origin+'/ws')).status,400);
const frames=[[],[]],connections=[];
async function until(check){let end=Date.now()+6000;while(!check()){assert.ok(Date.now()<end,'raw SDK WebSocket proxy timeout');await delay(10);}}
try{
  for(let i=0;i<2;i++){
    const connection=createTransport({kind:'ws',url:origin.replace('http:','ws:')+'/ws',hello:'{"type":"hello","protocol":1}',receive:text=>frames[i].push(JSON.parse(text)),closed:()=>{}});
    connections.push(connection);await connection.ready;
  }
  await until(()=>frames.every(list=>list.some(f=>f.type==='welcome'))&&frames[0].some(f=>f.players?.length===2));
  const welcome=frames[0].find(f=>f.type==='welcome'),id=welcome.playerID,start=welcome.players.find(p=>p.id===id).x;
  assert.ok(await connections[0].send('{"type":"input","seq":1,"x":1,"z":0}'));
  await until(()=>frames[1].some(f=>f.players?.some(p=>p.id===id&&p.x>start+.2)));
  await connections[0].send('{"type":"input","seq":2,"x":0,"z":0}');
  await new Promise((resolve,reject)=>{
    const request=http.request(new URL('/ws',origin),{headers:{Connection:'Upgrade',Upgrade:'websocket','Sec-WebSocket-Version':'13','Sec-WebSocket-Key':'dGhlIHNhbXBsZSBub25jZQ==',Origin:'http://untrusted.invalid'}},response=>{try{assert.equal(response.statusCode,403);response.resume();resolve();}catch(error){reject(error);}});
    request.on('upgrade',(_response,socket)=>{socket.destroy();reject(Error('Cross-origin upgrade was accepted'));});request.on('error',reject);request.end();
  });
  console.log('UNITY_ROUTES PASS actual SDK transport, two Go-authoritative peers, movement, same-origin boundary and static routes');
}finally{for(const connection of connections)connection.close();}
