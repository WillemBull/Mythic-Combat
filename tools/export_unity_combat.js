/* U12: actual browser combat results, consumed by Unity EditMode tests. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/combat_reference.json"),JSON.stringify(globalThis.__combatReference));
console.log("[combat-ref] exported "+globalThis.__combatReference.cases.length+" browser cases");
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  var cases=[],passives=[];
  hasPassive=function(side,key){return passives.some(p=>p.side===side&&p.key===key);};
  Math.random=function(){return 0.5;};
  function reset(){
    S.t=170;S.units=[];S.effects=[];S.deadHumans=[];S.floodedHexes=[];S.chaosBuffUntil=[20,20];
    S.food=10;S.aiFood=10;S.favor=10;S.aiFavor=10;S.bldDmg=[0,0];S.phase="battle";
    S.playerFaction="greek";S.aiFaction="greek";
    buildings.length=0;rebuildBldHexMap();
    TMAP=TMAP_INITIAL.map(row=>Array(COLS).fill("P"));passives=[];activeGods=[];activeAiGods=[];
  }
  function unit(side,def,hex){
    var u=makeUnit(side,"spear",hex||[4,6],null);
    u.def=Object.assign({cat:"human",dtype:"melee",armor:"light",hp:100,dmg:20,speed:1,size:1,range:1,as:1},def);
    u.key=def.key||"test";u.maxhp=u.hp=u.def.hp;u.flying=!!u.def.flying;
    S.units.push(u);return u;
  }
  function building(side,type,hexes,hp){
    var b={side:side,type:type,name:type,hexes:hexes,hp:hp||100,maxhp:hp||100,dead:false};
    buildings.push(b);rebuildBldHexMap();return b;
  }
  function packedDef(u){
    var d=u.def,typed=["cat","dtype","armor","hp","dmg","speed","size","range","as"];
    var out={key:u.key,cat:d.cat,dtype:d.dtype,armor:d.armor,hp:d.hp,dmg:d.dmg,speed:d.speed,size:d.size,range:d.range,attackSpeed:d.as,extras:[]};
    for(var key of Object.keys(d))if(!typed.includes(key))out.extras.push({key:key,kind:typeof d[key]==="boolean"?"bool":typeof d[key],value:String(d[key])});
    return out;
  }
  var fields=["buffUntil","buffDmg","stunUntil","rootUntil","slowUntil","invulnUntil","aegisUntil","bloodlustUntil","weakenUntil","frenzyUntil","poisonUntil","poisonDps","lastStandEndsAt","retreatDeadline","ambushUsed","invisible","hasRevived","lastStandUsed","retreating","free","hold"];
  function packedUnit(u){
    var out={side:u.side,key:u.key,def:packedDef(u),c:u.hex[0],r:u.hex[1],x:u.pos.x,y:u.pos.y,hp:Math.max(0,u.hp),dead:!!u.dead,
      route:u.path?u.path.map(h=>({c:h[0],r:h[1]})):null,index:u.pathIdx};
    for(var f of fields)out[f]=u[f]||0;
    // JSON booleans must stay booleans for Unity's serializer.
    for(var f of ["ambushUsed","invisible","hasRevived","lastStandUsed","retreating","free","hold"])out[f]=!!u[f];
    return out;
  }
  function packedBuilding(b){return {side:b.side,type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1],hp:Math.max(0,b.hp),maxHp:b.maxhp,invulnUntil:b.invulnUntil||0,lastHitAt:b.lastHitAt||0,dead:!!b.dead};}
  function capture(name,setup,hit){
    reset();var pair=setup(),a=pair[0],t=pair[1];
    var terrainCells=[];
    for(var r=0;r<ROWS;r++)for(var c=0;c<COLS;c++)if(TMAP[r][c]!=="P")terrainCells.push({c:c,r:r,code:TMAP[r][c]});
    var row={name:name,hit:!!hit,elapsed:elapsed(),gardenDamage:[gardenMult(0,"gardenDmg"),gardenMult(1,"gardenDmg")],factions:[S.playerFaction,S.aiFaction],passives:passives.slice(),terrain:terrainCells,
      units:S.units.map(packedUnit),buildings:buildings.map(packedBuilding),targetBuilding:isBldg(t),targetIndex:isBldg(t)?buildings.indexOf(t):S.units.indexOf(t),expectedDamage:calcDmg(a,t)};
    if(hit)dealDamage(a,t);
    row.after=S.units.map(packedUnit);row.buildingsAfter=buildings.map(packedBuilding);
    row.food=[S.food,S.aiFood];row.favor=[S.favor,S.aiFavor];row.buildingDamage=S.bldDmg.slice();
    row.deaths=S.deadHumans.map(d=>({key:d.key,side:d.side,c:d.hex[0],r:d.hex[1],at:d.at}));cases.push(row);
  }
  function pair(ad,td){return [unit(0,ad||{},[4,6]),unit(1,td||{},[4,5])];}
  for(var a of ["human","hero","myth"])for(var t of ["human","hero","myth"])
    capture("category-"+a+"-"+t,()=>pair({cat:a},{cat:t}),true);
  for(var dtype of ["melee","ranged","crushing"])for(var armor of ["light","shielded","heavy"])for(var terrainCode of ["P","F","W"])
    capture("cover-"+dtype+"-"+armor+"-"+terrainCode,()=>{var p=pair({dtype:dtype},{armor:armor});TMAP[5][4]=terrainCode;return p;},true);
  for(var faction of ["egypt","aztec","china","greek"])
    capture("water-"+faction,()=>{var p=pair();S.aiFaction=faction;TMAP[5][4]="W";building(1,"city",[[3,5],[3,4]]);return p;},true);
  for(var dtype of ["melee","ranged","crushing"])for(var siege of [false,true])
    capture("building-"+dtype+"-"+siege,()=>[unit(0,{dtype:dtype,siege:siege}),building(1,"city",[[4,4],[4,3]])],true);
  for(var passive of ["rangedDmg10","kingsAuthority","openTileDmg","gardenOfKunlun","chaosDamageBoost","moonlight","razorWings","waterDamageBonus","fireDefeatsWater","warcry"])
    capture("passive-"+passive,()=>{var p=pair({dtype:"ranged"});passives=[{side:0,key:passive}];building(0,"city",[[3,6],[3,7]]);unit(0,{},[5,5]);TMAP[5][5]="W";if(passive==="gardenOfKunlun"){S.playerFaction="china";p[0].hex=[4,7];TMAP[7][4]="F";}return p;},true);
  capture("fortress-defense",()=>{var p=pair();passives=[{side:1,key:"fortressDefense"}];building(1,"fortress",[[3,5],[3,4]]);return p;},true);
  capture("siege-passives",()=>{passives=[{side:0,key:"automata"},{side:0,key:"earthshaker"}];return [unit(0,{dtype:"crushing",siege:true}),building(1,"city",[[4,4],[4,3]])];},true);
  for(var cat of ["human","hero","myth"])
    capture("mentor-"+cat,()=>{var p=pair({cat:cat});unit(0,{teachRange:2,teachDmg:1.3},[3,6]);return p;},true);
  for(var status of ["invulnUntil","aegisUntil","bloodlustUntil"])
    capture("target-"+status,()=>{var p=pair();p[1][status]=20;return p;},true);
  for(var status of ["stunUntil","weakenUntil","frenzyUntil","buffUntil"])
    capture("attacker-"+status,()=>{var p=pair({rageDmg:1.7});p[0][status]=20;p[0].buffDmg=1.4;return p;},true);
  for(var flag of ["antimyth","bonusVsHero","bonusVsSerpent","surpriseAttack","abroadDmg"])
    capture("def-"+flag,()=>{var ad={};ad[flag]=1.8;var p=pair(ad,{cat:flag==="bonusVsHero"?"hero":"myth",serpent:true});p[0].hex=[4,4];return p;},true);
  capture("dual-wielder",()=>{var p=pair({dtype:"ranged",meleeWithin:1},{meleeResist:0.1});TMAP[5][4]="F";return p;},true);
  capture("falling-buildings",()=>{var p=pair({strongerAsBuildingsFall:true});var b=building(0,"city",[[3,7],[3,8]]);b.hp=25;return p;},true);
  capture("water-ambush",()=>{var p=pair({ambushInWater:true});TMAP[6][4]="W";return p;},true);
  capture("on-hit-statuses",()=>{var p=pair({slows:true,intoxicates:5,poisons:true,poisonDur:6,poisonDps:9,disruptsRoute:true,drainsFavor:3});p[1].path=[[4,4],[4,3]];p[1].free=false;return p;},true);
  capture("pestilence-overwrite",()=>{var p=pair({intoxicates:8});passives=[{side:0,key:"pestilence"}];return p;},true);
  for(var flag of ["revivesOnce","retracesOnDeath","lastStandOnDeath"])
    capture("revival-"+flag,()=>{var td={hp:30};td[flag]=true;td.retreatInvuln=true;var p=pair({dmg:100},td);p[1].path=[[4,8],[4,7],[4,6],[4,5]];p[1].pathIdx=3;p[1].free=false;return p;},true);
  capture("revival-already-used",()=>{var p=pair({dmg:100},{hp:30,revivesOnce:true});p[1].hasRevived=true;return p;},true);
  capture("death-rewards",()=>{var p=pair({dmg:100},{hp:30});S.playerFaction=S.aiFaction="aztec";passives=[{side:0,key:"bloodFavor"},{side:0,key:"inevitable"},{side:1,key:"ferrymansToll"},{side:1,key:"earthMother"},{side:1,key:"deathFavorNearTemple"}];building(1,"temple",[[3,5],[3,4]]);return p;},true);
  capture("building-destroyed",()=>{passives=[{side:1,key:"deathFavorSurge"},{side:0,key:"newGrowth"},{side:1,key:"newGrowth"}];return [unit(0,{dmg:1000}),building(1,"city",[[4,4],[4,3]],10)];},true);
  capture("building-ward",()=>{var b=building(1,"city",[[4,4],[4,3]]);b.invulnUntil=20;return [unit(0,{surpriseAttack:2}),b];},true);
  capture("execution",()=>{var p=pair({dmg:60,executesBelow:0.5},{revivesOnce:true});passives=[{side:0,key:"bloodFavor"}];return p;},true);
  for(var flag of ["splash","chaosSplash"])
    capture(flag+"-positions",()=>{var ad={dmg:20};ad[flag]=0.5;var p=pair(ad);unit(0,{},[3,5]);var u=unit(1,{},[8,10]);u.pos={...p[0].pos};var v=unit(1,{},[3,5]);v.pos=hexCenter(8,10);return p;},true);
  for(var flag of ["splash","chaosSplash"])
    capture(flag+"-deaths",()=>{var ad={dmg:40};ad[flag]=1;var p=pair(ad);unit(1,{hp:10,revivesOnce:true},[3,5]);var ward=unit(1,{hp:10},[5,5]);ward.invulnUntil=20;passives=[{side:0,key:"bloodFavor"},{side:0,key:"inevitable"}];return p;},true);
  capture("clone-on-kill",()=>{S.playerFaction="aztec";return pair(Object.assign({key:"centzonhuitznahua"},AZ_UNITS.centzonhuitznahua),{hp:1});},true);
  capture("garden-active",()=>{var p=pair();S.playerFaction="china";p[0].hex=[4,7];TMAP[7][4]="F";activeGods=[FACTIONS.china.allGods.find(g=>g.passiveKey==="gardenOfKunlun")];passives=[{side:0,key:"gardenOfKunlun"}];return p;},true);
  capture("expired-statuses",()=>{var p=pair({rageDmg:2});p[0].buffUntil=p[0].weakenUntil=p[0].frenzyUntil=p[0].stunUntil=10;p[1].invulnUntil=p[1].aegisUntil=p[1].bloodlustUntil=10;return p;},true);
  capture("consumed-ambush",()=>{var p=pair({surpriseAttack:2});p[0].ambushUsed=true;passives=[{side:0,key:"razorWings"}];return p;},true);
  capture("same-side-cleave",()=>{var p=[unit(0,{splash:1,dmg:100}),unit(0,{hp:10},[4,5])];unit(0,{},[3,5]);passives=[{side:0,key:"bloodFavor"}];return p;},true);
  capture("same-side-chaos",()=>{var p=[unit(0,{chaosSplash:1,dmg:100}),unit(0,{hp:10},[4,5])];unit(0,{},[3,5]);return p;},true);
  capture("revival-executed",()=>pair({dmg:100,executesBelow:0.5},{hp:30,revivesOnce:true}),true);
  capture("last-stand-already-used",()=>{var p=pair({dmg:100},{hp:30,lastStandOnDeath:true});p[1].lastStandUsed=true;return p;},true);
  capture("retreat-no-route",()=>pair({dmg:100},{hp:30,retracesOnDeath:true,retreatInvuln:true}),true);
  capture("clone-at-cap",()=>{S.playerFaction="aztec";var d=Object.assign({key:"centzonhuitznahua"},AZ_UNITS.centzonhuitznahua),p=pair(d,{hp:1});for(var i=0;i<5;i++)unit(0,d,[i,9]);return p;},true);
  capture("clone-blocked-ground",()=>{S.playerFaction="aztec";var p=pair(Object.assign({key:"centzonhuitznahua"},AZ_UNITS.centzonhuitznahua),{hp:1});for(var h of neighbors(4,6))unit(0,{},h);return p;},true);
  capture("clone-flyers-do-not-block",()=>{S.playerFaction="aztec";var p=pair(Object.assign({key:"centzonhuitznahua"},AZ_UNITS.centzonhuitznahua),{hp:1});for(var h of neighbors(4,6))unit(0,{flying:true},h);return p;},true);
  // Every real definition gets exercised against every category, preserving its optional flags.
  for(var faction of ["egypt","china","aztec","greek"]){
    var table=FACTIONS[faction].units;
    for(var key of Object.keys(table))for(var cat of ["human","hero","myth"])
      capture("table-"+faction+"-"+key+"-"+cat,()=>{var p=pair(Object.assign({key:key},table[key]),{cat:cat});S.playerFaction=faction;return p;},false);
  }
  globalThis.__combatReference={source:"godsbound_beta.html",hex:HEX,ox:OX,oy:OY,cases:cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
