/* U32: the setup rules — painting a half, placing its buildings, and the AI's layouts.
 *
 * Everything here is measured from the browser's own functions: freshPlayerHalfTerrain,
 * paintTerrain (with the budget, the mountain reach-gate and Egypt's Nile line), canPlaceBuilding,
 * validAiLayout, aiTerrainLayoutValid and composeAbsoluteTMAP. The AI's terrain GENERATOR is not
 * replayed here: it shuffles with a comparator whose result is engine-specific and validates through
 * findPath's random jitter, so Unity's port is checked against its PROPERTIES (budget, blocked
 * hexes, reachability, determinism per seed) instead of against a recorded layout.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/setup_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const out=globalThis.__setupReference;
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/setup_reference.json"),
  JSON.stringify(out,null,2)+"\n");
console.log("[setup-ref] "+out.fresh.length+" fresh halves, "+out.placement.length+" placement boards ("+
  out.placement.reduce((n,b)=>n+b.pairs.length,0)+" pairs), "+out.paint.length+" paint runs, "+
  out.layoutChecks.length+" AI layout checks, "+out.terrainChecks.length+" AI terrain checks, "+
  out.compose.length+" compositions");
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  var TYPES=["fortress","city","temple"];
  function rowsOf(grid){ return grid.map(function(row){ return row.join(""); }); }
  /* Flat shapes only: Unity's JsonUtility cannot read an array of arrays. */
  function pair(h){ return {c0:h[0][0],r0:h[0][1],c1:h[1][0],r1:h[1][1]}; }
  function hexes(list){ return list.map(function(h){ return {c:h[0],r:h[1]}; }); }
  function bldRows(list){ return list.map(function(b){ return {type:b.type,c0:b.hexes[0][0],r0:b.hexes[0][1],c1:b.hexes[1][0],r1:b.hexes[1][1]}; }); }
  /* The terrain step, as renderSetupStep leaves it: TMAP IS the player's own half. */
  function editHalf(faction,buildings){
    S.playerFaction=faction; S.aiFaction=(faction==="egypt")?"china":"egypt";
    S.phase="setup"; S.setupStep="terrain";
    playerHalf.terrain=freshPlayerHalfTerrain();
    var layout=buildings||defaultDeckPreset(faction).buildings;
    playerHalf.buildings.forEach(function(b){
      var saved=layout.find(function(x){ return x.type===b.type; });
      b.hexes=saved.hexes.map(function(h){ return [h[0],h[1]]; });
    });
    TMAP=playerHalf.terrain;
    S.paintMap={}; S.painted={F:0,M:0,W:0};
  }
  /* The rest of setup, where TMAP is the whole board again. */
  function wholeBoard(){ S.setupStep="faction"; TMAP=composeAbsoluteTMAP(); composeAbsoluteBuildings(); }

  /* ---- each faction's fresh half ---- */
  var fresh=Object.keys(FACTIONS).map(function(f){
    editHalf(f);
    return {faction:f,rows:rowsOf(playerHalf.terrain)};
  });

  /* ---- canPlaceBuilding over a board ---- */
  var placement=[];
  function placementBoard(name,faction,paints){
    editHalf(faction);
    (paints||[]).forEach(function(p){ playerHalf.terrain[p[1]][p[0]]=p[2]; });
    var target=playerHalf.buildings.find(function(b){ return b.type==="temple"; });
    var pairs=[];
    for(var r=0;r<PLAYER_ROWS;r++) for(var c=0;c<COLS;c++){
      neighbors(c,r).forEach(function(nb){
        pairs.push({c0:c,r0:r,c1:nb[0],r1:nb[1],ok:!!canPlaceBuilding(target,[[c,r],[nb[0],nb[1]]])});
      });
    }
    /* Pairs the neighbour walk can never produce: apart, identical, off the half, off the board. */
    [[0,0,4,4],[3,3,3,3],[8,5,0,0],[4,4,4,PLAYER_ROWS],[0,0,-1,0],[COLS-1,2,COLS,2],[2,2,2,3],[1,4,2,4]]
      .forEach(function(p){
        pairs.push({c0:p[0],r0:p[1],c1:p[2],r1:p[3],ok:!!canPlaceBuilding(target,[[p[0],p[1]],[p[2],p[3]]])});
      });
    placement.push({name:name,faction:faction,rows:rowsOf(playerHalf.terrain),
      buildings:bldRows(playerHalf.buildings),moving:"temple",pairs:pairs});
  }
  placementBoard("a fresh Egyptian half","egypt");
  placementBoard("a half with rock and water","egypt",[[3,3,"M"],[4,3,"M"],[0,0,"W"],[1,0,"W"],[8,5,"F"]]);
  placementBoard("a fresh Chinese half","china");

  /* ---- paintTerrain runs ---- */
  var paint=[];
  function paintRun(name,faction,steps,buildings){
    editHalf(faction,buildings);
    var results=steps.map(function(s){
      return {c:s[0],r:s[1],type:s[2],ok:!!paintTerrain(s[0],s[1],s[2])};
    });
    paint.push({name:name,faction:faction,steps:results,rows:rowsOf(playerHalf.terrain),
      buildings:bldRows(playerHalf.buildings),painted:[S.painted.F,S.painted.M,S.painted.W]});
  }
  paintRun("a forest, erased and repainted","egypt",[
    [0,0,"F"],[0,0,"F"],[0,0,"M"],[0,0,"D"],[0,0,"D"],[0,0,"W"]]);
  paintRun("the mountain budget","egypt",
    [[0,0],[1,0],[2,0],[0,1],[1,1],[2,1],[3,1]].map(function(h){ return [h[0],h[1],"M"]; }));
  paintRun("painting onto a building","egypt",[[1,4,"F"],[2,4,"M"],[4,4,"W"],[0,4,"F"]]);
  paintRun("off the half","egypt",[[0,PLAYER_ROWS,"F"],[0,-1,"F"],[0,5,"F"]]);
  paintRun("the Nile runs in a line","egypt",[
    [0,0,"W"],[0,1,"W"],[0,2,"W"],[1,2,"W"],[3,3,"W"],[1,1,"W"]]);
  paintRun("China pools water freely","china",[
    [0,0,"W"],[0,1,"W"],[1,1,"W"],[3,3,"W"],[1,0,"W"]]);
  /* The corner temple's whole ring is three hexes, so the third mountain is the one that would
     wall it in -- with the default layout the ring is eight and the BUDGET refuses first. */
  paintRun("a mountain may not wall a building in","egypt",
    [[1,4,"M"],[0,4,"M"],[2,5,"M"],[2,4,"M"],[2,5,"F"]],
    [{type:"temple",hexes:[[0,5],[1,5]]},{type:"city",hexes:[[4,4],[5,4]]},{type:"fortress",hexes:[[6,2],[7,2]]}]);

  /* ---- the AI's building layouts ---- */
  /* A plain AI half first: the shipped layouts are judged against terrain, and a generated half
     can legitimately put rock or water where layout 0's city stands. */
  aiHalfTerrain=deriveTMAP().slice(0,AI_ROWS).map(function(row){ return [...row]; });
  wholeBoard();
  var layoutChecks=[];
  function layoutCheck(name,layout){
    layoutChecks.push({name:name,valid:!!validAiLayout(layout),complete:!!(layout.city&&layout.temple&&layout.fortress),
      city:pair(layout.city||[[0,0],[0,0]]),temple:pair(layout.temple||[[0,0],[0,0]]),
      fortress:pair(layout.fortress||[[0,0],[0,0]])});
  }
  AI_LAYOUTS.forEach(function(l,i){ layoutCheck("shipped layout "+i,l); });
  layoutCheck("off the AI half",{city:[[4,AI_ROWS],[5,AI_ROWS]],temple:[[1,1],[2,1]],fortress:[[6,3],[7,3]]});
  layoutCheck("off the board",{city:[[COLS-1,1],[COLS,1]],temple:[[1,1],[2,1]],fortress:[[6,3],[7,3]]});
  layoutCheck("two buildings on one hex",{city:[[4,1],[5,1]],temple:[[5,1],[6,1]],fortress:[[6,3],[7,3]]});
  layoutCheck("hexes apart",{city:[[4,1],[6,1]],temple:[[1,1],[2,1]],fortress:[[6,3],[7,3]]});
  layoutCheck("a missing wing",{city:[[4,1],[5,1]],temple:[[1,1],[2,1]]});
  /* On blocking terrain: painted into the live map first, then put back. */
  var keepRow=TMAP[1].slice();
  TMAP[1][4]="W"; layoutCheck("a city in the water",AI_LAYOUTS[0]);
  TMAP[1][4]="M"; layoutCheck("a city on rock",AI_LAYOUTS[0]);
  TMAP[1]=keepRow;

  /* ---- aiTerrainLayoutValid ---- */
  applyAiLayout(AI_LAYOUTS[0]);
  wholeBoard();
  var terrainChecks=[];
  function terrainCheck(name,layout){
    terrainChecks.push({name:name,valid:!!aiTerrainLayoutValid(layout),
      F:hexes(layout.F),M:hexes(layout.M),W:hexes(layout.W)});
  }
  /* The fallback is deliberately NOT valid: it is authored for the rare path that skips the
     validator, and several of its hexes sit beside a building. */
  terrainCheck("the hand-authored fallback",AI_TERRAIN_LAYOUT);
  /* Seeded, so re-exporting does not churn the fixture: the generator itself is verified in Unity
     by its properties (budget, blocked hexes, reachability, determinism), not by this one layout. */
  var seed=12345;
  var realRandom=Math.random;
  Math.random=function(){ seed=(seed*1103515245+12345)%2147483648; return seed/2147483648; };
  var generated=generateAiTerrain();
  Math.random=realRandom;
  terrainCheck("one the generator produced",generated);
  terrainCheck("a hex in row 0",{F:[[0,0],[8,1],[0,3],[8,5],[0,1]],M:AI_TERRAIN_LAYOUT.M,W:AI_TERRAIN_LAYOUT.W});
  terrainCheck("one forest short",{F:AI_TERRAIN_LAYOUT.F.slice(1),M:AI_TERRAIN_LAYOUT.M,W:AI_TERRAIN_LAYOUT.W});
  terrainCheck("a mountain beside the city",{F:AI_TERRAIN_LAYOUT.F,
    M:[[4,2],[5,3],[3,4],[2,4],[3,1],[6,1]],W:AI_TERRAIN_LAYOUT.W});
  terrainCheck("the same hex twice",{F:AI_TERRAIN_LAYOUT.F,M:AI_TERRAIN_LAYOUT.M,
    W:[[0,5],[8,2],[5,5],[6,2],[0,5]]});

  /* ---- composing the two halves ---- */
  var compose=[];
  [["egypt","china"],["china","egypt"],["aztec","egypt"]].forEach(function(pair){
    editHalf(pair[0]);
    S.aiFaction=pair[1];
    playerHalf.terrain[0][0]="M"; playerHalf.terrain[5][8]="W";
    aiHalfTerrain=aiLayoutHalf(AI_TERRAIN_LAYOUT);
    wholeBoard();
    compose.push({player:pair[0],ai:pair[1],
      playerRows:rowsOf(playerHalf.terrain),aiRows:rowsOf(aiHalfTerrain),
      absolute:rowsOf(TMAP),buildings:bldRows(buildings.filter(function(b){ return b.side===0; }))});
  });

  globalThis.__setupReference={source:"godsbound_beta.html",aiRows:AI_ROWS,playerRows:PLAYER_ROWS,cols:COLS,
    budget:Object.keys(TERRAIN_BUDGET).map(function(k){ return {code:k,max:TERRAIN_BUDGET[k]}; }),
    lanes:AI_LANES.map(function(cols){ return {cols:cols}; }),
    fallback:["F","M","W"].map(function(code){ return {code:code,hexes:hexes(AI_TERRAIN_LAYOUT[code])}; }),
    layouts:AI_LAYOUTS.map(function(l){ return {city:pair(l.city),temple:pair(l.temple),fortress:pair(l.fortress)}; }),
    fresh:fresh,placement:placement,paint:paint,layoutChecks:layoutChecks,terrainChecks:terrainChecks,compose:compose};
})();
`;
eval(code);
/*END-DRIVER*/
}
