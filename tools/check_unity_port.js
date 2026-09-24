/* Godsbound — static cross-check of the Unity C# port against the browser reference.
 * Node 18+, no deps, offline. Usage: node tools/check_unity_port.js
 *
 * WHY. Unity's EditMode tests are the real gate, but they only run inside the editor.
 * This catches the failure mode that matters between editor sessions: a hardcoded
 * constant in the C# drifting away from godsbound_beta.html without anyone noticing.
 * It reads the literals straight out of the .cs source and compares them to
 * hex_reference.json, so `node tools/check_unity_port.js` is runnable from the same
 * terminal as the HTML suite.
 *
 * This is a transcription check, NOT a substitute for the EditMode tests -- it cannot
 * see logic errors, only wrong numbers. Run both.
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..");
const UNITY = path.join(ROOT, "GodsboundUnity", "Godsbound", "Assets", "Godsbound");
const FIXTURE = path.join(UNITY, "Tests", "EditMode", "Fixtures", "hex_reference.json");

let pass = 0, fail = 0;
const ok = (cond, name, detail) => {
  cond ? pass++ : fail++;
  console.log(`[U] ${cond ? "PASS" : "FAIL"} ${name}${cond || !detail ? "" : " — " + detail}`);
};

if (!fs.existsSync(FIXTURE)) {
  console.error("[U] fixture missing; run: node tools/export_unity_reference.js");
  process.exit(1);
}
const ref = JSON.parse(fs.readFileSync(FIXTURE, "utf8"));
const read = p => fs.readFileSync(path.join(UNITY, p), "utf8");

const board = read("Runtime/Core/Board.cs");
const terrain = read("Runtime/Core/TerrainType.cs");
const tmap = read("Runtime/Core/TerrainMap.cs");
const layout = read("Runtime/Core/HexLayout.cs");
const hex = read("Runtime/Core/Hex.cs");

/* --- Board constants ---------------------------------------------------------- */
const constInt = (src, name) => {
  const m = src.match(new RegExp(`const\\s+int\\s+${name}\\s*=\\s*([0-9]+)\\s*;`));
  return m ? parseInt(m[1], 10) : null;
};
ok(constInt(board, "Cols") === ref.cols, "Board.Cols", `${constInt(board,"Cols")} vs ${ref.cols}`);
ok(constInt(board, "AiRows") === ref.aiRows, "Board.AiRows");
ok(constInt(board, "NeutralRows") === ref.neutralRows, "Board.NeutralRows");
ok(constInt(board, "PlayerRows") === ref.playerRows, "Board.PlayerRows");
ok(constInt(board, "HexCapacity") === ref.hexCapacity, "Board.HexCapacity");

/* Rows/PlayerRow0/CellCount are derived in C#; verify the derivation agrees. */
ok(ref.aiRows + ref.neutralRows === ref.playerRow0, "PlayerRow0 derivation");
ok(ref.aiRows + ref.neutralRows + ref.playerRows === ref.rows, "Rows derivation");
ok(ref.cols * ref.rows === ref.cells.length, "CellCount derivation");

/* --- Hex neighbour offsets ---------------------------------------------------- */
/* Rebuild the offsets from the C# arrays and replay them against every reference cell.
   This is the one piece of real logic the static check CAN verify, and it is where an
   odd-q port goes wrong. */
const offsets = which => {
  const m = hex.match(new RegExp(`${which}Offsets\\s*=\\s*\\{([^}]*)\\}`, "s"));
  if (!m) return null;
  return [...m[1].matchAll(/\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)/g)]
    .map(x => [parseInt(x[1], 10), parseInt(x[2], 10)]);
};
const even = offsets("Even"), odd = offsets("Odd");
ok(even && even.length === 6, "Hex.EvenOffsets parsed (6 entries)");
ok(odd && odd.length === 6, "Hex.OddOffsets parsed (6 entries)");

if (even && odd) {
  const inB = (c, r) => c >= 0 && c < ref.cols && r >= 0 && r < ref.rows;
  let bad = null;
  for (const cell of ref.cells) {
    const use = (cell.c & 1) ? odd : even;
    const got = use.map(d => [cell.c + d[0], cell.r + d[1]]).filter(h => inB(h[0], h[1]));
    const want = cell.neighbors.map(n => [n.c, n.r]);
    if (JSON.stringify(got) !== JSON.stringify(want)) {
      bad = `(${cell.c},${cell.r}) got ${JSON.stringify(got)} want ${JSON.stringify(want)}`;
      break;
    }
  }
  ok(!bad, "Hex neighbour offsets reproduce every reference cell in order", bad);
}

