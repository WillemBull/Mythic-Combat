/* Godsbound — Unity game-data exporter (U5). Node 18+, no deps, offline. */
"use strict";
/*
 * Sibling of tools/export_unity_reference.js. That one exports the BOARD's answers; this
 * one exports the DATA TABLES: EG_UNITS / CN_UNITS / AZ_UNITS (and Greek), the god tables,
 * and FACTIONS.
 *
 * WHY EXPORT RATHER THAN TRANSCRIBE. There are 66 units across 4 factions with a union of
 * 56 distinct fields, plus 42 gods. Hand-typing that into C# would be wrong somewhere on
 * the first pass and would silently desync the moment a balance number changed in the HTML.
 * So the HTML stays the source of truth and this regenerates the data.
 *
 * HOW NOTHING GETS LOST. Only the core stat fields are given typed C# properties; the long
 * tail of ability flags rides along in `extras` as {key, kind, value}. The export also
 * emits a FIELD INVENTORY — every field name seen anywhere — and the Unity tests assert
 * that every inventoried field is either typed or present in extras. A new field in the
 * HTML therefore fails a test instead of vanishing.
 *
 * Usage: node tools/export_unity_data.js
 * Out:   GodsboundUnity/Godsbound/Assets/Godsbound/Resources/GameData/game_data.json
 *        (under Resources/ so runtime code can load it, not just the tests)
 */
const {src}=require("../tests/stub_dom.js");
const fs=require("fs");
const path=require("path");
const __self=fs.readFileSync(__filename,"utf8");
const _DS="/*"+"DRIVER*/", _DE="/*END-"+"DRIVER*/"; // split so this line can't self-match below
const driver=__self.slice(__self.indexOf(_DS)+_DS.length, __self.indexOf(_DE));
eval(src+driver);

const ROOT=path.join(__dirname,"..");
const OUT=path.join(ROOT,"GodsboundUnity","Godsbound","Assets","Godsbound",
                    "Resources","GameData","game_data.json");
const data=globalThis.__godsboundData;

fs.mkdirSync(path.dirname(OUT),{recursive:true});
fs.writeFileSync(OUT,JSON.stringify(data));
const kb=(fs.statSync(OUT).size/1024).toFixed(0);

console.log("[unity-data] wrote "+path.relative(ROOT,OUT)+" ("+kb+" KB)");
console.log("[unity-data] factions: "+data.factions.map(f=>f.id).join(", "));
console.log("[unity-data] units: "+data.units.length+" | gods: "+data.gods.length);
console.log("[unity-data] unit fields: "+data.unitFieldInventory.length+
            " ("+data.typedUnitFields.length+" typed, "+
            (data.unitFieldInventory.length-data.typedUnitFields.length)+" in extras)");
console.log("[unity-data] god fields: "+data.godFieldInventory.length+
            " ("+data.typedGodFields.length+" typed)");

/* Fail loudly rather than shipping a half-export. */
const problems=[];
const unitIds=new Set(data.units.map(u=>u.faction+"/"+u.key));
for(const f of data.factions){
  for(const grp of ["defaultLoadout","defaultHeroes","defaultGodPick"])
    for(const k of f[grp])
      if(grp==="defaultGodPick"){ if(!data.gods.some(g=>g.faction===f.id&&g.key===k)) problems.push(f.id+" "+grp+" -> unknown god '"+k+"'"); }
      else if(!unitIds.has(f.id+"/"+k)) problems.push(f.id+" "+grp+" -> unknown unit '"+k+"'");
}
for(const u of data.units)
  for(const e of u.extras)
    if(!["number","bool","string"].includes(e.kind)) problems.push(u.key+" extra '"+e.key+"' has unsupported kind "+e.kind);
if(problems.length){ console.error("[unity-data] FAIL:\n  "+problems.join("\n  ")); process.exit(1); }
console.log("[unity-data] economy (measured from tickEconomy): food "+
            data.economy.foodWithCity+"/s with city, "+data.economy.foodWithoutCity+"/s without | "+
            "favor player "+data.economy.playerFavorWithTemple+"/"+data.economy.playerFavorWithoutTemple+
            ", AI "+data.economy.aiFavorWithTemple+"/"+data.economy.aiFavorWithoutTemple);
if(data.economy.favorIsSideAsymmetric)
  console.log("[unity-data] NOTE: favor rates differ between player and AI -- ported as-is");
if(!data.economy.bountyAppliesToAi)
  console.log("[unity-data] NOTE: Demeter's Bounty does not pay the AI side -- ported as-is");
console.log("[unity-data] god rates (measured): water favor "+data.godRates.waterFavorPerTile+
            "/tile, Ptah building hp x"+data.godRates.ptahBuildingHpMultiplier);
console.log("[unity-data] building types: "+
            data.buildingTypes.map(t=>t.type+" "+t.hp).join(", "));
console.log("[unity-data] default player layout: "+
            data.defaultPlayerBuildings.map(b=>b.type+" ("+b.c0+","+b.r0+")").join(", "));
console.log("[unity-data] pathfinding: edge jitter "+data.pathfinding.edgeJitter+
            ", speed floor "+data.pathfinding.speedFloor);
