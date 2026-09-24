/* U31: the deck preset gates, measured against normalizeDeckPreset.
 *
 * Each case is a preset in the SHAPE UNITY USES -- terrain as row strings, a building as
 * {type,c0,r0,c1,r1} -- converted here into the browser's raw shape and run through the real
 * normalizeDeckPreset. A case records whether it survived and, when it did, exactly what came back,
 * so the C# gate is verified against the browser rather than against its own reading of the rules.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/deck_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=globalThis.__deckReference;
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/deck_reference.json"),
  JSON.stringify(out,null,2)+"\n");
console.log("[deck-ref] "+out.cases.length+" cases ("+out.cases.filter(c=>c.ok).length+" accepted, "+
  out.cases.filter(c=>!c.ok).length+" rejected), "+out.budget.length+" terrain budgets");
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  function rows(fill){ return Array.from({length:PLAYER_ROWS},function(){ return fill.repeat(COLS); }); }
  function paint(base,marks){
    var grid=rows(base).map(function(row){ return [...row]; });
    marks.forEach(function(m){ grid[m[1]][m[0]]=m[2]; });
    return grid.map(function(row){ return row.join(""); });
  }
  function preset(faction,over){
    var d=defaultDeckPreset(faction);
    var base={faction:faction,loadout:d.loadout.slice(),heroes:d.heroes.slice(),gods:d.gods.slice(),
      terrain:d.terrain.map(function(row){ return row.join(""); }),
      buildings:d.buildings.map(function(b){ return {type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1]}; })};
    Object.keys(over||{}).forEach(function(k){ base[k]=over[k]; });
    return base;
  }
  /* The Unity shape in, the browser's raw shape out. */
  function raw(p){
    return {faction:p.faction,loadout:p.loadout,heroes:p.heroes,gods:p.gods,terrain:p.terrain,
      buildings:p.buildings.map(function(b){ return {type:b.type,hexes:[[b.c0,b.r0],[b.c1,b.r1]]}; })};
  }
  var cases=[];
  function check(name,p){
    var got=normalizeDeckPreset(raw(p));
    cases.push({name:name,preset:p,ok:!!got,
      normalized:got?{faction:got.faction,loadout:got.loadout,heroes:got.heroes,gods:got.gods,
        terrain:got.terrain.map(function(row){ return row.join(""); }),
        buildings:got.buildings.map(function(b){ return {type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1]}; })}
        :{faction:"",loadout:[],heroes:[],gods:[],terrain:[],buildings:[]}});
  }
  Object.keys(FACTIONS).forEach(function(f){ check("default "+f,preset(f)); });
  var eg=FACTIONS.egypt, cn=FACTIONS.china;
  var egHumans=Object.keys(eg.units).filter(function(k){return eg.units[k].cat==="human";});
  var egHeroes=Object.keys(eg.units).filter(function(k){return eg.units[k].cat==="hero";});
  var egMyth=Object.keys(eg.units).find(function(k){return eg.units[k].cat==="myth";});
  check("three gods",preset("egypt",{gods:eg.defaultGodPick.slice(0,3)}));
  check("five gods",preset("egypt",{gods:eg.allGods.slice(0,5).map(function(g){return g.key;})}));
  check("the same god twice",preset("egypt",{gods:[eg.defaultGodPick[0],eg.defaultGodPick[0],eg.defaultGodPick[1],eg.defaultGodPick[2]]}));
  check("a god from another pantheon",preset("egypt",{gods:[cn.defaultGodPick[0]].concat(eg.defaultGodPick.slice(1))}));
  check("five humans",preset("egypt",{loadout:egHumans.slice(0,5)}));
  check("three humans",preset("egypt",{loadout:egHumans.slice(0,3)}));
  check("the same human twice",preset("egypt",{loadout:[egHumans[0],egHumans[0],egHumans[1],egHumans[2]]}));
  check("a myth in the hand",preset("egypt",{loadout:[egMyth].concat(egHumans.slice(0,3))}));
  check("a hero in the hand",preset("egypt",{loadout:[egHeroes[0]].concat(egHumans.slice(0,3))}));
  check("one hero",preset("egypt",{heroes:eg.defaultHeroes.slice(0,1)}));
  check("a human in a hero slot",preset("egypt",{heroes:[egHumans[0],egHeroes[0]]}));
  check("an unknown faction",preset("egypt",{faction:"atlantis"}));
  check("five terrain rows",preset("egypt",{terrain:rows("P").slice(0,PLAYER_ROWS-1)}));
  check("a short terrain row",preset("egypt",{terrain:rows("P").map(function(row,i){ return i?row:row.slice(1); })}));
  check("an unknown terrain letter",preset("egypt",{terrain:paint("P",[[0,0,"X"]])}));
  var mountains=[],forests=[];
  for(var i=0;i<TERRAIN_BUDGET.M+1;i++) mountains.push([i,0,"M"]);
  for(var j=0;j<TERRAIN_BUDGET.F;j++) forests.push([j,1,"F"]);
  check("one mountain over budget",preset("egypt",{terrain:paint("P",mountains)}));
  check("painted to the budget",preset("egypt",{terrain:paint("P",mountains.slice(0,TERRAIN_BUDGET.M).concat(forests))}));
  var std=preset("egypt").buildings;
  check("two buildings",preset("egypt",{buildings:std.slice(0,2)}));
  check("two cities",preset("egypt",{buildings:[std[1],{type:"city",c0:1,r0:4,c1:2,r1:4},std[0]]}));
  check("an unknown building type",preset("egypt",{buildings:[{type:"granary",c0:1,r0:4,c1:2,r1:4},std[1],std[0]]}));
  check("hexes that are not adjacent",preset("egypt",{buildings:[{type:"temple",c0:1,r0:4,c1:4,r1:4},std[1],std[0]]}));
  check("hexes that are the same",preset("egypt",{buildings:[{type:"temple",c0:1,r0:4,c1:1,r1:4},std[1],std[0]]}));
  check("two buildings on one hex",preset("egypt",{buildings:[{type:"temple",c0:4,r0:4,c1:3,r1:4},std[1],std[0]]}));
  check("a building off the half",preset("egypt",{buildings:[{type:"temple",c0:1,r0:PLAYER_ROWS,c1:2,r1:PLAYER_ROWS},std[1],std[0]]}));
  check("a building off the board",preset("egypt",{buildings:[{type:"temple",c0:COLS-1,r0:4,c1:COLS,r1:4},std[1],std[0]]}));
  check("a building on water",preset("egypt",{terrain:paint("P",[[1,4,"W"],[2,4,"W"]])}));
  check("a building on a mountain",preset("egypt",{terrain:paint("P",[[4,4,"M"]])}));
  check("a building on a forest",preset("egypt",{terrain:paint("P",[[4,4,"F"],[5,4,"F"]])}));
  globalThis.__deckReference={source:"godsbound_beta.html",playerRows:PLAYER_ROWS,cols:COLS,
    budget:Object.keys(TERRAIN_BUDGET).map(function(k){ return {code:k,max:TERRAIN_BUDGET[k]}; }),
    cases:cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
