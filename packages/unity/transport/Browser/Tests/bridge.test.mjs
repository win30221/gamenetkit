import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createTransport} from '../transport.js';

test('handle ABI isolates simultaneous connections, subpath runtime, replacement and disposal',async t=>{
  const names=['WebSocket','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab','document'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved)if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  class Socket {
    static instances=[];
    constructor(){this.readyState=0;this.bufferedAmount=0;this.sent=[];Socket.instances.push(this);}
    send(text){this.sent.push(text);}close(){this.readyState=3;}open(){this.readyState=1;this.onopen?.();}
  }
  const messages=[];globalThis.WebSocket=Socket;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(a,b)=>Object.assign(a,b);globalThis.UTF8ToString=v=>v;
  globalThis.SendMessage=(...v)=>messages.push(v);
  globalThis.document={baseURI:new URL('../',import.meta.url).href};
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  source=source.replace("import('/transport.js')",`import(${JSON.stringify(new URL('../transport.js',import.meta.url).href)})`);
  await import('data:text/javascript;base64,'+Buffer.from(source+'\n// handle ABI test').toString('base64'));
  const lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  const a=lib.RB_Create(),b=lib.RB_Create();assert.ok(a>0&&b>a);
  lib.RB_Open(a,'ws://localhost/a','hello-a','','A',0,'./transport.js');
  lib.RB_Open(b,'ws://localhost/b','hello-b','','B',0,'/transport.js');await flush();
  const first=Socket.instances[0],second=Socket.instances[1];first.open();second.open();await flush();
  first.onmessage({data:'one'});second.onmessage({data:'two'});
  assert.deepEqual(messages.map(v=>[v[0],v[2]]),[['A','one'],['B','two']]);
  lib.RB_CloseFor(a);first.onmessage({data:'stale'});second.onmessage({data:'still B'});
  lib.RB_SendTo(b,'outgoing B');await flush();assert.equal(second.sent.at(-1),'outgoing B');
  assert.deepEqual(messages.at(-1),['B','OnTransportMessage','still B']);
  lib.RB_Open(a,'ws://localhost/replacement','hello','','A2',0,'/transport.js');await flush();
  Socket.instances[2].open();await flush();first.onmessage({data:'old'});Socket.instances[2].onmessage({data:'new'});
  assert.equal(messages.at(-1)[0],'A2');
  lib.RB_Close();second.onmessage({data:'legacy close did not close B'});assert.equal(messages.at(-1)[2],'legacy close did not close B');
  lib.RB_Destroy(a);lib.RB_Destroy(b);assert.equal(Object.keys(RBLab.states).length,0);
  const handles=Array.from({length:64},()=>lib.RB_Create());assert.ok(handles.every(v=>v>0));assert.equal(lib.RB_Create(),0);
  for(const handle of handles)lib.RB_Destroy(handle);assert.ok(lib.RB_Create()>0);
});

