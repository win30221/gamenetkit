import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createTransport,maxMessageBytes} from '../transport.js';

test('WS and WT enforce the same UTF-8 byte limit for sending and receiving',async t=>{
  const WS=websocket(t),WT=webtransport(t);
  for(const kind of ['ws','wt']){
    const received=[],errors=[];
    const c=createTransport({...options(kind),receive:v=>received.push(v),closed:v=>errors.push(v)});
    const wire=kind==='ws'?WS.instances.at(-1):WT.instances.at(-1);
    if(kind==='ws')wire.open();else wire.opened.resolve();await c.ready;
    const exact='界'.repeat(Math.floor(maxMessageBytes/3))+'xx';assert.equal(Buffer.byteLength(exact),maxMessageBytes);
    assert.equal(await c.send(exact),true);
    await assert.rejects(c.send(exact+'x'),RangeError);
    await assert.rejects(c.send(new Uint8Array([1])),RangeError);
    if(kind==='ws')wire.onmessage({data:exact});else wire.incomingRead.enqueue(new TextEncoder().encode(exact));
    await flush();assert.deepEqual(received,[exact]);
    if(kind==='ws')wire.onmessage({data:exact+'x'});else wire.incomingRead.enqueue(new Uint8Array(maxMessageBytes+1));
    await flush();assert.deepEqual(received,[exact]);assert.deepEqual(errors,['invalid_server_message']);c.close();
  }
});

test('oversized hello and non-text WS data fail before reaching application callbacks',async t=>{
  const WS=websocket(t),errors=[];
  const large=createTransport({...options(),hello:'x'.repeat(maxMessageBytes+1),closed:v=>errors.push(v)});
  await assert.rejects(large.ready);assert.equal(WS.instances.length,0);assert.deepEqual(errors,['invalid_client_message']);
  for(const data of [new Uint8Array([1]),new ArrayBuffer(1)]){
    const received=[],closed=[];const c=createTransport({...options(),receive:v=>received.push(v),closed:v=>closed.push(v)});
    WS.instances.at(-1).open();await c.ready;WS.instances.at(-1).onmessage({data});
    assert.deepEqual(received,[]);assert.deepEqual(closed,['invalid_server_message']);c.close();
  }
});

test('WebTransport rejects invalid UTF-8 in datagrams and truncated reliable text',async t=>{
  const WT=webtransport(t);
  for(const reliable of [false,true]){
    const received=[],errors=[];const c=createTransport({...options('wt'),receive:v=>received.push(v),closed:v=>errors.push(v)});
    const wt=WT.instances.at(-1);wt.opened.resolve();await c.ready;
    if(reliable){wt.controlRead.enqueue(new TextEncoder().encode('valid\n'));await flush();wt.controlRead.enqueue(new Uint8Array([0xe7]));wt.controlRead.close();}
    else wt.incomingRead.enqueue(new Uint8Array([0xff]));
    await flush();assert.deepEqual(received,reliable?['valid']:[]);assert.equal(errors.length,1);c.close();
  }
});

const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();};
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
function websocket(t){
  const original=globalThis.WebSocket;
  class Fake {static instances=[];constructor(){this.readyState=0;this.bufferedAmount=0;this.sent=[];Fake.instances.push(this);}send(s){this.sent.push(s);}close(){this.readyState=3;this.closes=(this.closes||0)+1;}open(){this.readyState=1;this.onopen?.();}}
  globalThis.WebSocket=Fake;t.after(()=>globalThis.WebSocket=original);return Fake;
}
function webtransport(t){
  const original=globalThis.WebTransport;
  class Fake {
    static instances=[];
    constructor(){
      this.opened=deferred();this.ready=this.opened.promise;this.ended=deferred();this.closed=this.ended.promise;this.hellos=[];this.sent=[];this.streamCalls=0;
      this.control=new ReadableStream({start:c=>this.controlRead=c});this.incoming=new ReadableStream({start:c=>this.incomingRead=c});
      this.datagrams={readable:this.incoming,writable:new WritableStream({write:async b=>{this.sent.push(new TextDecoder().decode(b));if(this.writeGate)await this.writeGate.promise;}})};
      Fake.instances.push(this);
    }
    async createBidirectionalStream(){this.streamCalls++;return {readable:this.control,writable:new WritableStream({write:b=>this.hellos.push(new TextDecoder().decode(b))})};}
    close(){if(this.didClose)return;this.didClose=true;this.ended.resolve();this.controlRead.close();this.incomingRead.close();}
  }
  globalThis.WebTransport=Fake;t.after(()=>globalThis.WebTransport=original);return Fake;
}
function options(kind='ws'){return {kind,url:'https://127.0.0.1/wt',hello:'hello',hash:'ab'.repeat(32),receive(){},closed(){}};}

