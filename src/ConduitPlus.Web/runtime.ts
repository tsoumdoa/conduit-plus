import {renderLine} from './line';
export type Hit = {key:string,x:number,y:number,w:number,h:number};
export type Card = {id:string,rev:number,kind:'metric'|'bar'|'table'|'line',title:string,value:string|null,unit:string,values:(number|null)[],labels:string[],keys:string[],threshold:number,headers:string[],rows:{key:string,cells:(string|null)[]}[],width:number,hoverKey:string|null,selectedKeys:string[]};
export type Theme = {surface:string,ink:string,muted:string,line:string,track:string,accent:string,bad:string,good:string,area:string,thresholdBand:string,font:string,padding:number,fontSize:number,rowHeight:number,radius:number};
function render(cardsJson:string,themeJson:string,scale:number) {
  if (!Number.isFinite(scale) || scale <= 0) throw new Error('Invalid scale');
  const cards:Card[]=JSON.parse(cardsJson), t:Theme=JSON.parse(themeJson);
  document.body.replaceChildren();
  let atlasY=0;
  return cards.map(c=>{
    const h=c.kind==='metric'?92:c.kind==='line'?220:48+(c.kind==='bar'?c.values.length:c.rows.length+1)*t.rowHeight;
    const el=document.createElement('div'); el.className='card'; el.dataset.id=c.id;
    Object.assign(el.style,{left:'0px',top:`${atlasY}px`,width:`${c.width}px`,height:`${h}px`,background:t.surface,borderRadius:`${t.radius}px`,fontFamily:t.font});
    document.body.append(el);
    function box(x:number,y:number,w:number,h:number,color:string) { const b=document.createElement('div');b.className='box';Object.assign(b.style,{left:`${x}px`,top:`${y}px`,width:`${w}px`,height:`${h}px`,background:color});el.append(b); }
    function text(value:string,x:number,y:number,size:number,color:string,w?:number) { const s=document.createElement('span');s.textContent=value;Object.assign(s.style,{left:`${x}px`,top:`${y}px`,fontSize:`${size}px`,lineHeight:'1.2',color,...(w===undefined?{}:{width:`${w}px`})});el.append(s);return s; }
    text(c.title,t.padding,10,t.fontSize,t.muted);
    const hitRegions:Hit[]=[];
    function row(key:string,i:number) {const hit={key,x:t.padding,y:36+i*t.rowHeight,w:c.width-2*t.padding,h:t.rowHeight};hitRegions.push(hit);if(c.hoverKey===key||c.selectedKeys.includes(key))box(hit.x,hit.y,hit.w,hit.h,t.track);if(c.selectedKeys.includes(key))box(hit.x,hit.y,2,hit.h,t.accent);return hit;}
    if(c.kind==='metric'){const s=text(c.value??'—',t.padding,36,28,t.ink);text(c.unit,Math.min(c.width-45,t.padding+s.getBoundingClientRect().width+8),51,t.fontSize,t.muted);}
    else if(c.kind==='bar'){
      const max=Math.max(c.threshold,...c.values.map(v=>Math.abs(v??0))), signed=c.values.some(v=>v!==null&&v<0);
      c.values.forEach((v,i)=>{const r=row(c.keys[i],i);text(c.labels[i],r.x+2,r.y+2,t.fontSize,t.ink,51);const x=r.x+55,w=r.w-110,y=r.y+7,origin=signed?x+w/2:x,span=signed?w/2:w;box(x,y,w,8,t.track);if(v!==null)box(v<0?origin-Math.abs(v)/max*span:origin,y,Math.abs(v)/max*span,8,v>c.threshold?t.bad:t.good);box(origin+c.threshold/max*span,y-2,1,12,t.muted);text(v===null?'—':v.toFixed(2),r.x+r.w-43,r.y+2,t.fontSize,t.ink);});
    } else if(c.kind==='line') {
      hitRegions.push(...renderLine(el,c,t));
    } else {
      const w=(c.width-2*t.padding)/c.headers.length;
      c.headers.forEach((s,j)=>text(s,t.padding+j*w+2,38,t.fontSize,t.muted,w-5));
      c.rows.forEach((r,i)=>{const hit=row(r.key,i+1);box(hit.x,hit.y,hit.w,1,t.line);r.cells.forEach((s,j)=>text(s??'—',t.padding+j*w+2,hit.y+2,t.fontSize,t.ink,w-5));});
    }
    const layout={id:c.id,rev:c.rev,x:0,y:atlasY,w:c.width,h,hitRegions};atlasY+=h+8;return layout;
  });
}
declare global {interface Window {conduit:{render:typeof render}}}
window.conduit={render};
export {};
