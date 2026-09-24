/* U13: actual browser match results, consumed by Unity EditMode tests.
 *
 * The win comparison is short enough to retype and precisely the kind of thing that gets
 * retyped WRONG in the same way twice -- an inverted sign or a mis-ordered tiebreaker reads
 * fine in both the code and a hand-written expectation. So this drives the browser's real
 * endMatch() across a matrix of end states and records what the end screen actually said.
 *
 * Out: GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/objectives_reference.json
 */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path");
const self=fs.readFileSync(__filename,"utf8");
const begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
fs.writeFileSync(path.join(__dirname,"../GodsboundUnity/Godsbound/Assets/Godsbound/Tests/EditMode/Fixtures/objectives_reference.json"),
  JSON.stringify(globalThis.__objectivesReference,null,1));
console.log("[objectives-ref] exported "+globalThis.__objectivesReference.cases.length+" browser cases"+
  ", matchSeconds="+globalThis.__objectivesReference.matchSeconds);
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  var cases=[];

  /* endMatch() hands a Legends level or the drill their own ending before the normal
     comparison runs. Neither is part of a standard match, so both are cleared per case. */
  function reset(pKills,aKills,dmg0,dmg1){
    S.over=false; campaign=null; drill=null;
    S.bldDmg=[dmg0,dmg1];
    buildings.length=0;
    // Three buildings a side, exactly as a real match has. Only .side and .dead matter to
    // the comparison, but they are built through the same shape the rest of the code expects.
    var types=["city","temple","fortress"];
    for(var side=0;side<2;side++){
      var kills = side===1 ? pKills : aKills; // pKills counts DESTROYED side-1 buildings
      for(var i=0;i<3;i++){
        var r = side===1 ? 1 : ROWS-2;
        buildings.push({side:side,type:types[i],name:types[i],hexes:[[i*2,r],[i*2+1,r]],
                        hp:i<kills?0:100,maxhp:100,dead:i<kills});
      }
    }
    rebuildBldHexMap();
  }

  function capture(name,pKills,aKills,dmg0,dmg1,atSeconds){
    reset(pKills,aKills,dmg0,dmg1);
    S.t = 180 - atSeconds;                 // browser clock counts DOWN; elapsed() is 180 - S.t
    $("endtitle").textContent="";
    endMatch();
    var title=$("endtitle").textContent;
    // Derived FROM the rendered title, never asserted alongside it -- if endMatch's own
    // wording ever changes, this exporter fails loudly instead of silently disagreeing.
    var outcome = title.indexOf("VICTORY")>=0 ? 1 : title.indexOf("DEFEAT")>=0 ? -1
                : title.indexOf("DRAW")>=0 ? 0 : null;
    if(outcome===null) throw new Error("unrecognised end title for case "+name+": "+JSON.stringify(title));
    cases.push({
      name:name,
      playerBuildingsDestroyed:pKills, enemyBuildingsDestroyed:aKills,
      playerBuildingDamage:dmg0, enemyBuildingDamage:dmg1,
      atSeconds:atSeconds, instant:(pKills===3||aKills===3),
      outcome:outcome, title:title, detail:$("enddetail").textContent
    });
  }

  /* ---- every branch of the comparison, in both directions ---- */
  // All three destroyed: the instant win, and the same comparison decides it.
  capture("player wipes the enemy",            3,0, 900,  100, 92);
  capture("enemy wipes the player",            0,3, 100,  900, 77);
  capture("both wiped, equal damage",          3,3, 500,  500, 150);
  // Buildings destroyed decides before damage, even when damage disagrees.
  capture("one building ahead beats damage",   1,0, 10,   9999, 180);
  capture("one building behind loses",         0,1, 9999, 10,   180);
  capture("two to one",                        2,1, 0,    0,    180);
  // Level on buildings: damage is the tiebreaker.
  capture("level, player damage ahead",        1,1, 600,  599,  180);
  capture("level, enemy damage ahead",         1,1, 599,  600,  180);
  capture("no kills, player damage ahead",     0,0, 1,    0,    180);
  capture("no kills, enemy damage ahead",      0,0, 0,    1,    180);
  // Fractional damage: the comparison is not integer-rounded, though the readout is.
  capture("fractional damage decides",         0,0, 10.5, 10.4, 180);
  capture("fractional damage decides, other way", 0,0, 10.4, 10.5, 180);
  // Dead level on both measures.
  capture("nothing happened at all",           0,0, 0,    0,    180);
  capture("identical damage, no kills",        0,0, 250,  250,  180);

  globalThis.__objectivesReference={
    generated:new Date().toISOString(),
    source:"godsbound_beta.html endMatch()",
    matchSeconds:180,        // read back below from the live clock, not trusted as typed
    cases:cases
  };
  // Prove the 180 above is the game's own number rather than a constant retyped here.
  startMatch();
  globalThis.__objectivesReference.matchSeconds=S.t;
})();
`;
eval(code);

/*END-DRIVER*/
}