test('WebSocket preserves opaque text and local callback timestamps',async t=>{
  const Fake=websocket(t),messages=[],sent=[];let now=100;
  const c=createTransport({...options(),clock:()=>now,sent:(...args)=>sent.push(args),receive:(...args)=>messages.push(args)}),ws=Fake.instances[0];
  t.after(()=>c.close());ws.open();await c.ready;
  const values=['  {"extra":7, "type":"pong", "echo":0}  ','{"type":"ping",broken','arbitrary text 雪',''];
  for(const data of values){assert.equal(await c.send(data),true);now+=.25;ws.onmessage({data});}
  assert.deepEqual(ws.sent,['hello',...values]);
  assert.deepEqual(sent,values.map((data,i)=>[data,100+i*.25]));
  assert.deepEqual(messages,values.map((data,i)=>[data,100+(i+1)*.25]));
  ws.bufferedAmount=4096;assert.equal(await c.send('blocked'),false);assert.equal(sent.length,values.length);
  c.close();ws.onmessage({data:'late'});assert.equal(messages.length,values.length);
});

test('WebTransport preserves opaque datagrams and reliable text with receive timestamps',async t=>{
  const Fake=webtransport(t),messages=[],sent=[];let now=10;
  const c=createTransport({...options('wt'),clock:()=>now,sent:(...args)=>sent.push(args),receive:(...args)=>messages.push(args)}),wt=Fake.instances[0];
  t.after(()=>c.close());wt.opened.resolve();await c.ready;
  const data='  {"metadata":true, "type":"pong"}  ';
  assert.equal(await c.send(data),true);assert.deepEqual(sent,[[data,10]]);
  now=12.5;wt.incomingRead.enqueue(new TextEncoder().encode(data));await flush();
  now=20;wt.controlRead.enqueue(new TextEncoder().encode('not JSON\n'+data+'\n'));await flush();
  assert.deepEqual(messages,[[data,12.5],['not JSON',20],[data,20]]);
  assert.deepEqual(wt.sent,[data]);
});

test('WebTransport has one opt-in waiting slot and timestamps actual submission',async t=>{
  const Fake=webtransport(t),sent=[];let now=10;
  const c=createTransport({...options('wt'),clock:()=>now,sent:(...args)=>sent.push(args)}),wt=Fake.instances[0];
  t.after(()=>c.close());wt.opened.resolve();await c.ready;
  const gate=deferred();wt.writeGate=gate;
  const first=c.send('first');await flush();
  assert.equal(await c.send('default drops'),false);
  const queued=c.send('opaque queued text',{waitForSlot:true});
  assert.equal(await c.send('slot full',{waitForSlot:true}),false);
  assert.deepEqual(sent,[['first',10]]);
  now=100;wt.writeGate=undefined;gate.resolve();
  assert.equal(await first,true);assert.equal(await queued,true);
  assert.deepEqual(wt.sent,['first','opaque queued text']);
  assert.deepEqual(sent,[['first',10],['opaque queued text',100]]);
});

