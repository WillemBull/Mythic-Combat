/* U22: existing building images and measured browser geometry, offline. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path"),crypto=require("crypto");
const self=fs.readFileSync(__filename,"utf8"),begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const root=path.resolve(__dirname,".."),assets=path.join(root,"GodsboundUnity/Godsbound/Assets/Godsbound");
// Same texture import settings as the already-verified unit art; stable GUIDs survive re-export.
const template=fs.readFileSync(path.join(assets,"Resources/UnitArt/spear_0.png.meta"),"utf8");
for(const entry of globalThis.__buildingArt.art){
  const source=path.resolve(root,entry.source);
  if(!source.startsWith(path.join(root,"assets","sprites")+path.sep))throw Error("Art outside sprite directory");
  const dest=path.join(assets,"Resources",entry.resource+".png");
  fs.mkdirSync(path.dirname(dest),{recursive:true});fs.copyFileSync(source,dest);
  const previous=fs.existsSync(dest+".meta")?fs.readFileSync(dest+".meta","utf8"):"";
  const guid=previous.match(/guid: (\w+)/)?.[1]||crypto.createHash("md5").update(entry.resource).digest("hex");
  fs.writeFileSync(dest+".meta",template.replace(/guid: \w+/,"guid: "+guid));
}
fs.writeFileSync(path.join(assets,"Resources/GameData/building_presentation.json"),JSON.stringify(globalThis.__buildingArt,null,2)+"\n");
fs.writeFileSync(path.join(assets,"Tests/EditMode/Fixtures/building_presentation_reference.json"),JSON.stringify(globalThis.__buildingGeometry,null,2)+"\n");
console.log("Exported "+globalThis.__buildingArt.art.length+" building images and "+globalThis.__buildingGeometry.cases.length+" geometry cases");
if(false){
/*DRIVER*/
code+=String.raw`
;(function(){
  const fn=draw.toString();
  function number(pattern){const m=fn.match(pattern);if(!m)throw Error("Building draw source changed: "+pattern);return Number(m[1]);}
  const data={source:"godsbound_beta.html",canvas:BLD_CANVAS_HEXES,anchorX:BLD_ANCHOR_X,anchorY:BLD_ANCHOR_Y,
    healthGap:number(/\.top - HEX\*([0-9.]+)/),healthWidth:number(/hpBar\(c.x,barY,HEX\*([0-9.]+)/),
    labelInset:number(/\.bottom - HEX\*([0-9.]+)/),labelHeight:number(/lh=HEX\*([0-9.]+)/),art:[]};
  // Use drawBuilding itself to establish mirroring and image dimensions, not a retyped fixture.
  const cases=[];
  getSpriteImage=()=>({complete:true,naturalWidth:480});chromaKeyed=x=>x;
  for(const faction of Object.keys(FACTIONS))for(const type of ["city","temple","fortress"]){
    const key="bld_"+faction+"_"+type,entry=IMG_SPRITES[key];
    if(entry[0]!==entry[1])throw Error("Building art is no longer shared by both sides");
    data.art.push({key,faction,type,source:entry[0],resource:"BuildingArt/"+faction+"_"+type,scale:BLD_SCALE[key]||1});
    for(const side of [0,1])for(const c of [2,3]){
      S.playerFaction=faction;S.aiFaction=faction;
      let flipped=false,drawn=null;
      ctx.scale=(x,y)=>{if(x===-1&&y===1)flipped=true;};
      ctx.drawImage=(image,x,y,w,h)=>{drawn={x,y,w,h};};
      drawBuilding(mkBld(side,type,type,"",[[c,5],[c+1,5]],600),{x:0,y:0});
      if(!drawn)throw Error("Building no longer takes image path");
      const box=bldSpriteBox({x:0,y:0},BLD_SCALE[key]);
      cases.push({faction,type,side,c,mirror:flipped,x:box.x/HEX,y:box.y/HEX,size:box.d/HEX,
        imageSize:drawn.w/HEX,healthY:box.top/HEX-data.healthGap,
        labelY:box.bottom/HEX-data.labelInset+data.labelHeight/2});
    }
  }
  globalThis.__buildingArt=data;globalThis.__buildingGeometry={source:"godsbound_beta.html",cases};
})();
`;
eval(code);
/*END-DRIVER*/
}
