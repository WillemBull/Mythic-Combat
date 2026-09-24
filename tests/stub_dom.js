// Shared headless DOM/canvas stub for all Godsbound test suites (Node 18+, no deps).
// Added by roadmap step H.1 -- this replaces the ~35KB of copy-pasted stub that used to
// live at the top of every tests/test_*.js file. It is exported as a SOURCE STRING (not
// live objects) because each suite must eval() the stub, the extracted game script, and
// its own driver test code together in ONE shared scope -- that's the only way the
// driver's bare identifiers (DECK, S, hexCenter, ...) can see the game's top-level
// let/const bindings.
//
// Usage in a suite file (see any tests/test_*.js for a real example):
//   "use strict";
//   const {src}=require("./stub_dom.js");
//   const fs=require("fs");
//   const __self=fs.readFileSync(__filename,"utf8");
//   const driver=__self.slice(__self.indexOf(DRIVER_START)+DRIVER_START.length, __self.indexOf(DRIVER_END));
//   eval(src+driver);
//   if(false){
//   DRIVER_START (as a comment)
//   ...the suite's actual test code, unchanged...
//   DRIVER_END (as a comment)
//   }
module.exports.src = `
const els={};
function mkEl(id){
  const el={id,children:[],_cls:new Set(),_html:"",textContent:"",style:{},dataset:{},
    classList:{add:(...c)=>c.forEach(x=>el._cls.add(x)),remove:(...c)=>c.forEach(x=>el._cls.delete(x)),
      toggle:(c,v)=>{if(v===undefined)v=!el._cls.has(c);v?el._cls.add(c):el._cls.delete(c);},contains:c=>el._cls.has(c)},
    appendChild(ch){el.children.push(ch);return ch;},closest(){return null;},
    getBoundingClientRect(){return{left:0,top:0,right:400,bottom:700,width:400,height:700};},
    getContext(){return ctxStub();},addEventListener(){},clientWidth:400,clientHeight:700};
  Object.defineProperty(el,"innerHTML",{get(){return el._html;},set(v){el._html=v;
    const re=/id="([^"]+)"/g;let m;while((m=re.exec(v)))els[m[1]]=mkEl(m[1]);}});
  return el;
}
function ctxStub(){return new Proxy({},{get(t,p){if(p==="canvas")return null;if(!(p in t))t[p]=()=>{};return t[p];},set(t,p,v){t[p]=v;return true;}});}
const listeners={};
let canvasCreateCount=0; // counts document.createElement calls (used by test_sprites.js to verify chromaKeyed()'s per-cache-key caching)
const document={getElementById(id){if(!els[id])els[id]=mkEl(id);return els[id];},
  createElement(){canvasCreateCount++;return mkEl("dyn"+Math.random());},querySelectorAll(){return [];},
  addEventListener(t,f){(listeners[t]=listeners[t]||[]).push(f);}};
let rafCb=null,simNow=0;
const window={addEventListener(){},devicePixelRatio:1};
const stored={};
const localStorage={getItem:k=>stored[k]??null,setItem:(k,v)=>{stored[k]=String(v);},removeItem:k=>{delete stored[k];}};
const location={search:""}; // added H.1, for H.2's ?gallery debug view
const performance={now:()=>simNow};
function requestAnimationFrame(cb){rafCb=cb;}
function setTimeout(){return 0;} function clearTimeout(){}
/* Image is a browser-only global (used by getSpriteImage to decode base64 sprites).
   Stub it as a synchronous "always loads instantly" fake -- real browsers decode
   asynchronously, but for testing the drawImage-vs-vector branch we just need
   complete/naturalWidth to look "loaded" right after src is set. */
class Image {
  constructor(){ this.complete=false; this.naturalWidth=0; this._src=""; }
  set src(v){ this._src=v; this.complete=true; this.naturalWidth=100; }
  get src(){ return this._src; }
}
const fs=require("fs");
const path=require("path");
const html=fs.readFileSync(path.join(__dirname,"..","godsbound_beta.html"),"utf8");
const rawHtml=html; // alias kept for one suite (test_passives.js) that reads the raw source under this name
let code=html.substring(html.indexOf("<script>")+8, html.lastIndexOf("</script>"));
`;
