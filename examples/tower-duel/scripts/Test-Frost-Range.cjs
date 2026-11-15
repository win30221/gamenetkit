// Real public/local WebSocket matches. No changed rules or injected game state.
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message)};
async function until(test,timeout=45000){const end=Date.now()+timeout;while(Date.now()<end){if(test())return;await pause(30)}throw Error('Timed out waiting for authoritative frost evidence')}
async function run(origin='http://127.0.0.1:18143',output=path.join(root,'artifacts/tower-duel/frost-range/network')){
 origin=origin.replace(/\/$/,'');fs.mkdirSync(output,{recursive:true});
 const config=await(await fetch(origin+'/api/config')).json(),cards=new Map(config.cards.map(c=>[c.id,c]));
 async function scenario(casterSeat){
  const records=[];let tracking=false;const samples=[];
  async function connect(mode,room=''){
   const socket=new WebSocket(origin.replace(/^http/,'ws')+'/ws'),r={socket,state:null,actions:new Map(),nextAction:0,errors:[]};records.push(r);
   socket.addEventListener('message',event=>{
    const m=JSON.parse(String(event.data));
    if(m.type==='welcome'||m.type==='snapshot'){
     const prior=r.state;if(prior?.match===m.match&&prior.handVersion>m.handVersion){m.hand=prior.hand;m.handVersion=prior.handVersion;m.energy=prior.energy}
     r.state=m;
     if(tracking&&r===records[casterSeat])for(const effect of m.effects){
      if(effect.kind!=='frost'||m.tick<effect.impact||m.tick>=effect.end)continue;
      for(const unit of m.units){
       const distance=Math.hypot(unit.x-effect.x,unit.z-effect.z);
       samples.push({tick:m.tick,id:unit.id,seat:unit.seat,distance,radius:effect.radius,slowed:unit.slowed,readyTick:unit.readyTick});
      }
     }
    }else if(m.type==='action'){
     r.actions.set(m.action,m);
     if(r.state&&m.handVersion>=r.state.handVersion){r.state.hand=m.hand;r.state.handVersion=m.handVersion;r.state.energy=m.energy}
    }else if(m.type==='error')r.errors.push(m.code);
   });
   await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true})});
   socket.send(JSON.stringify({type:'hello',protocol:config.protocol,mode,room,name:'Frost range QA',mapHash:config.mapHash,rulesHash:config.rulesHash,token:''}));
   await until(()=>r.state,10000);return r;
  }
  async function deploy(r,card,x,z){
   await until(()=>r.state.energy>=cards.get(card).cost+.02);
   const action=++r.nextAction;r.socket.send(JSON.stringify({type:'deploy',match:r.state.match,action,card,x,z}));
   await until(()=>r.actions.has(action),10000);const ack=r.actions.get(action);assert(ack.accepted,'Deployment rejected: '+ack.reason);return ack;
  }
  try{
   const a=await connect('create'),b=await connect('join',a.state.room),caster=records[casterSeat],victim=records[1-casterSeat],direction=1-casterSeat*2;
   for(const r of records)r.socket.send(JSON.stringify({type:'ready',match:r.state.match}));
   await until(()=>records.every(r=>r.state.phase==='playing'));
   while(!caster.state.hand.includes('frost')){
    const card=caster.state.hand.map(id=>cards.get(id)).filter(c=>c.type==='troop').sort((x,y)=>x.cost-y.cost)[0];
    assert(card,'No troop available to cycle to frost');await deploy(caster,card.id,-5.5,-10.5*direction);
   }
   await until(()=>caster.state.energy>=cards.get('frost').cost+.02);
   const troop=await deploy(victim,'guard',5.5,4.5*direction);
   await until(()=>caster.state.units.some(u=>u.id===troop.unitID));
   const target=caster.state.units.find(u=>u.id===troop.unitID);
   tracking=true;const spell=await deploy(caster,'frost',target.x,target.z+2.65*direction);
   await until(()=>caster.state.effects.some(e=>e.id===spell.unitID));
   const effect=caster.state.effects.find(e=>e.id===spell.unitID);
   await until(()=>caster.state.tick>=effect.end+2,10000);
   const observed=samples.filter(s=>s.id===troop.unitID);
   assert(observed.some(s=>s.distance<s.radius-.002&&s.slowed),'No slowed target inside the circle');
   assert(observed.some(s=>s.distance>s.radius+.002&&s.distance<s.radius+.3&&!s.slowed),'No immediately released target just outside the circle');
   for(const s of samples){
    const expected=s.seat!==casterSeat&&s.distance<=s.radius;
    if(Math.abs(s.distance-s.radius)>.002)assert(s.slowed===expected,'Snapshot frost/foot-center mismatch: '+JSON.stringify(s));
   }
   assert(records.every(r=>r.errors.length===0),'Protocol errors: '+records.flatMap(r=>r.errors).join(','));
   return {casterSeat,room:a.state.room,match:a.state.match,target:troop.unitID,effect:spell.unitID,checkedSnapshots:observed.length,
    lastSlowed:observed.filter(s=>s.slowed).at(-1),firstReleased:observed.find(s=>s.distance>s.radius+.002&&!s.slowed),errors:[]};
  }finally{
   for(const r of records){if(r.socket.readyState===WebSocket.OPEN&&r.state)r.socket.send(JSON.stringify({type:'leave',match:r.state.match}));r.socket.close()}
  }
 }
 const results=await Promise.all([scenario(0),scenario(1)]);
 const result={passed:true,origin,rulesHash:config.rulesHash,fixture:'Ordinary real WebSocket rooms, normal energy and hand cycling',results};
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));return result;
}
module.exports={run};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
