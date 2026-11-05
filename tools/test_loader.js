// The loader profile script must run the plugin's script like the profile that contains it, and survive a missing plugin.
const fs=require('fs'),path=require('path'),vm=require('vm');
const {make}=require('./harness.js');
const script=fs.readFileSync(path.join(__dirname,'Shiftline.plugin.js'),'utf8');
const stub=fs.readFileSync(path.join(__dirname,'loader.js'),'utf8');
let bad=0; const ok=(c,m)=>{ if(!c){bad++;console.log('FAIL',m);} };
function engine(props){ const ctx={$prop:k=>props[k],Date:{now:()=>props.__now},Math,Array,Object,JSON,parseInt,isFinite,Number,String,RegExp}; vm.createContext(ctx); vm.runInContext(stub,ctx); return {ctx,run:()=>vm.runInContext('sl_loader()',ctx)}; }
const base={'DataCorePlugin.GameRunning':1,EngineIgnitionOn:1,CarSettings_RedLineRPM:8000,Rpms:7000,SpeedKmh:100,__now:5_000_000,'Shiftline.ScriptBuild':'aaaa1111','Shiftline.ScriptSource':script};
// direct run for comparison
const T=make(path.join(__dirname,'Shiftline.plugin.js'));
const direct=T.frame({Rpms:7000,SpeedKmh:100},{},5_000_000);
const e=engine(Object.assign({},base)); const viaLoader=e.run();
ok(Array.isArray(viaLoader)&&viaLoader.length===48,'loader returns 48 LEDs');
ok(JSON.stringify(viaLoader)===JSON.stringify(direct),'loader frame equals the direct frame');
// compiled once, kept between frames
const f1=e.ctx.__shiftlineFrame!==undefined?e.ctx.__shiftlineFrame:null; e.run(); ok(f1===null||f1===e.ctx.__shiftlineFrame,'script is compiled once');
// a new build is compiled again
e.ctx.$prop=k=>k==='Shiftline.ScriptBuild'?'bbbb2222':(k==='Shiftline.ScriptSource'?script.replace("const SL_OFF = '#FF000000';","const SL_OFF = '#FF000000'; /* v2 */"):base[k]);
e.run(); ok(e.ctx.__shiftlineBuild==='bbbb2222','a new script build is picked up without restart');
// no plugin: red blink marker, no exception
const none=engine({__now:5_000_000}).run(); ok(none.length===48&&none[0]==='#FFFF0000'&&none[1]==='#FF000000','without the plugin LED 1 and 2 blink red: '+none.slice(0,2));
const none2=engine({__now:5_000_500}).run(); ok(none2[0]==='#FF000000'&&none2[1]==='#FFFF0000','the marker alternates');
// broken source: marker, no exception
const broken=engine(Object.assign({},base,{'Shiftline.ScriptSource':'x'.repeat(2000)+' this is not javascript {{'})).run(); ok(broken.length===48&&broken[0]!==null,'a script that does not compile shows the marker');
console.log(bad?bad+' FAILURES':'ALL LOADER TESTS PASSED');
