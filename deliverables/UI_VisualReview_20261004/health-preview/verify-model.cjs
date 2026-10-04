function createHealthModel(){
 const unit=20;
 const scenarios={
 units:{name:"整格扣血／補血",max:240,hp:240,shield:0,steps:[["damage",8],["damage",7],["damage",5],["damage",40],["heal",8],["heal",7],["heal",5],["heal",40]]},
 shield:{name:"護盾緊貼血格",max:240,hp:120,shield:80,steps:[["damage",8],["damage",7],["damage",5],["damage",75],["damage",5],["heal",20],["shield",25],["damage",20],["damage",5]]},
 high:{name:"高血量密度",max:1200,hp:1200,shield:400,steps:[["damage",8],["damage",12],["damage",180],["damage",260],["damage",240],["heal",100],["shield",160],["damage",320],["heal",200]]}
 };
 let s;
 const copy=()=>JSON.parse(JSON.stringify(s));
 function reset(key){const c=scenarios[key];s={key,max:c.max,hp:c.hp,shield:c.shield,shieldCap:c.shield,hc:Math.ceil(c.hp/unit),sc:Math.ceil(c.shield/unit),hb:0,sb:0,index:0,event:"準備就緒",delta:0,kind:"idle"};return copy();}
 function pool(kind,delta){
  const isH=kind==="h",value=isH?"hp":"shield",count=isH?"hc":"sc",bank=isH?"hb":"sb",max=isH?s.max:s.shieldCap;
  s[bank]+=delta;
  while(s[bank]<=-unit){s[count]=Math.max(0,s[count]-1);s[bank]+=unit;}
  while(s[bank]>=unit){s[count]=Math.min(Math.ceil(max/unit),s[count]+1);s[bank]-=unit;}
  if(s[value]<=0){s[count]=0;s[bank]=0;}
  if(isH&&s.hp>=s.max){s.hc=Math.ceil(s.max/unit);s.hb=0;}
 }
 function act(kind,amount){
  const before=copy();let dh=0,ds=0;
  if(kind==="damage"){
   ds=-Math.min(s.shield,amount);s.shield+=ds;pool("s",ds);
   dh=-Math.min(s.hp,amount+ds);s.hp+=dh;pool("h",dh);
  }else if(kind==="heal"&&s.hp>0){dh=Math.min(s.max-s.hp,amount);s.hp+=dh;pool("h",dh);}
  else if(kind==="shield"&&s.hp>0){ds=amount;s.shield+=amount;s.shieldCap=Math.max(s.shieldCap,s.shield);s.sc=Math.ceil(s.shield/unit);s.sb=0;}
  if(s.hp===0){s.shield=0;s.sc=0;s.sb=0;}
  s.kind=kind;s.delta=dh+ds;
  const changed=(s.hc+s.sc)-(before.hc+before.sc);
  const label=kind==="damage"?"受傷":kind==="heal"?"治療":"獲得護盾";
  s.event=label+" "+(s.delta>0?"+":"")+s.delta+" HP · "+(changed===0?"未滿一格，格數不變":(changed>0?"亮回 ":"扣除 ")+Math.abs(changed)+" 整格");
  if(s.hp===0)s.event="生命歸零 · 清除全部血格";
  return {before,after:copy(),dh,ds};
 }
 function next(){const item=scenarios[s.key].steps[s.index];if(!item)return null;const result=act(item[0],item[1]);s.index++;result.after=copy();return result;}
 return {unit,scenarios,reset,act,next,state:copy};
}

const fs=require("fs"),assert=require("assert");
const m=createHealthModel();m.reset("units");
assert.equal(m.act("damage",8).after.hc,12);
assert.equal(m.act("damage",7).after.hc,12);
let s=m.act("damage",5).after;assert.equal(s.hp,220);assert.equal(s.hc,11);
assert.equal(m.act("heal",8).after.hc,11);
assert.equal(m.act("heal",7).after.hc,11);
assert.equal(m.act("heal",5).after.hc,12);
m.reset("shield");s=m.act("damage",75).after;assert.equal(s.hp,120);assert.equal(s.shield,5);assert.equal(s.sc,1);
s=m.act("damage",10).after;assert.equal(s.shield,0);assert.equal(s.hp,115);assert.equal(s.hc,6);
m.reset("units");s=m.act("damage",500).after;assert.equal(s.hp,0);assert.equal(s.hc,0);assert.equal(m.act("heal",20).after.hp,0);
m.reset("shield");m.act("damage",80);s=m.act("shield",25).after;assert.equal(s.sc,2);
s=m.act("damage",8).after;assert.equal(s.sc,2);s=m.act("damage",12).after;assert.equal(s.sc,1);s=m.act("damage",5).after;assert.equal(s.sc,0);
const all={};
for(const key of Object.keys(m.scenarios)){const initial=m.reset(key),steps=[];while(true){const r=m.next();if(!r)break;steps.push(r);const s=r.after;assert(Number.isInteger(s.hc)&&Number.isInteger(s.sc));assert(s.hp>=0&&s.shield>=0&&s.hc>=0&&s.sc>=0);}all[key]={initial,steps,name:m.scenarios[key].name};}
fs.writeFileSync(__dirname+"/timelines.json",JSON.stringify(all,null,2),"utf8");
console.log("PASS: 8+7+5 damage / healing, shield overflow, 25 shield endpoint, death, all scenario integer counts. "+Object.values(all).reduce((n,x)=>n+x.steps.length,0)+" timeline steps.");
