/* Offline U16–U18 scene inputs, existing art, and browser drag fixtures. */
"use strict";
const {src}=require("../tests/stub_dom.js");
const fs=require("fs"),path=require("path"),crypto=require("crypto");
const self=fs.readFileSync(__filename,"utf8"),begin="/*"+"DRIVER*/",end="/*END-"+"DRIVER*/";
eval(src+self.slice(self.indexOf(begin)+begin.length,self.indexOf(end)));
const root=path.resolve(__dirname,".."),assets=path.join(root,"GodsboundUnity/Godsbound/Assets/Godsbound");
for(const entry of globalThis.__presentation.art){
  const source=path.resolve(root,entry.source);
  if(!source.startsWith(path.join(root,"assets","sprites")+path.sep))throw Error("Art outside sprite directory");
  const dest=path.join(assets,"Resources",entry.resource+".png");
  fs.mkdirSync(path.dirname(dest),{recursive:true});fs.copyFileSync(source,dest);
  const meta=dest+".meta",previous=fs.existsSync(meta)?fs.readFileSync(meta,"utf8"):"";
  const guid=previous.match(/guid: (\w+)/)?.[1]||crypto.createHash("md5").update(entry.resource).digest("hex");
  fs.writeFileSync(meta,`fileFormatVersion: 2
guid: ${guid}
TextureImporter:
  serializedVersion: 13
  internalIDToNameTable: []
  externalObjects: {}
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  spriteMode: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  swizzle: 50462976
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    overridden: 0
`);
}
fs.writeFileSync(path.join(assets,"Resources/GameData/presentation.json"),JSON.stringify(globalThis.__presentation,null,2)+"\n");
fs.writeFileSync(path.join(assets,"Tests/EditMode/Fixtures/deployment_reference.json"),JSON.stringify(globalThis.__deployment,null,2)+"\n");
console.log("Exported "+globalThis.__presentation.art.length+" unit images and "+globalThis.__deployment.cases.length+" drag cases");
if(false){
/*DRIVER*/
code+=`
;(function(){
  startMatch();
  const data={source:"godsbound_beta.html",food:S.food,favor:S.favor,aiFood:S.aiFood,aiFavor:S.aiFavor,anchorDown:0.42,handSize:HAND_SIZE,art:[],
    powerHints:Object.keys(POWER_HINTS).map(k=>({key:k,text:POWER_HINTS[k]}))};
  for(const [key,def] of Object.entries(ALL_UNITS))for(const side of [0,1]){
    const entry=IMG_SPRITES[key],file=typeof entry==="object"?entry[side]:entry;
    if(!file)continue;
    data.art.push({key,side,source:file,resource:"UnitArt/"+key+"_"+side,height:32*unitSpriteScale({def,size:def.size,side})/HEX,mirror:side===1&&typeof entry!=="object"});
  }
  // Verify the anchor against the draw implementation rather than silently drifting.
  const anchor=drawUnitSprite.toString().match(/p.y\\+HEX\\*([0-9.]+)/);
  if(!anchor)throw Error("Sprite anchor expression changed");data.anchorDown=Number(anchor[1]);
  globalThis.__presentation=data;
  const cases=[];
  const sets=[[[7,8],[7,7],[7,6],[7,5]],[[7,8],[7,7],[7,6],[7,8]],[[7,7]],[[7,8],[7,6]],[[7,8],[0,0]],[[7,8],[6,9]]];
  for(let i=0;i<sets.length;i++){
    startMatch();TMAP=Array.from({length:ROWS},()=>Array(COLS).fill("P"));
    const source=buildings.find(b=>b.side===0&&b.type==="fortress");
    S.drag={key:"spear",def:unitDef("spear",0),path:[],spawnBld:source,trainStart:elapsed()};
    for(const h of sets[i]){
      const p=hexCenter(...h),screen=toScreen(p.x,p.y);
      for(const handler of listeners.pointermove)handler({clientX:screen.x,clientY:screen.y,target:{closest:()=>null}});
    }
    cases.push({name:"drag-"+i,source:source.hexes.map(h=>({c:h[0],r:h[1]})),moves:sets[i].map(h=>({c:h[0],r:h[1]})),route:S.drag.path.map(h=>({c:h[0],r:h[1]}))});
  }
  /* U25: the real useCard rotation, with unlocked myths waiting beyond the hand. */
  const rotations=[];
  startMatch();
  const plays=[[0,5,1,0,0,2],[3,3,3,3],[5,0,5,0,5]];
  for(const extra of [[],["ammit"],["ammit","scorpion"]])for(const seq of plays){
    DECK.length=0;DECK.push(...DECK_INITIAL,...extra);
    const start=DECK.slice(),used=[],after=[];
    for(const index of seq){const key=DECK[index];used.push(key);useCard(key);after.push(DECK.join(","));}
    rotations.push({handSize:HAND_SIZE,start,used,after});
  }
  globalThis.__deployment={source:"godsbound_beta.html",cases,rotations};
})();
`;
eval(code);
/*END-DRIVER*/
}
