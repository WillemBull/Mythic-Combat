/* U28: named unit abilities, measured as small browser matches.
 *
 * Each scenario builds a plains board with the live startMatch building layout, places real
 * units, and runs the browser's own per-frame order for units only:
 *   S.t -= dt; tickGodEffects(dt); updateUnit(every unit, in array order); tickFormChains();
 *   dead units dropped.
 * (Economy, training, fortress guns and the AI are not ticked -- they have their own fixtures.)
 * Every frame records every unit, so the C# replay must reproduce spawns, deaths and timers.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/abilities_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=globalThis.__abilitiesReference;
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/abilities_reference.json"),
  JSON.stringify(out)+"\n");
console.log("[abilities-ref] "+out.cases.length+" scenarios: "+out.cases.map(c=>c.name+"("+c.frames.length+")").join(", "));
if(false){
/*DRIVER*/
code+=`
;(function(){
  Math.random=function(){return 0;};
  var DT=1/32;
  function factionOf(key){
    for(var f of Object.keys(FACTIONS)) if(FACTIONS[f].units[key]) return f;
    throw new Error("no faction for "+key);
  }
  function reset(player,ai){
    festival=null; drill=null; campaign=null;
    S.playerFaction=player; S.aiFaction=ai;
    S.godPick=FACTIONS[player].defaultGodPick.slice(); S.aiGodPick=FACTIONS[ai].defaultGodPick.slice();
    S.loadout=FACTIONS[player].defaultLoadout.slice(); S.heroPick=FACTIONS[player].defaultHeroes.slice();
    startMatch(false);
    S.phase="battle"; S.over=false; S.t=170;
    for(var r=0;r<ROWS;r++) for(var c=0;c<COLS;c++) TMAP[r][c]="P";
    S.units=[]; S.effects=[]; S.deadHumans=[]; S.training=[]; S.floodedHexes=[];
    S.hunt=[null,null]; S.swallowed=[]; S.trueSightUntil=[0,0]; S.veilUntil=0; S.bldDmg=[0,0];
    S.favor=0; S.aiFavor=0; S.food=0; S.aiFood=0; aiWave=null;
    buildings.forEach(function(b){ b.invulnUntil=0; });
  }
  var seeds;
  function add(side,key,c,r,o){
    o=o||{};
    var u=makeUnit(side,key,[c,r],o.path||null);
    if(o.hp!==undefined) u.hp=o.hp;
    if(o.hold) u.hold=true;
    if(o.frenzy) u.frenzyUntil=o.frenzy;
    if(o.invisible) u.invisible=true;
    S.units.push(u);
    seeds.push({side:side,key:key,faction:factionOf(key),c:c,r:r,hp:u.hp,hold:!!o.hold,frenzy:o.frenzy||0,invisible:!!o.invisible,
      route:(o.path||[]).map(function(h){return {c:h[0],r:h[1]};})});
    return u;
  }
  function snap(){
    return S.units.map(function(u){ return {key:u.key,side:u.side,c:u.hex[0],r:u.hex[1],hp:+u.hp.toFixed(4),dead:!!u.dead,
      invuln:u.invulnUntil||0,frenzy:u.frenzyUntil||0,stun:u.stunUntil||0,retreating:!!u.retreating,
      lastStand:u.lastStandEndsAt||0,sniping:!!u.sniping,invisible:!!u.invisible,revived:!!u.hasRevived,
      target:u.target? (isBldg(u.target)? -2-buildings.indexOf(u.target) : S.units.indexOf(u.target)) : -1}; });
  }
  var cases=[];
  function scenario(name,player,ai,setup,frames,godsOn){
    reset(player,ai); seeds=[];
    (godsOn||[]).forEach(function(g){ (g.side?S.aiGods:S.playerGods)[g.key].unlocked=true; });
    var extra=setup()||{};
    var row={name:name,player:player,ai:ai,dt:DT,elapsed:10,units:seeds,gods:godsOn||[],
      hunt:S.hunt.map(function(h){return h?{unit:S.units.findIndex(function(u){return u.id===h.id;}),until:h.until}:{unit:-1,until:0};}),
      buildings:buildings.map(function(b){return {side:b.side,type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1],hp:b.hp};}),
      frames:[]};
    for(var i=0;i<frames;i++){
      S.t-=DT;
      tickGodEffects(DT);
      for(var k=0;k<S.units.length;k++) updateUnit(S.units[k],DT);
      tickFormChains();
      S.units=S.units.filter(function(u){return !u.dead;});
      row.frames.push({units:snap(),bldHp:buildings.map(function(b){return +b.hp.toFixed(4);}),
        favor:[S.favor,S.aiFavor],swallowed:S.swallowed.length});
    }
    cases.push(row);
  }
  /* Sekhmet's frenzy: the only unit in reach is an ally, and it is hit. */
  scenario("frenzy hits an ally","egypt","china",function(){
    add(0,"spear",4,8,{hold:true,frenzy:12}); add(0,"archer",4,7,{hold:true}); add(1,"ji",4,4,{hold:true});
  },70);
  /* Artemis's quarry: hidden, yet its own side's neighbour turns on it. */
  scenario("quarry exposure beats concealment","greek","china",function(){
    var q=add(1,"ji",4,5,{hold:true,invisible:true}); add(1,"dao",4,4,{hold:true}); add(0,"hoplite",1,9,{hold:true});
    S.hunt[0]={id:q.id,until:40};
  },70);
  /* Theseus: the killing blow sends him back along his route, untouchable, never attacking.
     The killer is RANGED on purpose. A melee killer standing free would chase him off its hex the
     moment he stepped away, and Unity reserves a sidestep destination BEFORE sliding into it while
     the browser claims the hex on arrival (a deliberate U11 departure, see HANDOFF) -- so a melee
     killer would make this scenario measure that known drift instead of the retreat. A bow keeps
     firing from where it stands, which also proves the ward: every shot lands for nothing. */
  scenario("clew retreat","greek","china",function(){
    add(0,"theseus",4,6,{hp:5,path:[[4,9],[4,8],[4,7],[4,6],[4,5]]}); S.units[0].pathIdx=3;
    seeds[0].index=3;
    add(1,"xbow",4,5,{hold:true});
  },260);
  /* Ajax: the killing blow plants him for his stand; then he dies for real. */
  scenario("last stand then death","greek","china",function(){
    add(0,"ajax",4,6,{hp:5,hold:true}); add(1,"dao",4,5,{hold:true});
  },230);
  /* Hou Yi: every few seconds he stops and shoots the nearest enemy anywhere on the board. */
  scenario("sun-shooter","china","egypt",function(){
    add(0,"houyi",4,11,{hold:true}); add(1,"spear",1,1,{hold:true}); add(1,"elephant",8,2,{hold:true});
  },230);
  /* Bata: the bull dies into a tree; his side chops the tree; the reborn form walks out. */
  scenario("bata chain","egypt","china",function(){
    add(0,"bata",4,6,{hp:3,hold:true}); add(1,"dao",4,5,{hold:true}); add(0,"khopesh",5,6,{hold:true});
  },60);
  scenario("tree chopped, reborn walks out","egypt","china",function(){
    add(0,"bata_tree",4,9,{hp:10}); add(0,"khopesh",5,9,{hold:true}); add(1,"ji",0,2,{hold:true});
  },60);
  /* Nezha: the lotus turns into the reborn hero on a timer nobody has to earn. */
  scenario("nezha lotus timer","china","egypt",function(){
    add(0,"nezha_lotus",4,9,{}); add(0,"ji",1,11,{hold:true});
  },180);
  /* A healer that is not fighting mends the most hurt adjacent ally once per interval. */
  scenario("unengaged heal cadence","egypt","china",function(){
    add(0,"priest",4,9,{hold:true}); add(0,"spear",4,10,{hp:30,hold:true}); add(0,"archer",5,9,{hp:60,hold:true}); add(0,"axe",1,11,{hp:5,hold:true});
  },100);
  /* Plague Bearer: every shot lands on up to three enemies in range. */
  scenario("plague bearer fan-out","egypt","china",function(){
    add(0,"plaguebearer",4,7,{hold:true}); add(1,"ji",4,6,{hold:true}); add(1,"dao",4,5,{hold:true}); add(1,"xbow",5,6,{hold:true}); add(1,"ji",3,6,{hold:true});
  },70);
  /* Orpheus: a shade joins him on a timer. */
  scenario("song of the dead","greek","china",function(){
    add(0,"orpheus",4,10,{hold:true});
  },200);
  /* Perseus: the first enemy in reach, and the line behind it, turn to stone. */
  scenario("gorgon's head","greek","china",function(){
    add(0,"perseus",4,7,{hold:true}); add(1,"ji",4,6,{hold:true}); add(1,"dao",4,5,{hold:true}); add(1,"xbow",4,3,{hold:true}); add(1,"ji",6,8,{hold:true});
  },40);
  /* Odysseus: unseen until he reaches a wall, then one strike on the building. */
  scenario("nobody at the wall","greek","china",function(){
    add(0,"odysseus",4,6,{path:[[4,5],[4,4],[4,3],[4,2]]}); add(1,"ji",0,5,{hold:true});
  },160);
  /* Heracles: Hera's clock makes him rage on its own schedule. */
  scenario("divine rage","greek","china",function(){
    add(0,"heracles",4,10,{hold:true}); add(0,"hoplite",4,9,{hold:true});
  },480);
  /* Ariadne: at the end of her drawn route she becomes two.
     Her route ends BESIDE a wall on purpose, and the twin spawns beside the same wall: both then
     stand and strike it. Ending in open ground made the two race north for the same hexes, which
     measured Unity's documented congestion-and-reservation departure rather than the split. */
  scenario("labyrinthine split","greek","china",function(){
    add(0,"ariadne",6,5,{path:[[6,4]]});
  },90);
  /* Tepoztecatl: swallowed by a nearby myth instead of dying, back out when it falls. */
  scenario("swallowed and freed","aztec","china",function(){
    add(0,"tepoztecatl",4,6,{hp:1,hold:true}); add(1,"dragon",4,5,{hold:true,hp:100}); add(0,"atlatl",4,3,{hold:true});
  },260);
  scenario("swallowed then cuts out","aztec","china",function(){
    add(0,"tepoztecatl",4,6,{hp:3,hold:true}); add(1,"dragon",4,5,{hold:true});
  },300);

  globalThis.__abilitiesReference={source:"godsbound_beta.html",cases:cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
