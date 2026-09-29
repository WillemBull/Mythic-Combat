/* U11: run the browser's movement and export deterministic EditMode reference traces. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs");
const path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/", end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=path.join(__dirname,"..","GodsboundUnity","Godsbound","Assets","Godsbound",
                    "Tests","EditMode","Fixtures","movement_reference.json");
fs.writeFileSync(out,JSON.stringify(globalThis.__movementReference));
console.log("[movement-ref] exported "+globalThis.__movementReference.routes.length+
            " route traces and "+globalThis.__movementReference.deviation.length+" deviation frames");

if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  buildings.length=0; rebuildBldHexMap();
  hasPassive=function(){return false;};
  Math.random=function(){return 0.5;};
  var routes=[];
  function frame(u){
    return {x:u.pos.x,y:u.pos.y,c:u.hex[0],r:u.hex[1],index:u.pathIdx,
            arrived:u.arrived,free:u.free,wait:u.waitT,deviation:u.deviateT,sidestepped:u.sidestepped};
  }
  function routeCase(name, code2, faction, side, flying, blocked, underFire){
    S.t=180; S.units=[]; S.training=[]; S.effects=[]; S.floodedHexes=[];
    S.playerFaction=faction; S.aiFaction=faction;
    TMAP=TMAP_INITIAL.map(row=>Array(COLS).fill(code2));
    var key=flying?"phoenix":"spear";
    var route=[[4,9],[4,8],[4,7]];
    var u=makeUnit(side,key,route[0],route.map(h=>h.slice()));
    S.units=[u];
    var blocker=null;
    if(blocked){
      blocker=makeUnit(side,key,route[1],[[4,7],[4,6]]);
      S.units.push(blocker);
    }
    var samples=[];
    for(var i=0;i<400;i++){
      S.t=180-i*0.05;
      if(blocker && i===30) blocker.dead=true;
      if(underFire){u.hp-=0.1;u.animHitUntil=elapsed()+1;}
      followPath(u,0.05);
      samples.push(frame(u));
      if(u.free && !u.path) break;
    }
    if(!u.free) throw new Error("reference route did not finish: "+name);
    routes.push({name:name,terrain:code2,faction:faction,side:side,key:key,
      flying:flying,blocked:blocked,underFire:underFire,dt:0.05,samples:samples});
  }
  // Desert, Road and High Ground were removed 2026-09-28; P/F/W are the codes a unit can stand on.
  for(var terrainCode of ["P","F","W"]){
    routeCase("ground-"+terrainCode,terrainCode,"greek",0,false,false,false);
    routeCase("flying-"+terrainCode,terrainCode,"greek",0,true,false,false);
  }
  routeCase("flying-mountains","M","greek",0,true,false,false);
  routeCase("egypt-water","W","egypt",0,false,false,false);
  routeCase("china-own-forest","F","china",0,false,false,false);
  routeCase("china-enemy-forest","F","china",1,false,false,false);
  routeCase("routed-friendly-wait","P","greek",0,false,true,false);
  routeCase("under-fire","P","greek",0,false,false,true);

  // Hold isolates the delay, as in test_deployment.js, without removing movement speed.
  S.t=180; S.units=[]; TMAP=TMAP_INITIAL.map(row=>Array(COLS).fill("P"));
  S.playerFaction="greek"; S.aiFaction="egypt";
  var route=[[4,7],[4,6],[4,5]];
  var u=makeUnit(0,"spear",[4,8],route); u.hold=true;
  var targetHex=neighbors(4,7).find(h=>hexDist(u.hex,h)===2);
  var target=makeUnit(1,"spear",targetHex,null);
  S.units=[u,target];
  var deviation=[];
  for(var i=0;i<40;i++){
    S.t=180-i*0.05; u.target=target;
    updateUnit(u,0.05); deviation.push(frame(u));
  }
  globalThis.__movementReference={source:"godsbound_beta.html",hex:HEX,ox:OX,oy:OY,
    routes:routes,deviationTarget:{c:targetHex[0],r:targetHex[1]},deviation:deviation};
})();
`;
eval(code);
/*END-DRIVER*/
}
