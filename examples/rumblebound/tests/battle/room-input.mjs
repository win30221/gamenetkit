import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source=readFileSync(new URL('../../client/Assets/Plugins/WebGL/BattleClipboard.jslib',import.meta.url),'utf8');
function harness(fullscreen=false){
  const nodes=[],messages=[];
  function element(tag){const node={tag,style:{},children:[],listeners:{},value:'',setAttribute(k,v){this[k]=v;},appendChild(child){this.children.push(child);child.parent=this;},remove(){this.removed=true;},focus(){this.focused=true;},addEventListener(k,fn){this.listeners[k]=fn;},emit(k,event={}){this.listeners[k]?.({preventDefault(){},stopPropagation(){},...event});}};nodes.push(node);return node;}
  const canvas=element('canvas'),body=element('body');
  const document={body,createElement:element,fullscreenElement:fullscreen?canvas:null,exitFullscreen(){this.exited=true;return Promise.resolve();}};
  const context={document,Module:{canvas},LibraryManager:{library:{}},mergeInto:Object.assign,UTF8ToString:v=>v,SendMessage(target,method,json){messages.push({target,method,...JSON.parse(json)});}};
  vm.runInNewContext(source,context);const api=context.LibraryManager.library;context.RF_BattleRoomInput=api.$RF_BattleRoomInput;
  return {api,nodes,messages,document,canvas,open(value='',id=1,target='Battle'){api.RF_Battle_EditRoomCode(value,target,id);return {input:nodes.filter(n=>n.tag==='input').at(-1),form:nodes.filter(n=>n.tag==='form').at(-1),panel:context.RF_BattleRoomInput.active};}};
}
test('native text entry supports paste, normalizes code, and returns once',()=>{
  const h=harness(),{input,form}=h.open('abc');assert.equal(input.type,'text');assert.equal(input.maxLength,12);assert.equal(input.focused,true);
  input.value=' ab12cd ';form.emit('submit');form.emit('submit');
  assert.deepEqual(h.messages,[{target:'Battle',method:'OnBattleRoomCodeInput',requestId:1,status:'accepted',code:'AB12CD'}]);assert.equal(h.canvas.focused,true);
});
test('empty input stays open; cancel and stale owner are isolated',()=>{
  const h=harness(),{form}=h.open('   ');form.emit('submit');assert.equal(h.messages.length,0);
  h.api.RF_Battle_CancelRoomInput('Other');assert.equal(h.messages.length,0);
  h.api.RF_Battle_CancelRoomInput('Battle');assert.equal(h.messages[0].status,'cancelled');
  form.emit('submit');assert.equal(h.messages.length,1);
});
test('replacement invalidates old panel and Escape cancels current request',()=>{
  const h=harness(),old=h.open('OLD');h.open('NEW',2);old.form.emit('submit');
  const panel=h.nodes.findLast(n=>n.role==='dialog');panel.emit('keydown',{key:'Escape'});
  assert.deepEqual(h.messages.map(m=>[m.requestId,m.status]),[[1,'cancelled'],[2,'cancelled']]);assert.equal(panel.removed,true);
});
test('canvas fullscreen exits before mounting an accessible DOM input',()=>{
  const h=harness(true);h.open();assert.equal(h.document.exited,true);assert.equal(h.document.body.children.length,1);
});
