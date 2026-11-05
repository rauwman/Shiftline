const vm=require('vm'),fs=require('fs');
function make(file){
  const src=fs.readFileSync(file,'utf8');
  let now=1_000_000, props={};
  const ctx={$prop:k=>props[k],Date:{now:()=>now},Math,Array,Object,JSON,parseInt,isFinite,Number,String,RegExp};
  vm.createContext(ctx); vm.runInContext(src+'\nthis.sl_frame=sl_frame; this.__defaults=SL_DEFAULTS; this.__scen=SL_TEST_SCENARIOS.map(s=>s[0]);',ctx);
  const base={'DataCorePlugin.GameRunning':1,EngineIgnitionOn:1,CarSettings_RedLineRPM:8000,Rpms:0};
  return {ctx,
    frame:(p,cfg,t)=>{ props=Object.assign({},base,p); for(const[k,v] of Object.entries(cfg||{})) props['Shiftline.'+k]=v; if(t!==undefined) now=t; return ctx.sl_frame(); },
    setTime:t=>{now=t}, defaults:ctx.__defaults, scenarios:ctx.__scen};
}
const M={'#FF00FF00':'G','#FFFFFF00':'Y','#FFFF0000':'R','#FF0000FF':'B','#FF000000':'_','#FFFF1493':'S','#FFFFFFFF':'W','#FFFF8C00':'O','#FF00BFFF':'T','#FFFFA500':'A'};
const ch=c=>c===null?'.':(M[c]||'?');
const centre=a=>a.length===1?'[null]':a.slice(5,21).map(ch).join('');
const sides=a=>a.length===1?'[null]':a.slice(0,5).map(ch).join('')+'|'+a.slice(21).map(ch).join('');
module.exports={make,ch,centre,sides,M};
if(require.main===module){
  const T=make(require('path').join(__dirname,'Shiftline.plugin.js'));
  console.log('== defaults (no plugin props): rpm 50/72/85/95/99.9 %');
  console.log([0.5,0.72,0.85,0.95,0.999].map(x=>centre(T.frame({Rpms:8000*x},{},6000))).join('  '));
  console.log('== all RPM styles (plugin props)');
  for(const st of ['middle','centreOut','leftRight','rightLeft','f1']) console.log(st.padEnd(10),[0.72,0.85,0.95,0.999].map(x=>centre(T.frame({Rpms:8000*x},{RpmStyle:st},6000))).join('  '));
  console.log('== colour count / mode (leftRight, rpm 99.9 %)');
  const lists={'2':'#00FF00,#FF0000','3':'#00FF00,#FFFF00,#FF0000','5':'#00FF00,#80FF00,#FFFF00,#FF8000,#FF0000'};
  for(const [n,m] of [['2','blocks'],['3','blocks'],['5','blocks'],['3','gradient'],['5','gradient']]){
    const r=T.frame({Rpms:7990},{RpmStyle:'leftRight',RpmColours:lists[n],RpmColourMode:m},6000).slice(5,21);
    console.log(`${n} ${m}`.padEnd(12), r.map(c=>c===null?'------':c.slice(3)).join(' '));
  }
  console.log('== redline modes @ 101 % (middle)');
  for(const m of ['flash','solid','off']) console.log(m.padEnd(6),centre(T.frame({Rpms:8100},{RpmRedline:m},6000)),' / other phase',centre(T.frame({Rpms:8100},{RpmRedline:m},6030)));
  console.log('== brake styles (pedal 0/40/70/100 %), rpm 6000');
  for(const b of ['sides','centre-left-right','centre-right-left','centre-outside-in','centre-middle-out']){
    console.log(b.padEnd(19),[0,40,70,100].map(p=>{const r=T.frame({Rpms:6000,Brake:p},(b==='sides'?{}:{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:{'centre-left-right':'leftRight','centre-right-left':'rightLeft','centre-outside-in':'outIn','centre-middle-out':'inOut'}[b]}),6000);return b==='sides'?sides(r):centre(r)}).join('  '));
  }
  console.log('== custom colours / thresholds');
  const r1=T.frame({Brake:60},{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:'leftRight',BrakeColour:'#0000FF',BrakeBlinkStage1:50},6000); console.log(centre(r1), centre(T.frame({Brake:60},{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:'leftRight',BrakeColour:'#0000FF',BrakeBlinkStage1:50},6100)));
  console.log('== invalid inputs fall back to defaults');
  console.log(centre(T.frame({Rpms:7900},{RpmColours:'banana',RpmStartPercent:'abc',RpmStyle:''},6000)));
  console.log('== led functions'); for(const f of ['rpmBrake','rpmOnly','brakeOnly','brakeThrottle','fuel','flags']) console.log(f.padEnd(14),centre(T.frame({Rpms:6800,Throttle:50,Brake:60,FuelPercent:60},{LedFunction:f},6000)));
}
