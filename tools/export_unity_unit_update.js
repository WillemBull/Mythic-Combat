/* U15: browser targeting, visibility, march choice and ordinary attack timelines. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/unit_update_reference.json"),JSON.stringify(globalThis.__unitUpdateReference));
console.log("[unit-update-ref] exported "+globalThis.__unitUpdateReference.cases.length+" browser scenarios");
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();Math.random=function(){return 0;};
  var cases=[],passives=[];
  hasPassive=function(side,key){return passives.some(p=>p.side===side&&p.key===key);};
  function reset(){
    S.units=[];S.effects=[];S.deadHumans=[];S.floodedHexes=[];S.hunt=[null,null];
    S.playerFaction=S.aiFaction="greek";S.trueSightUntil=[0,0];S.veilUntil=0;
    S.t=170;S.bldDmg=[0,0];S.chaosBuffUntil=[0,0];aiWave=null;passives=[];
    buildings.length=0;rebuildBldHexMap();TMAP=TMAP_INITIAL.map(row=>Array(COLS).fill("P"));
  }
  function unit(side,c,r,def){
    var u=makeUnit(side,"spear",[c,r],null);
    u.def=Object.assign({hp:10000,dmg:20,cat:"human",dtype:"melee",armor:"light",range:1,as:1,speed:1,size:1},def||{});
    u.key="test";u.hp=u.maxhp=u.def.hp;u.flying=!!u.def.flying;u.atkT=0;
    S.units.push(u);return u;
  }
  function bld(c,r,type){var b={side:1,type:type,name:type,hp:10000,maxhp:10000,hexes:[[c,r],[c,r+1]],dead:false};buildings.push(b);rebuildBldHexMap();return b;}
  function packedDef(u){
    var d=u.def,typed=["hp","dmg","cat","dtype","armor","range","as","speed","size"],out={key:u.key,extras:[]};
    for(var k of typed)out[k==="as"?"attackSpeed":k]=d[k];
    for(var k of Object.keys(d))if(!typed.includes(k))out.extras.push({key:k,kind:typeof d[k]==="boolean"?"bool":typeof d[k],value:String(d[k])});
    return out;
  }
  function target(u){return {unit:S.units.indexOf(u.target),building:buildings.indexOf(u.target)};}
  function frame(u){return {target:target(u),march:buildings.indexOf(u.marchGoal),route:u.path?u.path.map(h=>({c:h[0],r:h[1]})):[],index:u.pathIdx,
    c:u.hex[0],r:u.hex[1],x:u.pos.x,y:u.pos.y,arrived:u.arrived,free:u.free,attackTimer:u.atkT,hp:S.units.map(o=>o.hp),buildingHp:buildings.map(b=>b.hp)};}
  function capture(name,setup,frames){
    reset();setup();var u=S.units[0],cells=[];
    for(var r=0;r<ROWS;r++)for(var c=0;c<COLS;c++)if(TMAP[r][c]!=="P")cells.push({c:c,r:r,code:TMAP[r][c]});
    // An exactly representable sub-clamp step isolates scheduling from float/double edge rounding.
    var row={name:name,dt:1/32,elapsed:10,factions:[S.playerFaction,S.aiFaction],reveal:S.trueSightUntil.slice(),veil:S.veilUntil,passives:passives.slice(),terrain:cells,
      groves:S.floodedHexes.map(f=>({c:f.c,r:f.r,owner:f.owner})),
      units:S.units.map(o=>({side:o.side,c:o.hex[0],r:o.hex[1],x:o.pos.x,y:o.pos.y,def:packedDef(o),dead:!!o.dead,free:!!o.free,hold:!!o.hold,arrived:!!o.arrived,
        invisible:!!o.invisible,stunUntil:o.stunUntil,target:target(o),attackTimer:o.atkT,route:o.path?o.path.map(h=>({c:h[0],r:h[1]})):[],index:o.pathIdx})),
      buildings:buildings.map(b=>({side:b.side,type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1],hp:b.hp})),
      pick:S.units.indexOf(scanEncounter(u)),hidden:S.units.map(e=>hiddenFrom(e,u)),frames:[]};
    for(var i=0;i<(frames||0);i++){S.t=170-i*row.dt;updateUnit(u,row.dt);row.frames.push(frame(u));}
    cases.push(row);
  }
  capture("nearer",()=>{unit(0,4,6,{range:3});unit(1,4,3);unit(1,4,5);});
  capture("route-blocker",()=>{var u=unit(0,4,6,{range:3});unit(1,4,7);unit(1,4,3);u.path=[[4,4],[4,3]];u.free=false;});
  capture("ally-focus",()=>{unit(0,4,6,{range:3});unit(1,4,5);var t=unit(1,4,3);var a=unit(0,5,4);a.target=t;});
  capture("dead-ally-no-focus",()=>{unit(0,4,6,{range:3});unit(1,4,5);var t=unit(1,4,3);var a=unit(0,5,4);a.target=t;a.dead=true;});
  for(var cat of ["human","hero","myth"])capture("counter-"+cat,()=>{unit(0,4,6,{range:3,cat:cat});unit(1,4,5,{cat:cat});unit(1,4,4,{cat:CAT_BEATS[cat]});});
  capture("stable-tie",()=>{unit(0,4,6);unit(1,4,5);unit(1,4,7);});
  capture("siege-skips-units",()=>{unit(0,4,6,{targetsBuildings:true});unit(1,4,5);});
  capture("ally-only-fallback",()=>{unit(0,4,6);unit(0,4,5,{allyTargetOnly:true});});
  capture("enemy-before-ally-only",()=>{unit(0,4,6,{range:3});unit(0,4,5,{allyTargetOnly:true});unit(1,4,3);});
  capture("enemy-ignores-ally-only",()=>{unit(0,4,6);unit(1,4,5,{allyTargetOnly:true});passives=[{side:0,key:"trueSight"}];});
  for(var ranged of [false,true])for(var blocked of [false,true])capture("los-"+ranged+"-"+blocked,()=>{
    unit(0,4,6,{range:2,dtype:ranged?"ranged":"melee"});unit(1,4,4);
    for(var h of neighbors(4,6).filter(h=>hexDist(h,[4,4])===1))TMAP[h[1]][h[0]]=blocked?"M":"P";
  });
  for(var side of [0,1])for(var revealed of [false,true])capture("bamboo-"+side+"-"+revealed,()=>{
    var r=side===0?8:4;unit(1-side,4,r+1);unit(side,4,r);S.playerFaction=S.aiFaction="china";TMAP[r][4]="F";if(revealed)S.trueSightUntil[side]=20;
  });
  capture("bamboo-reveals-to-victim",()=>{var u=unit(0,4,5),e=unit(1,4,4);S.aiFaction="china";TMAP[4][4]="F";e.target=u;});
  capture("bamboo-hides-from-other",()=>{unit(0,4,5);var e=unit(1,4,4),other=unit(0,3,4);S.aiFaction="china";TMAP[4][4]="F";e.target=other;});
  capture("foreign-forest-visible",()=>{unit(0,4,6);unit(1,4,7);S.aiFaction="china";TMAP[7][4]="F";});
  capture("grown-grove",()=>{unit(0,4,6);unit(1,4,7);S.floodedHexes=[{c:4,r:7,to:"F",owner:1}];TMAP[7][4]="F";});
  capture("enemy-grove-visible",()=>{unit(0,4,6);unit(1,4,7);S.floodedHexes=[{c:4,r:7,to:"F",owner:0}];TMAP[7][4]="F";});
  for(var side of [0,1])for(var expired of [false,true])capture("twilight-"+side+"-"+expired,()=>{unit(1-side,4,5);unit(side,4,6);S.veilUntil=expired?10:20;});
  capture("invisible-true-sight",()=>{unit(0,4,6);unit(1,4,5).invisible=true;passives=[{side:0,key:"trueSight"}];});
  for(var reason of ["dead","kiter","hidden","los","stunned","visible"])
    capture("update-"+reason,()=>{var u=unit(0,4,6,{dtype:reason==="los"?"ranged":"melee",range:reason==="los"?2:1}),e=unit(1,4,reason==="kiter"?3:reason==="los"?4:5);u.target=e;u.hold=true;
      if(reason==="dead")e.dead=true;if(reason==="hidden")e.invisible=true;if(reason==="stunned")u.stunUntil=20;
      if(reason==="los")for(var h of neighbors(4,6).filter(h=>hexDist(h,e.hex)===1))TMAP[h[1]][h[0]]="M";
    },1);
  capture("routed-kiter",()=>{var u=unit(0,4,6),e=unit(1,4,4);u.target=e;u.path=[[4,7],[4,8]];u.free=false;u.hold=true;},1);
  capture("arrival-before-hit",()=>{var u=unit(0,4,6),e=unit(1,4,5);u.target=e;u.arrived=false;u.pos=hexCenter(4,7);},30);
  for(var speed of [1,2,1.25])capture("attack-rate-"+speed,()=>{var u=unit(0,4,6,{as:speed}),e=unit(1,4,5);u.target=e;},80);
  capture("building-defender-interrupt",()=>{var u=unit(0,4,6);u.target=bld(4,4,"city");unit(1,4,5);},1);
  capture("siege-keeps-building",()=>{var u=unit(0,4,6,{targetsBuildings:true});u.target=bld(4,4,"city");unit(1,4,5);},1);
  capture("march-choice",()=>{unit(0,4,8);bld(1,1,"city");bld(5,4,"temple");},25);
  capture("march-terrain",()=>{unit(0,4,8);bld(4,3,"city");bld(8,5,"temple");for(var c=1;c<8;c++)TMAP[5][c]="M";},25);
  // Spatial scoring matrix across range, categories and blocking-route placement.
  for(var range of [1,2,3])for(var cat of ["human","hero","myth"])for(var route of [false,true])
    capture("matrix-"+range+"-"+cat+"-"+route,()=>{var u=unit(0,4,6,{range:range,cat:cat});unit(1,3,6,{cat:"hero"});unit(1,4,4,{cat:"myth"});unit(1,6,5,{cat:"human"});if(route){u.path=[[5,6],[6,5]];u.free=false;}});
  globalThis.__unitUpdateReference={source:"godsbound_beta.html",hex:HEX,ox:OX,oy:OY,cases:cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
