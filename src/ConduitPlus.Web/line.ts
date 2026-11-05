import type {Card, Hit, Theme} from './runtime';

// Same 1/2/5 decade algorithm as ChartTicks.Nice (the host validates layout/hits).
function niceTicks(minimum:number, maximum:number):number[] {
  if(minimum===maximum) {minimum-=.5; maximum+=.5;}
  const raw=(maximum-minimum)/4;
  const power=10**Math.floor(Math.log10(raw));
  const fraction=raw/power;
  const step=(fraction<=1?1:fraction<=2?2:fraction<=5?5:10)*power;
  const first=Math.floor(minimum/step), last=Math.ceil(maximum/step);
  return Array.from({length:last-first+1},(_,i)=>(first+i)*step);
}

export function renderLine(el:HTMLElement, c:Card, t:Theme):Hit[] {
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
  svg.setAttribute('width',String(c.width)); svg.setAttribute('height','220');
  Object.assign(svg.style,{position:'absolute',left:'0',top:'0',fontFamily:t.font});
  el.append(svg);
  function node(name:string, attrs:Record<string,string|number>, text?:string) {
    const n=document.createElementNS(svg.namespaceURI,name);
    Object.entries(attrs).forEach(([key,value])=>n.setAttribute(key,String(value)));
    if(text!==undefined) n.textContent=text;
    svg.append(n); return n;
  }
  const values=c.values.filter((v):v is number=>v!==null);
  const ticks=niceTicks(Math.min(0,...values),Math.max(c.threshold,...values));
  const lo=ticks[0], hi=ticks[ticks.length-1], right=c.width-20;
  const y=(v:number)=>184-(v-lo)/(hi-lo)*140;
  const step=(right-44)/(c.values.length-1), x=(i:number)=>44+i*step;
  const line=(x1:number,y1:number,x2:number,y2:number,color:string,width=1)=>
    node('line',{x1,y1,x2,y2,stroke:color,'stroke-width':width});
  node('rect',{x:44,y:44,width:right-44,height:y(c.threshold)-44,fill:t.thresholdBand});
  ticks.forEach(tick=>{
    line(44,y(tick),right,y(tick),t.line);
    node('text',{x:5,y:y(tick)-7,'font-size':10,fill:t.muted,'dominant-baseline':'text-before-edge'},String(Number(tick.toPrecision(4))));
  });
  line(44,44,44,184,t.muted); line(44,184,right,184,t.muted);
  let segment:{x:number,y:number}[]=[];
  function flush() {
    if(segment.length===0) return;
    const points=segment.map(p=>`${p.x},${p.y}`).join(' ');
    node('polygon',{points:`${segment[0].x},${y(0)} ${points} ${segment[segment.length-1].x},${y(0)}`,fill:t.area});
    node('polyline',{points,fill:'none',stroke:t.accent,'stroke-width':2,'stroke-linejoin':'round'});
    segment.forEach(p=>node('rect',{x:p.x-2,y:p.y-2,width:4,height:4,fill:t.accent}));
    segment=[];
  }
  c.values.forEach((v,i)=>{if(v===null) flush(); else segment.push({x:x(i),y:y(v)});});
  flush();
  line(44,y(c.threshold),right,y(c.threshold),t.bad);
  c.values.forEach((_,i)=>{
    if(i===0||i===c.values.length-1||i%Math.max(1,Math.ceil(c.values.length/4))===0)
      node('text',{x:x(i)-12,y:190,'font-size':10,fill:t.muted,'dominant-baseline':'text-before-edge'},c.labels[i]);
    if(c.hoverKey===c.keys[i]||c.selectedKeys.includes(c.keys[i])) line(x(i),44,x(i),184,t.ink,2);
  });
  return c.keys.map((key,i)=>({key,x:Math.max(44,44+(i-.5)*step),y:44,
    w:step*(i===0||i===c.keys.length-1?.5:1),h:140}));
}
