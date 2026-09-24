/* U21: run the browser gun against controlled boards. Offline, no dependencies. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8"),begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/fortress_reference.json"),JSON.stringify(globalThis.__fortresses,null,2)+"\n");
console.log("Exported "+globalThis.__fortresses.cases.length+" fortress cases");
if(false){
/*DRIVER*/
code+=String.raw`
;(function(){
  const cases=[];
  const presets=[
    {name:"nearest-footprint",units:[{c:6,r:5,hp:60}]},
    {name:"lowest-health",units:[{c:4,r:4,hp:70},{c:6,r:5,hp:25}]},
    {name:"stable-ties",units:[{c:4,r:4,hp:25},{c:6,r:5,hp:25}]},
    {name:"out-of-range",units:[{c:0,r:0,hp:10}]},
    {name:"flying",units:[{c:4,r:4,hp:60,flying:true}]},
    {name:"ally-only",units:[{c:4,r:4,hp:10,ally:true},{c:6,r:5,hp:60}]},
    {name:"friendly",units:[{c:4,r:4,hp:10,friendly:true}]},
    {name:"invulnerable",units:[{c:4,r:4,hp:10,invuln:20},{c:6,r:5,hp:60}]},
    {name:"invulnerability-boundary",units:[{c:4,r:4,hp:60,invuln:10}]},
    {name:"concealed-still-hit",units:[{c:4,r:4,hp:60,invisible:true}]},
    {name:"dead-unit",units:[{c:4,r:4,hp:0}]},
    {name:"dead-fortress",dead:true,units:[{c:4,r:4,hp:60}]},
    {name:"city-does-not-fire",type:"city",units:[{c:4,r:4,hp:60}]},
    {name:"waiting-cooldown",timer:0.1,units:[{c:4,r:4,hp:60}]},
    {name:"cooldown-boundary",timer:0.05,units:[{c:4,r:4,hp:60}]},
    {name:"lethal",units:[{c:4,r:4,hp:10}]},
    {name:"lethal-does-not-revive",units:[{c:4,r:4,hp:10,revives:true}]},
    {name:"empty-ready-retry",timer:-5,units:[]},
    {name:"occupied-building",units:[{c:4,r:5,hp:60}]},
    {name:"reserved-hex-not-position",units:[{c:4,r:4,hp:60,farPosition:true}]}
  ];
  startMatch();
  for(const side of [0,1])for(const p of presets){
    S.playerFaction="aztec";S.aiFaction="egypt";
    S.t=170;S.food=S.aiFood=S.favor=S.aiFavor=0;S.units=[];S.effects=[];S.deadHumans=[];
    const b=mkBld(side,p.type||"fortress","Test","",[[4,5],[5,5]],660);
    b.dead=!!p.dead;b.atkTimer=p.timer||0;buildings.splice(0,buildings.length,b);rebuildBldHexMap();
    hasPassive=(s,k)=>s===side&&k==="inevitable";
    for(const v of p.units){
      const us=v.friendly?side:1-side;
      const u=makeUnit(us,us===0?"atlatl":"spear",[v.c,v.r]);
      u.def={...u.def,allyTargetOnly:!!v.ally,flying:!!v.flying,revivesOnce:!!v.revives};
      u.hp=v.hp;u.dead=v.hp<=0;u.invulnUntil=v.invuln||0;u.invisible=!!v.invisible;
      if(v.farPosition)u.pos=hexCenter(0,0);
      S.units.push(u);
    }
    tickFortresses(0.05);
    cases.push({...p,side,timer:p.timer||0,type:p.type||"fortress",dead:!!p.dead,
      expectedHp:S.units.map(u=>u.hp),expectedDead:S.units.map(u=>u.dead),
      nextTimer:b.atkTimer,shots:S.effects.filter(e=>e.kind==="tracer").length,
      humans:S.deadHumans.length,favor:[S.favor,S.aiFavor]});
  }
  globalThis.__fortresses={source:"godsbound_beta.html",cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
