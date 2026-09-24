/* U23: god rosters, the real unlock flow, hasPassive and passive-driven economy/pricing,
 * measured from the browser game for the Unity EditMode tests.
 *
 * Nothing here is transcribed. The player's unlocks go through the REAL two-tap onGodTap
 * (preview, then confirm), AI-side passives go through the real hasPassive over activeAiGods,
 * and economy/pricing answers come from the real tickEconomy / unitCost / queueTrain.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/gods_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=globalThis.__godsReference;
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/gods_reference.json"),
  JSON.stringify(out,null,1)+"\n");
console.log("[gods-ref] rosters "+out.rosters.map(r=>r.faction+" "+r.keys.length).join(", ")+
  " | "+out.unlockCases.length+" unlock sequences, "+out.sidePassiveCases.length+" side-passive cases, "+
  out.economyCases.length+" economy cases, "+out.priceCases.length+" price cases");
if(false){
/*DRIVER*/
code+=`
;(function(){
  Math.random=()=>0.25; // startMatch shuffles layouts/terrain; fixed so the export is stable
  var FACTION_IDS=Object.keys(FACTIONS);
  var ALL_KEYS=[];
  FACTION_IDS.forEach(function(f){ FACTIONS[f].allGods.forEach(function(g){
    if(g.passiveKey && ALL_KEYS.indexOf(g.passiveKey)<0) ALL_KEYS.push(g.passiveKey); }); });
  ALL_KEYS.sort();
  var TYPES=["city","temple","fortress"];

  function begin(player,ai,pick,aiPick){
    S.playerFaction=player; S.aiFaction=ai;
    S.godPick=pick.slice(); S.aiGodPick=aiPick.slice();
    S.loadout=FACTIONS[player].defaultLoadout.slice(); S.heroPick=FACTIONS[player].defaultHeroes.slice();
    festival=null; drill=null; campaign=null;
    startMatch(false);
    S.phase="battle"; S.over=false; S.t=180-30;
    S.passivesStrippedUntil=[0,0];
    buildings.forEach(function(b){ b.maxhp=b.maxhp; });
  }
  function truePassives(side){ return ALL_KEYS.filter(function(k){ return hasPassive(side,k); }); }
  function maxHp(side){ return TYPES.map(function(t){ return getBld(side,t).maxhp; }); }
  function hp(side){ return TYPES.map(function(t){ return getBld(side,t).hp; }); }

  var rosters=FACTION_IDS.map(function(f){
    return {faction:f, keys:FACTIONS[f].allGods.map(function(g){return g.key;}),
            defaultPick:FACTIONS[f].defaultGodPick.slice()};
  });

  /* ---- the player's two-tap unlock flow ---- */
  function orders(pick){
    var rev=pick.slice().reverse();
    return [pick.slice(), rev, [pick[2],pick[0],pick[3],pick[1]]];
  }
  function extraPick(f){
    var keys=FACTIONS[f].allGods.map(function(g){return g.key;});
    var def=FACTIONS[f].defaultGodPick;
    var others=keys.filter(function(k){return def.indexOf(k)<0;});
    var special={egypt:["thoth","ptah","set","ra"],china:["nuwa","longwang","jade","erlangshen"],
                 aztec:["quetzalcoatl","xipetotec","tlaloc","huitzilopochtli"],greek:["hermes","demeter","hephaestus","apollo"]}[f];
    var p=(special||[]).filter(function(k){return keys.indexOf(k)>=0;});
    for(var i=0;p.length<4&&i<others.length;i++) if(p.indexOf(others[i])<0) p.push(others[i]);
    return p;
  }
  var unlockCases=[];
  FACTION_IDS.forEach(function(f){
    var ai=f==="egypt"?"china":"egypt";
    [FACTIONS[f].defaultGodPick.slice(), extraPick(f)].forEach(function(pick){
      orders(pick).forEach(function(order){
        [[250,false],[180,false],[60,false],[250,true]].forEach(function(mode){
          var startFavor=mode[0], refill=mode[1];
          begin(f,ai,pick,FACTIONS[ai].defaultGodPick);
          var killed=null;
          if(startFavor===180){ killed="fortress"; getBld(0,"fortress").hp=0; getBld(0,"fortress").dead=true; }
          else getBld(0,"temple").hp=getBld(0,"temple").maxhp*0.5;
          var baseMax=maxHp(0), baseHp=hp(0);
          S.favor=startFavor;
          var deckStart=DECK.length;
          var steps=[];
          order.forEach(function(key){
            if(refill) S.favor=startFavor;
            var before=S.favor, wasUnlocked=S.playerGods[key].unlocked;
            S.godPreview=null;
            onGodTap(key);
            var afterPreview=S.favor, previewUnlocked=S.playerGods[key].unlocked;
            onGodTap(key);
            S.armedPower=null;
            steps.push({key:key, favorBefore:before, favorAfterPreview:afterPreview,
              previewUnlocked:previewUnlocked, favorAfter:S.favor, wasUnlocked:wasUnlocked,
              unlocked:activeGods.filter(function(g){return S.playerGods[g.key].unlocked;}).map(function(g){return g.key;}),
              myths:DECK.slice(deckStart), passives:truePassives(0),
              maxHpRatio:maxHp(0).map(function(m,i){return +(m/baseMax[i]).toFixed(6);}),
              hpRatio:hp(0).map(function(h,i){return baseHp[i]?+(h/baseHp[i]).toFixed(6):0;})});
          });
          unlockCases.push({faction:f, aiFaction:ai, pick:pick, order:order, startFavor:startFavor, refill:refill,
            killed:killed||"", temple50:startFavor!==180, selected:activeGods.map(function(g){return g.key;}), steps:steps});
        });
      });
    });
  });

  /* ---- hasPassive on either side, set directly, plus The Flaying ---- */
  var sidePassiveCases=[];
  FACTION_IDS.forEach(function(f){
    var other=f==="egypt"?"china":"egypt";
    var pick=FACTIONS[f].defaultGodPick;
    for(var side=0;side<2;side++){
      for(var mask=0;mask<16;mask+=5){
        begin(side?other:f, side?f:other, side?FACTIONS[other].defaultGodPick:pick, side?pick:FACTIONS[other].defaultGodPick);
        var st=side?S.aiGods:S.playerGods;
        var unlocked=pick.filter(function(k,i){return (mask>>i)&1;});
        unlocked.forEach(function(k){ st[k].unlocked=true; });
        var strip=[0,0], at=30;
        sidePassiveCases.push({faction:f, side:side, unlocked:unlocked, elapsed:at, stripUntil:0,
          passives:truePassives(side), otherSide:truePassives(1-side)});
        S.passivesStrippedUntil[side]=at+5;
        sidePassiveCases.push({faction:f, side:side, unlocked:unlocked, elapsed:at, stripUntil:at+5,
          passives:truePassives(side), otherSide:truePassives(1-side)});
        S.passivesStrippedUntil[side]=at;
        sidePassiveCases.push({faction:f, side:side, unlocked:unlocked, elapsed:at, stripUntil:at,
          passives:truePassives(side), otherSide:truePassives(1-side)});
      }
    }
  });

  /* ---- economy with each economic passive on/off, through real unlocks ---- */
  var economyCases=[];
  var eco=[["egypt","bastet"],["aztec","quetzalcoatl"],["china","jade"],["china","longwang"],["greek","demeter"],["egypt","horus"]];
  eco.forEach(function(e){
    var f=e[0], god=e[1];
    var pick=FACTIONS[f].defaultGodPick.indexOf(god)>=0?FACTIONS[f].defaultGodPick.slice():[god].concat(FACTIONS[f].defaultGodPick.slice(0,3));
    [0,1].forEach(function(side){
      [false,true].forEach(function(on){
        [[],["fortress"],["fortress","city"],["temple"]].forEach(function(dead){
          var other=f==="egypt"?"china":"egypt";
          begin(side?other:f, side?f:other, side?FACTIONS[other].defaultGodPick:pick, side?pick:FACTIONS[other].defaultGodPick);
          for(var r=0;r<ROWS;r++) for(var c=0;c<COLS;c++) TMAP[r][c]="P";
          var water=[[1,ROWS-1],[3,PLAYER_ROW0],[2,PLAYER_ROW0-1],[5,0],[6,AI_ROWS-1],[7,2]];
          water.forEach(function(w){ TMAP[w[1]][w[0]]="W"; });
          [0,1].forEach(function(s){ dead.forEach(function(t){ var b=getBld(s,t); b.hp=0; b.dead=true; }); });
          var st=side?S.aiGods:S.playerGods;
          if(on) st[god].unlocked=true;
          S.food=0; S.favor=0; S.aiFood=0; S.aiFavor=0; S.blightUntil=[0,0];
          tickEconomy(1);
          economyCases.push({faction:f, god:god, side:side, on:on, dead:dead, selected:(side?activeAiGods:activeGods).map(function(g){return g.key;}),
            water:water.map(function(w){return {c:w[0],r:w[1]};}),
            food:[+S.food.toFixed(6), +S.aiFood.toFixed(6)], favor:[+S.favor.toFixed(6), +S.aiFavor.toFixed(6)]});
        });
      });
    });
  });

  /* ---- prices and training time through the real unitCost / queueTrain ---- */
  var priceCases=[];
  [["egypt","thoth",["spear","ammit","khopesh"]],["china","nuwa",["ji","xbow","dragon"]],["greek","hermes",["hoplite","toxotes"]]].forEach(function(e){
    var f=e[0], god=e[1];
    var pick=[god].concat(FACTIONS[f].defaultGodPick.filter(function(k){return k!==god;}).slice(0,3));
    [0,1].forEach(function(side){
      [false,true].forEach(function(on){
        var other=f==="egypt"?"china":"egypt";
        begin(side?other:f, side?f:other, side?FACTIONS[other].defaultGodPick:pick, side?pick:FACTIONS[other].defaultGodPick);
        (side?S.aiGods:S.playerGods)[god].unlocked=on;
        e[2].forEach(function(key){
          var def=FACTIONS[f].units[key]; if(!def) throw new Error("no unit "+f+"/"+key);
          var c=unitCost(def.cost, side, def.cat==="human");
          S.training.length=0;
          var src=getBld(side,"city");
          queueTrain(side,key,[src.hexes[0]],src);
          var t=S.training[S.training.length-1];
          priceCases.push({faction:f, god:god, side:side, on:on, unit:key,
            food:+c.f.toFixed(6), favor:+c.v.toFixed(6), trainSeconds:+(t.readyAt-elapsed()).toFixed(6),
            godCosts:(side?activeAiGods:activeGods).map(function(g){return {key:g.key, unlock:godUnlockCost(g,side), power:godPowerCost(g,side)};})});
        });
      });
    });
  });

  globalThis.__godsReference={source:"godsbound_beta.html", passiveKeys:ALL_KEYS, rosters:rosters,
    unlockCases:unlockCases, sidePassiveCases:sidePassiveCases, economyCases:economyCases, priceCases:priceCases};
})();
`;
eval(code);
/*END-DRIVER*/
}
