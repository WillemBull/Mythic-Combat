/* Godsbound — Unity port reference exporter (U0/U1/U3). Node 18+, no deps, offline. */
"use strict";
/*
 * WHY THIS EXISTS. The Unity rebuild has to match the browser game's behaviour exactly,
 * and the failure mode of a hand-typed port is a silently wrong constant that nothing
 * catches until units path through a mountain. So instead of retyping the rules into C#
 * and hoping, this script RUNS the browser game's own functions (neighbors, hexDist,
 * TMAP_INITIAL, TINFO, passable) across the whole board and writes the answers to JSON.
 * The Unity EditMode tests then assert the C# port reproduces that JSON cell for cell.
 *
 * godsbound_beta.html stays the single source of truth. When a rule changes there,
 * re-run this and the Unity tests tell you exactly what drifted.
 *
 * Usage: node tools/export_unity_reference.js
 * Out:   GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/hex_reference.json
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
                    "Tests","EditMode","Fixtures","hex_reference.json");
const ref=globalThis.__godsboundRef;

/* Reshape for Unity's JsonUtility, which cannot read dictionaries, top-level arrays, or
   nested primitive arrays. Everything below is either a flat number array or an array of
   flat serialisable objects -- the two shapes JsonUtility does handle. Keeping the
   fixture parseable by the built-in serialiser is what lets the Unity tests stay
   dependency-free, which the project's offline rule requires. */
const cellIndex=(c,r)=>r*ref.cols+c;
const distMatrix=new Array(ref.cells.length*ref.cells.length);
for(const [c1,r1,c2,r2,d] of ref.distances)
  distMatrix[cellIndex(c1,r1)*ref.cells.length+cellIndex(c2,r2)]=d;

const out={
  generated:ref._generated,
  source:ref._source,
  cols:ref.cols, rows:ref.rows,
  aiRows:ref.aiRows, neutralRows:ref.neutralRows,
  playerRows:ref.playerRows, playerRow0:ref.playerRow0,
  tmapInitial:ref.tmapInitial,
  tiltSquash:ref.tiltSquash,
  hexCapacity:ref.hexCapacity,
  geomHex:ref.geometry.hex, geomOx:ref.geometry.ox, geomOy:ref.geometry.oy,
  /* flat [c,r,x,y, ...] */
  centers:[].concat(...ref.geometry.centers),
  terrain:Object.keys(ref.terrainInfo).map(k=>Object.assign({code:k},ref.terrainInfo[k])),
  terrainPassability:ref.terrainPassability,
  cells:ref.cells,
  /* Flat 117x117 distance matrix, index = cellIndex(a)*cellCount + cellIndex(b),
     cellIndex = r*cols + c. Storing it as a matrix instead of 5-tuples drops the
     fixture from ~430KB to ~45KB while keeping every ordered pair covered. */
  distMatrix
};

fs.mkdirSync(path.dirname(OUT),{recursive:true});
fs.writeFileSync(OUT,JSON.stringify(out));
const kb=(fs.statSync(OUT).size/1024).toFixed(0);
console.log("[unity-ref] wrote "+path.relative(ROOT,OUT)+" ("+kb+" KB)");
console.log("[unity-ref] board "+out.cols+"x"+out.rows+" = "+out.cells.length+" cells");
console.log("[unity-ref] terrain codes: "+out.terrain.map(t=>t.code).join(","));
console.log("[unity-ref] neighbour records: "+out.cells.reduce((n,c)=>n+c.neighbors.length,0));
console.log("[unity-ref] distance matrix entries: "+out.distMatrix.length);
console.log("[unity-ref] per-code passability: "+out.terrainPassability.map(t=>t.code+(t.ground?"+":"-")).join(" "));
if(out.distMatrix.some(v=>v===undefined)) { console.error("[unity-ref] FAIL: distance matrix has holes"); process.exit(1); }

