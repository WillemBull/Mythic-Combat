"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8"),begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const data=globalThis.__ai,raw=data.raw;
function number(text,re){const m=text.match(re);if(!m)throw Error("AI source changed: "+re);return Number(m[1]);}
Object.assign(data.rules,{
 initialTrainDelay:number(raw.start,/aiTrainT=(\d+)/),initialHeroAt:number(raw.start,/aiHeroAt=(\d+)/),
 heroRepeat:number(raw.pick,/aiHeroAt=elapsed\(\)\+(\d+)/),counterMyths:number(raw.pick,/length>=(\d+) && !aiHeroAlive/),
 heroCounterChance:number(raw.pick,/Math.random\(\)<([.\d]+)/),rangedMinimum:number(raw.pick,/foe.length>=(\d+)/),
 rangedRatio:number(raw.pick,/foe.length>=\d+ &&.*?>=([.\d]+)/),mythReserve:number(raw.pick,/cost.v\+(\d+)/),
 waveMinimum:number(raw.wave,/size:(\d+)\+/),waveRandom:number(raw.wave,/Math.random\(\)\*(\d+)/),
 waveTimeout:number(raw.release,/startedAt<=(\d+)/),waypointRow:number(raw.route,/waypoint=\[lane,(\d+)\]/),
 defenseDistance:number(raw.tick,/unitDist\(u,b\)<=(\d+)/),defenseMinimum:number(raw.tick,/invaders.length>=(\d+)/),
 defenseMaximum:number(raw.tick,/Math.min\((\d+),invaders.length/),rallySettleDistance:number(raw.update,/PLAYER_ROW0\]\)<=(\d+)/),
 aoeHits:number(raw.trigger,/aiPowerHits\(key,hex\)>=(\d+)/),
 controlHits:number(raw.trigger,/caught.length>=(\d+)/),
 buffUnits:number(raw.trigger,/own.length>=(\d+)/),
 aoeRadius:number(raw.hits,/hexDist\(u.hex,hex\)<=(\d+)/),
 controlRadius:number(raw.trigger,/caught=[\s\S]*?hexDist\(u.hex,hex\)<=(\d+)/),
 buffRadius:number(raw.trigger,/own=[\s\S]*?hexDist\(u.hex,hex\)<=(\d+)/),
 soilRadius:number(raw.picker,/hexesInRadius\(h,(\d+)\)/),
 castNever:number(raw.start,/aiLastCastAt=-(\d+)/)*-1
});
const gen=raw.terrainGen;
Object.assign(data.terrain,{
 attempts:number(gen,/attempt<(\d+)/),
 mountainRowMin:number(gen,/for\(let r=(\d+);r<=\d+;r\+\+\) for\(let c=0;c<COLS;c\+\+\) if\(legal/),
 mountainRowMax:number(gen,/for\(let r=\d+;r<=(\d+);r\+\+\) for\(let c=0;c<COLS;c\+\+\) if\(legal/),
 mountainLaneBonus:number(gen,/constricted.includes\(lane\)\?(\d+)/),
 mountainMidRow:number(gen,/\(r===(\d+)\?\d+:0\)\+Math.random\(\)\*\d+\}\)/),
 mountainMidBonus:number(gen,/\(r===\d+\?(\d+):0\)\+Math.random\(\)\*\d+\}\)/),
 mountainJitter:number(gen,/\(r===\d+\?\d+:0\)\+Math.random\(\)\*(\d+)/),
 nileStartRowMin:number(gen,/for\(let r=(\d+);r<=\d+;r\+\+\) if\(legal/),
 nileStartRowMax:number(gen,/for\(let r=\d+;r<=(\d+);r\+\+\) if\(legal/),
 nileStartLaneBonus:number(gen,/includes\(c\)\?(\d+):0\)\+Math.random\(\)\*\d+\}\);\s*starts.sort/),
 nileStartJitter:number(gen,/includes\(c\)\?\d+:0\)\+Math.random\(\)\*(\d+)\}\);\s*starts.sort/),
 waterRowMin:number(gen,/for\(let r=(\d+);r<AI_ROWS;r\+\+\) for\(let c=0;c<COLS;c\+\+\) if\(legal\(c,r,used\)&&!protectedRoutes/),
 forestRowMin:number(gen,/for\(let r=(\d+);r<AI_ROWS;r\+\+\) for\(let c=0;c<COLS;c\+\+\) if\(legal\(c,r,used\)\)\{/),
 waterDeepRow:number(gen,/\(r>=(\d+)\?\d+:0\)/),
 waterDeepBonus:number(gen,/\(r>=\d+\?(\d+):0\)/),
 waterLaneBonus:number(gen,/includes\(c\)\?(\d+):0\)\+Math.random\(\)\*\d+\}\);\s*waterPool.sort/),
 waterJitter:number(gen,/includes\(c\)\?\d+:0\)\+Math.random\(\)\*(\d+)\}\);\s*waterPool.sort/),
 forestNearBuildingRange:number(gen,/hexDist\(\[c,r\],h\)<=(\d+)/),
 forestNearBuildingBonus:number(gen,/nearBld\?(\d+):0/),
 forestNearMountainBonus:number(gen,/nearM\?(\d+):0/),
 forestFunnelBonus:number(gen,/chinaFunnel\?(\d+):0/),
 forestJitter:number(gen,/chinaFunnel\?\d+:0\)\+Math.random\(\)\*(\d+)/),
 gateCol:number(gen,/\[\[(\d+),AI_ROWS-1\]\]/)
});
delete data.raw;
const assets=path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound");
fs.writeFileSync(path.join(assets,"Resources/GameData/ai.json"),JSON.stringify(data,null,2)+"\n");
fs.writeFileSync(path.join(assets,"Tests/EditMode/Fixtures/ai_reference.json"),JSON.stringify(globalThis.__aiFixture,null,2)+"\n");
console.log("Exported "+data.profiles.length+" AI profiles, "+globalThis.__aiFixture.choices.length+" choices and "+globalThis.__aiFixture.deployments.length+" deployments");
console.log("U24: "+globalThis.__aiFixture.godTimelines.length+" god-unlock timelines, "+globalThis.__aiFixture.godCompetition.length+" unlock-vs-training cases");
console.log("U29: "+globalThis.__aiFixture.powerDecisions.length+" power-decision boards, "+globalThis.__aiFixture.powerCasts.length+" AI casts, "+data.rules.powerClasses.length+" classes");
if(false){
/*DRIVER*/
code+=`
;(function(){
 // Match Unity's stable default layout; never snapshot the randomized live AI layout.
 const initial=[];
 for(const layout of defaultDeckPreset().buildings)for(const side of [0,1]){
   const b=buildings.find(b=>b.type===layout.type);
   const hexes=layout.hexes.map(h=>[h[0],side?ROWS-1-(h[1]+PLAYER_ROW0):h[1]+PLAYER_ROW0]);
   initial.push({...b,side,hexes});
 }
 startMatch();
 const profiles=[];
 for(const style of Object.keys(AI_PROFILES))for(const faction of Object.keys(AI_PROFILES[style])){
   const p=AI_PROFILES[style][faction];
   profiles.push({style,faction,trainMin:p.trainMin,trainRand:p.trainRand,heroAt:p.heroAt,mythChance:p.mythChance,rushCity:!!p.rushCity,pool:p.pool,
     godSched:Object.keys(p.godSched||{}).map(key=>({key,at:p.godSched[key]}))});
 }
 globalThis.__ai={source:"godsbound_beta.html",profiles,rules:{saveMax:AI_SAVE_MAX,lanes:LANES},raw:{
   start:startMatch.toString(),pick:pickAiUnit.toString(),wave:ensureAiWave.toString(),release:tickAiWave.toString(),
   route:routeAiWaveMember.toString(),tick:tickAI.toString(),update:updateUnit.toString(),
   trigger:aiPowerTriggerOK.toString()}};
 globalThis.__ai.rules.castGap=AI_CAST_GAP;
 globalThis.__ai.rules.supportWindow=AI_SUPPORT_WINDOW;
 globalThis.__ai.rules.powerClasses=Object.keys(AI_POWER_CLASS).map(key=>({key,cls:AI_POWER_CLASS[key]}));
 globalThis.__ai.raw.hits=aiPowerHits.toString();
 globalThis.__ai.raw.picker=pickAiPowerHex.toString();
 /* U32: the AI's own half — the three shipped building layouts, the lanes its terrain funnels
    along, the hand-authored fallback, and the generator's source to measure its weights from. */
 /* Flat shapes only: Unity's JsonUtility cannot read an array of arrays. */
 const pair=h=>({c0:h[0][0],r0:h[0][1],c1:h[1][0],r1:h[1][1]});
 const hexes=list=>list.map(h=>({c:h[0],r:h[1]}));
 globalThis.__ai.layouts=AI_LAYOUTS.map(l=>({city:pair(l.city),temple:pair(l.temple),fortress:pair(l.fortress)}));
 globalThis.__ai.terrain={lanes:AI_LANES.map(cols=>({cols:cols})),
   fallback:["F","M","W"].map(code=>({code:code,hexes:hexes(AI_TERRAIN_LAYOUT[code])}))};
 globalThis.__ai.raw.terrainGen=generateAiTerrain.toString();
 const choices=[],deployments=[],rallies=[];
 let roll=0.2;Math.random=()=>roll;
 const realHasPassive=hasPassive;
 hasPassive=()=>false;
 function reset(faction,style,time,food,favor){
   S.playerFaction="egypt";S.aiFaction=faction;S.t=180-time;S.over=false;S.phase="battle";
   S.units=[];S.training=[];S.effects=[];S.aiFood=food;S.aiFavor=favor;S.ptahCharge=[false,false];
   S.forge=[0,0];S.ptahDiscount=[false,false];
   activeAiGods=[];aiProfile=AI_PROFILES[style][faction];aiHeroKeys=FACTIONS[faction].defaultHeroes.slice();
   aiSaveStart=null;aiHeroAt=55;aiTrainT=0;aiWave=null;lastLane=null;aiTargetRot=0;drill=null;
   buildings.length=0;for(const b of initial)buildings.push({...b,hexes:b.hexes.map(h=>h.slice()),hp:b.maxhp,dead:false});
   rebuildBldHexMap();TMAP=TMAP_INITIAL.map(()=>Array(COLS).fill("P"));
 }
 function add(side,key,c,r){const u=makeUnit(side,key,[c,r],null);S.units.push(u);return u;}
 for(const p of profiles)for(const random of [0.2,0.8])for(const food of [0,20,30,55,120])
 for(const mode of ["empty","myths","hero","ranged","invaded","siege","queued","expired","scheduled","unlocked"]){
   roll=random;reset(p.faction,p.style,60,food,150);
   if(mode==="myths"){add(0,"ammit",2,8);add(0,"ammit",3,8);}
   if(mode==="hero")add(0,"bata",2,8);
   if(mode==="ranged")for(let i=0;i<4;i++)add(0,"archer",i,8);
   if(mode==="invaded")add(0,"spear",2,3);
   const siege=aiProfile.pool.find(k=>unitDef(k,1).targetsBuildings);
   if(mode==="siege"||mode==="scheduled"||mode==="unlocked")add(1,siege,4,3);
   if(mode==="queued")S.training.push({side:1,key:siege});
   if(mode==="expired")aiSaveStart=60-AI_SAVE_MAX;
   if(mode==="unlocked"){
     activeAiGods=FACTIONS[p.faction].allGods.slice(0,1);
     for(const g of activeAiGods)S.aiGods[g.key]={unlocked:true};aiHeroAt=100;
   }
   const sample={faction:p.faction,style:p.style,random,food,favor:150,time:60,mode,
     units:S.units.map(u=>({side:u.side,key:u.key,c:u.hex[0],r:u.hex[1]})),queued:S.training.map(t=>t.key),
     saveStart:aiSaveStart===null?-1:aiSaveStart,heroAt:aiHeroAt,myths:activeAiGods.map(g=>g.myth)};
   sample.expected=pickAiUnit();sample.nextHeroAt=aiHeroAt;sample.nextSave=aiSaveStart===null?-1:aiSaveStart;
   choices.push(sample);
 }
 roll=0.5;
 for(const faction of ["china","egypt"])for(const mode of ["attack","defend","city-dead","only-temple","no-buildings"]){
   reset(faction,"balanced",20,120,100);
   if(mode==="defend"){add(0,"spear",4,1);add(0,"spear",3,2);}
   if(mode==="city-dead"||mode==="only-temple"||mode==="no-buildings"){
     for(const b of buildings)if(b.side===1&&(mode==="no-buildings"||b.type==="city"||mode==="only-temple"&&b.type==="fortress")){b.dead=true;b.hp=0;}
   }
   const sample={faction,mode,units:S.units.map(u=>({side:u.side,key:u.key,c:u.hex[0],r:u.hex[1]})),dead:buildings.filter(b=>b.dead).map(b=>b.type)};
   tickAI(0.05);sample.food=S.aiFood;sample.favor=S.aiFavor;
   sample.orders=S.training.map(t=>({key:t.key,source:t.src.type,duty:t.duty,path:t.path.map(h=>({c:h[0],r:h[1]}))}));
   sample.lane=aiWave?aiWave.lane:-1;sample.size=aiWave?aiWave.size:0;deployments.push(sample);
 }
 reset("china","balanced",0,0,0);
 for(const flying of [false,true])for(const lane of LANES)rallies.push({flying,lane,cells:aiRallyHexes(flying,lane).map(h=>({c:h[0],r:h[1]}))});
 /* ---- U24: the god-unlock half of tickAI, with the REAL hasPassive ----
    Training is parked (aiTrainT huge) and casting suppressed (aiLastCastAt = now), so each
    tick exercises exactly the unlock loop. Favor is fed on a fixed schedule and recorded per
    step, so the C# replay reads the same inputs instead of re-accumulating floats. */
 hasPassive=realHasPassive;
 function godsFor(faction){
   activeAiGods=FACTIONS[faction].allGods.filter(g=>FACTIONS[faction].defaultGodPick.includes(g.key));
   S.aiGods={};activeAiGods.forEach(g=>{S.aiGods[g.key]={unlocked:false,cdUntil:0};});
 }
 const godTimelines=[];
 for(const p of profiles)for(const income of [1,2.2,3.4])for(const start of [0,120]){
   reset(p.faction,p.style,0,0,start);godsFor(p.faction);aiTrainT=1e9;
   const favorIn=[],favorOut=[],events=[];const step=0.5;
   for(let i=0;i<=360;i++){
     const t=i*step;S.t=180-t;
     if(i>0)S.aiFavor=Math.min(S.FAVOR_CAP,S.aiFavor+income*step);
     favorIn.push(S.aiFavor);aiLastCastAt=elapsed();
     const before=activeAiGods.filter(g=>S.aiGods[g.key].unlocked).map(g=>g.key);
     tickAI(step);aiTrainT=1e9;
     favorOut.push(S.aiFavor);
     for(const g of activeAiGods)if(S.aiGods[g.key].unlocked&&!before.includes(g.key))events.push({step:i,time:t,key:g.key});
   }
   godTimelines.push({faction:p.faction,style:p.style,income,start,stepSeconds:step,
     selected:activeAiGods.map(g=>g.key),favorIn,favorOut,events,
     myths:activeAiGods.filter(g=>S.aiGods[g.key].unlocked).map(g=>g.myth)});
 }
 /* Unlock versus training in ONE tick: the browser buys a due god before it trains. */
 const godCompetition=[];
 roll=0.5;
 for(const p of profiles.filter(p=>p.style==="balanced"))for(const mode of ["god-first","saving","not-due","myth-after-unlock"]){
   const sched=p.godSched.slice().sort((a,b)=>a.at-b.at);
   const first=sched[0],second=sched[1];
   const firstGod=FACTIONS[p.faction].allGods.find(g=>g.key===first.key);
   const secondGod=FACTIONS[p.faction].allGods.find(g=>g.key===second.key);
   const myth=FACTIONS[p.faction].units[firstGod.myth];
   let time=second.at+1,food=120,favor=godUnlockCost(secondGod,1)+myth.cost.v+30-1;
   if(mode==="saving"){food=5;favor=godUnlockCost(secondGod,1);}
   if(mode==="not-due"){time=second.at-1;}
   if(mode==="myth-after-unlock"){favor=godUnlockCost(secondGod,1)+myth.cost.v+30;}
   reset(p.faction,p.style,time,food,favor);godsFor(p.faction);
   S.aiGods[first.key].unlocked=true;aiLastCastAt=elapsed();aiHeroAt=999; // keep heroes out: this is about myths and siege
   const siege=aiProfile.pool.find(k=>unitDef(k,1).targetsBuildings);
   if(mode!=="saving")add(1,siege,4,3);
   tickAI(0.05);
   godCompetition.push({faction:p.faction,style:p.style,mode,time,food,favor,granted:[first.key],
     siegeAlive:mode!=="saving",siege,
     unlocked:activeAiGods.filter(g=>S.aiGods[g.key].unlocked).map(g=>g.key),
     orders:S.training.map(t=>t.key),foodAfter:S.aiFood,favorAfter:S.aiFavor,
     heroAt:999,saveStart:aiSaveStart===null?-1:aiSaveStart});
 }
 /* ---- U29: AI power decisions. Real passives here, unlike the choice/deployment cases:
    a power's price and Osiris's corpse window read them, and the Unity replay has real gods. ---- */
 hasPassive=realHasPassive;
 const powerDecisions=[],powerCasts=[];
 function godsForKeys(faction,keys){
   activeAiGods=FACTIONS[faction].allGods.filter(g=>keys.includes(g.key));
   S.aiGods={};activeAiGods.forEach(g=>{S.aiGods[g.key]={unlocked:false,cdUntil:0};});
 }
 /* By SIDE and TYPE: this exporter's building order is not the browser's live order, and
    hurting the player's city instead of the AI's would silently make nuwa/bastet untestable. */
 function hurt(side,type,amount,hitAt){
   const b=buildings.find(b=>b.side===side&&b.type===type);
   b.hp-=amount; if(hitAt!==undefined) b.lastHitAt=hitAt;
 }
 function boardRows(){
   return {units:S.units.map(u=>({side:u.side,key:u.key,c:u.hex[0],r:u.hex[1],hp:u.hp,engaged:!!u.target})),
     buildings:buildings.map(b=>({side:b.side,type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1],
       hp:b.hp,maxhp:b.maxhp,dead:!!b.dead,lastHitAt:b.lastHitAt===undefined?-1:b.lastHitAt})),
     dead:S.deadHumans.map(d=>({side:d.side,key:d.key,at:d.at})),
     terrain:TMAP.map(row=>row.join("")),
     playerPick:activeGods.map(g=>g.key)};
 }
 function scene(faction,keys,time,favor,build,terrain,unlock){
   reset(faction,"balanced",time,120,favor); godsForKeys(faction,keys);
   /* reset() leaves the fields the earlier fixtures never touched, and a cast in one case would
      otherwise carry its flood tiles, chaos window and corpses into the next. */
   S.deadHumans.length=0; S.floodedHexes.length=0; S.chaosBuffUntil=[0,0]; S.trueSightUntil=[0,0];
   S.veilUntil=0; S.hunt=[null,null]; S.blightUntil=[0,0]; S.passivesStrippedUntil=[0,0];
   buildings.forEach(b=>{ b.invulnUntil=0; b.lastHitAt=undefined; });
   aiTrainT=1e9; aiHeroAt=1e9; aiLastCastAt=-99;
   (terrain||[]).forEach(t=>{TMAP[t.r][t.c]=t.t;});
   build();
   (unlock||keys).forEach(k=>{S.aiGods[k].unlocked=true;});
 }
 /* A deck is exactly four gods, so the whole roster is covered by several picks over the same
    board rather than by unlocking everything at once (which Unity's GodState rightly refuses). */
 function decisions(label,faction,picks,time,favor,build,terrain){
   picks.forEach((keys,i)=>{
     scene(faction,keys,time,favor,build,terrain);
     const rows=keys.map(k=>{
       const hex=pickAiPowerHex(k);
       return {key:k,cls:AI_POWER_CLASS[k]||"situational",hex:hex?{c:hex[0],r:hex[1]}:{c:-1,r:-1},
         hits:hex?aiPowerHits(k,hex):0,trigger:hex?!!aiPowerTriggerOK(k,hex):false};
     });
     powerDecisions.push(Object.assign({label:label+" ["+(i+1)+"]",faction,time,favor,unlocked:keys.slice(),rows},boardRows()));
   });
 }
 /* Cast cases run at t=20, before either faction's first scheduled god is due: tickAI unlocks
    before it casts, and an unlock would both spend the favor and put a SECOND god in the
    candidate list.
    ONE god ready per case: the browser shuffles its candidates with a comparator whose result is
    engine-specific, so a case with two ready gods would assert an order Unity cannot reproduce. */
 function castCase(label,faction,key,pick,time,favor,build,opts){
   opts=opts||{};
   scene(faction,pick,time,favor,build,opts.terrain,[key]);
   if(opts.gapBlocked) aiLastCastAt=elapsed();
   const board=boardRows(),favorBefore=S.aiFavor;
   tickAI(0.05);
   powerCasts.push(Object.assign({label,faction,key,pick:pick.slice(),time,favor,gapBlocked:!!opts.gapBlocked},board,{
     favorBefore,favorAfter:+S.aiFavor.toFixed(4),cdAfter:S.aiGods[key].cdUntil||0,
     chaosAfter:S.chaosBuffUntil.slice(),lastCastAt:+aiLastCastAt.toFixed(4),
     unitsAfter:S.units.map(u=>({side:u.side,key:u.key,c:u.hex[0],r:u.hex[1],hp:+u.hp.toFixed(4),dead:!!u.dead,
       slowUntil:u.slowUntil||0,stunUntil:u.stunUntil||0,conscriptedUntil:u.conscriptedUntil||0,transformed:!!u.transformed})),
     bldHpAfter:buildings.map(b=>+b.hp.toFixed(4)),bldInvulnAfter:buildings.map(b=>b.invulnUntil||0),
     overlays:S.floodedHexes.length}));
 }
 /* Every god of each AI faction, in decks of four: three picks cover China's ten and Egypt's eleven. */
 const CHINA=[["jade","sunwukong","nuwa","longwang"],["gonggong","erlangshen","xiwangmu","zhurong"],["chang","leigong","jade","nuwa"]];
 const EGYPT=[["horus","ra","bastet","osiris"],["isis","anubis","set","thoth"],["sekhmet","nephthys","ptah","horus"]];
 decisions("china: an empty board","china",CHINA,60,300,()=>{});
 decisions("china: three invaders massed","china",CHINA,60,300,()=>{
   const a=add(0,"spear",4,5),b=add(0,"archer",4,4),c=add(0,"axe",5,5); a.target=add(1,"ji",4,6); b.hold=true; c.hold=true;
 });
 decisions("china: one invader","china",CHINA,60,300,()=>{ add(0,"spear",4,6); add(1,"ji",4,8); });
 decisions("china: a city under attack","china",CHINA,60,300,()=>{
   hurt(1,"city",200,elapsed()-1); const a=add(0,"spear",4,2),b=add(0,"archer",5,2); a.target=buildings.find(b=>b.side===1&&b.type==="city"); b.hold=true;
 });
 decisions("china: a city hurt but quiet","china",CHINA,60,300,()=>{ hurt(1,"city",200); add(0,"spear",4,2); add(0,"archer",5,2); });
 decisions("china: no soil near the army","china",CHINA,60,300,()=>{
   add(1,"ji",4,4); add(1,"dao",4,3); add(0,"spear",4,7);
 },[0,1,2,3,4,5,6,7,8].flatMap(c=>[1,2,3,4,5,6,7].map(r=>({c,r,t:"M"}))));
 decisions("egypt: invaders in a rank","egypt",EGYPT,60,300,()=>{
   add(0,"spear",3,5); add(0,"archer",4,5); add(0,"axe",4,7,{}); add(1,"spear",4,3);
 });
 decisions("egypt: corpses and a hurt wall","egypt",EGYPT,90,300,()=>{
   hurt(1,"temple",150,elapsed()-1);
   S.deadHumans.push({key:"spear",at:elapsed()-10,hex:[4,3],side:1});
   S.deadHumans.push({key:"archer",at:elapsed()-70,hex:[4,3],side:1});
   add(0,"spear",4,4,{}); add(0,"archer",4,5,{});
 });
 castCase("china casts the dragon rain","china","longwang",CHINA[0],20,300,()=>{
   add(0,"spear",4,5); add(0,"archer",4,4); add(0,"axe",5,5);
 });
 castCase("china holds the rain for one target","china","longwang",CHINA[0],20,300,()=>{ add(0,"spear",4,5); });
 castCase("china holds the rain inside the gap","china","longwang",CHINA[0],20,300,()=>{
   add(0,"spear",4,5); add(0,"archer",4,4); add(0,"axe",5,5);
 },{gapBlocked:true});
 castCase("china cannot afford the rain","china","longwang",CHINA[0],20,5,()=>{
   add(0,"spear",4,5); add(0,"archer",4,4); add(0,"axe",5,5);
 });
 castCase("china mends a wall under fire","china","nuwa",CHINA[0],20,300,()=>{
   hurt(1,"city",200,elapsed()-1); add(0,"spear",4,2);
 });
 castCase("china leaves a quiet wall alone","china","nuwa",CHINA[0],20,300,()=>{ hurt(1,"city",200); add(0,"spear",4,2); });
 castCase("china conscripts the nearest","china","jade",CHINA[0],20,300,()=>{ add(0,"spear",4,4); add(0,"archer",4,6); });
 castCase("egypt sweeps a rank","egypt","horus",EGYPT[0],20,300,()=>{ add(0,"spear",3,5); add(0,"archer",4,5); });
 hasPassive=()=>false;
 const input=[[1,1],[2,1],[2,2],[2,1],[3,1],[4,1],[3,1],[3,2]];
 globalThis.__aiFixture={source:"godsbound_beta.html",choices,deployments,rallies,
   loopInput:input.map(h=>({c:h[0],r:h[1]})),loopOutput:loopErasedPath(input).map(h=>({c:h[0],r:h[1]})),
   godTimelines,godCompetition,powerDecisions,powerCasts};
})();
`;
eval(code);
/*END-DRIVER*/
}