test('close promptly cancels a waiting slot and a hung write; late settlement cannot affect replacement',async t=>{
  const Fake=webtransport(t),failures=[],sent=[];
  const old=createTransport({...options('wt'),sent:data=>sent.push(data),closed:code=>failures.push(code)}),wt=Fake.instances[0];
  wt.opened.resolve();await old.ready;
  wt.writeGate=deferred();const active=old.send('old'),waiting=old.send('never submitted',{waitForSlot:true});
  await flush();old.close();
  const result=[];active.then(v=>result.push(['active',v]));waiting.then(v=>result.push(['waiting',v]));await flush();
  assert.deepEqual(result.sort(),[['active',false],['waiting',false]]);assert.deepEqual(sent,['old']);
  const fresh=createTransport(options('wt')),next=Fake.instances[1];next.opened.resolve();await fresh.ready;
  t.after(()=>fresh.close());assert.equal(await fresh.send('new'),true);
  wt.writeGate.reject(Error('late platform failure'));await flush();
  assert.equal(await fresh.send('new again'),true);assert.deepEqual(next.sent,['new','new again']);assert.deepEqual(failures,[]);
});

test('a failed WebTransport write rejects its waiter once and leaves a replacement usable',async t=>{
  const Fake=webtransport(t),failures=[];
  const c=createTransport({...options('wt'),closed:code=>failures.push(code)}),wt=Fake.instances[0];
  wt.opened.resolve();await c.ready;wt.writeGate=deferred();
  const active=c.send('fails'),waiting=c.send('discarded',{waitForSlot:true});await flush();
  wt.writeGate.reject(Error('injected failure'));
  assert.equal(await active,false);assert.equal(await waiting,false);
  assert.deepEqual(failures,['send_failed']);assert.deepEqual(wt.sent,['fails']);
  const fresh=createTransport(options('wt')),next=Fake.instances[1];next.opened.resolve();await fresh.ready;
  assert.equal(await fresh.send('works',{waitForSlot:true}),true);fresh.close();
});

test('cancelled WebSocket ignores late open and messages',async t=>{
  const Fake=websocket(t),messages=[],failures=[];
  const c=createTransport({...options(),receive:m=>messages.push(m),closed:c=>failures.push(c)}),ws=Fake.instances[0];
  c.close();await assert.rejects(c.ready,/cancelled/);ws.open();ws.onmessage({data:'stale'});ws.onclose();
  assert.deepEqual(ws.sent,[]);assert.deepEqual(messages,[]);assert.deepEqual(failures,[]);
});
test('WebSocket error is reported once and write queue is bounded',async t=>{
  const Fake=websocket(t),failures=[];const c=createTransport({...options(),closed:c=>failures.push(c)}),ws=Fake.instances[0];
  ws.open();await c.ready;assert.equal(await c.send('input'),true);ws.bufferedAmount=4096;assert.equal(await c.send('overflow'),false);
  ws.onerror();ws.onclose();assert.deepEqual(failures,['connection_closed']);assert.deepEqual(ws.sent,['hello','input']);
});
test('unopened connection times out and releases its transport',async t=>{
  const Fake=websocket(t);t.mock.timers.enable({apis:['setTimeout']});const failures=[];
  const c=createTransport({...options(),closed:c=>failures.push(c)});t.mock.timers.tick(8000);
  await assert.rejects(c.ready);assert.equal(Fake.instances[0].readyState,3);assert.deepEqual(failures,['connection_timeout']);
});
test('cancelled WebTransport cannot open a late bidirectional stream',async t=>{
  const Fake=webtransport(t),failures=[];const c=createTransport({...options('wt'),closed:c=>failures.push(c)}),wt=Fake.instances[0];
  c.close();await assert.rejects(c.ready);wt.opened.resolve();await flush();
  assert.equal(wt.streamCalls,0);assert.equal(wt.didClose,true);assert.deepEqual(failures,[]);
});
test('WebTransport handles split reliable messages and isolates old pending writes',async t=>{
  const Fake=webtransport(t),messages=[],failures=[];
  const first=createTransport({...options('wt'),receive:m=>messages.push(m),closed:c=>failures.push(c)}),old=Fake.instances[0];
  old.opened.resolve();await first.ready;
  old.controlRead.enqueue(new TextEncoder().encode('split '));old.controlRead.enqueue(new TextEncoder().encode('text\n'));
  await flush();assert.deepEqual(messages,['split text']);
  old.writeGate=deferred();const pending=first.send('old input');await flush();first.close();
  const next=createTransport(options('wt')),fresh=Fake.instances[1];fresh.opened.resolve();await next.ready;
  assert.equal(await next.send('new input'),true);old.writeGate.resolve();assert.equal(await pending,false);
  assert.deepEqual(fresh.sent,['new input']);assert.deepEqual(failures,[]);next.close();
});