if(false){
/*DRIVER*/
code+=`
;(function(){
  const cols=COLS, rows=TMAP.length;

  /* RESET TO THE PRISTINE MAP FIRST. Loading the game runs its init, which composes
     faction terrain -- including RANDOMIZED AI layouts -- into the live TMAP. Harvesting
     terrain() off that recorded a forest at (0,2) and a mountain at (0,3) on the first
     export, which made two Unity tests fail against a base map that never had them, and
     would have made the fixture differ run to run. The fixture must describe TMAP_INITIAL,
     the map a fresh C# TerrainMap starts from. Caught by the Unity EditMode suite
     2026-09-07 -- leave this reset in place. */
  TMAP = TMAP_INITIAL.map(row=>[...row]);
  const initialMismatch=[];
  for(let r=0;r<rows;r++)for(let c=0;c<cols;c++)
    if(terrain(c,r)!==TMAP_INITIAL[r][c]) initialMismatch.push(c+","+r);
  if(initialMismatch.length) throw new Error("live TMAP still differs from TMAP_INITIAL at "+initialMismatch.join(" "));

  /* Per-cell truth: neighbours, terrain code, ground/flying passability, row ownership. */
  const cells=[];
  for(let r=0;r<rows;r++)for(let c=0;c<cols;c++){
    cells.push({
      c, r,
      terrain: terrain(c,r),
      neighbors: neighbors(c,r).map(h=>({c:h[0],r:h[1]})),
      passableGround: !!passable(c,r,false),
      passableFlying: !!passable(c,r,true),
      neutralRow: !!isNeutralRow(r),
      side: (typeof sideForRow==="function") ? sideForRow(r) : null
    });
  }

  /* hexDist over EVERY ordered pair. 117^2 = 13,689 records is cheap in JSON, and total
     coverage beats a chosen subset -- a hand-port's off-by-one hides on odd columns,
     which is precisely where a curated sample tends not to look. */
  const distances=[];
  for(let r1=0;r1<rows;r1++)for(let c1=0;c1<cols;c1++)
    for(let r2=0;r2<rows;r2++)for(let c2=0;c2<cols;c2++)
      distances.push([c1,r1,c2,r2,hexDist([c1,r1],[c2,r2])]);

  const terrainInfo={};
  for(const code2 of Object.keys(TINFO)){
    const t=TINFO[code2];
    terrainInfo[code2]={name:t.name, speed:t.speed, block:!!t.block,
                        cover:(t.cover===undefined?0:t.cover), highground:!!t.highground,
                        /* fill/fill2 are the board colours renderBG() paints. Presentation
                           data, not simulation -- but exported so the Unity palette is
                           verified against the browser game like every other constant. */
                        fill:t.fill, fill2:t.fill2};
  }

  /* Per-code passability, computed by the game's own passable() by planting each code on
     a scratch hex. The base map only contains P and D, so without this the passability
     fixture would never exercise a mountain -- and blocking terrain is the whole point of
     the rule. Restores the cell afterwards so the harvest above stays pristine. */
  const terrainPassability=[];
  {
    const sc=4, sr=4, keep=TMAP[sr][sc];
    for(const t of Object.keys(TINFO)){
      TMAP[sr][sc]=t;
      terrainPassability.push({code:t, ground:!!passable(sc,sr,false), flying:!!passable(sc,sr,true)});
    }
    TMAP[sr][sc]=keep;
  }

  /* hexCenter depends on the live HEX/OX/OY, so pin them alongside the samples --
     the C# layout test reproduces the centres at these exact values. */
  const centers=[];
  for(let r=0;r<rows;r++)for(let c=0;c<cols;c++){
    const p=hexCenter(c,r);
    centers.push([c,r,+p.x.toFixed(6),+p.y.toFixed(6)]);
  }

  globalThis.__godsboundRef={
    _generated:"tools/export_unity_reference.js — do not hand-edit; re-run to refresh",
    _source:"godsbound_beta.html",
    cols, rows,
    aiRows:AI_ROWS, neutralRows:NEUTRAL_ROWS, playerRows:PLAYER_ROWS, playerRow0:PLAYER_ROW0,
    tmapInitial:TMAP_INITIAL.slice(),
    tiltSquash:TILT_SQUASH,
    hexCapacity:(typeof HEX_CAPACITY!=="undefined")?HEX_CAPACITY:null,
    geometry:{hex:HEX, ox:OX, oy:OY, sq3:SQ3, centers},
    terrainInfo, terrainPassability, cells, distances
  };
})();
`;
eval(code);

/*END-DRIVER*/
}