test('actual WebGL bridge routes messages and errors through the SDK inbox callback, and isolates replacement lifetimes', async t => {
  const names=['WebSocket','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved) if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  const messages=[];
  class Socket {
    static instances=[];
    constructor(){this.readyState=0;this.bufferedAmount=0;Socket.instances.push(this);}
    send(){if(this.fail)throw new Error('injected write failure');}
    close(){this.readyState=3;}
    open(){this.readyState=1;this.onopen?.();}
  }
  globalThis.WebSocket=Socket;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(target,source)=>Object.assign(target,source);
  globalThis.SendMessage=(target,method,payload)=>messages.push({target,method,payload});
  globalThis.UTF8ToString=value=>value;
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  // Resolve the web server's /transport.js mount to the same SDK runtime in this harness.
  source=source.replace("import('/transport.js')",`import(${JSON.stringify(new URL('../transport.js',import.meta.url).href)})`);
  await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
  const lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  lib.RB_Connect('ws://localhost/ws','hello','','UnityTarget',0);await flush();
  const first=Socket.instances[0];assert.ok(first);first.open();await flush();
  first.onmessage({data:'opaque message 1'});
  assert.deepEqual(messages.at(-1),{target:'UnityTarget',method:'OnTransportMessage',payload:'opaque message 1'});
  first.fail=true;lib.RB_Send('opaque outgoing');await flush();
  assert.equal(messages.at(-1).method,'OnTransportMessage');assert.match(messages.at(-1).payload,/send_failed/);
  lib.RB_Close();const count=messages.length;
  first.onmessage({data:'stale message'});assert.equal(messages.length,count);
  lib.RB_Connect('ws://localhost/ws','hello','','Replacement',0);await flush();
  const second=Socket.instances[1];second.open();await flush();
  first.onmessage({data:'old'});second.onmessage({data:'opaque message 2'});
  assert.deepEqual(messages.at(-1),{target:'Replacement',method:'OnTransportMessage',payload:'opaque message 2'});
  lib.RB_Close();
});

test('actual WebGL RB_Send waits one slot for any text and close cancels a hung platform write',async t=>{
  const names=['WebTransport','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab','__transportSDK'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved)if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  class Transport {
    static instances=[];
    constructor(){
      this.ready=Promise.resolve();this.ended=deferred();this.closed=this.ended.promise;this.sent=[];
      this.control=new ReadableStream({start:c=>this.controlRead=c});
      this.incoming=new ReadableStream({start:c=>this.incomingRead=c});
      this.datagrams={readable:this.incoming,writable:{getWriter:()=>({write:async b=>{this.sent.push(new TextDecoder().decode(b));if(this.gate)await this.gate.promise;}})}};
      Transport.instances.push(this);
    }
    async createBidirectionalStream(){return {readable:this.control,writable:{getWriter:()=>({write:async()=>{}})}};}
    close(){if(this.stopped)return;this.stopped=true;this.ended.resolve();this.controlRead.close();this.incomingRead.close();}
  }
  const messages=[],sends=[];
  globalThis.WebTransport=Transport;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(target,source)=>Object.assign(target,source);
  globalThis.SendMessage=(...args)=>messages.push(args);globalThis.UTF8ToString=value=>value;
  // Observe returned promises without altering the actual SDK or RB_Send logic.
  globalThis.__transportSDK={createTransport(options){const c=createTransport(options),send=c.send;c.send=(...args)=>{const promise=send(...args);sends.push(promise);return promise;};return c;}};
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  source=source.replace("import('/transport.js')",'Promise.resolve(globalThis.__transportSDK)');
  await import('data:text/javascript;base64,'+Buffer.from(source+'\n// bounded WT bridge test').toString('base64'));
  const lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  lib.RB_Connect('https://localhost/wt','hello','ab'.repeat(32),'First',1);await flush();
  const old=Transport.instances[0];old.gate=deferred();
  lib.RB_Send('first');lib.RB_Send('second arbitrary text');await flush();
  assert.deepEqual(old.sent,['first']);
  old.gate.resolve();await flush();assert.deepEqual(await Promise.all(sends),[true,true]);
  assert.deepEqual(old.sent,['first','second arbitrary text']);
  old.gate=deferred();lib.RB_Send('hung');lib.RB_Send('cancel waiting');await flush();lib.RB_Close();
  const settled=[];sends[2].then(v=>settled.push(v));sends[3].then(v=>settled.push(v));await flush();
  assert.deepEqual(settled,[false,false]);assert.deepEqual(old.sent,['first','second arbitrary text','hung']);
  lib.RB_Connect('https://localhost/wt','hello','ab'.repeat(32),'Replacement',1);await flush();
  lib.RB_Send('fresh');assert.equal(await sends[4],true);
  old.gate.reject(Error('late rejection'));await flush();
  assert.deepEqual(Transport.instances[1].sent,['fresh']);assert.deepEqual(messages,[]);
  lib.RB_Close();
});

test('bridge retries a loading connection and reports resolved false once, then admits a replacement',async t=>{
  const names=['WebSocket','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved)if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  class Socket {
    static instances=[];
    constructor(){this.readyState=0;this.bufferedAmount=0;this.sent=[];Socket.instances.push(this);}
    send(text){this.sent.push(text);}close(){this.readyState=3;}open(){this.readyState=1;this.onopen?.();}
  }
  const messages=[];globalThis.WebSocket=Socket;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(target,source)=>Object.assign(target,source);globalThis.UTF8ToString=value=>value;
  globalThis.SendMessage=(target,method,payload)=>messages.push({target,method,payload});
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  source=source.replace("import('/transport.js')",`import(${JSON.stringify(new URL('../transport.js',import.meta.url).href)})`);
  await import('data:text/javascript;base64,'+Buffer.from(source+'\n// rejected submission bridge test').toString('base64'));
  const lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  const handle=lib.RB_Create();
  lib.RB_Open(handle,'ws://localhost/early','hello','','Early',0,'/transport.js');
  assert.equal(lib.RB_TrySendTo(handle,'before runtime is ready'),0);await flush();
  assert.equal(Socket.instances.length,1);assert.deepEqual(messages,[]);
  const early=Socket.instances[0];early.open();await flush();
  assert.equal(lib.RB_TrySendTo(handle,'retry after ready'),1);await flush();assert.deepEqual(early.sent,['hello','retry after ready']);
  lib.RB_Open(handle,'ws://localhost/busy','hello','','Busy',0,'/transport.js');await flush();
  const busy=Socket.instances[1];busy.open();await flush();busy.bufferedAmount=4096;
  assert.equal(lib.RB_TrySendTo(handle,'buffer is full'),1);await flush();
  assert.equal(messages.length,1);assert.equal(messages[0].target,'Busy');assert.match(messages[0].payload,/send_failed/);
  assert.equal(busy.readyState,3);assert.equal(lib.RB_TrySendTo(handle,'already failed'),0);await flush();assert.equal(messages.length,1);
  lib.RB_Open(handle,'ws://localhost/replacement','hello','','Fresh',0,'/transport.js');await flush();
  const fresh=Socket.instances[2];fresh.open();await flush();
  assert.equal(lib.RB_TrySendTo(handle,'accepted'),1);await flush();assert.deepEqual(fresh.sent,['hello','accepted']);
  lib.RB_CloseFor(handle);assert.equal(lib.RB_TrySendTo(handle,'after close'),0);await flush();assert.equal(messages.length,1);
  lib.RB_Destroy(handle);
});

test('synchronous error callbacks can reopen the same handle without the old failure poisoning it',async t=>{
  const names=['WebSocket','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved)if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  class Socket {
    static instances=[];
    constructor(){this.readyState=0;this.bufferedAmount=0;this.sent=[];Socket.instances.push(this);}
    send(text){this.sent.push(text);}close(){this.readyState=3;}open(){this.readyState=1;this.onopen?.();}
  }
  const messages=[];let lib,handle;
  globalThis.WebSocket=Socket;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(target,source)=>Object.assign(target,source);globalThis.UTF8ToString=value=>value;
  globalThis.SendMessage=(target,method,payload)=>{
    messages.push({target,method,payload});
    if(target==='Old'&&JSON.parse(payload).type==='error')lib.RB_Open(handle,'ws://localhost/replacement','new hello','','Fresh',0,'/transport.js');
  };
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  source=source.replace("import('/transport.js')",`import(${JSON.stringify(new URL('../transport.js',import.meta.url).href)})`);
  await import('data:text/javascript;base64,'+Buffer.from(source+'\n// reentrant replacement bridge test').toString('base64'));
  lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  handle=lib.RB_Create();lib.RB_Open(handle,'ws://localhost/old','old hello','','Old',0,'/transport.js');await flush();
  const old=Socket.instances[0];old.open();await flush();old.bufferedAmount=4096;
  assert.equal(lib.RB_TrySendTo(handle,'fails'),1);await flush();
  assert.equal(messages.length,1);assert.equal(messages[0].target,'Old');assert.equal(old.readyState,3);
  const fresh=Socket.instances[1];assert.ok(fresh);fresh.open();await flush();
  assert.equal(lib.RB_TrySendTo(handle,'new input'),1);await flush();
  assert.deepEqual(fresh.sent,['new hello','new input']);assert.equal(fresh.readyState,1);assert.equal(messages.length,1);
  lib.RB_Destroy(handle);
});

test('binary pointer ABI owns heap bytes and bounds a single polling inbox across epochs',async t=>{
  const names=['WebSocket','LibraryManager','mergeInto','SendMessage','UTF8ToString','RBLab','HEAPU8','setValue','performance'];
  const saved=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  t.after(()=>{for(const [name,descriptor] of saved)if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];});
  class Socket {
    static instances=[];
    constructor(){this.readyState=0;this.bufferedAmount=0;this.sent=[];Socket.instances.push(this);}
    send(value){this.sent.push(value);}close(){this.readyState=3;}open(){this.readyState=1;this.onopen?.();}
  }
  globalThis.WebSocket=Socket;globalThis.LibraryManager={library:{}};
  globalThis.mergeInto=(a,b)=>Object.assign(a,b);globalThis.UTF8ToString=v=>v;
  globalThis.SendMessage=()=>assert.fail('binary must never use SendMessage');
  let now=1234.5;globalThis.performance={now:()=>now};
  globalThis.HEAPU8=new Uint8Array(65536);const view=new DataView(HEAPU8.buffer);
  globalThis.setValue=(ptr,value,type)=>type==='double'?view.setFloat64(ptr,value,true):view.setInt32(ptr,value,true);
  let source=await readFile(new URL('../../Runtime/Plugins/WebGL/RealtimeTransport.jslib',import.meta.url),'utf8');
  source=source.replace("import('/transport.js')",`import(${JSON.stringify(new URL('../transport.js',import.meta.url).href)})`);
  await import('data:text/javascript;base64,'+Buffer.from(source+'\n// binary pointer ABI test').toString('base64'));
  const lib=LibraryManager.library;globalThis.RBLab=lib.$RBLab;
  const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
  const handle=lib.RB_Create();HEAPU8.set([1,2,3],32);
  lib.RB_OpenBinary(handle,'ws://binary',32,3,'/transport.js');HEAPU8[32]=99;await flush();
  const ws=Socket.instances[0];ws.open();await flush();assert.deepEqual([...ws.sent[0]],[1,2,3]);
  assert.equal(lib.RB_TrySendTo(handle,'wrong mode'),0);
  HEAPU8.set([7,8],32);assert.equal(lib.RB_TrySendBytes(handle,32,2),1);HEAPU8[32]=99;assert.deepEqual([...ws.sent[1]],[7,8]);
  ws.bufferedAmount=4096;assert.equal(lib.RB_TrySendBytes(handle,32,2),0);ws.bufferedAmount=0;
  assert.equal(lib.RB_ClockMS(),1234.5);
  ws.onmessage({data:new Uint8Array([9,10]).buffer});ws.onmessage({data:new ArrayBuffer(0)});now=1300;
  assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),2);assert.deepEqual([...HEAPU8.slice(128,130)],[9,10]);assert.equal(view.getInt32(16,true),0);assert.equal(view.getFloat64(8,true),1234.5);assert.equal(lib.RB_ClockMS(),1300);
  assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),0);assert.equal(view.getInt32(16,true),0);assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),-1);
  for(let i=0;i<65;i++)ws.onmessage({data:new Uint8Array([i]).buffer});
  assert.equal(RBLab.states[handle].packets.length,1);assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),0);assert.equal(view.getInt32(16,true),4);assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),-1);assert.equal(ws.readyState,3);
  lib.RB_OpenBinary(handle,'ws://new',32,2,'/transport.js');await flush();const fresh=Socket.instances[1];fresh.open();await flush();
  ws.onmessage({data:new Uint8Array([99]).buffer});fresh.onmessage({data:new Uint8Array([11]).buffer});lib.RB_InterruptBinary(handle);
  assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),1);assert.equal(HEAPU8[128],11);assert.equal(view.getInt32(16,true),0);
  assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),0);assert.equal(view.getInt32(16,true),1);
  lib.RB_OpenBinary(handle,'ws://wrong',32,2,'/transport.js');await flush();Socket.instances[2].open();await flush();Socket.instances[2].onmessage({data:'text'});
  assert.equal(lib.RB_PollPacket(handle,128,32768,8,16),0);assert.equal(view.getInt32(16,true),3);
  lib.RB_Destroy(handle);
});