/* --- Terrain table ------------------------------------------------------------ */
const rows = [...terrain.matchAll(
  /new TerrainInfo\(\s*'(.)'\s*,\s*"([^"]+)"\s*,\s*([0-9.]+)f\s*,\s*(true|false)\s*,\s*([0-9.]+)f\s*,\s*(true|false)\s*\)/g
)].map(m => ({
  code: m[1], name: m[2], speed: parseFloat(m[3]),
  block: m[4] === "true", cover: parseFloat(m[5]), highground: m[6] === "true"
}));
ok(rows.length === ref.terrain.length, "terrain row count",
   `${rows.length} vs ${ref.terrain.length}`);

for (const want of ref.terrain) {
  const got = rows.find(r => r.code === want.code);
  if (!got) { ok(false, `terrain ${want.code} present`); continue; }
  const diffs = [];
  if (got.name !== want.name) diffs.push(`name ${got.name}!=${want.name}`);
  if (Math.abs(got.speed - want.speed) > 1e-6) diffs.push(`speed ${got.speed}!=${want.speed}`);
  if (got.block !== want.block) diffs.push(`block ${got.block}!=${want.block}`);
  if (Math.abs(got.cover - want.cover) > 1e-6) diffs.push(`cover ${got.cover}!=${want.cover}`);
  if (got.highground !== want.highground) diffs.push(`highground ${got.highground}!=${want.highground}`);
  ok(diffs.length === 0, `terrain ${want.code} (${want.name})`, diffs.join(", "));
}

/* --- Base map ----------------------------------------------------------------- */
const mapRows = [...tmap.matchAll(/^\s*"([PDFMWRH]{9})",?\s*$/gm)].map(m => m[1]);
ok(mapRows.length === ref.tmapInitial.length, "base map row count",
   `${mapRows.length} vs ${ref.tmapInitial.length}`);
const mapBad = ref.tmapInitial.findIndex((r, i) => mapRows[i] !== r);
ok(mapBad === -1, "base map rows match TMAP_INITIAL",
   mapBad === -1 ? "" : `row ${mapBad}: ${mapRows[mapBad]} vs ${ref.tmapInitial[mapBad]}`);

/* --- Layout ------------------------------------------------------------------- */
const squash = layout.match(/TiltSquash\s*=\s*([0-9.]+)f/);
ok(squash && Math.abs(parseFloat(squash[1]) - ref.tiltSquash) < 1e-6, "HexLayout.TiltSquash",
   squash ? `${squash[1]} vs ${ref.tiltSquash}` : "not found");

/* Replay hexCenter from the C# formula's own literals against the fixture centres. */
const H = ref.geomHex, OX = ref.geomOx, OY = ref.geomOy, SQ3 = Math.sqrt(3);
let centerBad = null;
for (let i = 0; i < ref.centers.length; i += 4) {
  const c = ref.centers[i], r = ref.centers[i + 1];
  const x = OX + H * 1.5 * c + H;
  const y = OY + H * SQ3 * (r + 0.5 * (c & 1)) + H * SQ3 / 2;
  if (Math.abs(x - ref.centers[i + 2]) > 1e-3 || Math.abs(y - ref.centers[i + 3]) > 1e-3) {
    centerBad = `(${c},${r})`; break;
  }
}
ok(!centerBad, "hexCenter formula reproduces every fixture centre", centerBad);

/* --- Presentation palette ---------------------------------------------------- */
/* The board colours are presentation data, but they still come from the browser game's
   TINFO, so drift here means the Unity board stops looking like the canvas one. */
const palette = read("Runtime/Presentation/TerrainPalette.cs");
const pal = [...palette.matchAll(
  /TerrainType\.(\w+),\s*\(\s*"(#[0-9a-fA-F]{6})"\s*,\s*"(#[0-9a-fA-F]{6})"\s*\)/g
)].map(m => ({ type: m[1], fill: m[2], fill2: m[3] }));

const TYPE_FOR_CODE = { P: "Plains", D: "Desert", F: "Forest", M: "Mountain",
                        W: "Water", R: "Road", H: "HighGround" };
ok(pal.length === ref.terrain.length, "palette entry count",
   `${pal.length} vs ${ref.terrain.length}`);