console.log("[unity-data] training: thoth x"+data.training.thothDiscount+
            ", nuwa x"+data.training.nuWaHumanFoodDiscount+
            ", messenger x"+data.training.messengerTrainMultiplier+
            ", blocked-exit retry "+data.training.blockedExitRetrySeconds+"s");
console.log("[unity-data] referential integrity OK");

if(false){
/*DRIVER*/
code+=`
;(function(){
  /* Fields given typed C# properties. Everything else rides in extras. Chosen as the
     stats the simulation reads on every unit, not by what looked important. */
  const TYPED_UNIT=["name","emoji","cost","cat","armor","dtype","hp","dmg","as","range",
                    "speed","size","train","spriteScaleMult"];
  const TYPED_GOD=["key","name","emoji","cost","power","pcost","cd","myth","desc",
                   "passiveKey","passive"];

  const kindOf=v=>typeof v==="number"?"number":typeof v==="boolean"?"bool":"string";
  const extrasFrom=(obj,typed)=>Object.keys(obj).filter(k=>!typed.includes(k)).sort().map(k=>{
    const v=obj[k];
    return {key:k, kind:kindOf(v), value:String(v)};
  });

  const FACTION_IDS=Object.keys(FACTIONS);

  const units=[], gods=[];
  const unitFields=new Set(), godFields=new Set();

  for(const fid of FACTION_IDS){
    const F=FACTIONS[fid];

    for(const key of Object.keys(F.units)){
      const u=F.units[key];
      Object.keys(u).forEach(k=>unitFields.add(k));

      const sc=u.spriteScaleMult;
      const perSide=(sc!==undefined && typeof sc==="object");

      units.push({
        faction:fid, key,
        name:u.name, emoji:u.emoji||"",
        /* cost is {f}, {v} or {f,v} -- flattened, absent component is 0 */
        costFood:(u.cost&&u.cost.f)||0,
        costFavor:(u.cost&&u.cost.v)||0,
        cat:u.cat||"", armor:u.armor||"", dtype:u.dtype||"",
        hp:u.hp||0, dmg:u.dmg||0, attackSpeed:u.as||0, range:u.range||0,
        speed:u.speed||0, size:u.size||0, train:u.train||0,
        hasPerSideScale:perSide,
        spriteScaleUniform:(sc!==undefined&&!perSide)?sc:0,
        spriteScaleSide0:perSide?(sc[0]||0):0,
        spriteScaleSide1:perSide?(sc[1]||0):0,
        extras:extrasFrom(u,TYPED_UNIT)
      });
    }

    for(const g of F.allGods){
      Object.keys(g).forEach(k=>godFields.add(k));
      gods.push({
        faction:fid, key:g.key, name:g.name, emoji:g.emoji||"",
        cost:g.cost||0, power:g.power||"", pcost:g.pcost||0, cd:g.cd||0,
        myth:g.myth||"", desc:g.desc||"",
        passiveKey:g.passiveKey||"", passive:g.passive||"",
        extras:extrasFrom(g,TYPED_GOD)
      });
    }
  }

  const factions=FACTION_IDS.map(fid=>{
    const F=FACTIONS[fid];
    return {
      id:fid, name:F.name||"", label:F.label||"", aiTitle:F.aiTitle||"",
      unitKeys:Object.keys(F.units),
      allGodKeys:F.allGods.map(g=>g.key),
      defaultLoadout:F.defaultLoadout.slice(),
      defaultHeroes:F.defaultHeroes.slice(),
      defaultGodPick:F.defaultGodPick.slice(),
      /* economy is a small per-faction override bag (e.g. aztec templeBonus/favorPerDeath) */
      economy:extrasFrom(F.economy||{},[])
    };
  });

  /* ---- PATHFINDING (U8) ------------------------------------------------------
     findPath's two tuning numbers are inline literals in its cost expression, so they are
     extracted from the function's own SOURCE TEXT and the extraction asserts the pattern
     is present. If the cost formula is ever reshaped, the export fails loudly instead of
     shipping a stale constant into Unity. */
  var pathfinding=(function(){
    /* Plain string parsing, not regex: this code lives inside a template literal, which
       eats the backslashes a regex needs. */
    var srcText=findPath.toString();
    function numberAfter(marker){
      var i=srcText.indexOf(marker);
      if(i<0) throw new Error("findPath: could not find "+marker);
      var rest=srcText.slice(i+marker.length);
      var m="";
      for(var j=0;j<rest.length;j++){
        var ch=rest[j];
        if(ch===" ")            { if(m==="") continue; else break; }
        if(ch>="0"&&ch<="9")    { m+=ch; continue; }
        if(ch==="."&&m.indexOf(".")<0){ m+=ch; continue; }
        break;
      }
      var v=parseFloat(m);
      if(!isFinite(v)) throw new Error("findPath: unparseable number after "+marker);
      return v;
    }
    return { edgeJitter:numberAfter("Math.random()*"), speedFloor:numberAfter("Math.max(sp,") };
  })();

  /* ---- TRAINING (U10) --------------------------------------------------------
     The price and queue modifiers are inline literals inside their functions, so they are
     read out of the functions' own source text and the read throws if a marker is gone.
     THOTH_DISCOUNT is a real global and is taken directly. */
  var training=(function(){
    function numIn(srcText,marker){
      var i=srcText.indexOf(marker);
      if(i<0) throw new Error("training export: could not find "+marker);
      var rest=srcText.slice(i+marker.length), m="";
      for(var j=0;j<rest.length;j++){
        var ch=rest[j];
        if(ch===" ")             { if(m==="") continue; else break; }
        if(ch>="0"&&ch<="9")     { m+=ch; continue; }
        if(ch==="."&&m.indexOf(".")<0){ m+=ch; continue; }
        break;
      }
      var v=parseFloat(m);
      if(!isFinite(v)) throw new Error("training export: unparseable number after "+marker);
      return v;
    }
    var out={
      /* Thoth's Scribe's Ledger: multiplies EVERY cost, food and favour, units and gods. */
      thothDiscount:THOTH_DISCOUNT,
      /* Nu Wa's Mother of Humanity: food only, human units only. */
      nuWaHumanFoodDiscount:numIn(unitCost.toString(), String.fromCharCode(34)+"motherOfHumanity"+String.fromCharCode(34)+")) ?"),
      /* Hermes the Messenger: training finishes sooner. */
      messengerTrainMultiplier:numIn(queueTrain.toString(), String.fromCharCode(34)+"messenger"+String.fromCharCode(34)+")?"),
      /* Blocked exit hex: push the ready time out and retry, never drop the unit. */
      blockedExitRetrySeconds:numIn(tickTraining.toString(), "t.readyAt+=")
    };
    Object.keys(out).forEach(function(k){
      if(!(out[k]>0)) throw new Error("training export: "+k+" is not positive");
    });
    if(out.thothDiscount>=1) throw new Error("training export: thothDiscount should reduce cost");
    if(out.nuWaHumanFoodDiscount>=1) throw new Error("training export: nuWa discount should reduce cost");
    if(out.messengerTrainMultiplier>=1) throw new Error("training export: messenger should shorten training");
    return out;
  })();

  /* U11: read movement tuning from the original functions. The browser's fixed-pixel
     arrival tolerance is normalized against the same stub viewport used by fixtures. */
  var movement=(function(){
    function numberAfter(fn, marker){
      var source=fn.toString(), at=source.indexOf(marker);
      if(at<0) throw new Error("movement export: missing "+marker);
      var rest=source.slice(at+marker.length).trimStart(), number="";
      for(var i=0;i<rest.length;i++){
        var ch=rest[i];
        if((ch>="0"&&ch<="9")||ch===".") number+=ch; else break;
      }
      var value=Number(number);
      if(!number || !isFinite(value)) throw new Error("movement export: invalid "+marker);
      return value;
    }
    return {
      baseSpeedMultiplier:numberAfter(effSpeed,"u.def.speed*"),
      slowMultiplier:numberAfter(effSpeed,"u.slowUntil) sp*="),
      moveSpeedMultiplier:numberAfter(effSpeed,'"moveSpeed10")) sp*='),
      moonlightMultiplier:1+MOONLIGHT_BONUS,
      fastWaterMultiplier:numberAfter(moveToward,'==="W") terr='),
      nearWaterMultiplier:numberAfter(moveToward,'==="W"))) terr*='),
      deviateDelay:DEVIATE_DELAY,
      congestionDelay:numberAfter(followPath,"if(u.waitT>"),
      congestionRetry:numberAfter(followPath,"else u.waitT="),
      snapDistanceInHexes:numberAfter(moveToward,"Math.max(step,")/HEX
    };
  })();

  /* U12: every inline combat coefficient is harvested from its owning function.
     Markers deliberately fail closed when the browser changes its expression. */
  var combat=(function(){
    var numbers=[];
    function read(key,fn,marker){
      var s=fn.toString(), at=s.indexOf(marker);
      if(at<0) throw new Error("combat export: missing "+key+" marker "+marker);
      var rest=s.slice(at+marker.length).trimStart(), text="";
      for(var i=0;i<rest.length;i++){
        var c=rest[i];
        if((c>="0"&&c<="9")||c===".") text+=c; else break;
      }
      if(!text || !isFinite(Number(text))) throw new Error("combat export: invalid "+key);
      numbers.push({key:key,value:Number(text)});
    }
    var calc=[
      ["aegis",'(tgt.aegisUntil||0)) d*='], ["stunned","att.stunUntil) d*="],
      ["bloodlust",'(tgt.bloodlustUntil||0)) d*='], ["weakened","att.weakenUntil) d*="],
      ["rangedBonus",'"rangedDmg10")) d*='], ["mentorFallback","mentor.def.teachDmg||"],
      ["authorityRange","hexDist(att.hex,h)))<="], ["authority","if(near) d*="],
      ["openGround",'!['+String.fromCharCode(34)+'F'+String.fromCharCode(34)+','+String.fromCharCode(34)+'M'+String.fromCharCode(34)+'].includes(terrain(att.hex[0],att.hex[1]))) d*='],
      ["chaosBoost","S.chaosBuffUntil[att.side]) d*="],
      ["razorWings",'att.free && !att.ambushUsed) d*='],
      ["waterAmbush",'att.def.ambushInWater && terrain(att.hex[0],att.hex[1])==="W") d*='],
      ["waterDamage",'terrain(n[0],n[1])==="W"))) d*='],
      ["fallenBuildings","(1-avgHp)*"],
      ["buildingCrushing",'adt==="crushing"?'], ["buildingMelee",'adt==="melee"?'],
      ["buildingRanged",'adt==="melee"?0.35 : '],
      ["siege","if(att.def.siege) d*="], ["automata",'"automata")) d*='],
      ["earthshaker",'"earthshaker")) d*='],
      ["fortressRange","hexDist(tgt.hex,h)))<="], ["fortressDefense","hexDist(tgt.hex,h)))<=2) d*="],
      ["warcry","if(mine>theirs) d*="], ["category","===tgt.def.cat) d*="],
      ["fireDefeatsWater",'neighbors(tgt.hex[0],tgt.hex[1]).some(n=>terrain(n[0],n[1])==="W"))) d*='],
      ["armorStrong","DMG_STRONG[adt]===tgt.def.armor) d*="],
      ["armorWeak","DMG_WEAK[adt]===tgt.def.armor) d*="],
      ["forestCover",'terrain(tgt.hex[0],tgt.hex[1])==="F") d*='],
      ["lakeDefense","if(onLakeTexcoco(tgt)) d*="],
      ["waterVulnerability",'factionForSide(tgt.side)!=="egypt") d*=']
    ];
    calc.forEach(p=>read(p[0],calcDmg,p[1]));
    var hit=[
      ["slowDuration","if(att.def.slows) tgt.slowUntil=elapsed()+"],
      ["poisonDuration","att.def.poisonDur||"], ["poisonDps","att.def.poisonDps||"],
      ["pestilenceDuration",'"pestilence")) tgt.weakenUntil=elapsed()+'],
      ["reviveHealth","tgt.hp=tgt.maxhp*"],
      ["templeRange","hexDist(tgt.hex,h)))<="], ["templeFavor","S.aiFavor+"],
      ["bloodFavor",'"bloodFavor")){'+String.fromCharCode(10)+'          if(att.side) S.aiFavor=Math.min(S.FAVOR_CAP,S.aiFavor+'],
      ["ferrymanFavor",'if(tgt.side) S.aiFavor=Math.min(S.FAVOR_CAP,S.aiFavor+2);'+String.fromCharCode(10)+'          else S.favor=Math.min(S.FAVOR_CAP,S.favor+'],
      ["earthFood","S.aiFood+"], ["cloneCap","if(alive<"], ["splashRadius","spacing*"]
    ];
    hit.forEach(p=>read(p[0],dealDamage,p[1]));
    read("scanDistance",scanEncounter,"let sc=-d*");
    read("scanBlocker",scanEncounter,"hexDist(e.hex,next)<=1) sc+=");
    read("scanAlly",scanEncounter,"attackedByAllies(u,e)) sc+=");
    read("scanCounter",scanEncounter,"CAT_BEATS[u.def.cat]===e.def.cat) sc+=");
    read("initialAttackSpread",makeUnit,"atkT:Math.random()*");
    read("buildingDeathFavor",onBuildingDestroyed,"S.aiFavor+");
    read("newGrowthFood",onBuildingDestroyed,"S.aiFood+");
    read("inevitableFavor",favorOnDeath,"grant(opp,");
    read("lastStandDuration",beginLastStand,"u.def.lastStandDur||");
    read("lastStandHealth",beginLastStand,"u.maxhp*");
    read("retreatWard",beginRetreat,"u.invulnUntil=elapsed()+");
    numbers.push({key:"retreatMax",value:RETREAT_MAX},{key:"moonlight",value:1+MOONLIGHT_BONUS});
    function pairs(table){return Object.entries(table).map(p=>({key:p[0],target:p[1]}));}
    return {armorDamageMultipliers:ARMOR_DMG_MULTIPLIERS,numbers:numbers,
            categoryBeats:pairs(CAT_BEATS),damageStrong:pairs(DMG_STRONG),damageWeak:pairs(DMG_WEAK)};
  })();

  /* ---- BUILDINGS (U7, corrected in U8) ---------------------------------------
     DO NOT snapshot the live buildings array. It is mutated before this code can read it
     (the player's layout is applied from the deck preset, and the AI's is RANDOMIZED per
     match), so reading it produced different data on every run -- and one sampled layout
     put the AI city on (4,0), silently breaking a pathfinding test. There simply is no
     canonical AI layout to export.

     So export the two things that ARE data: per-type hp, and the player's default layout
     from the deck preset. C# derives the AI's default by mirroring the player's about the
     board's centre -- same columns, same offset from its own edge -- which reproduces the
     module-load layout exactly and stays deterministic. */
  var buildingTypes=(function(){
    var seen={}, out=[];
    buildings.forEach(function(b){
      if(seen[b.type]) return;
      seen[b.type]=true;
      out.push({ type:b.type, name:b.name, emoji:b.emoji||"", hp:b.maxhp });
    });
    return out;
  })();

  /* Stored in LOCAL half-rows in the preset; lifted to absolute rows here so C# never has
     to know about the offset. */
  var defaultPlayerBuildings=(function(){
    var preset=defaultDeckPreset();
    return preset.buildings.map(function(b){
      return { type:b.type,
               c0:b.hexes[0][0], r0:b.hexes[0][1]+PLAYER_ROW0,
               c1:b.hexes[1][0], r1:b.hexes[1][1]+PLAYER_ROW0 };
    });
  })();

  (function(){
    var bad=[];
    if(buildingTypes.length!==3) bad.push("expected three building types, got "+buildingTypes.length);
    buildingTypes.forEach(function(t){ if(!(t.hp>0)) bad.push(t.type+" has no hp"); });
    if(defaultPlayerBuildings.length!==3) bad.push("expected three player buildings");
    defaultPlayerBuildings.forEach(function(b){
      if(hexDist([b.c0,b.r0],[b.c1,b.r1])!==1) bad.push(b.type+" hexes are not adjacent");
      [[b.c0,b.r0],[b.c1,b.r1]].forEach(function(h){
        if(!inBounds(h[0],h[1])) bad.push(b.type+" hex "+h+" is off the board");
        if(h[1]<PLAYER_ROW0) bad.push(b.type+" hex "+h+" is not in the player's half");
      });
      /* The mirror C# will apply must also land in the AI half and stay adjacent. */
      var m0=[b.c0, ROWS-1-b.r0], m1=[b.c1, ROWS-1-b.r1];
      if(hexDist(m0,m1)!==1) bad.push(b.type+" mirrored hexes are not adjacent");
      [m0,m1].forEach(function(h){
        if(h[1]>=AI_ROWS) bad.push(b.type+" mirrored hex "+h+" is not in the AI half");
      });
    });
    if(bad.length) throw new Error("building export invalid: "+bad.join("; "));
  })();

  /* ---- ECONOMY (U6) ----------------------------------------------------------
     MEASURED, not transcribed. tickEconomy's rates are inline literals, so instead of
     copying them we boot a match, drive the real function with dt=1 (so each delta IS a
     per-second rate) and record what it produces. hasPassive is temporarily overridden to
     switch individual passives on, which is how the passive multipliers get measured too.
     Nothing in the economy block below is a hand-typed number. */
  var economy=(function(){
    startMatch();
    var realHasPassive=hasPassive;
    var forced=null;
    hasPassive=function(side,key){ return forced!==null ? (key===forced) : realHasPassive(side,key); };

    function rate(side,city,temple){
      getBld(side,"city").dead=!city;
      getBld(side,"temple").dead=!temple;
      S.food=0;S.favor=0;S.aiFood=0;S.aiFavor=0;S.blightUntil=[0,0];
      tickEconomy(1);
      return side===0 ? {food:+S.food.toFixed(6),favor:+S.favor.toFixed(6)}
                      : {food:+S.aiFood.toFixed(6),favor:+S.aiFavor.toFixed(6)};
    }
    function withPassive(key,side,city,temple){ forced=key; var r=rate(side,city,temple); forced=null; return r; }

    var base0=rate(0,true,true), base0NoTemple=rate(0,true,false), base0NoCity=rate(0,false,true);
    var base1=rate(1,true,true), base1NoTemple=rate(1,true,false);

    var boosted=withPassive("cityFoodBoost",0,true,true);
    var morning=withPassive("morningStar",0,true,true);
    var bounty =withPassive("bounty",0,true,true);
    var bountyAi=withPassive("bounty",1,true,true);
    var mandate=withPassive("mandateOfHeaven",0,true,true);
    /* Mandate scales with buildings still standing (3 -> full, 2 -> reduced, fewer -> none).
       Kill the FORTRESS to reach exactly two alive without disturbing city/temple income. */
    var mandate2=(function(){
      var fort=getBld(0,"fortress"); var was=fort.dead; fort.dead=true;
      var r=withPassive("mandateOfHeaven",0,true,true); fort.dead=was; return r;
    })();

    /* blight: measured, because the source's else-guard wraps only the FOOD line */
    getBld(0,"city").dead=false; getBld(0,"temple").dead=false;
    S.food=0;S.favor=0;S.blightUntil=[999,0]; tickEconomy(1);
    var blight={food:S.food,favor:+S.favor.toFixed(6)};

    /* cap clamping */
    S.blightUntil=[0,0]; S.food=S.FOOD_CAP-0.5; S.favor=S.FAVOR_CAP-0.5; tickEconomy(1);
    var capped={food:S.food,favor:S.favor};

    hasPassive=realHasPassive;

    var mult=FOOD_RATE_MULT;
    var block={
      foodCap:S.FOOD_CAP, favorCap:S.FAVOR_CAP,
      foodRateMult:mult,
      morningStarBonus:MORNING_STAR_BONUS,
      /* per-second rates with no passives */
      foodWithCity:base0.food, foodWithoutCity:base0NoCity.food,
      playerFavorWithTemple:base0.favor, playerFavorWithoutTemple:base0NoTemple.favor,
      aiFavorWithTemple:base1.favor, aiFavorWithoutTemple:base1NoTemple.favor,
      /* pre-multiplier bases, recovered from the measurements */
      cityFoodBase:+(base0.food/mult).toFixed(6),
      cityFoodBaseBoosted:+(boosted.food/mult).toFixed(6),
      noCityFoodBase:+(base0NoCity.food/mult).toFixed(6),
      /* passive effects, measured as deltas against the same baseline */
      morningStarFoodMultiplier:+(morning.food/base0.food).toFixed(6),
      mandateThreeBuildingsMultiplier:+(mandate.favor/base0.favor).toFixed(6),
      mandateTwoBuildingsMultiplier:+(mandate2.favor/base0.favor).toFixed(6),
      bountyCityFavorPerSecond:+(bounty.favor-base0.favor).toFixed(6),
      /* blight stops food only -- the source's guard wraps just the food line */
      blightStopsFood:blight.food===0,
      blightStopsFavor:blight.favor===0,
      capsClamp:capped.food===S.FOOD_CAP&&capped.favor===S.FAVOR_CAP,
      /* the player/AI favor comparison, recorded explicitly. Asymmetric until 2026-09-16,
         when Willem ruled both sides onto one formula -- Bounty included. */
      favorIsSideAsymmetric:base0.favor!==base1.favor||base0NoTemple.favor!==base1NoTemple.favor,
      bountyAppliesToAi:+(bountyAi.favor-base1.favor).toFixed(6)===+(bounty.favor-base0.favor).toFixed(6)
    };
    var bad=[];
    for(var k in block) if(typeof block[k]==="number" && !isFinite(block[k])) bad.push(k);
    if(bad.length) throw new Error("economy measurement produced non-finite "+bad.join(","));
    if(!block.capsClamp) throw new Error("caps did not clamp -- measurement setup is wrong");
    return block;
  })();

  /* Measure actual creation order: tickTraining visits the queue backwards. Keep this
     after the building and economy snapshots so its match state cannot affect them. */
  (function(){
    var savedUnits=S.units, savedTraining=S.training, savedUid=S.uid;
    try {
      S.units=[]; S.training=[];
      for(var i=0;i<3;i++)
        S.training.push({side:0,key:"spear",path:[[i,AI_ROWS]],readyAt:elapsed()});
      tickTraining();
      training.simultaneousCompletionOrder=S.units.map(function(u){return u.hex[0];});
      if(training.simultaneousCompletionOrder.length!==3)
        throw new Error("training measurement did not spawn all three orders");
    } finally { S.units=savedUnits; S.training=savedTraining; S.uid=savedUid; }
  })();

  /* ---- GOD RATES (U23) --------------------------------------------------------
     Measured, like the economy: Long Wang's favour per water tile comes from the real
     waterTileBonus on a one-tile board, Ptah's building multiplier from a real two-tap
     unlock. Runs last, on a throwaway match, and puts the clock back afterwards. */
  var godRates=(function(){
    var savedT=S.t, savedMap=TMAP.map(function(r){return r.slice();});
    var realHasPassive=hasPassive;
    try {
      hasPassive=function(side,key){ return key==="lordOfFourSeas"; };
      for(var r=0;r<ROWS;r++) for(var c=0;c<COLS;c++) TMAP[r][c]="P";
      TMAP[ROWS-1][0]="W";
      var perTile=waterTileBonus(0);
      TMAP[ROWS-1][1]="W";
      if(Math.abs(waterTileBonus(0)-2*perTile)>1e-9) throw new Error("water favor is not linear per tile");
      hasPassive=realHasPassive;
      var savedPick=S.godPick.slice(), savedFaction=S.playerFaction;
      S.playerFaction="egypt"; S.godPick=["ptah","horus","bastet","osiris"];
      startMatch(false);
      var city=getBld(0,"city"), before=city.maxhp;
      S.favor=999; S.godPreview=null; onGodTap("ptah"); onGodTap("ptah");
      if(!S.playerGods.ptah.unlocked) throw new Error("Ptah did not unlock");
      var ptah=+(city.maxhp/before).toFixed(6);
      city.maxhp=before;
      S.playerFaction=savedFaction; S.godPick=savedPick;
      return {waterFavorPerTile:+perTile.toFixed(6), ptahBuildingHpMultiplier:ptah,
              ptahPassiveKey:"buildingHpBoost"};
    } finally {
      hasPassive=realHasPassive; S.t=savedT;
      for(var r2=0;r2<ROWS;r2++) TMAP[r2]=savedMap[r2];
    }
  })();

  /* ---- POWERS (U26) ----------------------------------------------------------
     Every inline power coefficient is read from the owning CASE of applyGodPower (or the
     owning helper), scoped so a marker cannot match another god's branch. Fails closed. */
  var powers=(function(){
    var numbers=[];
    function grab(key,text,marker,label){
      var at=text.indexOf(marker);
      if(at<0) throw new Error("powers export: missing "+key+" marker "+marker+" in "+label);
      var rest=text.slice(at+marker.length).trimStart(), t="";
      for(var i=0;i<rest.length;i++){ var c=rest[i]; if((c>="0"&&c<="9")||c===".") t+=c; else break; }
      if(!t||!isFinite(Number(t))) throw new Error("powers export: invalid "+key);
      numbers.push({key:key,value:Number(t)});
    }
    var src=applyGodPower.toString();
    function caseBody(k){
      var at=src.indexOf('case "'+k+'"'); if(at<0) throw new Error("powers export: no case "+k);
      var next=src.indexOf(String.fromCharCode(10)+'    case "',at+5); return src.slice(at, next<0?src.length:next);
    }
    var C=function(k,key,marker){ grab(key,caseBody(k),marker,"case "+k); };
    C("horus","horusDamage","raSolarDamage() : ");
    C("bastet","bastetWard","bld.invulnUntil=elapsed()+");
    C("osiris","osirisWindowAnubis",'"corpseWindow")?');
    C("osiris","osirisWindow",'"corpseWindow")?90:');
    C("osiris","osirisCount",".slice(-");
    C("osiris","osirisDespawn","u.despawnAt=elapsed()+");
    C("isis","isisLock","lockedUntil = elapsed()+");
    C("anubis","anubisRadius","hexDist(u.hex,hex)<=");
    C("set","setRadius","hexDist(u.hex,hex)>");
    C("set","setSlow","u.slowUntil=elapsed()+");
    C("thoth","thothRadius","hexDist(u.hex,hex)<=");
    C("sekhmet","sekhmetRadius","hexDist(u.hex,hex)>");
    C("sekhmet","sekhmetFrenzy","u.frenzyUntil=elapsed()+");
    C("nephthys","nephthysVeil","(g.veilDur||");
    C("horus","reviveHealth","u.hp=u.maxhp*");
    grab("chaosWindow",castPlayerPower.toString(),"S.chaosBuffUntil[1]=elapsed()+","castPlayerPower");
    /* U27: China */
    C("jade","jadeRadius","hexDist(u.hex,hex)<=");
    C("jade","jadeConscript","target.conscriptedUntil=elapsed()+");
    C("sunwukong","wukongRadius","hexDist(u.hex,hex)<=");
    C("longwang","floodRadius","hexesInRadius(hex,");
    C("longwang","floodDuration","expiresAt:elapsed()+");
    C("longwang","floodDamage","u.hp-=");
    C("longwang","floodHitRadius","hexDist(u.hex,hex)>");
    C("gonggong","rubbleRadius","hexesInRadius(hex,");
    C("gonggong","rubbleDuration","expiresAt:elapsed()+");
    C("erlangshen","thirdEyeBar","S.trueSightUntil[enemySide]=elapsed()+");
    C("xiwangmu","groveRadius","g.groveRadius||");
    C("xiwangmu","groveDuration","(g.groveDur||");
    C("zhurong","fireRadius","hexesInRadius(hex,");
    C("zhurong","fireDps","dmgPerSec:");
    C("zhurong","fireDuration","expiresAt:elapsed()+");
    C("chang","moonfallSlow","u.slowUntil=elapsed()+");
    C("leigong","thunderRadius","hexDist(u.hex,hex)>");
    C("leigong","thunderStun","u.stunUntil=elapsed()+");
    var ge=tickGodEffects.toString();
    /* U28: Greece */
    C("zeus","zeusRadius","hexDist(u.hex,hex)<=");
    C("zeus","zeusArcRadius","hexDist(u.hex,t1.hex)<=");
    C("zeus","zeusDamage","[[t1,");
    C("zeus","zeusArcDamage","[arc,");
    C("poseidon","quakeRadius","hexDist(h,hex)))>");
    C("poseidon","quakeDamage","razeBuilding(casterSide,b,");
    C("athena","aegisRadius","hexDist(u.hex,hex)>");
    C("athena","aegisDuration","u.aegisUntil=elapsed()+");
    C("ares","aresRadius","hexDist(u.hex,hex)>");
    C("ares","aresDuration","u.buffUntil=elapsed()+");
    C("ares","aresDamage","u.buffDmg=");
    C("ares","aresSpeed","u.buffSpd=");
    C("apollo","apolloRadius","hexDist(u.hex,hex)>");
    C("apollo","apolloDamage","u.hp-=");
    C("apollo","apolloWeaken","u.weakenUntil=elapsed()+");
    C("hermes","hermesTake","Math.min(");
    C("demeter","blightDuration","S.blightUntil[enemySide]=elapsed()+");
    C("hades","hadesDespawn","u.despawnAt=elapsed()+");
    C("hades","hadesCount",".slice(-");
    C("dionysus","vineRadius","hexDist(u.hex,hex)>");
    C("dionysus","vineRoot","u.rootUntil=elapsed()+");
    C("hephaestus","forgeCharges","S.forge[casterSide]=");
    C("artemis","huntRadius","hexDist(u.hex,hex)<=");
    C("artemis","huntDuration","until:elapsed()+");
    /* U29: the Aztecs */
    C("huitzilopochtli","solarDuration","u.buffUntil=elapsed()+");
    C("huitzilopochtli","solarSpeed","u.buffSpd=");
    C("huitzilopochtli","solarDamage","u.buffDmg=");
    C("huitzilopochtli","bloodPrice","const cost=u.hp*");
    C("tlaloc","rainRadius","hexesInRadius(hex,");
    C("tlaloc","rainDuration","expiresAt:elapsed()+");
    C("tezcatlipoca","mirrorRadius","hexDist(u.hex,hex)<=");
    C("tezcatlipoca","mirrorDespawn","mirror.despawnAt=elapsed()+");
    C("xipetotec","flayDuration","S.passivesStrippedUntil[enemySide]=elapsed()+");
    C("coatlicue","serpentPoison","u.poisonUntil=elapsed()+");
    C("coatlicue","serpentDps","u.poisonDps=");
    C("mictlantecuhtli","bargainFavor","S.aiFavor+");
    C("mictlantecuhtli","bargainDelay","null,null,elapsed()+");
    C("coyolxauhqui","dismemberRadius","hexDist(u.hex,hex)<=");
    C("coyolxauhqui","dismemberHealth","half1.maxhp=half1.hp*");
    C("coyolxauhqui","dismemberDespawn","half1.despawnAt=elapsed()+");
    C("ehecatl","windDuration","u.buffUntil=elapsed()+");
    C("ehecatl","windSpeed","u.buffSpd=");
    C("itzpapalotl","obsidianRadius","hexesInRadius(hex,");
    C("itzpapalotl","obsidianDamage","u.hp-=");
    C("itzpapalotl","obsidianSlow","u.slowUntil=elapsed()+");
    var tt=tickTraining.toString();
    grab("forgeHp",tt,"u.maxhp=Math.round(u.maxhp*","tickTraining");
    grab("earthshaker",razeBuilding.toString(),'"earthshaker")) d*=',"razeBuilding");
    /* named abilities (updateUnit, tickGodEffects hero sweep, form chains) */
    var uu=updateUnit.toString();
    grab("healInterval",uu,"u.healT=","updateUnit");
    grab("retreatRefresh",ge,"u.invulnUntil=Math.max(u.invulnUntil,elapsed()+","tickGodEffects");
    grab("gorgonReach",ge,"if(hexDist(u.hex,e.hex)>","tickGodEffects");
    grab("ambushDefault",ge,"u.def.ambushBuildingDmg||","tickGodEffects");
    grab("rageDefault",ge,"(u.def.rageDur||","tickGodEffects");
    grab("quarryScore",scanEncounter.toString(),"if(isQuarry(e)) sc+=","scanEncounter");
    var bs=beginSwallow.toString();
    grab("swallowRadius",bs,"hexDist(m.hex,u.hex)<=","beginSwallow");
    grab("swallowDefault",bs,"(u.def.swallowedByMyth||","beginSwallow");
    grab("swallowReturnHealth",advanceSwallows.toString(),"back.hp=back.maxhp*","advanceSwallows");
    var tw=tickThundersWarning.toString();
    grab("warningRange",tw,"hexDist(u.hex,h)))<=","tickThundersWarning");
    grab("warningSlow",tw,"u.slowUntil=elapsed()+","tickThundersWarning");
    grab("lyreRange",ge,"tmp.hexes.map(h=>hexDist(u.hex,h)))<=","tickGodEffects");
    grab("lyreHeal",ge,"u.hp=Math.min(u.maxhp,u.hp+","tickGodEffects");
    grab("heroRegen",ge,"u.hp+u.maxhp*","tickGodEffects");
    grab("auraRange",ge,"(u.def.auraRange||","tickGodEffects");
    numbers.push({key:"judgmentBase",value:JUDGMENT_BASE});
    var ptahShape=ptahCanShape.toString().match(new RegExp('key==="([a-z_]+)"'));
    if(!ptahShape) throw new Error("powers export: ptahCanShape changed");
    /* the Ra aspects are a data table already -- exported as rows */
    var raAspects=RA_ASPECTS.map(function(a){return {name:a.name,until:a.until,damage:a.dmg};});
    return {numbers:numbers, raAspects:raAspects, ptahExtraKey:ptahShape[1]};
  })();

  const gun=tickFortresses.toString();
  function gunNumber(pattern){
    const match=gun.match(pattern); if(!match) throw Error("Fortress source changed: "+pattern);
    return Number(match[1]);
  }
  const fortresses={damage:gunNumber(/tgt.hp-=([0-9.]+)/),
    range:gunNumber(/unitDist\\(u,b\\)<=([0-9.]+)/),
    cooldown:gunNumber(/b.atkTimer=([0-9.]+)/),
    tracerLife:gunNumber(/life:([0-9.]+)/)};
  /* U31: the deck model's own data. The terrain budget is a SETUP RULE (normalizeDeckPreset
     enforces it), and each faction's shipped default deck is what a player starts from, so both
     belong with the rest of the measured game data rather than with presentation. */
  var terrainBudget=Object.keys(TERRAIN_BUDGET).map(function(k){ return {code:k,max:TERRAIN_BUDGET[k]}; });
  var defaultDecks=Object.keys(FACTIONS).map(function(f){
    var d=defaultDeckPreset(f);
    return {faction:d.faction,loadout:d.loadout,heroes:d.heroes,gods:d.gods,
      terrain:d.terrain.map(function(row){ return row.join(""); }),
      buildings:d.buildings.map(function(b){ return {type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1]}; })};
  });
  globalThis.__godsboundData={
    generated:"tools/export_unity_data.js — do not hand-edit; re-run to refresh",
    source:"godsbound_beta.html",
    /* The caps live on S, not as globals -- S.FOOD_CAP / S.FAVOR_CAP (120 / 250). */
    foodCap:S.FOOD_CAP, favorCap:S.FAVOR_CAP,
    matchSeconds:S.t, fortresses, godRates:godRates, powers:powers,
    playerRows:PLAYER_ROWS, terrainBudget:terrainBudget, defaultDecks:defaultDecks,
    factions, units, gods, economy, buildingTypes:buildingTypes, defaultPlayerBuildings:defaultPlayerBuildings, training:training, movement:movement, combat:combat, pathfinding:pathfinding,
    unitFieldInventory:[...unitFields].sort(),
    typedUnitFields:TYPED_UNIT.slice().sort(),
    godFieldInventory:[...godFields].sort(),
    typedGodFields:TYPED_GOD.slice().sort()
  };
})();
`;
eval(code);
/*END-DRIVER*/
}