test('WebTransport accepts coalesced bounded text messages on reliable stream',async t=>{
  const Fake=webtransport(t),messages=[],failures=[];
  const c=createTransport({...options('wt'),receive:m=>messages.push(m),closed:code=>failures.push(code)}),wt=Fake.instances[0];
  wt.opened.resolve();await c.ready;
  const first='a'.repeat(5000),second='b'.repeat(5000);
  wt.controlRead.enqueue(new TextEncoder().encode(first+'\n'+second+'\n'));
  await flush();
  assert.deepEqual(messages,[first,second]);assert.deepEqual(failures,[]);c.close();
});

test('WebTransport accepts generic UTF-8 text above 8 KiB',async t=>{
  const Fake=webtransport(t),messages=[],failures=[];
  const c=createTransport({...options('wt'),receive:m=>messages.push(m),closed:code=>failures.push(code)}),wt=Fake.instances[0];
  wt.opened.resolve();await c.ready;
  const message='界'.repeat(4000);
  assert.ok(Buffer.byteLength(message)>8192);assert.ok(Buffer.byteLength(message)<=16384);
  wt.controlRead.enqueue(new TextEncoder().encode(message+'\n'+message+'\n'));
  await flush();assert.deepEqual(messages,[message,message]);assert.deepEqual(failures,[]);c.close();
});

test('WebTransport bounds each complete reliable message and an unterminated buffer at 32 KiB',async t=>{
  const Fake=webtransport(t),encoder=new TextEncoder(),welcome='first message';
  for(const input of [
    ['x'.repeat(32769)+'\n'],
    [welcome+'\n'+'x'.repeat(32769)+'\n'],
    ['x'.repeat(16384),'x'.repeat(16385)],
    ['界'.repeat(10923)+'\n']
  ]){
    const messages=[],failures=[];
    const c=createTransport({...options('wt'),receive:m=>messages.push(m),closed:code=>failures.push(code)}),wt=Fake.instances.at(-1);
    wt.opened.resolve();await c.ready;
    for(const chunk of input){wt.controlRead.enqueue(encoder.encode(chunk));await flush();}
    assert.deepEqual(messages,input[0].startsWith(welcome)?[welcome]:[]);
    assert.deepEqual(failures,['invalid_server_message']);assert.equal(wt.didClose,true);c.close();
  }
});

test('WebTransport accepts exactly 32 KiB split across reliable reads',async t=>{
  const Fake=webtransport(t),messages=[],failures=[];
  const c=createTransport({...options('wt'),receive:m=>messages.push(m),closed:code=>failures.push(code)}),wt=Fake.instances[0];
  wt.opened.resolve();await c.ready;
  const message='x'.repeat(32768);
  wt.controlRead.enqueue(new TextEncoder().encode(message.slice(0,16384)));
  await flush();assert.deepEqual(messages,[]);assert.deepEqual(failures,[]);
  wt.controlRead.enqueue(new TextEncoder().encode(message.slice(16384)+'\n'));
  await flush();assert.deepEqual(messages,[message]);assert.deepEqual(failures,[]);c.close();
});