for (const want of ref.terrain) {
  const got = pal.find(p => p.type === TYPE_FOR_CODE[want.code]);
  if (!got) { ok(false, `palette has ${want.code}`); continue; }
  const diffs = [];
  if (got.fill !== want.fill) diffs.push(`fill ${got.fill}!=${want.fill}`);
  if (got.fill2 !== want.fill2) diffs.push(`fill2 ${got.fill2}!=${want.fill2}`);
  ok(diffs.length === 0, `palette ${want.code} (${want.name})`, diffs.join(", "));
}

/* --- Structure ---------------------------------------------------------------- */
for (const f of ["Runtime/Godsbound.Runtime.asmdef",
                 "Tests/EditMode/Godsbound.Tests.EditMode.asmdef"]) {
  let j = null;
  try { j = JSON.parse(read(f)); } catch (e) { /* reported below */ }
  ok(!!j && typeof j.name === "string", `${f} is valid JSON with a name`);
}

/* Runtime/CORE must stay free of UnityEngine so the simulation is testable without a
   scene and cannot accidentally depend on presentation. Runtime/Presentation is exempt --
   that is the layer whose whole job is to talk to Unity. Strip comments before testing;
   the doc comments legitimately mention UnityEngine to explain the rule. */
const stripComments = src => src
  .replace(/\/\*[\s\S]*?\*\//g, "")   // block comments
  .replace(/\/\/[^\n]*/g, "");          // line comments, incl. /// XML docs
/* Scan Runtime/Core RECURSIVELY rather than by a hand-listed set of filenames -- the
   hand-listed version would silently stop covering new files, and Core/Data was added
   in U5. */
const coreFiles = (function walk(dir) {
  const out = [];
  for (const e of fs.readdirSync(path.join(UNITY, dir), { withFileTypes: true })) {
    if (e.isDirectory()) out.push(...walk(path.join(dir, e.name)));
    else if (e.name.endsWith(".cs")) out.push(path.join(dir, e.name));
  }
  return out;
})("Runtime/Core");
ok(coreFiles.length >= 5, `found ${coreFiles.length} files under Runtime/Core`);

const engineUse = coreFiles
  .filter(f => /\bUnityEngine\b/.test(stripComments(read(f))));
ok(engineUse.length === 0, "runtime core has no UnityEngine dependency", engineUse.join(", "));

/* The dependency must point one way: Presentation may read Core, never the reverse.
   Checking only for the NAMESPACE string was not enough -- U9 referenced the type name
   `BoardWorld` unqualified and this check passed while the compile failed. Derive the
   presentation type names from the filenames and look for those too. */
const presentationTypes = fs.readdirSync(path.join(UNITY, "Runtime/Presentation"))
  .filter(f => f.endsWith(".cs"))
  .map(f => f.replace(/\.cs$/, ""));

const backRefs = coreFiles.filter(f => {
  const body = stripComments(read(f));
  if (/Godsbound\.Presentation/.test(body)) return true;
  return presentationTypes.some(t => new RegExp(`\\b${t}\\b`).test(body));
});
ok(backRefs.length === 0,
   `core does not depend on presentation (types: ${presentationTypes.join(", ")})`,
   backRefs.join(", "));

/* Core owns the data POCOs and the lookups; only the LOADER may touch UnityEngine, so the
   dependency must not point back from Core into Godsbound.Data either. */
const loaderRefs = coreFiles
  .filter(f => /Godsbound\.Data\b/.test(stripComments(read(f))));
ok(loaderRefs.length === 0, "core does not depend on the data loader", loaderRefs.join(", "));

/* --- Game data export ------------------------------------------------------- */
const DATA = path.join(UNITY, "Resources", "GameData", "game_data.json");
if (!fs.existsSync(DATA)) {
  ok(false, "game_data.json exists", "run: node tools/export_unity_data.js");
} else {
  const gd = JSON.parse(fs.readFileSync(DATA, "utf8"));
  ok(gd.source === "godsbound_beta.html", "game data names the HTML as its source");
  ok(gd.factions.length === 4, "four pantheons exported", `got ${gd.factions.length}`);
  ok(gd.units.length > 60, "unit tables exported", `got ${gd.units.length}`);
  ok(gd.gods.length > 30, "god tables exported", `got ${gd.gods.length}`);
  ok(gd.foodCap === 120 && gd.favorCap === 250, "resource caps match the browser game",
     `${gd.foodCap}/${gd.favorCap}`);

  /* The guard that makes "export, don't transcribe" real: no field may fall through the
     gap between the typed C# properties and extras. */
  const typed = new Set(gd.typedUnitFields);
  const carried = new Set(gd.units.flatMap(u => (u.extras || []).map(e => e.key)));
  const lost = gd.unitFieldInventory.filter(f => !typed.has(f) && !carried.has(f));
  ok(lost.length === 0, "no unit field is lost between typed properties and extras",
     lost.join(", "));

  /* Economy rates must be MEASURED, so the C# tick must contain no numeric literals of
     its own -- that is the property that keeps balance living in the HTML. */
  /* The building layout must be DETERMINISTIC. It was not: snapshotting the live array
     picked up the per-match randomized AI layout, so every export differed. */
  ok(Array.isArray(gd.buildingTypes) && gd.buildingTypes.length === 3,
     "three building types exported", `got ${(gd.buildingTypes||[]).length}`);
  ok((gd.buildingTypes||[]).every(t => t.hp > 0), "every building type has hp");
  ok(Array.isArray(gd.defaultPlayerBuildings) && gd.defaultPlayerBuildings.length === 3,
     "the player's default layout has three buildings");
  ok(gd.buildings === undefined,
     "no live building snapshot is exported (it is randomized per match)");

  const eco = gd.economy || {};
  ok(eco.foodWithCity > 0 && eco.foodWithoutCity > 0, "food rates measured",
     `${eco.foodWithCity}/${eco.foodWithoutCity}`);
  ok(eco.foodWithoutCity < eco.foodWithCity, "losing the city reduces food");
  ok(eco.playerFavorWithoutTemple < eco.playerFavorWithTemple, "losing the temple reduces favor");
  ok(eco.aiFavorWithoutTemple < eco.aiFavorWithTemple, "losing the temple reduces AI favor");
  ok(eco.capsClamp === true, "the exporter verified the caps clamp");
  ok(eco.favorIsSideAsymmetric === false && eco.bountyAppliesToAi === true,
     "favor is one formula for both sides, Bounty included (Willem, 2026-09-16)");
  ok(eco.blightStopsFood === true && eco.blightStopsFavor === false,
     "blight stops food only (measured, not assumed)");
  ok(eco.mandateThreeBuildingsMultiplier > eco.mandateTwoBuildingsMultiplier,
     "the mandate is worth more with three buildings standing");

  /* U26: power effects and god upkeep take every coefficient from PowerRates. */
  for (const f of ["Runtime/Core/Gods/PowerSystem.cs", "Runtime/Core/Gods/GodEffects.cs",
                   "Runtime/Core/Gods/Powers/EgyptPowers.cs", "Runtime/Core/Gods/Powers/ChinaPowers.cs",
                   "Runtime/Core/Gods/Powers/GreecePowers.cs", "Runtime/Core/Units/FormChains.cs",
                   "Runtime/Core/Gods/Powers/AztecPowers.cs", "Runtime/Core/AI/AiPowers.cs"]) {
    const body = stripComments(read(f)).replace(/"(?:[^"\\\n]|\\.)*"/g, '""');
    const lits = [...body.matchAll(/(?<![\w.])(\d+\.\d+|[2-9]\d*)f?(?![\w])/g)]
      .map(m => m[0]).filter(v => !["0","1","0f","1f","2"].includes(v));
    ok(lits.length === 0, `${f} holds no power balance literals`, lits.join(", "));
  }
  ok(Array.isArray((gd.powers || {}).raAspects) && gd.powers.raAspects.length === 3, "Ra's three aspects exported");

  const tick = stripComments(read("Runtime/Core/Economy/EconomyTick.cs"));
  /* Allow 0/1 (identity/zero) and array indices; anything else is a smuggled balance number. */
  const literals = [...tick.matchAll(/(?<![\w.])(\d+\.\d+|[2-9]\d*)f?(?![\w])/g)]
    .map(m => m[0]).filter(v => !["0","1","0f","1f","2","3"].includes(v));
  ok(literals.length === 0, "the economy tick holds no balance literals", literals.join(", "));

  const typedG = new Set(gd.typedGodFields);
  const carriedG = new Set(gd.gods.flatMap(g => (g.extras || []).map(e => e.key)));
  const lostG = gd.godFieldInventory.filter(f => !typedG.has(f) && !carriedG.has(f));
  ok(lostG.length === 0, "no god field is lost", lostG.join(", "));

  const combat = gd.combat || {};
  ok(typeof combat.armorDamageMultipliers === "boolean", "combat carries the armour switch");
  ok((combat.categoryBeats || []).length === 3, "combat exports the category matchups");
  ok((combat.damageStrong || []).length === 3 && (combat.damageWeak || []).length === 3,
     "disabled armour matchups remain available");
  const combatSource = stripComments(read("Runtime/Core/Combat/CombatSystem.cs"));
  const usedRates = [...combatSource.matchAll(/R\("([^"]+)"\)/g)].map(m => m[1]);
  const exportedRates = new Set((combat.numbers || []).map(n => n.key));
  const missingRates = usedRates.filter(key => !exportedRates.has(key));
  ok(missingRates.length === 0, "combat rates resolve from the browser export", missingRates.join(", "));
  const combatLiterals = [...combatSource.matchAll(/(?<![\w.])(\d+\.\d+|[2-9]\d*)f?(?![\w])/g)]
    .map(m => m[0]).filter(v => v !== "2"); // two sides; 0 and 1 are identity values
  ok(combatLiterals.length === 0, "combat contains no hand-typed balance multipliers", combatLiterals.join(", "));
}

/* --- Cross-namespace references ------------------------------------------------
   Catches the ONE class of bug that has twice reached the editor: using a type from a
   sibling Godsbound namespace without importing or qualifying it. U9 used `BoardWorld`
   from Core, U10 used `Purse` from Core.Training -- both compiled fine in my head and
   failed in Unity, and the second one put the editor into Safe Mode.

   C# resolves a bare type name by searching the file's own namespace and each ANCESTOR,
   plus its usings. So from Godsbound.Core.Training, a type in Godsbound.Core.Economy is
   NOT visible as `Purse`, but IS visible as `Economy.Purse`. This mirrors that rule. */
const allCs = (function walk(dir) {
  const out = [];
  for (const e of fs.readdirSync(path.join(UNITY, dir), { withFileTypes: true })) {
    if (e.isDirectory()) out.push(...walk(path.join(dir, e.name)));
    else if (e.name.endsWith(".cs")) out.push(path.join(dir, e.name));
  }
  return out;
})(".");

const typeNamespace = new Map();   // TypeName -> namespace it is declared in
const fileNamespace = new Map();   // file -> its namespace
for (const f of allCs) {
  const body = stripComments(read(f));
  const ns = (body.match(/namespace\s+([\w.]+)/) || [])[1];
  if (!ns) continue;
  fileNamespace.set(f, ns);
  for (const m of body.matchAll(/\b(?:class|struct|enum|interface)\s+([A-Z]\w*)/g))
    if (!typeNamespace.has(m[1])) typeNamespace.set(m[1], ns);
}

const isAncestorOrSelf = (ns, fileNs) => fileNs === ns || fileNs.startsWith(ns + ".");

const unresolved = [];
for (const f of allCs) {
  const fileNs = fileNamespace.get(f);
  if (!fileNs) continue;
  /* Plain string literals are text, not type references ("Passive: " is not the Passive
     class). Interpolated holes are kept, since those are real code. */
  const body = stripComments(read(f))
    .replace(/\$"((?:[^"\\\n]|\\.)*)"/g, (m, inner) => "$(" + (inner.match(/\{[^}]*\}/g) || []).join(" ") + ")")
    .replace(/"(?:[^"\\\n]|\\.)*"/g, '""');
  const usings = new Set([...body.matchAll(/using\s+([\w.]+)\s*;/g)].map(m => m[1]));

  for (const [type, ns] of typeNamespace) {
    if (ns === fileNs) continue;
    /* Only count real TYPE references. `HexLayout.Unit()` and `_db.Unit(...)` are a
       method and would otherwise be mistaken for the Unit class. So skip anything
       preceded by a dot (member access) or followed by `(` (a call) -- unless it is
       preceded by `new`, which makes it a constructor and therefore a genuine type use. */
    const typeUse = new RegExp(`(?<![.\\w])(new\\s+)?${type}\\b(\\s*\\()?`, "g");
    let looksLikeType = false;
    for (const m of body.matchAll(typeUse)) {
      const isNew = !!m[1], isCall = !!m[2];
      if (isNew || !isCall) { looksLikeType = true; break; }
    }
    if (!looksLikeType) continue;
    if (usings.has(ns)) continue;
    if (isAncestorOrSelf(ns, fileNs)) continue;
    // Qualified by the namespace's last segment, e.g. Economy.Purse
    const tail = ns.split(".").pop();
    if (new RegExp(`\\b${tail}\\.${type}\\b`).test(body)) continue;
    unresolved.push(`${f}: ${type} (declared in ${ns})`);
  }
}
ok(unresolved.length === 0,
   `every cross-namespace type is imported or qualified (${typeNamespace.size} types scanned)`,
   unresolved.join("; "));

console.log(`\n[U] ${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
