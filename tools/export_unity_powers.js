/* U26: god power casts and timed god effects, measured from the browser game.
 *
 * Every case builds a known board (all plains, the live startMatch building layout with a
 * fixed RNG), places units, then either
 *   - casts through the REAL castPlayerPower (side 0: validation, payment, cooldown, apply,
 *     Set's chaos window), or
 *   - calls the REAL applyGodPower for side 1 (AI casting itself is U29),
 * and records the whole observable state after the cast and after each tickGodEffects step.
 * The C# replays the same inputs and must reproduce every number.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/powers_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=globalThis.__powersReference;
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/powers_reference.json"),
  JSON.stringify(out,null,1)+"\n");
console.log("[powers-ref] "+out.casts.length+" casts ("+[...new Set(out.casts.map(c=>c.god))].join(",")+"), "+
  out.effects.length+" effect timelines");
if(false){
/*DRIVER*/
code+=`
;(function(){
  var roll=0.3; Math.random=function(){ return roll; };
  /* Capture applyGodPower's verdict even when the real castPlayerPower calls it. */
  var lastApplied=null, realApply=applyGodPower;
  applyGodPower=function(){ var r=realApply.apply(null,arguments); lastApplied=(r!==false); return r; };
  var CONFIGS={
    egypt:{player:"egypt",ai:"china",caster:0},
    vsGreek:{player:"greek",ai:"egypt",caster:1},
    aztec:{player:"aztec",ai:"egypt",caster:0},
    china:{player:"china",ai:"egypt",caster:0},
    chinaAI:{player:"egypt",ai:"china",caster:1},
    greek:{player:"greek",ai:"egypt",caster:0},
    greekAI:{player:"egypt",ai:"greek",caster:1},
    aztecAI:{player:"egypt",ai:"aztec",caster:1}
  };
  function setup(cfg,pick,aiPick,time){
    festival=null; drill=null; campaign=null;
    S.playerFaction=cfg.player; S.aiFaction=cfg.ai;
    S.godPick=pick.slice(); S.aiGodPick=aiPick.slice();
    S.loadout=FACTIONS[cfg.player].defaultLoadout.slice(); S.heroPick=FACTIONS[cfg.player].defaultHeroes.slice();
    roll=0.3; startMatch(false);
    S.phase="battle"; S.over=false; S.t=180-time;
    for(var r=0;r<ROWS;r++) for(var c=0;c<COLS;c++) TMAP[r][c]="P";
    S.units.length=0; S.deadHumans.length=0; S.training.length=0;
    S.favor=0; S.aiFavor=0; S.food=0; S.aiFood=0;
    /* startMatch does not clear a building's invulnUntil, nor S.floodedHexes (browser quirks, both
       recorded in HANDOFF), so a ward or a still-running flood from the previous case would leak
       into this one -- a cast case with no ticks never expires its own overlays. Cleared here. */
    buildings.forEach(function(b){ b.invulnUntil=0; });
    S.floodedHexes.length=0;
    S.trueSightUntil=[0,0]; S.ptahCharge=[false,false]; S.chaosBuffUntil=[0,0]; S.veilUntil=0;
    S.hunt=[null,null]; S.blightUntil=[0,0]; S.forge=[0,0]; S.bldDmg=[0,0];
    S.passivesStrippedUntil=[0,0];
  }
  function bldRows(){ return buildings.map(function(b){ return {side:b.side,type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1],maxhp:b.maxhp,hp:b.hp,dead:!!b.dead}; }); }
  function add(side,key,c,r,o){
    o=o||{};
    var p=o.path||null;
    var u=makeUnit(side,key,[c,r],p);
    if(o.pathIdx) u.pathIdx=o.pathIdx;
    if(o.hp!==undefined) u.hp=o.hp;
    if(o.invuln) u.invulnUntil=o.invuln;
    if(o.poison){ u.poisonUntil=o.poison.until; u.poisonDps=o.poison.dps; }
    if(o.despawn) u.despawnAt=o.despawn;
    if(o.invisible) u.invisible=true;
    if(o.conscript){ u.origSide=u.side; u.side=o.conscript.side; u.conscriptedUntil=o.conscript.until; u.free=true; u.path=null; u.pathIdx=0; }
    S.units.push(u); return u;
  }
  function unitIn(u,o){ o=o||{};
    return {side:u.origSide!==undefined?u.origSide:u.side,key:u.key,c:u.hex[0],r:u.hex[1],hp:u.hp,
      path:(o.path||[]).map(function(h){return {c:h[0],r:h[1]};}),pathIdx:o.pathIdx||0,
      invuln:o.invuln||0,poisonUntil:o.poison?o.poison.until:0,poisonDps:o.poison?o.poison.dps:0,despawn:o.despawn||0,
      invisible:!!o.invisible,conscriptSide:o.conscript?o.conscript.side:-1,conscriptUntil:o.conscript?o.conscript.until:0}; }
  function unitOut(u){
    return {side:u.side,key:u.key,c:u.hex[0],r:u.hex[1],hp:+u.hp.toFixed(4),dead:!!u.dead,
      slowUntil:u.slowUntil||0,frenzyUntil:u.frenzyUntil||0,invulnUntil:u.invulnUntil||0,
      despawnAt:u.despawnAt||0,free:!!u.free,hasRevived:!!u.hasRevived,retreating:!!u.retreating,
      lastStandEndsAt:u.lastStandEndsAt||0,maxhp:u.maxhp,stunUntil:u.stunUntil||0,invisible:!!u.invisible,
      conscriptedUntil:u.conscriptedUntil||0,transformed:!!u.transformed,flying:!!u.flying,
      aegisUntil:u.aegisUntil||0,bloodlustUntil:u.bloodlustUntil||0,buffUntil:u.buffUntil||0,buffDmg:u.buffDmg,buffSpd:u.buffSpd,
      weakenUntil:u.weakenUntil||0,rootUntil:u.rootUntil||0,
      route:(u.path||[]).map(function(h){return {c:h[0],r:h[1]};}),routeIndex:u.pathIdx||0};
  }
  function snapshot(){
    return {units:S.units.map(unitOut),
      favor:[+S.favor.toFixed(4),+S.aiFavor.toFixed(4)],food:[+S.food.toFixed(4),+S.aiFood.toFixed(4)],
      bldInvuln:buildings.map(function(b){return b.invulnUntil||0;}),
      bldHp:buildings.map(function(b){return +b.hp.toFixed(4);}),
      playerGods:activeGods.map(function(g){var st=S.playerGods[g.key];return {key:g.key,unlocked:st.unlocked,cd:st.cdUntil||0,locked:st.lockedUntil||0};}),
      aiGods:activeAiGods.map(function(g){var st=S.aiGods[g.key];return {key:g.key,unlocked:st.unlocked,cd:st.cdUntil||0,locked:st.lockedUntil||0};}),
      hunt:[0,1].map(function(sd){var h=S.hunt[sd];if(!h)return {unit:-1,until:0};return {unit:S.units.findIndex(function(u){return u.id===h.id;}),until:h.until};}),
      blight:S.blightUntil.slice(), forge:S.forge.slice(), bldDmg:S.bldDmg.slice(),
      stripped:S.passivesStrippedUntil.slice(),
      training:S.training.map(function(t){return {side:t.side,key:t.key,readyAt:+t.readyAt.toFixed(4),
        spawnCount:t.spawnCount||1,c:t.path&&t.path[0]?t.path[0][0]:-1,r:t.path&&t.path[0]?t.path[0][1]:-1};}),
      veilUntil:S.veilUntil||0, trueSight:S.trueSightUntil.slice(), ptah:S.ptahCharge.slice(), chaos:S.chaosBuffUntil.slice(),
      deadHumans:S.deadHumans.map(function(d){return {side:d.side,key:d.key,at:d.at};}),
      terrain:TMAP.map(function(row){return row.join("");}),
      floods:S.floodedHexes.map(function(f){return {c:f.c,r:f.r,orig:f.orig,to:f.to||"W",owner:f.owner===undefined?-1:f.owner,dps:f.dmgPerSec||0,expires:f.expiresAt};})};
  }
  var casts=[];
  function cast(label,cfgName,god,hex,opts){
    opts=opts||{};
    var cfg=CONFIGS[cfgName];
    var cf=cfg.caster?cfg.ai:cfg.player;
    var egyPick=opts.pick||[god].concat(FACTIONS[cf].defaultGodPick.filter(function(k){return k!==god;})).slice(0,4);
    if(opts.extraGod) egyPick=[god,opts.extraGod].concat(FACTIONS[cf].defaultGodPick.filter(function(k){return k!==god&&k!==opts.extraGod;})).slice(0,4);
    var otherPick=opts.otherPick||FACTIONS[cfg.caster?cfg.player:cfg.ai].defaultGodPick.slice();
    var pick=cfg.caster?otherPick:egyPick, aiPick=cfg.caster?egyPick:otherPick;
    var time=opts.time!==undefined?opts.time:30;
    setup(cfg,pick,aiPick,time);
    (opts.bldDamage||[]).forEach(function(d){ buildings[d.i].hp-=d.amount; });
    (opts.bldWard||[]).forEach(function(w){ buildings[w.i].invulnUntil=w.until; });
    (opts.killBld||[]).forEach(function(i){ buildings[i].hp=0; buildings[i].dead=true; });
    (opts.terrain||[]).forEach(function(t){ TMAP[t.r][t.c]=t.t; });
    rebuildBldHexMap();
    var inputs=[];
    (opts.units||[]).forEach(function(q){ add(q.side,q.key,q.c,q.r,q); inputs.push(unitIn(S.units[S.units.length-1],q)); });
    (opts.dead||[]).forEach(function(d){ S.deadHumans.push({key:d.key,at:d.at,hex:[0,0],side:d.side}); });
    var casterState=cfg.caster?S.aiGods:S.playerGods, enemyState=cfg.caster?S.playerGods:S.aiGods;
    var casterRoster=cfg.caster?activeAiGods:activeGods;
    (opts.casterUnlocked||[god]).forEach(function(k){ if(casterState[k]) casterState[k].unlocked=true; });
    if(opts.casterCd) casterState[god].cdUntil=opts.casterCd;
    if(opts.casterLocked) casterState[god].lockedUntil=opts.casterLocked;
    (opts.enemyUnlocked||[]).forEach(function(e){ enemyState[e.key].unlocked=true; enemyState[e.key].cdUntil=e.cd||0; });
    if(opts.trueSight!==undefined) S.trueSightUntil[cfg.caster]=opts.trueSight;
    if(opts.ptahArmed) S.ptahCharge[cfg.caster]=true;
    if(opts.forgeLit) S.forge[cfg.caster]=opts.forgeLit;
    if(opts.purse){ S.favor=opts.purse[0]; S.food=opts.purse[1]; S.aiFavor=opts.purse[2]; S.aiFood=opts.purse[3]; }
    var favor=opts.favor!==undefined?opts.favor:200;
    if(!opts.purse){ if(cfg.caster) S.aiFavor=favor; else S.favor=favor; }
    roll=opts.random!==undefined?opts.random:0.5;
    var before={buildings:bldRows(),units:inputs,terrain:TMAP.map(function(row){return row.join("");}),
      playerPick:activeGods.map(function(g){return g.key;}),aiPick:activeAiGods.map(function(g){return g.key;}),
      state:snapshot()};
    var g=casterRoster.find(function(x){return x.key===god;});
    lastApplied=null;
    if(cfg.caster===0) castPlayerPower(god,hex,opts.pickedGod);
    else applyGodPower(1,g,hex,opts.pickedGod);
    var applied=lastApplied;
    var after=snapshot();
    var ticks=[];
    var steps=opts.ticks||0, dt=opts.dt||0.5;
    for(var i=0;i<steps;i++){ S.t-=dt; tickGodEffects(dt); tickFloods(dt); tickThundersWarning(); ticks.push(snapshot()); }
    casts.push({label:label,config:cfgName,player:cfg.player,ai:cfg.ai,caster:cfg.caster,god:god,
      hex:{c:hex[0],r:hex[1]},pickedGod:opts.pickedGod||"",time:time,random:roll,dt:dt,
      attempted:applied!==null, applied:applied===true, viaPlayerCast:cfg.caster===0,
      before:before, after:after, ticks:ticks});
  }
  var P=function(c,r){return [c,r];};
  var E=function(side,key,c,r,o){ var q=o||{}; q.side=side;q.key=key;q.c=c;q.r=r; return q; };

  /* ---- Horus: the target's ROW ---- */
  cast("horus row","egypt","horus",P(4,4),{units:[E(1,"ji",0,4),E(1,"xbow",8,4),E(1,"ji",4,3),E(0,"spear",2,4),E(1,"dao",6,4,{hp:30}),E(1,"ram",3,4,{invuln:99})]});
  cast("horus empty row still costs","egypt","horus",P(4,2),{units:[E(1,"ji",4,4)]});
  cast("horus vs greek deaths","vsGreek","horus",P(4,9),{otherPick:["dionysus","hades","artemis","hephaestus"],
    units:[E(0,"theseus",1,9,{hp:20,path:[[1,10],[1,9],[1,8]],pathIdx:2}),E(0,"ajax",3,9,{hp:10}),E(0,"hoplite",5,9,{hp:10}),E(0,"satyr",7,9,{hp:10})],
    casterUnlocked:["horus"],enemyUnlocked:[{key:"dionysus"}]});
  cast("horus vs greek no resurrection","vsGreek","horus",P(4,9),{otherPick:["dionysus","hades","artemis","hephaestus"],
    units:[E(0,"satyr",7,9,{hp:10}),E(0,"hoplite",0,0)]});
  /* ---- Ra: the target's COLUMN, damage by time of day ---- */
  [20,70,150].forEach(function(t){
    cast("ra column t"+t,"egypt","ra",P(3,5),{time:t,units:[E(1,"ji",3,0),E(1,"xbow",3,5),E(1,"ji",4,5),E(1,"dao",3,11)]});
  });
  /* ---- Bastet: nearest own building warded ---- */
  cast("bastet ward","egypt","bastet",P(4,10),{ticks:2});
  cast("bastet ward far corner","egypt","bastet",P(8,12));
  /* ---- Osiris: last 3 fallen humans within the window, beside the nearest own building ---- */
  cast("osiris revive","egypt","osiris",P(4,10),{time:80,
    dead:[{side:0,key:"spear",at:5},{side:0,key:"archer",at:30},{side:0,key:"axe",at:40},{side:1,key:"ji",at:50},{side:0,key:"khopesh",at:60},{side:0,key:"spear",at:70}],
    ticks:44});
  cast("osiris corpse window","egypt","osiris",P(4,10),{time:80,extraGod:"anubis",casterUnlocked:["osiris","anubis"],
    dead:[{side:0,key:"spear",at:5},{side:0,key:"archer",at:15}]});
  cast("osiris nobody","egypt","osiris",P(4,10),{time:80,dead:[{side:0,key:"spear",at:1}]});
  /* ---- Isis: lock an enemy god ---- */
  cast("isis auto","egypt","isis",P(4,4),{enemyUnlocked:[{key:"jade",cd:40},{key:"nuwa",cd:31},{key:"sunwukong",cd:31}]});
  cast("isis picked","egypt","isis",P(4,4),{pickedGod:"jade",enemyUnlocked:[{key:"jade",cd:40},{key:"nuwa",cd:31}]});
  cast("isis picked locked-out","egypt","isis",P(4,4),{pickedGod:"longwang",enemyUnlocked:[{key:"nuwa",cd:0}]});
  cast("isis nothing unlocked","egypt","isis",P(4,4));
  /* ---- Anubis: judgment of the nearest enemy ---- */
  [0.1,0.5,0.95].forEach(function(r){
    cast("anubis roll "+r,"egypt","anubis",P(4,4),{random:r,units:[E(1,"ji",4,6,{hp:10}),E(1,"xbow",4,5,{hp:40}),E(1,"dao",5,4)]});
  });
  cast("anubis full health","egypt","anubis",P(4,4),{random:0.15,units:[E(1,"ji",4,4)]});
  cast("anubis out of reach","egypt","anubis",P(4,4),{units:[E(1,"ji",4,8)]});
  /* ---- Set: slow both sides nearby ---- */
  cast("set storm","egypt","set",P(4,6),{units:[E(1,"ji",4,5),E(0,"spear",5,6),E(1,"xbow",4,9),E(0,"archer",2,6)]});
  cast("set nothing","egypt","set",P(0,0));
  /* ---- Thoth: reverse the traveled route ---- */
  cast("thoth reverse","egypt","thoth",P(4,5),{units:[E(1,"ji",4,5,{path:[[4,2],[4,3],[4,4],[4,5],[4,6],[4,7]],pathIdx:4}),E(1,"xbow",6,5)]});
  cast("thoth no route","vsGreek","thoth",P(4,9),{units:[E(0,"hoplite",4,9)]});
  /* ---- Sekhmet: frenzy both sides nearby ---- */
  cast("sekhmet frenzy","egypt","sekhmet",P(4,6),{units:[E(1,"ji",4,5),E(0,"spear",5,6),E(1,"xbow",4,9)]});
  /* ---- Nephthys: veil, refused under true sight ---- */
  cast("nephthys veil","egypt","nephthys",P(0,0));
  cast("nephthys barred","egypt","nephthys",P(0,0),{trueSight:35});
  /* ---- Ptah: arm a charge, refused when already armed ---- */
  cast("ptah arm","egypt","ptah",P(0,0));
  cast("ptah armed already","egypt","ptah",P(0,0),{ptahArmed:true});
  /* ================= China (U27) ================= */
  var T=function(c,r,t){return {c:c,r:r,t:t};};
  var block=function(c0,c1,r0,r1,t){ var o=[]; for(var c=c0;c<=c1;c++) for(var r=r0;r<=r1;r++) o.push(T(c,r,t)); return o; };
  cast("jade conscript","china","jade",P(4,4),{units:[E(1,"spear",4,6,{path:[[4,7],[4,6],[4,5]],pathIdx:1}),E(1,"archer",4,5),E(0,"ji",4,4)],ticks:18});
  cast("jade nobody near","china","jade",P(4,4),{units:[E(1,"spear",4,8)]});
  cast("jade by the AI","chinaAI","jade",P(4,9),{units:[E(0,"spear",4,10),E(0,"bata_tree",4,9)]});
  cast("wukong transforms","china","sunwukong",P(4,9),{random:0.6,casterUnlocked:["sunwukong","jade","nuwa"],
    units:[E(0,"ji",4,10,{hp:60}),E(0,"xbow",5,9),E(1,"spear",4,9)]});
  cast("wukong no myths","china","sunwukong",P(4,9),{units:[E(0,"ji",4,10)]});
  cast("wukong skips forms","china","sunwukong",P(4,9),{casterUnlocked:["sunwukong","longwang"],random:0.1,
    units:[E(0,"nezha_lotus",4,9),E(0,"ji",5,9)],ticks:1});
  cast("nuwa repairs the worst","china","nuwa",P(0,0),{bldDamage:[{i:0,amount:100},{i:2,amount:300},{i:4,amount:50}]});
  cast("nuwa nothing hurt","china","nuwa",P(0,0));
  cast("dragon rain","china","longwang",P(4,5),{terrain:[T(4,5,"M"),T(3,5,"W"),T(5,4,"F")],
    units:[E(1,"spear",4,4,{hp:30}),E(0,"ji",4,6),E(1,"archer",4,8),E(1,"axe",2,5,{invuln:99})],ticks:18});
  cast("dragon rain on water and rock","china","longwang",P(4,5),{terrain:block(0,8,2,8,"W"),units:[E(1,"spear",4,4)]});
  cast("broken pillar","china","gonggong",P(4,4),{pick:["gonggong","jade","nuwa","longwang"],terrain:[T(4,3,"W")],units:[E(1,"spear",4,4)],ticks:18});
  cast("third eye","china","erlangshen",P(0,0),{pick:["erlangshen","jade","nuwa","longwang"],
    units:[E(1,"spear",4,4,{invisible:true}),E(1,"archer",4,5,{despawn:99}),E(1,"axe",5,5,{conscript:{side:0,until:40}}),E(0,"ji",4,9,{invisible:true}),E(0,"ji",3,3,{conscript:{side:1,until:40}})],ticks:2});
  cast("third eye by the AI","chinaAI","erlangshen",P(0,0),{pick:["erlangshen","jade","nuwa","longwang"]});
  cast("garden of kunlun","china","xiwangmu",P(4,8),{pick:["xiwangmu","jade","nuwa","longwang"],
    terrain:[T(4,8,"R"),T(3,8,"F"),T(5,8,"W"),T(4,9,"H"),T(4,7,"D")],ticks:18});
  cast("garden no soil","china","xiwangmu",P(4,4),{pick:["xiwangmu","jade","nuwa","longwang"],terrain:block(2,6,2,6,"M")});
  cast("primordial fire","china","zhurong",P(4,5),{pick:["zhurong","jade","nuwa","longwang"],
    units:[E(1,"spear",4,5,{hp:30}),E(0,"ji",4,6),E(1,"archer",4,7,{invuln:33}),E(1,"axe",4,8)],ticks:14});
  cast("moonfall","china","chang",P(0,0),{pick:["chang","jade","nuwa","longwang"],
    units:[E(1,"spear",4,4,{invisible:true}),E(0,"ji",4,9),E(1,"bata_tree",2,2)]});
  cast("moonfall empty","china","chang",P(0,0),{pick:["chang","jade","nuwa","longwang"]});
  cast("heaven's judgment","china","leigong",P(4,5),{pick:["leigong","jade","nuwa","longwang"],
    units:[E(1,"spear",4,5,{invisible:true}),E(1,"jackal",4,6),E(0,"ji",4,4),E(1,"archer",4,8)]});
  cast("fire over a flood expires in order","china","zhurong",P(4,5),{pick:["zhurong","longwang","nuwa","jade"],
    terrain:[T(4,5,"W")],ticks:16});

  /* ================= Greece (U28) ================= */
  var GP=["zeus","poseidon","athena","ares"], GP2=["apollo","hermes","demeter","hades"], GP3=["dionysus","hephaestus","artemis","zeus"];
  cast("zeus bolt and arc","greek","zeus",P(4,4),{pick:GP,units:[E(1,"spear",4,5,{hp:100}),E(1,"archer",4,3),E(1,"axe",4,8),E(1,"elephant",5,5,{invuln:99})]});
  cast("zeus no arc","greek","zeus",P(4,4),{pick:GP,units:[E(1,"spear",4,4)]});
  cast("zeus nobody","greek","zeus",P(4,4),{pick:GP,units:[E(1,"spear",4,8)]});
  cast("earthquake destroys","greek","poseidon",P(4,2),{pick:GP,bldDamage:[{i:1,amount:400}],enemyUnlocked:[{key:"nephthys"}],
    otherPick:["nephthys","horus","bastet","osiris"]});
  cast("earthquake misses","greek","poseidon",P(4,11),{pick:GP});
  cast("earthquake skips a ward","greek","poseidon",P(4,2),{pick:GP,bldWard:[{i:0,until:99}]});
  cast("aegis","greek","athena",P(4,9),{pick:GP,units:[E(0,"hoplite",4,9),E(0,"toxotes",4,11),E(1,"spear",4,8),E(0,"peltast",4,12)]});
  cast("aegis empty","greek","athena",P(4,4),{pick:GP});
  cast("bloodlust","greek","ares",P(4,9),{pick:GP,units:[E(0,"hoplite",4,9),E(1,"spear",4,8),E(0,"toxotes",0,0)]});
  cast("sun's arrows","greek","apollo",P(4,4),{pick:GP2,units:[E(1,"spear",4,4,{hp:40}),E(1,"archer",4,5),E(0,"hoplite",4,3),E(1,"axe",4,6,{invuln:99}),E(1,"bata_tree",3,4)]});
  cast("theft","greek","hermes",P(0,0),{pick:GP2,purse:[200,110,30,55]});
  cast("theft from the rich","greek","hermes",P(0,0),{pick:GP2,purse:[240,100,200,100]});
  cast("theft nothing","greek","hermes",P(0,0),{pick:GP2,purse:[200,0,0,0]});
  cast("theft by the AI","greekAI","hermes",P(0,0),{pick:GP2,purse:[25,70,200,0]});
  cast("blight","greek","demeter",P(0,0),{pick:GP2,ticks:0});
  cast("realm of the dead","greek","hades",P(4,10),{pick:GP2,time:80,
    dead:[{side:1,key:"spear",at:30},{side:0,key:"hoplite",at:50},{side:1,key:"archer",at:70},{side:0,key:"toxotes",at:75},{side:1,key:"axe",at:10}]});
  cast("the vine","greek","dionysus",P(4,4),{pick:GP3,units:[E(1,"spear",4,4),E(0,"hoplite",4,5),E(1,"archer",5,5)]});
  cast("the forge","greek","hephaestus",P(0,0),{pick:GP3});
  cast("the forge already lit","greek","hephaestus",P(0,0),{pick:GP3,forgeLit:2});
  cast("actaeon's end","greek","artemis",P(4,4),{pick:GP3,units:[E(1,"archer",4,6),E(1,"spear",4,5),E(1,"bata_tree",4,4)]});
  cast("actaeon's end nobody","greek","artemis",P(4,4),{pick:GP3,units:[E(1,"spear",0,0)]});
  cast("actaeon's end by the AI","greekAI","artemis",P(4,9),{pick:GP3,units:[E(0,"spear",4,9)]});

  /* ================= the Aztecs (U29) ================= */
  var AP=["huitzilopochtli","quetzalcoatl","tezcatlipoca","mictlantecuhtli"];
  var AP2=["tlaloc","xipetotec","coatlicue","coyolxauhqui"];
  var AP3=["ehecatl","itzpapalotl","huitzilopochtli","tlaloc"];
  cast("solar war pays in blood","aztec","huitzilopochtli",P(4,9),{pick:AP,
    units:[E(0,"macuahuitl",4,9),E(0,"atlatl",4,11,{hp:1}),E(1,"spear",4,8)],ticks:2});
  cast("solar war with no army","aztec","huitzilopochtli",P(4,9),{pick:AP});
  cast("wind serpent wipes routes","aztec","quetzalcoatl",P(4,4),{pick:AP,
    units:[E(1,"spear",4,4,{path:[[4,5],[4,6]],pathIdx:1}),E(1,"bata_tree",3,4),E(0,"jaguar",4,10,{path:[[4,9]]})]});
  cast("wind serpent with nobody to push","aztec","quetzalcoatl",P(4,4),{pick:AP});
  cast("the weeping rain","aztec","tlaloc",P(4,5),{pick:AP2,terrain:[T(4,5,"M"),T(5,5,"W"),T(3,5,"D")],ticks:18});
  cast("the weeping rain over a wall","aztec","tlaloc",P(4,11),{pick:AP2});
  cast("smoking mirror","aztec","tezcatlipoca",P(4,5),{pick:AP,units:[E(1,"spear",4,5,{hp:60}),E(1,"archer",4,4)],ticks:2});
  cast("smoking mirror on a crowded hex","aztec","tezcatlipoca",P(4,5),{pick:AP,
    units:[E(1,"elephant",4,5),E(1,"spear",5,5),E(1,"spear",5,4),E(1,"spear",4,4),E(1,"spear",3,4),E(1,"spear",3,5),E(1,"spear",4,6)]});
  cast("smoking mirror with nobody near","aztec","tezcatlipoca",P(4,5),{pick:AP,units:[E(1,"spear",0,0)]});
  cast("the flaying","aztec","xipetotec",P(0,0),{pick:AP2,enemyUnlocked:[{key:"horus"}],ticks:0});
  cast("serpent skirt","aztec","coatlicue",P(0,0),{pick:AP2,
    units:[E(1,"spear",4,4,{hp:30}),E(1,"bata_tree",3,4),E(0,"jaguar",4,10)],ticks:12});
  cast("serpent skirt with no enemies","aztec","coatlicue",P(0,0),{pick:AP2});
  cast("the bargain","aztec","mictlantecuhtli",P(4,2),{pick:AP,random:0.5,ticks:0});
  cast("the bargain rolls the pool","aztec","mictlantecuhtli",P(4,2),{pick:AP,random:0.4,
    casterUnlocked:["mictlantecuhtli","huitzilopochtli","quetzalcoatl"]});
  cast("the bargain with their walls down","aztec","mictlantecuhtli",P(4,2),{pick:AP,killBld:[0,1,2]});
  cast("the bargain by the AI","aztecAI","mictlantecuhtli",P(4,10),{pick:AP,random:0});
  cast("dismemberment","aztec","coyolxauhqui",P(4,5),{pick:AP2,units:[E(1,"spear",4,5),E(1,"archer",4,4)],ticks:2});
  cast("dismemberment with nobody near","aztec","coyolxauhqui",P(4,5),{pick:AP2});
  cast("first wind","aztec","ehecatl",P(4,9),{pick:AP3,units:[E(0,"jaguar",4,9),E(1,"spear",4,8)],ticks:2});
  cast("obsidian wings","aztec","itzpapalotl",P(4,4),{pick:AP3,
    units:[E(1,"spear",4,4,{hp:40}),E(1,"archer",4,5),E(1,"axe",4,6,{invuln:99}),E(1,"bata_tree",3,4),E(0,"jaguar",4,3)]});
  cast("obsidian wings over empty ground","aztec","itzpapalotl",P(4,4),{pick:AP3});
  /* ---- validation: nothing pays ---- */
  cast("locked god","egypt","horus",P(4,4),{casterUnlocked:[]});
  cast("on cooldown","egypt","horus",P(4,4),{casterCd:31});
  cast("power locked","egypt","horus",P(4,4),{casterLocked:31});
  cast("cannot afford","egypt","horus",P(4,4),{favor:39});

  /* ---- timed effects on their own ---- */
  var effects=[];
  function effect(label,cfgName,godsOn,units,ticks,dt,pick){
    var cfg=CONFIGS[cfgName];
    setup(cfg,pick||FACTIONS[cfg.player].defaultGodPick,FACTIONS[cfg.ai].defaultGodPick,30);
    var inputs=[];
    units.forEach(function(q){ add(q.side,q.key,q.c,q.r,q); inputs.push(unitIn(S.units[S.units.length-1],q)); });
    godsOn.forEach(function(e){ (e.side?S.aiGods:S.playerGods)[e.key].unlocked=true; });
    var before={buildings:bldRows(),units:inputs,terrain:TMAP.map(function(row){return row.join("");}),playerPick:activeGods.map(function(g){return g.key;}),aiPick:activeAiGods.map(function(g){return g.key;})};
    var tl=[];
    for(var i=0;i<ticks;i++){ S.t-=dt; tickGodEffects(dt); tickFloods(dt); tickThundersWarning(); tl.push(snapshot()); }
    effects.push({label:label,config:cfgName,player:cfg.player,ai:cfg.ai,time:30,dt:dt,gods:godsOn,before:before,ticks:tl});
  }
  effect("poison","egypt",[],[E(1,"ji",4,4,{hp:30,poison:{until:34,dps:5}}),E(1,"xbow",4,5,{hp:8,poison:{until:40,dps:5}}),E(1,"dao",4,6,{hp:30,poison:{until:40,dps:5},invuln:32})],12,0.5);
  effect("inevitable and aztec favor on poison","aztec",[{side:0,key:"mictlantecuhtli"}],[E(1,"spear",4,4,{hp:2,poison:{until:40,dps:5}})],2,0.5);
  effect("hero regen","egypt",[{side:0,key:"isis"}],[E(0,"doomedprince",4,9,{hp:10}),E(0,"spear",4,10,{hp:10})],6,0.5);
  effect("apis favor","egypt",[],[E(0,"apis",4,9),E(1,"ji",4,3)],6,0.5);
  effect("despawn","egypt",[],[E(0,"spear",4,9,{despawn:31}),E(0,"spear",5,9)],4,0.5);
  effect("huo shu aura","egypt",[],[E(1,"huoshu",4,5),E(0,"spear",4,6,{hp:12}),E(0,"archer",5,5),E(0,"axe",4,8),E(1,"ji",3,5)],4,0.5);
  effect("lyre","vsGreek",[{side:0,key:"apollo"}],[E(0,"hoplite",4,9,{hp:10}),E(0,"hoplite",1,1,{hp:10})],4,0.5,["apollo","hades","artemis","hephaestus"]);
  effect("triptolemus food","vsGreek",[],[E(0,"triptolemus",4,9)],4,0.5);
  effect("conscription expires","egypt",[],[E(1,"ji",4,8,{conscript:{side:0,until:31}}),E(1,"ji",4,2,{conscript:{side:0,until:99}})],4,0.5);
  effect("thunder's warning","china",[{side:0,key:"leigong"}],[E(1,"spear",4,9),E(1,"spear",4,5),E(0,"ji",4,10)],3,0.5,["leigong","jade","nuwa","longwang"]);

  /* ---- Hephaestus's Forge and Ptah's Creator's Word spent at training (tickTraining) ---- */
  var training=[];
  [[3,false,["hoplite","toxotes"]],[1,true,["hoplite","toxotes"]],[2,true,["talos"]],[0,true,["hoplite"]]].forEach(function(t){
    setup(CONFIGS.greek,["hephaestus","zeus","ares","athena"],FACTIONS.egypt.defaultGodPick,30);
    S.forge[0]=t[0]; S.ptahCharge[0]=t[1];
    var src=getBld(0,"city"), exits=bldAdjacent(src,false);
    t[2].forEach(function(key,i){ queueTrain(0,key,[exits[i]],src,undefined,elapsed()); });
    var queued=S.training.map(function(q){return {key:q.key,spawnCount:q.spawnCount||1};});
    tickTraining();
    training.push({forge:t[0],ptah:t[1],keys:t[2],queued:queued,forgeAfter:S.forge[0],ptahAfter:S.ptahCharge[0],
      units:S.units.map(function(u){return {key:u.key,maxhp:u.maxhp,hp:u.hp,forged:!!u.forged};})});
  });
  globalThis.__powersReference={source:"godsbound_beta.html",casts:casts,effects:effects,training:training};
})();
`;
eval(code);
/*END-DRIVER*/
}
