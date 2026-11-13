const path=require('node:path');
const core=process.env.PLAYWRIGHT_CORE||path.resolve(__dirname,'../../../.local/tower-duel/browser/node_modules/playwright-core');
const {PNG}=require(path.join(core,'lib/utilsBundle.js'));

// Measure rendered card frames, independently of the UI layout calculations.
function portraitFrames(buffer){
 const {width,height,data}=PNG.sync.read(buffer),startY=Math.floor(height*.75),start=startY*width;
 const mask=new Uint8Array(width*height),queue=new Int32Array(width*height),found=[];
 for(let i=start;i<mask.length;i++){const p=i*4;if(data[p+3]>240&&Math.abs(data[p]-181)<32&&Math.abs(data[p+1]-198)<32&&Math.abs(data[p+2]-213)<32)mask[i]=1}
 for(let i=start;i<mask.length;i++){
  if(mask[i]!==1)continue;
  let head=0,tail=1,minX=width,maxX=0,minY=height,maxY=0,count=0;queue[0]=i;mask[i]=2;
  while(head<tail){
   const at=queue[head++],x=at%width,y=Math.floor(at/width);count++;minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);
   for(let dy=-2;dy<=2;dy++)for(let dx=-2;dx<=2;dx++){
    const nx=x+dx,ny=y+dy;if(nx<0||nx>=width||ny<startY||ny>=height)continue;
    const next=ny*width+nx;if(mask[next]===1){mask[next]=2;queue[tail++]=next}
   }
  }
  const w=maxX-minX+1,h=maxY-minY+1;
  if(w>20&&h>28&&count>(w+h)*1.3&&w/h>.65&&w/h<.84)found.push({x:minX,y:minY,width:w,height:h,ratio:w/h,pixels:count});
 }
 found.sort((a,b)=>b.width*b.height-a.width*a.height);const cards=[];
 for(const candidate of found){if(cards.every(card=>candidate.x+candidate.width<=card.x||candidate.x>=card.x+card.width||candidate.y+candidate.height<=card.y||candidate.y>=card.y+card.height))cards.push(candidate);if(cards.length===4)break}
 cards.sort((a,b)=>a.x-b.x);
 if(cards.length!==4)throw Error('Expected four visible portrait card frames; found '+cards.length);
 if(Math.max(...cards.map(c=>c.y))-Math.min(...cards.map(c=>c.y))>2||Math.max(...cards.map(c=>c.height))-Math.min(...cards.map(c=>c.height))>2)throw Error('Card frames do not form one row');
 for(const card of cards)if(Math.abs(card.width-card.height*112/150)>2.5)throw Error('Rendered card changed aspect ratio: '+JSON.stringify(card));
 return cards;
}
// Read stable arena landmarks from the actual pixels, without camera/UI formulas.
function verticalFraming(buffer){
 const {width,height,data}=PNG.sync.read(buffer),half=Math.max(3,Math.floor(width*.008)),middle=Math.floor(width/2);
 const rows=(from,to,test)=>{
  const hits=[];
  for(let y=Math.floor(height*from);y<height*to;y++){
   let count=0;for(let x=middle-half;x<=middle+half;x++){const p=(y*width+x)*4;if(test(data[p],data[p+1],data[p+2]))count++}
   if(count/(half*2+1)>.75)hits.push(y);
  }
  return hits;
 };
 const water=rows(.2,.7,(r,g,b)=>r<55&&g>90&&g<180&&b>145&&b>g*1.1);
 let best=[],run=[];
 for(const y of water){if(run.length&&y!==run.at(-1)+1){if(run.length>best.length)best=run;run=[]}run.push(y)}
 if(run.length>best.length)best=run;
 if(best.length<height*.02)throw Error('Could not measure the rendered river');
 const dock=rows(.78,.86,(r,g,b)=>r<40&&g<65&&b<90);
 if(!dock.length)throw Error('Could not measure the rendered dock top');
 return {riverTop:best[0],riverBottom:best.at(-1),riverHeight:best.length,dockTop:dock[0]};
}
module.exports={portraitFrames,verticalFraming};
if(require.main===module)console.log(JSON.stringify(portraitFrames(require('node:fs').readFileSync(process.argv[2])),null,2));
