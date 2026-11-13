// UI-only snapshot fixture. Real server authority is covered by deployment_test.go.
const fs=require('node:fs');const path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const {chromium}=require(process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core'));
const {PNG}=require(path.join(process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core'),'lib/utilsBundle.js'));
const {portraitFrames}=require('./Card-Image-Checks.cjs');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(condition,message)=>{if(!condition)throw Error(message)};
async function until(predicate){for(let i=0;i<250;i++){if(predicate())return;await pause(40)}throw Error('UI fixture timed out')}

async function run(origin='http://127.0.0.1:18116',output=path.join(root,'artifacts/tower-duel/deployment/feedback')){
 fs.mkdirSync(output,{recursive:true});
 const config=await(await fetch(origin+'/api/config')).json();
 const map=JSON.parse(fs.readFileSync(path.join(root,'examples/tower-duel/protocol/map.json'),'utf8'));
 const rules=JSON.parse(fs.readFileSync(path.join(root,'examples/tower-duel/protocol/rules.json'),'utf8'));
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding']});
 const reports=[];
 try{
  for(const seat of [0,1]){
   const viewport=seat===0?{width:720,height:1000}:{width:390,height:844};
   const context=await browser.newContext({viewport,hasTouch:seat===1,isMobile:seat===1,deviceScaleFactor:1});
   const page=await context.newPage();const errors=[],sent=[],checks={};let route,heartbeat;
   page.on('pageerror',error=>errors.push(String(error)));
   const towers=()=>map.towers.map(t=>({...t,hp:t.kind==='keep'?rules.keepHP:rules.towerHP,maxHP:t.kind==='keep'?rules.keepHP:rules.towerHP,active:t.kind!=='keep'}));
   const state={type:'welcome',protocol:3,room:'QATEST',match:'fixture-'+seat,mapHash:config.mapHash,rulesHash:config.rulesHash,seat,tick:0,tickRate:30,snapshotRate:15,phase:'playing',remaining:150,countdown:0,energy:5.25,winner:-2,reason:'',players:[0,1].map(i=>({seat:i,name:'操作驗證'+i,connected:true,ready:true})),units:[],towers:towers(),hand:['guard','archer','swords','bomber'],nextCard:'charger',handVersion:0,actionWatermark:0,effects:[]};
   const send=type=>{state.type=type;state.tick+=2;route.send(JSON.stringify(state))};
   await page.routeWebSocket('**/ws',ws=>{
    route=ws;ws.onMessage(raw=>{
     const message=JSON.parse(String(raw));
     if(message.type==='hello'){send('welcome');heartbeat=setInterval(()=>send('snapshot'),67)}
     if(message.type==='deploy'){
      sent.push(message);state.actionWatermark=message.action;
      ws.send(JSON.stringify({type:'action',match:state.match,action:message.action,accepted:true,energy:state.energy,hand:state.hand,nextCard:state.nextCard,handVersion:state.handVersion,count:rules.cards.find(c=>c.id===message.card).count,unitID:101}));
     }
    });ws.onClose(()=>clearInterval(heartbeat));
   });
   const shot=name=>page.screenshot({path:path.join(output,name+'-seat'+seat+'.png')});
   function layout(){
    const scale=Math.sqrt(viewport.width/720*viewport.height/1000);
    return {dockScale:Math.min(.9,(viewport.width/scale-16)/600,viewport.height/scale*.18/234)*scale,
     lift:seat===1?Math.max(48,Math.min(72,viewport.height*.065)):Math.max(20,Math.min(36,viewport.height*.035))};
   }
   const cdp=seat===1?await context.newCDPSession(page):null;
   function screen(x,z){
    const h=viewport.height,w=viewport.width,centre=.19+(1-.19-.02)/2,sin=Math.sin(Math.PI/3),cos=.5;
    const far=11*sin+5.3*cos+.45,near=13.1*sin+.15,focus=(far-near)/2;
    const size=Math.max((far+near)/2/(1-.19-.02),(15*sin+2*cos+.15-focus)/(2*(1-centre))),sign=seat===0?1:-1;
    return {x:w/2+sign*x*h/(2*size),y:h*(1-centre)+(focus-sign*sin*z)*h/(2*size)+layout().lift};
   }
   async function pointer(type,point){
    if(cdp)await cdp.send('Input.dispatchTouchEvent',{type,touchPoints:type==='touchEnd'?[]:[{...point,id:1,radiusX:4,radiusY:4}]});
    else if(type==='touchStart'){await page.mouse.move(point.x,point.y);await page.mouse.down()}
    else if(type==='touchMove')await page.mouse.move(point.x,point.y,{steps:10});else await page.mouse.up();
   }
   async function attempt(name,index,x,z,expected){
    const {dockScale}=layout();
    const count=sent.length,start={x:viewport.width/2+(-300+16+56+122*index)*dockScale,y:viewport.height*.82+121*dockScale};
    await pointer('touchStart',start);await pause(50);await pointer('touchMove',screen(x,z));await pause(160);await shot(name);
    await pointer('touchEnd');await pause(350);
    assert(sent.length===count+(expected?1:0),name+' produced the wrong client deployment decision');checks[name]=true;
   }
   async function checkPickup(){
    const card=portraitFrames(await page.screenshot())[0],count=sent.length;
    const grab={x:card.x+card.width*.8,y:card.y+card.height*.8},delta={x:-10,y:-12};
    await pointer('touchStart',grab);await pointer('touchMove',{x:grab.x+delta.x,y:grab.y+delta.y});await pause(150);
    const buffer=await shot('card-follows-from-slot'),png=PNG.sync.read(buffer);
    const pixel=(x,y)=>{const at=(Math.round(y)*png.width+Math.round(x))*4;return [...png.data.subarray(at,at+3)]};
    const empty=pixel(card.x+card.width*.5,card.y+card.height-3);
    assert(Math.abs(empty[0]-19)<8&&Math.abs(empty[1]-36)<8&&Math.abs(empty[2]-56)<8,'Original slot still contains the dragged card');
    let border=false;
    for(let dy=-2;dy<=3;dy++){
     const rgb=pixel(card.x+card.width*.5+delta.x,card.y+delta.y+dy);
     if(rgb[0]>140&&rgb[1]>115&&rgb[2]<145&&rgb[0]>=rgb[1])border=true;
    }
    assert(border,'Card jumped away from its pickup position');
    await pointer('touchEnd');await pause(250);assert(sent.length===count,'Short pull deployed a card');
    checks.cardFollowsFromSlot=true;checks.originalSlotEmpty=true;
   }
   function healthBounds(buffer){
    const png=PNG.sync.read(buffer),p=screen(5,-7.7),sin=Math.sin(Math.PI/3),far=11*sin+5.3*.5+.45,near=13.1*sin+.15,focus=(far-near)/2;
    const centre=.19+(1-.19-.02)/2,size=Math.max((far+near)/2/(1-.19-.02),(15*sin+2*.5+.15-focus)/(2*(1-centre)));
    const y=p.y-layout().lift-3.9*.5*viewport.height/(2*size),found=[];
    for(let py=Math.floor(y-2);py<=y+12;py++)for(let px=Math.floor(p.x-60);px<=p.x+60;px++){
     const at=(py*png.width+px)*4;
     if(Math.abs(png.data[at]-105)<5&&Math.abs(png.data[at+1]-188)<5&&Math.abs(png.data[at+2]-240)<5)found.push([px,py]);
    }
    assert(found.length>10,'Cannot locate the rendered health fill');
    return {width:Math.max(...found.map(p=>p[0]))-Math.min(...found.map(p=>p[0]))+1,height:Math.max(...found.map(p=>p[1]))-Math.min(...found.map(p=>p[1]))+1};
   }
   try{
    await page.goto(origin,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>!!window.towerDuel,null,{timeout:45000});await pause(3500);
    await (seat===0?page.mouse.click(360,524):page.touchscreen.tap(195,427));await until(()=>!!heartbeat);await pause(600);
    await checkPickup();
    const forward=seat===0?1:-1,enemy=state.towers.find(t=>t.seat!==seat&&t.kind==='tower'&&t.x<0),own=state.towers.find(t=>t.seat===seat&&t.kind==='tower'&&t.x<0);
    await attempt('intact-enemy-closed',0,-4.5,3.5*forward,false);
    own.hp=0;send('snapshot');await pause(150);await attempt('own-loss-does-not-annex',0,-4.5,3.5*forward,false);
    enemy.hp=0;send('snapshot');await pause(150);await attempt('enemy-pocket-open',0,-4.5,3.5*forward,true);
    await attempt('opposite-pocket-closed',0,4.5,3.5*forward,false);
    await attempt('river-stays-closed',0,-4.5,0,false);
    await attempt('squad-front-edge-closed',2,-4.5,5.5*forward,false);
    state.towers.find(t=>t.seat!==seat&&t.kind==='tower'&&t.x>0).hp=0;send('snapshot');await pause(150);
    await attempt('both-pockets-open',2,.5,3.5*forward,true);
    state.towers=towers();state.match+='-rematch';send('welcome');await pause(300);
    await attempt('rematch-closes-pocket',0,-4.5,3.5*forward,false);
    state.units=[{id:101,seat,kind:'guard',x:-4.5,z:-3.5*forward,hp:182,maxHP:220,radius:.32,dx:0,dz:forward,active:true}];
    state.energy=5.35;send('snapshot');await pause(200);await shot('numeric-health-fractional-energy');
    state.energy=10;send('snapshot');await pause(70);await shot('full-energy-reminder');
    if(seat===0){
     const widths=[];
     for(const width of [1920,1366]){
      viewport.width=width;viewport.height=1080;await page.setViewportSize(viewport);await pause(500);
      const bounds=healthBounds(await shot('desktop-health-'+width));
      assert(bounds.height<=8&&bounds.width<=100,'Desktop health bar is oversized');widths.push(bounds);
     }
     assert(Math.abs(widths[0].width-widths[1].width)<=1&&Math.abs(widths[0].height-widths[1].height)<=1,'Window width changes health-bar size');
     checks.thinDesktopHealth=true;checks.healthSizeIndependentOfWidth=true;
     await checkPickup();
     const card=portraitFrames(await page.screenshot())[0],count=sent.length;
     const start={x:card.x+card.width*.5,y:card.y+card.height*.5};
     const target={x:start.x,y:viewport.height*.82+layout().lift*.4};
     await pointer('touchStart',start);await pointer('touchMove',target);await pause(150);await shot('model-before-pointer-leaves-dock');
     await pointer('touchEnd');await pause(350);
     assert(sent.length===count+1,'Model/drop still waits for the pointer to leave the full dock');checks.earlyModelTransition=true;
    }
    assert(errors.length===0,'Browser errors: '+errors.join(';'));
    reports.push({seat,viewport,checks,sent,errors});
   }finally{clearInterval(heartbeat);await context.close()}
  }
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({passed:true,fixture:'Synthetic snapshots for client placement and HUD only; server authority tested separately',reports},null,2));
  console.log(JSON.stringify({passed:true,seats:reports.map(r=>r.seat),checks:reports.map(r=>Object.keys(r.checks).length),report:path.join(output,'result.json')}));
 }finally{await browser.close()}
}
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
module.exports={run};
