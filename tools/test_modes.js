const {make,ch,centre,sides}=require('./harness.js');
const T=make(require('path').join(__dirname,'Shiftline.plugin.js'));
const re=/^#[0-9A-F]{8}$/;
let clk=1_000_000; const at=(d)=>{clk+=d; return clk;};
const full=a=>a.length===1?'[null]':a.slice(0,5).map(ch).join('')+'|'+a.slice(5,21).map(ch).join('')+'|'+a.slice(21).map(ch).join('');
console.log('scenario count:',T.scenarios.length);
// Manual: every scenario once
T.scenarios.forEach((name,i)=>{
  T.frame({}, {TestMode:'Manual',TestScenario:i,TestRpmPercent:85,TestBrakePercent:0}, at(5000)); // prime edge-triggered state
  const r=T.frame({}, {TestMode:'Manual',TestScenario:i,TestRpmPercent:85,TestBrakePercent:0}, at(1500));
  if(i<3||i>=17) console.log(String(i).padStart(2),name.padEnd(32),full(r));
});
// manual sliders
console.log('--- manual sliders in "Nothing" scenario');
const nothing=T.scenarios.indexOf('RPM and brake (use the sliders)');
for(const [rpm,b] of [[40,0],[85,0],[85,60],[110,0]]) console.log(`rpm ${rpm} brake ${b}`.padEnd(18), full(T.frame({},{TestMode:'Manual',TestScenario:nothing,TestRpmPercent:rpm,TestBrakePercent:b},at(5000))));
// index wrap + invalid
console.log('--- scenario index out of range wraps without error:', full(T.frame({},{TestMode:'Manual',TestScenario:9999},at(100))).length>0, full(T.frame({},{TestMode:'Manual',TestScenario:-3},at(100))).length>0);
// Auto loop validity
let frames=0,bad=0,seen=new Set();
for(let t=0;t<T.scenarios.length*3000+3000;t+=50){
  const r=T.frame({}, {TestMode:'Auto'}, at(50)); frames++;
  if(!(r.length===48)) bad++;
  for(const c of r){ if(c===null) continue; if(!re.test(c)) {bad++; if(bad<5) console.log('bad',c);} else seen.add(c);}
}
console.log({autoFrames:frames,bad,colours:seen.size});
// Test mode Off ignores scenario settings
console.log('mode Off + scenario set:', full(T.frame({Rpms:6800},{TestMode:'Off',TestScenario:5},at(100))));
// fuzz live mode with random config
const opts={RpmStyle:['middle','centreOut','leftRight','rightLeft','f1','zzz',null],RpmColours:['#00FF00,#FF0000','#00FF00,#FFFF00,#FF0000','#112233,#445566,#778899,#AABBCC,#DDEEFF','#00FF00','x',null,'#00FF00,#FF0000,#0000FF,#FFFFFF,#000000,#123456'],IdleColours:['#FF0000,#00FF00','#FF0000,#00FF00,#0000FF','junk',null],RpmColourMode:['blocks','gradient','x'],RpmRedline:['flash','solid','off'],LedFunction:['rpmBrake','rpmOnly','brakeOnly','brakeThrottle','fuel','flags','x',null],FuelColours:['#FF0000,#00FF00','#FF0000,#FFFF00,#00FF00','junk',null],FuelColourMode:['blocks','gradient','x'],BrightnessGeneral:[1,50,100,0,500,'x',null],BrightnessIdle:[1,50,100,-3,'y'],BrakeArea:['both','left','right','top','x',null],BrakeDirection:['outIn','leftRight','rightLeft','inOut','x',null],BrakeSmooth:[true,false],PedalsSmooth:[true,false],FuelSmooth:[true,false],IdleStyle:['wave','sweep','comet','fillDrain','centreOut','solid','colourFade','breathe','bounce','orbit','theatreChase','alternate','wipe','rain','ripple','twinkle','nope'],IdleSpeed:['slow','normal','fast','?'],RpmStartPercent:[0,50,70,95,150,-5,'x'],RpmEndPercent:[10,97,100,200],BrakeBlinkStage1:[0,80,100],BrakeBlinkStage2:[50,100],TestMode:['Off','Off','Off','Manual','Auto']};
let n=0,errs=0;
for(let i=0;i<30000;i++){
  const cfg={}; for(const [k,v] of Object.entries(opts)) if(Math.random()<0.7) cfg[k]=v[Math.floor(Math.random()*v.length)];
  cfg.TestScenario=Math.floor(Math.random()*80);
  const p={Rpms:Math.random()*9000,Brake:Math.random()*100,Throttle:Math.random()*100,FuelPercent:Math.random()*100,'DataCorePlugin.GameRunning':Math.random()<0.15?0:1};
  try{ const r=T.frame(p,cfg,at(41)); n++; for(const c of r){ if(c!==null&&!re.test(c)){errs++;console.log('bad colour',c,JSON.stringify(cfg));break;} } if(!r.length===48) errs++; }catch(e){errs++;console.log('EXC',e.message);break;}
}
console.log({fuzzFrames:n,errs});
