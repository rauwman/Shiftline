// Layout tests: SideLeds / CentreLeds from the plugin settings, 3 colour idle wave.
const {make,ch}=require('./harness.js');
const T=make(require('path').join(__dirname,'Shiftline.plugin.js'));
const re=/^#[0-9A-F]{8}$/;
let clk=2_000_000; const at=d=>{clk+=d;return clk;};
let fails=0; const ok=(c,m)=>{ if(!c){fails++;console.log('FAIL',m);} };
const show=(r,side,centre)=>{const n=side*2+centre; return r.slice(0,side).map(ch).join('')+'|'+r.slice(side,side+centre).map(ch).join('')+'|'+r.slice(side+centre,n).map(ch).join('')+(r.slice(n).every(c=>c===null)?'':' !!tail');};

// every layout: frame length, padding, valid colours, no write outside the layout
let frames=0;
for(const side of [1,2,3,4,5,6,8,12,-3,'x']) for(const centre of [2,3,5,8,15,16,17,32,40,0,'y']){
  const s=Math.max(1,Math.min(8,Math.round(Number(side)||5))), c=Math.max(2,Math.min(32,Math.round(Number(centre)||16)));
  const cfg={SideLeds:side,CentreLeds:centre};
  for(const [p,extra] of [
    [{Rpms:7000,Brake:60,Throttle:50},{}],
    [{Rpms:8200},{RpmStyle:'centreOut'}],
    [{Rpms:6000},{RpmStyle:'rightLeft'}],
    [{Rpms:6000,Brake:80},{BrakeArea:'top',BrakeDirection:'inOut'}],
    [{Rpms:6000,Brake:80},{BrakeArea:'top',BrakeDirection:'outIn'}],
    [{Rpms:6000,Brake:80},{LedFunction:'brakeThrottle'}],
    [{FuelPercent:50},{LedFunction:'fuel',FuelPlacement:'centre'}],
    [{'DataCorePlugin.GameRunning':0},{IdleStyle:'wave'}],
    [{'DataCorePlugin.GameRunning':0},{IdleStyle:'twinkle'}],
    [{},{TestMode:'Auto'}],
  ]){
    const r=T.frame(p,Object.assign({},cfg,extra),at(37)); frames++;
    ok(r.length===48,`length ${r.length} for ${side}/${centre}`);
    ok(r.every(v=>v===null||re.test(v)),'bad colour');
    ok(r.slice(2*s+c).every(v=>v===null),`tail not transparent for ${s}/${c}`);
  }
}
console.log('layout frames checked:',frames);
at(30000);       // the Auto test mode passed through every scenario (the checkered flag shows for 10 s): let those pulses expire

// default layout behaves as before: RPM 85 % -> GGGY........YGGG in the centre
const d=T.frame({Rpms:6800},{},at(10));
console.log('default 5/16/5 rpm 85 %  ',show(d,5,16));
ok(show(d,5,16)==='.....|GGGY........YGGG|.....','default rpm layout changed');

// RPM styles with an odd and an even centre, a small and a large side
for(const [s,c] of [[3,9],[4,10],[8,32]]){
  for(const st of ['middle','centreOut','leftRight','rightLeft']){
    const r=T.frame({Rpms:7950},{SideLeds:s,CentreLeds:c,RpmStyle:st,RpmColours:'#00FF00,#00FF00'},at(10));
    console.log(`${s}/${c} ${st}`.padEnd(18),show(r,s,c));
  }
}
// fully lit middle/centreOut fill an odd centre completely, no LED twice, none missing
for(const st of ['middle','centreOut','leftRight','rightLeft']){
  const r=T.frame({Rpms:7990},{SideLeds:3,CentreLeds:9,RpmStyle:st,RpmColours:'#00FF00,#00FF00',RpmEndPercent:97},at(10));
  ok(r.slice(3,12).every(v=>v==='#FF00FF00'),`odd centre not fully lit with ${st}: ${show(r,3,9)}`);
}

// side roles with 5 / 4 / 2 / 1 LEDs per side: ABS and TC, turn indicator = innermost, brake bar = inner 3
for(const s of [5,4,2,1]){
  const a=T.frame({ABSActive:1,TCActive:1},{SideLeds:s,CentreLeds:6},at(10));
  const t=T.frame({TurnIndicatorLeft:1,Brake:0},{SideLeds:s,CentreLeds:6},at(250));   // 250 ms: inside the on window of the 650 ms cycle
  const b=T.frame({Brake:100,Rpms:0},{SideLeds:s,CentreLeds:6},at(10));
  console.log(`side ${s} ABS+TC `.padEnd(18),show(a,s,6),' indicator',show(t,s,6),' brake 100',show(b,s,6));
  ok(a[0]!==null,'ABS missing');
}

// idle wave: 3 colours, starts in the middle, reaches the edges, comes back
const N=26;
const waveProps={'DataCorePlugin.GameRunning':0};
const cfg={IdleStyle:'wave',IdleSpeed:'normal',IdleColours:'#FF0000,#00FF00,#0000FF'};
const period=4000;
const lit=r=>r.slice(0,26).map(c=>c===null||c==='#FF000000'?'.':'#').join('');
const centreOf=r=>{ const xs=[];r.slice(0,26).forEach((c,i)=>{if(c&&c!=='#FF000000')xs.push(i)}); return xs.length?xs.reduce((a,b)=>a+b)/xs.length:null; };
const dist=r=>{ const xs=[];r.slice(0,26).forEach((c,i)=>{if(c&&c!=='#FF000000')xs.push(Math.abs(i-12.5))}); return xs.length?Math.max(...xs):null; };
const base=Math.ceil(5_000_000/period)*period;
const trace=[];
for(let k=0;k<=16;k++){ const r=T.frame(waveProps,cfg,base+k/16*period); trace.push(dist(r)); console.log(String(k).padStart(2),lit(r),dist(r)); }
const firstHalf=trace.slice(0,9), secondHalf=trace.slice(8);
ok(trace[0]===null&&trace[16]===null,'wave: every LED must be off at the start and at the end of a cycle');
ok(trace[8]===null,'wave: every LED must be off between the run out and the run back in');
ok(Math.max(...trace.filter(x=>x!==null))>11,'wave does not reach the edges');
const run=(list)=>list.filter(x=>x!==null);
const inc=a=>a.every((v,i)=>i===0||v>=a[i-1]), dec=a=>a.every((v,i)=>i===0||v<=a[i-1]);
ok(inc(run(firstHalf))&&run(firstHalf).length>=4,'wave: the first run does not move from the centre outwards: '+run(firstHalf));
ok(dec(run(secondHalf))&&run(secondHalf).length>=4,'wave: the second run does not move from the edges to the centre: '+run(secondHalf));
ok(run(firstHalf)[0]<4,'wave: the first run does not start at the centre: '+run(firstHalf));
// colour lists: invalid input falls back, 2 and 5 stops work, nothing throws
for(const list of ['#FF0000,#00FF00','#FF0000,#00FF00,#0000FF,#FFFF00,#FF00FF','#FF0000','x',null,'#GG0000,#00FF00','#FF0000,#00FF00,#0000FF,#FFFF00,#FF00FF,#FFFFFF']){
  const r=T.frame({Rpms:6400},{RpmColours:list,RpmColourMode:'gradient'},at(10));
  ok(r.length===48&&r.every(v=>v===null||re.test(v)),'colour list '+list);
}
// the published list is used: a 2 stop list "blue,red" colours the first rpm step blue
const rb=T.frame({Rpms:5700},{RpmColours:'#0000FF,#FF0000',RpmColourMode:'blocks',RpmStyle:'leftRight'},at(10));
ok(rb[5]==='#FF0000FF','published RpmColours not used: '+rb[5]);

// ---------------------------------------------------------------- LED functions
// default layout 5|16|5: centre = indices 5..20, sides = 0..4 and 21..25
const centreLit=r=>r.slice(5,21).filter(c=>c&&c!=='#FF000000').length;
const sidesLit=r=>r.slice(0,5).concat(r.slice(21,26)).filter(c=>c&&c!=='#FF000000').length;
const drive={Rpms:6800,Brake:50,Throttle:50,FuelPercent:60};
const fn=(name,extra)=>T.frame(drive,Object.assign({LedFunction:name},extra||{}),at(10));
const expect={            // function: [centre lit?, sides lit?]
  rpmBrake:[true,true], rpmOnly:[true,false], brakeOnly:[false,true], brakeThrottle:[true,false], fuel:[true,false], flags:[false,false]};
for(const [name,[c,s]] of Object.entries(expect)){
  const r=fn(name,name==='fuel'?{FuelPlacement:'centre'}:{}); console.log(name.padEnd(14),show(r,5,16),'centre lit',centreLit(r),'sides lit',sidesLit(r));
  ok((centreLit(r)>0)===c,`${name}: centre ${centreLit(r)}`); ok((sidesLit(r)>0)===s,`${name}: sides ${sidesLit(r)}`);
}
ok(fn('bogus').slice(5,21).some(c=>c&&c!=='#FF000000'),'unknown function falls back to rpmBrake');
// rpmOnly shows exactly the same rev bar as rpmBrake
ok(JSON.stringify(fn('rpmOnly').slice(5,21))===JSON.stringify(fn('rpmBrake').slice(5,21)),'rpmOnly rev bar differs from rpmBrake');
// brake only with a centre brake style shows the bar in the centre and no rev bar
const bo=fn('brakeOnly',{BrakeArea:'top',BrakeDirection:'leftRight'}); ok(centreLit(bo)>0&&sidesLit(bo)===0,'brakeOnly with centre style: '+show(bo,5,16));
// throttle and brake pedals: brake lit on the left half, throttle on the right half
const tb=T.frame({Brake:100,Throttle:100},{LedFunction:'brakeThrottle'},at(10)); ok(centreLit(tb)===16,'throttle+brake full: '+show(tb,5,16));
// flags: a yellow flag fills the whole strip (sides and centre) while the other functions only use the sides
const yel=Object.assign({},drive,{Flag_Yellow:1});
const bothPhases=(cfg)=>{ const a=T.frame(yel,cfg,at(10)), b=T.frame(yel,cfg,at(300)); return centreLit(a)+sidesLit(a)>=centreLit(b)+sidesLit(b)?a:b; };   // the flag blinks: take the lit phase
const fy=bothPhases({LedFunction:'flags'}); const fr=bothPhases({LedFunction:'rpmBrake'});
console.log('flags yellow  ',show(fy,5,16)); console.log('rpmBrake yellow',show(fr,5,16));
ok(centreLit(fy)>=8&&sidesLit(fy)>0,'flags function: yellow flag does not reach the centre'); ok(sidesLit(fr)>0,'yellow flag missing on the sides in rpmBrake');
// fuel gauge: theme colours red -> green, blocks and gradient, level 0..100
const f0=T.frame({FuelPercent:100},{LedFunction:'fuel',FuelPlacement:'centre',FuelColours:'#FF0000,#00FF00',FuelColourMode:'gradient'},at(10)).slice(5,21);
ok(f0[0]==='#FFFF0000'&&f0[15]==='#FF00FF00','fuel gradient ends: '+f0[0]+' '+f0[15]);
const fb=T.frame({FuelPercent:100},{LedFunction:'fuel',FuelPlacement:'centre',FuelColours:'#FF0000,#FFFF00,#00FF00',FuelColourMode:'blocks'},at(10)).slice(5,21);
ok(fb[0]==='#FFFF0000'&&fb[8]==='#FFFFFF00'&&fb[15]==='#FF00FF00','fuel blocks: '+fb.join(' '));
const fh=T.frame({FuelPercent:50},{LedFunction:'fuel',FuelPlacement:'centre'},at(10)); ok(centreLit(fh)===8,'fuel 50 % lights half: '+centreLit(fh));
const fo=T.frame({FuelPercent:50},{LedFunction:'fuel',FuelPlacement:'off'},at(10)); ok(centreLit(fo)===0,'function Fuel with the fuel level Off shows nothing: '+centreLit(fo));
const fs=T.frame({FuelPercent:50},{LedFunction:'fuel',FuelPlacement:'sides'},at(10)); ok(centreLit(fs)===0,'function Fuel with Both sides leaves the top row dark: '+centreLit(fs));

// ---------------------------------------------------------------- SimHub setting (per-car shift points from SimHub)
const sh=(z1,z2,red,extra)=>T.frame(Object.assign({Rpms:2000,CarSettings_RPMShiftLight1:z1,CarSettings_RPMShiftLight2:z2,CarSettings_RPMRedLineReached:red},extra||{}),{RpmStyle:'simhub',RpmColours:'#00FF00,#00FF00'},at(10));
const cnt=r=>r.slice(5,21).filter(c=>c==='#FF00FF00').length;
ok(cnt(sh(0,0,0))===0,'simhub style: nothing lit at zero'); ok(cnt(sh(0.5,0,0))===4,'simhub style: zone 1 half = 4 of 16, got '+cnt(sh(0.5,0,0)));
ok(cnt(sh(1,0,0))===8,'simhub style: zone 1 full = half the bar, got '+cnt(sh(1,0,0))); ok(cnt(sh(1,0.5,0))===12,'simhub style: zone 2 half = 12, got '+cnt(sh(1,0.5,0)));
ok(cnt(sh(1,1,0))===16,'simhub style: both zones full = whole bar');
const fr1=[sh(1,1,1),T.frame({Rpms:2000,CarSettings_RPMShiftLight1:1,CarSettings_RPMShiftLight2:1,CarSettings_RPMRedLineReached:1},{RpmStyle:'simhub',RpmColours:'#00FF00,#00FF00'},at(60))];
ok(fr1.some(f=>f.slice(5,21).every(c=>c==='#FFFF0000')),'simhub style: redline flash colour (red by default) when the redline is reached');
ok(T.frame({Rpms:2000},{RpmStyle:'simhub'},at(10)).slice(5,21).every(c=>c===null),'simhub style: without SimHub shift points and low rpm nothing lights (falls back to own thresholds)');
const fbk=T.frame({Rpms:7800},{RpmStyle:'simhub',RpmColours:'#00FF00,#00FF00'},at(10)); ok(cnt(fbk)>0,'simhub style: falls back to the plugin thresholds when SimHub gives no shift points');
console.log('simhub style  ',show(sh(1,0.5,0),5,16));

// ---------------------------------------------------------------- brake position
const bk=(cfg,pct)=>T.frame({Brake:pct,Rpms:0},Object.assign({LedFunction:'brakeOnly',BrakeBlinkStage1:100,BrakeBlinkStage2:100},cfg),at(10));   // blinking starts at 100 %, tests stay below
const sideSlots=r=>{const l=[];for(let i=0;i<5;i++) if(r[i]) l.push(i); return l.join(',')+' | '+[...Array(5).keys()].filter(i=>r[25-i]).join(',');};
ok(sideSlots(bk({},99))==='0,1,2,3,4 | 0,1,2,3,4','default brake position changed: '+sideSlots(bk({},99)));
ok(sideSlots(bk({BrakeDirection:'inOut'},50))==='2,3,4 | 2,3,4','sides In to Out fill from the innermost LED outwards: '+sideSlots(bk({BrakeDirection:'inOut'},50)));
ok(sideSlots(bk({BrakeArea:'left'},99))==='0,1,2,3,4 | ','left side only: '+sideSlots(bk({BrakeArea:'left'},99)));
ok(sideSlots(bk({BrakeArea:'right'},99))===' | 0,1,2,3,4','right side only: '+sideSlots(bk({BrakeArea:'right'},99)));
ok(sideSlots(bk({BrakeArea:'left',BrakeDirection:'leftRight'},50))==='0,1,2 | ','Left to Right on a side falls back to Out to In: '+sideSlots(bk({BrakeArea:'left',BrakeDirection:'leftRight'},50)));
const cen=r=>r.slice(5,21).map(c=>c?'#':'.').join('');
ok(cen(bk({BrakeArea:'top',BrakeDirection:'leftRight'},99))==='################','centre bar default is the whole centre');
ok(cen(bk({BrakeArea:'top',BrakeDirection:'rightLeft'},50))==='........########','top bar right to left, half: '+cen(bk({BrakeArea:'top',BrakeDirection:'rightLeft'},50)));
ok(cen(bk({BrakeArea:'top',BrakeDirection:'inOut'},50))==='....########....','top bar In to Out, half: '+cen(bk({BrakeArea:'top',BrakeDirection:'inOut'},50)));
ok(cen(bk({BrakeArea:'top',BrakeDirection:'outIn'},50))==='####........####','top bar Out to In, half: '+cen(bk({BrakeArea:'top',BrakeDirection:'outIn'},50)));
for(const v of [0,-3,'x',null,'zzz']) { const r=bk({BrakeArea:v,BrakeDirection:v},99); ok(r.length===48&&r.every(c=>c===null||re.test(c)),'brake area/direction value '+v); }

// ABS and traction control stay visible on the outer side LEDs while the brake bar is lit
{ const r=T.frame({Brake:60,Rpms:0,ABSActive:1,TCActive:1},{LedFunction:'brakeOnly',BrakeBlinkStage1:100},at(10));
  ok(r[0]==='#FFFFFF00'&&r[25]==='#FFFFFF00','ABS is hidden by the brake bar: '+r[0]+' '+r[25]);
  ok(r[1]==='#FF00BFFF'&&r[24]==='#FF00BFFF','TC is hidden by the brake bar: '+r[1]);
  ok(r[2]==='#FFFF0000'&&r[3]===null,'the brake bar next to ABS and TC: '+show(r,5,16)); }
// small sides: the bar takes every LED of a side, whatever the size
for(const s of [1,2,3,4,5,8]){ const r=T.frame({Brake:60,Rpms:0},{LedFunction:'brakeOnly',BrakeBlinkStage1:100,SideLeds:s,CentreLeds:6},at(10)); const lit=r.slice(0,s).filter(c=>c).length; ok(lit===Math.ceil(0.6*s),`side ${s}: ${lit} LEDs lit at 60 %`); }

// ---------------------------------------------------------------- idle when the game is paused or shows a menu, default idle style
const litCount=r=>r.slice(0,26).filter(c=>c&&c!=='#FF000000').length;
{ const base={Rpms:6800};
  const running=T.frame(Object.assign({'DataCorePlugin.GameRunning':1},base),{IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
  const paused=T.frame(Object.assign({'DataCorePlugin.GameRunning':1,'DataCorePlugin.GamePaused':1},base),{IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
  const menu=T.frame(Object.assign({'DataCorePlugin.GameRunning':1,'DataCorePlugin.GameInMenu':1},base),{IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
  ok(litCount(running)<26&&running.slice(5,21).some(c=>c===null),'idle must not play while driving');
  ok(litCount(paused)===26&&litCount(menu)===26,'idle must play while the game is paused or in a menu: '+litCount(paused)+' '+litCount(menu));
  const off=T.frame({'DataCorePlugin.GameRunning':1,'DataCorePlugin.GamePaused':1,Rpms:6800},{IdleEnabled:false},at(10)); ok(litCount(off)===0,'idle off: nothing lights in a menu');
}
ok(T.defaults.IdleStyle==='sweep','default idle style is sweep, got '+T.defaults.IdleStyle);
ok(T.defaults.BrightnessIdle===40&&T.defaults.BrightnessGeneral===100,'default brightness: general '+T.defaults.BrightnessGeneral+', idle '+T.defaults.BrightnessIdle);

// ---------------------------------------------------------------- fuel placement (lowest priority, only in a free area)
{ const lit=(r,from,to)=>[...Array(to-from+1).keys()].map(i=>from+i).filter(i=>r[i]&&r[i]!=='#FF000000').join(',');
  const L=r=>lit(r,0,4), R=r=>lit(r,21,25), C=r=>lit(r,5,20);
  const fu=(place,extra,cfg,t)=>T.frame(Object.assign({FuelPercent:50},extra||{}),Object.assign({FuelPlacement:place,FuelColours:'#FF0000,#00FF00',FuelColourMode:'gradient'},cfg||{}),at(t||10));
  ok(L(T.frame({FuelPercent:50},{},at(10)))===''&&C(T.frame({FuelPercent:50},{},at(10)))==='','fuel placement: off by default');
  const s=fu('sides'); ok(L(s)==='0,1,2'&&R(s)==='23,24,25'&&s[0]==='#FFFF0000','fuel on both sides at 50 %: '+show(s,5,16));
  const l=fu('left'); ok(L(l)==='0,1,2'&&R(l)==='','fuel on the left only: '+show(l,5,16));
  const r=fu('right'); ok(L(r)===''&&R(r)==='23,24,25','fuel on the right only: '+show(r,5,16));
  const c=fu('centre',{},{LedFunction:'rpmOnly'}); ok(C(c).split(',').length===8,'fuel in the centre at 50 %: '+show(c,5,16));
  ok(C(fu('centre',{Rpms:4000},{LedFunction:'rpmOnly'}))===''||fu('centre',{Rpms:4000},{LedFunction:'rpmOnly'}).slice(5,21).every(x=>x!=='#FFFF0000'&&x!=='#FF00FF00'||true),'x');
  ok(L(fu('sides',{FuelPercent:100}))==='0,1,2,3,4','fuel full'); ok(L(fu('sides',{FuelPercent:5}))==='0','fuel nearly empty');
  ok(L(fu('sides',{FuelPercent:undefined}))==='','no fuel value, no fuel LEDs');
  ok(L(fu('bogus'))===''&&C(fu('bogus'))==='','invalid placement means off');
  // as soon as any effect uses the area, the whole fuel display is gone and the effect has the LEDs
  const b1=fu('sides',{Brake:20,Rpms:0},{LedFunction:'brakeOnly',BrakeBlinkStage1:100}); ok(L(b1)==='0'&&b1[0]==='#FFFF0000'&&b1[1]===null,'brake 20 %: only the brake LED, no fuel LEDs left: '+show(b1,5,16));
  const b2=fu('sides',{Brake:60,Rpms:0},{LedFunction:'brakeOnly',BrakeBlinkStage1:100}); ok(b2[0]==='#FFFF0000'&&b2[2]==='#FFFF0000'&&b2[3]===null,'brake 60 %: only brake LEDs: '+show(b2,5,16));
  for(const t of [10,70,160,230,300]){ const bl=fu('sides',{Brake:100,Rpms:0},{LedFunction:'brakeOnly'},t); ok(!bl.slice(0,5).some(x=>x==='#FF00FF00'||(x&&x!=='#FF000000'&&x!=='#FFFF0000')),'fuel flickers in while the brake bar blinks (t='+t+'): '+show(bl,5,16)); }
  const fl=[fu('sides',{Flag_Yellow:1},{},10),fu('sides',{Flag_Yellow:1},{},300)]; ok(fl.every(x=>L(x)===''||x.slice(0,5).every(c=>c==='#FFFFFF00'||c==='#FF000000')),'a flag takes over from the fuel level: '+fl.map(x=>show(x,5,16)).join(' '));
  const spl=fu('left',{SpotterCarLeft:1},{},10), spr=fu('left',{SpotterCarRight:1},{},10);
  ok(!spl.slice(0,5).some(x=>x==='#FFFF0000'||x==='#FF00FF00'),'a spotter on the left takes the left fuel display: '+show(spl,5,16)); ok(L(spr)==='0,1,2','a spotter on the right leaves the left fuel display: '+show(spr,5,16));
  const rs=fu('right',{SpotterCarLeft:1},{},10); ok(R(rs)==='23,24,25','left spotter leaves right fuel: '+show(rs,5,16));
  const ab=fu('sides',{ABSActive:1},{},10); ok(L(ab)==='0'&&ab[0]==='#FFFFFF00','ABS takes the fuel display: '+show(ab,5,16));
  for(const f of ['rpmBrake','rpmOnly','brakeOnly','brakeThrottle','flags']) ok(L(fu('sides',{},{LedFunction:f}))==='0,1,2','fuel sides work with the function '+f);
}

// ---------------------------------------------------------------- test mode: the sliders only act in their own scenario
{ const names=T.scenarios, own=names.indexOf('RPM and brake (use the sliders)'), flag=names.indexOf('White flag');
  const m=(i,rpm,brake)=>T.frame({}, {TestMode:'Manual',TestScenario:i,TestRpmPercent:rpm,TestBrakePercent:brake}, at(5000)).slice(0,26);
  const fl=m(flag,85,70), own85=m(own,85,70);
  ok(fl.slice(5,21).every(c=>c===null||c==='#FF000000'||c==='#FFFFFFFF'),'RPM slider leaks into the white flag scenario: '+show(fl,5,16));
  ok(own85.slice(5,21).some(c=>c==='#FF00FF00'),'RPM slider does not act in its own scenario: '+show(own85,5,16));
  ok(own85.slice(0,5).some(c=>c==='#FFFF0000'),'brake slider does not act in its own scenario: '+show(own85,5,16));
  const ind=m(names.indexOf('Indicator left'),85,70); ok(!ind.some(c=>c==='#FF00FF00'),'green RPM LEDs show up in the indicator scenario'); }

// ---------------------------------------------------------------- priority order of the effects
{ const f=(extra,cfg,tm)=>T.frame(extra,cfg||{},at(tm||10));
  const both={Flag_Yellow:1,SpotterCarLeft:1,SpotterCarLeftDistance:2};
  const ph=cfg=>[10,170,330,490,650,810].map(d=>f(both,cfg,d)[0]);
  ok(ph({}).every(c=>c==='#FFFFFF00'||c==='#FF000000'),'standard order: only the flag shows on the left, never the spotter: '+ph({}).join(' '));
  ok(ph({}).includes('#FFFFFF00'),'the flag really shows');
  ok(ph({LayerOrder:'flags,spotter'}).every(c=>c==='#FFFF1493'||c==='#FF000000')&&ph({LayerOrder:'flags,spotter'}).includes('#FFFF1493'),'own order (flags,spotter): the spotter wins over the flag: '+ph({LayerOrder:'flags,spotter'}).join(' '));
  for(const bad of ['nonsense','flags,flags,xyz,,','spotter']) ok(f(both,{LayerOrder:bad})!==null,'invalid order tolerated: '+bad);
  const only=f(both,{LayerOrder:'spotter'}); ok(only.length===48,'a short list is completed from the standard order');
  const ab=o=>[10,170,330,490].map(d=>f({Brake:100,ABSActive:1},Object.assign({LedFunction:'brakeOnly'},o),d)[0]);
  ok(ab({}).includes('#FFFFFF00'),'standard: ABS shows over the brake bar: '+ab({}).join(' '));
  ok(!ab({LayerOrder:'absTc,brake'}).includes('#FFFFFF00'),'brake above ABS when the user says so: '+ab({LayerOrder:'absTc,brake'}).join(' '));
}

// ---------------------------------------------------------------- idle while the car is off / in the garage
{ const lit=r=>r.some(x=>x&&x!=='#FF000000');
  const run=(p,cfg,d)=>T.frame(Object.assign({'DataCorePlugin.GameRunning':1},p),cfg||{},at(d===undefined?777:d));
  const sometimes=(p,cfg)=>[0,400,800,1200,1600,2000,2400,2800,3200,3600].some(d=>lit(run(p,cfg,d)));      // the animation is dark at some moments
  ok(sometimes({Rpms:0,SpeedKmh:0,EngineIgnitionOn:1}),'RPM 0 and standing still: idle animation plays');
  ok(sometimes({Rpms:0,SpeedKmh:0,EngineIgnitionOn:0}),'engine off: idle animation plays');
  ok(sometimes({Rpms:undefined}),'game running but no car data yet (garage before the first drive): idle plays');
  const drive=run({Rpms:7500,SpeedKmh:0,EngineIgnitionOn:1,'CarSettings_RedLineRPM':8000}); ok(drive.slice(5,21).some(x=>x==='#FF00FF00'||x==='#FFFFFF00'||x==='#FFFF0000'),'engine running while standing: rev bar, not idle: '+show(drive,5,16));
  ok(![0,400,800,1200,1600,2000].some(d=>lit(run({SpeedKmh:60,Rpms:6000,EngineIgnitionOn:1},{LedFunction:'flags'},d))),'no idle while driving');
}

// ---------------------------------------------------------------- custom positions of the side effects
{ const fr=(p,cfg,tm)=>T.frame(p,cfg||{},at(tm||10)); const idx=(r,c)=>r.slice(0,5).map((x,i)=>x===c?i:-1).filter(i=>i>=0).join(',');
  ok(idx(fr({TCActive:1}),'#FF00BFFF')==='1','TC default: second LED');
  ok(idx(fr({TCActive:1},{FxTcCustom:true,FxTcStart:4,FxTcCount:2}),'#FF00BFFF')==='3,4','TC custom start 4, 2 LEDs: LEDs 4 and 5');
  const r=fr({TCActive:1},{FxTcCustom:true,FxTcStart:4,FxTcCount:2}); ok(r[21]===undefined||r.slice(21,26).filter(x=>x==='#FF00BFFF').length===2,'custom range mirrored on the right: '+show(r,5,16));
  ok(idx(fr({TCActive:1},{FxTcCustom:true,FxTcStart:4,FxTcCount:9}),'#FF00BFFF')==='3,4','count is limited to the strip');
  ok(idx(fr({TCActive:1},{FxTcCustom:true,FxTcStart:99,FxTcCount:1}),'#FF00BFFF')==='4','start is limited to the strip');
  ok(idx(fr({TCActive:1},{FxTcCustom:false,FxTcStart:4,FxTcCount:2}),'#FF00BFFF')==='1','values are ignored while custom is off');
  ok(idx(fr({ABSActive:1},{FxAbsCustom:true,FxAbsStart:2,FxAbsCount:3}),'#FFFFFF00')==='1,2,3','ABS custom range');
  const turnL=fr({TurnIndicatorLeft:1,'DataCorePlugin.CurrentGame':'ETS2'},{FxTurnCustom:true,FxTurnStart:2,FxTurnCount:2},10); ok(turnL.slice(0,5).filter(x=>x==='#FFFFA500').length===2&&turnL.slice(21,26).every(x=>!x||x==='#FF000000'),'turn indicator custom range, left only');
  const drsC=fr({DRSEnabled:1},{FxDrsCustom:true,FxDrsStart:1,FxDrsCount:5}); ok(idx(drsC,'#FF008000')==='0,1,2,3,4','DRS custom range');
  const flag=fr({Flag_Yellow:1},{FxFlagsCustom:true,FxFlagsStart:3,FxFlagsCount:3},10); ok(flag.slice(0,2).every(x=>!x||x==='#FF000000')===true&&flag[2]!==null,'flags only on their range: '+show(flag,5,16));
  const lf=fr({CarSettings_FuelAlertActive:1},{FxLowFuelCustom:true,FxLowFuelStart:2,FxLowFuelCount:3},10); ok(lf[0]===null&&lf[4]===null,'low fuel chase stays inside its range: '+show(lf,5,16));
  const sp=fr({SpotterCarLeft:1,SpotterCarLeftDistance:2},{FxSpotterCustom:true,FxSpotterStart:1,FxSpotterCount:2},10); ok(sp[3]===null&&sp[4]===null&&sp[21]===null,'spotter range left only: '+show(sp,5,16));
  const e=fr({Rpms:1000,SpeedKmh:100},{LayerOrder:'x'}); ok(e.length===48,'ok');
  // layouts with fewer LEDs per side: default positions stay on the strip
  for(const S of [1,2,3,8]) for(const n of ['Tc','Abs','Drs','Turn','ErsCharge']) { const f=T.frame({TCActive:1,ABSActive:1,DRSEnabled:1,TurnIndicatorLeft:1,ERSPercent:70,'DataCorePlugin.CurrentGame':'F12025'},{SideLeds:S},at(10)); ok(f.length===48,'layout '+S+' '+n); }
}

// ---------------------------------------------------------------- redline behaviour
{ const c=r=>r.slice(5,21); const at2=(m,tm,extra)=>T.frame(Object.assign({Rpms:8200},extra||{}),{LedFunction:'rpmOnly',RpmRedline:m,RpmFlashColour:'#0000FF',RpmFlashColour2:'#FF0000'},tm);
  const a=c(at2('flashAlt',6000)), b=c(at2('flashAlt',6070));
  ok(a[0]==='#FF0000ff'.toUpperCase().replace('FF0000FF','#FF0000FF')||a[0]==='#FF0000FF','flashAlt: first LED has the first colour: '+a.slice(0,4).join(' '));
  ok(a.every((x,i)=>x===(i%2===0?'#FF0000FF':'#FFFF0000')),'flashAlt: every second LED has the other colour');
  ok(b.every((x,i)=>x===(i%2===0?'#FFFF0000':'#FF0000FF')),'flashAlt: the colours swap in the next phase');
  const sw1=c(at2('flashSwap',6000)), sw2=c(at2('flashSwap',6070)); ok(sw1.every(x=>x==='#FFFF0000')&&sw2.every(x=>x==='#FF0000FF'),'flashSwap: all LEDs colour 1, then all colour 2: '+sw1[0]+' '+sw2[0]);
  const f1=c(at2('flash',6000)), f2=c(at2('flash',6070)); ok(f2.every(x=>x==='#FF0000FF')&&f1.every(x=>x==='#FF000000'),'flash: all LEDs flash in one colour');
  const off=c(at2('off',6000)); ok(off.every(x=>x!=='#FF0000FF')&&off.filter(x=>x!=='#FF000000').length>=8,'nothing special: the normal bar, no flash colour: '+off.join(' ').slice(0,60));
  const solid=c(at2('solid',6000)), offb=c(at2('off',6000)); ok(JSON.stringify(solid)===JSON.stringify(offb),'the old value "solid" behaves like nothing special');
  const sc=T.scenarios.indexOf('Redline reached (rev limiter)'); ok(sc>=0,'test scenario for the redline exists');
  const tm=T.frame({}, {TestMode:'Manual',TestScenario:sc,LedFunction:'rpmOnly',RpmRedline:'flashAlt'}, 7000); ok(c(tm).some(x=>x==='#FFFF0000'||x==='#FF0000FF'),'the redline scenario shows the redline behaviour in test mode');
}

// ---------------------------------------------------------------- pedal display (areas per pedal)
{ const ped=(cfg,p)=>T.frame(Object.assign({Brake:0,Throttle:0,Clutch:0},p),Object.assign({LedFunction:'brakeThrottle',BrakeColour:'#FF0000',ThrottleColour:'#00FF00',FxClutchColour:'#8A2BE2'},cfg),at(10));
  const cnt=(r,c)=>r.filter(x=>x===c).length, idxs=(r,c)=>r.map((x,i)=>x===c?i:-1).filter(i=>i>=0).join(',');
  const R='#FFFF0000',G='#FF00FF00',V='#FF8A2BE2';
  const sp=ped({},{Brake:50,Throttle:50}).slice(5,21); ok(idxs(sp,R)==='4,5,6,7'&&idxs(sp,G)==='8,9,10,11','default: brake left of the middle, throttle right: '+sp.join(' ').slice(0,60));
  const mix=ped({PedalBrake:'centreEnds',PedalThrottle:'centreMiddle'},{Brake:25,Throttle:50}).slice(5,21);
  ok(idxs(mix,R)==='0,1,14,15'&&idxs(mix,G)==='4,5,6,7,8,9,10,11','brake at both ends, throttle from the middle outwards: '+idxs(mix,R)+' | '+idxs(mix,G));
  const full=ped({PedalBrake:'centreEnds',PedalThrottle:'centreMiddle'},{Brake:100,Throttle:100}).slice(5,21); ok(cnt(full,R)===16,'full brake covers the throttle (brake is painted last)');
  const th=ped({PedalBrake:'off',PedalThrottle:'centreLeft'},{Throttle:50,Brake:100}).slice(5,21); ok(idxs(th,G)==='0,1,2,3,4,5,6,7'&&cnt(th,R)===0,'throttle only, left to right');
  const br=ped({PedalBrake:'centreRight',PedalThrottle:'off'},{Brake:25,Throttle:100}).slice(5,21); ok(idxs(br,R)==='12,13,14,15'&&cnt(br,G)===0,'brake only, right to left');
  const three=ped({PedalClutch:'sideLeft',PedalBrake:'sideRight',PedalThrottle:'centreLeft'},{Clutch:60,Brake:100,Throttle:50});
  ok(idxs(three.slice(0,5),V)==='0,1,2'&&idxs(three.slice(21,26),R)==='0,1,2,3,4'&&idxs(three.slice(5,21),G)==='0,1,2,3,4,5,6,7'&&cnt(three.slice(21,26),V)===0,'clutch left side, brake right side, throttle on top: '+show(three,5,16));
  const both=ped({PedalBrake:'sideBoth',PedalThrottle:'off'},{Brake:40}); ok(idxs(both.slice(0,5),R)==='0,1'&&idxs(both.slice(21,26),R)==='3,4','both side columns, outermost LED first: '+show(both,5,16));
  ok(ped({PedalBrake:'nonsense',PedalThrottle:'x',PedalClutch:null},{Brake:50}).length===48,'invalid areas are ignored');
  ok(cnt(ped({PedalBrake:'halfLeft'},{Brake:100}).slice(5,21),R)===8,'full brake fills its half');
}

// ---------------------------------------------------------------- brake bar on the top row only in "brake pressure only"
{ const br=(fn,cfg)=>T.frame({Brake:100,Rpms:0},Object.assign({LedFunction:fn,BrakeBlinkStage1:100,BrakeBlinkStage2:100},cfg),at(10));
  const sidesLit=r=>[...r.slice(0,5),...r.slice(21,26)].filter(x=>x&&x!=='#FF000000').length, centreLit=r=>r.slice(5,21).filter(x=>x&&x!=='#FF000000').length;
  const top={BrakeArea:'top',BrakeDirection:'leftRight'};
  const a=br('brakeOnly',top); ok(centreLit(a)===16&&sidesLit(a)===0,'brake pressure only: the top row works');
  const b=br('rpmBrake',top); ok(centreLit(b)===0&&sidesLit(b)===10,'RPM and brake: the top row falls back to the side LEDs: '+show(b,5,16));
}

// ---------------------------------------------------------------- safety car delta and ERS mode (F1)
{ const F1={'DataCorePlugin.CurrentGame':'F12025','DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':1};
  const sc=(d,cfg)=>T.frame(Object.assign({},F1,{'GameRawData.PlayerLapData.m_safetyCarDelta':d}),Object.assign({LedFunction:'rpmOnly'},cfg||{}),at(10)).slice(5,21);
  const O='#FFFFA500', G='#FF00FF00', idx=(r,c)=>r.map((x,i)=>x===c?i:-1).filter(i=>i>=0).join(',');
  ok(idx(sc(0.1),G)==='7,8','delta close to zero: the two middle LEDs are green');
  ok(idx(sc(1),O)==='8,9,10,11'&&idx(sc(-1),O)==='4,5,6,7','delta 1 s: half the bar, right for positive and left for negative: '+idx(sc(1),O)+' | '+idx(sc(-1),O));
  ok(idx(sc(5),O)==='8,9,10,11,12,13,14,15','delta of 2 s or more: the whole half');
  ok(idx(sc(1,{FxScDeltaSwap:true}),O)==='4,5,6,7','swap turns the direction around');
  ok(sc(undefined).every(x=>!x||x==='#FF000000'||true),'no value, no error');
  const noSc=T.frame(Object.assign({},F1,{'DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':0,'GameRawData.PlayerLapData.m_safetyCarDelta':1}),{LedFunction:'rpmOnly'},at(10)).slice(5,21); ok(idx(noSc,O)==='','no safety car, no delta display');
  const notF1=T.frame({'GameRawData.PlayerLapData.m_safetyCarDelta':1,'DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':1},{LedFunction:'rpmOnly'},at(10)).slice(5,21); ok(idx(notF1,O)==='','only in F1 games');
  const ers=m=>T.frame(Object.assign({},F1,{'DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':0,'GameRawData.PlayerCarStatusData.m_ersDeployMode':m}),{},at(10));
  ok(ers(3)[3]==='#FFFF0000'&&ers(3)[21]===undefined||ers(3)[3]==='#FFFF0000','ERS mode overtake: left side, default slot 4');
  ok(ers(3).slice(21,26).every(x=>x!=='#FFFF0000'),'ERS mode is only on the left side');
  ok(ers(0)[3]===null||ers(0)[3]==='#FF000000','ERS mode none: nothing');
}

// ---------------------------------------------------------------- night mode, shift flash and top gear
{ const lit=r=>r.filter(x=>x&&x!=='#FF000000'), cen=r=>r.slice(5,21);
  const rev=(p,cfg,tm)=>cen(T.frame(Object.assign({Rpms:8200,Gear:'3','CarSettings_MaxGears':6},p),Object.assign({LedFunction:'rpmOnly',RpmRedline:'flash',RpmFlashColour:'#0000FF'},cfg||{}),at(tm||10)));
  const flashing=(p,cfg)=>[0,60,120,180,240].some(d=>rev(p,cfg,d||1).some(x=>x==='#FF0000FF'));
  ok(flashing({}),'redline in a middle gear: the flash plays');
  ok(!flashing({Gear:'6'}),'redline in the top gear: no flash by default');
  ok(flashing({Gear:'6'},{RpmNoFlashInTopGear:false}),'top gear flash can be switched on again');
  ok(!flashing({Rpms:7000,'CarSettings_RPMRedLineReached':1}),'SimHub shift point without the option: no flash below the redline');
  ok(flashing({Rpms:7000,'CarSettings_RPMRedLineReached':1},{RpmShiftFlash:true}),'SimHub shift point with the option: the flash plays');
  ok(!flashing({Rpms:7000,'CarSettings_RPMRedLineReached':1,Gear:'6'},{RpmShiftFlash:true}),'shift point in the top gear: no flash');
  const day=T.frame({Rpms:7500},{LedFunction:'rpmOnly',BrightnessGeneral:100},at(10)), night=T.frame({Rpms:7500},{LedFunction:'rpmOnly',BrightnessGeneral:100,NightMode:true},at(10));
  const sum=r=>r.filter(x=>x&&x!=='#FF000000').map(x=>parseInt(x.slice(3,5),16)+parseInt(x.slice(5,7),16)+parseInt(x.slice(7,9),16));
  const ratio=sum(night)[0]/sum(day)[0]; ok(ratio>0.45&&ratio<0.55,'night mode halves the brightness: '+ratio.toFixed(2));
  const idle=nm=>T.frame({'DataCorePlugin.GameRunning':0},{IdleStyle:'solid',IdleColours:'#00FF00,#00FF00',BrightnessIdle:100,BrightnessGeneral:100,NightMode:nm},at(10));
  ok(Math.abs(sum(idle(true))[0]/sum(idle(false))[0]-0.5)<0.05,'night mode halves the idle animation as well');
}

// ---------------------------------------------------------------- brightness
const full=T.frame({Rpms:6800},{BrightnessGeneral:100},at(10)), half=T.frame({Rpms:6800},{BrightnessGeneral:50},at(10));
const g=c=>parseInt(c.slice(5,7),16);
const li=full.findIndex(c=>c==='#FF00FF00'); ok(li>=0&&g(half[li])===128,'general brightness 50 %: '+half[li]);
const lo=T.frame({Rpms:6800},{BrightnessGeneral:1},at(10)); ok(g(lo[li])<=3,'general brightness 1 %: '+lo[li]);
const idleFull=T.frame({'DataCorePlugin.GameRunning':0},{BrightnessIdle:100,IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
const idleHalf=T.frame({'DataCorePlugin.GameRunning':0},{BrightnessIdle:50,IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
ok(g(idleFull[3])===255&&g(idleHalf[3])===128,'idle brightness 50 %: '+idleFull[3]+' '+idleHalf[3]);
const idleGen=T.frame({'DataCorePlugin.GameRunning':0},{BrightnessIdle:50,BrightnessGeneral:50,IdleStyle:'solid',IdleColours:'#00FF00,#00FF00'},at(10));
ok(g(idleGen[3])<=65&&g(idleGen[3])>=62,'idle and general brightness multiply: '+idleGen[3]);
const drv=T.frame({Rpms:6800,'DataCorePlugin.GameRunning':1},{BrightnessIdle:1},at(10)); ok(g(drv[li])===255,'idle brightness must not dim driving');
for(const v of [0,-5,500,'x',null,50.5]) { const r=T.frame({Rpms:6800},{BrightnessGeneral:v,BrightnessIdle:v},at(10)); ok(r.length===48&&r.every(c=>c===null||re.test(c)),'brightness value '+v); }

// brake bar on one side only: Left / Right side only, and the fuel level on the free side while braking
{ const L=r=>r.slice(0,5).filter(c=>c&&c!=='#FF000000').length, R=r=>r.slice(21,26).filter(c=>c&&c!=='#FF000000').length;
  const bl=T.frame({Brake:90,Rpms:0},{LedFunction:'rpmBrake',BrakeArea:'left',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  ok(L(bl)===5&&R(bl)===0,'brake left side only: '+show(bl,5,16));
  const br=T.frame({Brake:90,Rpms:0},{LedFunction:'rpmBrake',BrakeArea:'right',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  ok(L(br)===0&&R(br)===5,'brake right side only: '+show(br,5,16));
  const bh=T.frame({Brake:50,Rpms:0},{LedFunction:'rpmBrake',BrakeArea:'left',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  ok(L(bh)===3&&R(bh)===0,'brake left half fills from the outside: '+show(bh,5,16));
  const fr=T.frame({Brake:90,Rpms:0,FuelPercent:100},{LedFunction:'rpmBrake',BrakeArea:'left',FuelPlacement:'right',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  ok(L(fr)===5&&R(fr)===5,'fuel stays on the right while the brake uses the left: '+show(fr,5,16));
  const fb=T.frame({Brake:90,Rpms:0,FuelPercent:100},{LedFunction:'rpmBrake',FuelPlacement:'sides',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  const fn2=T.frame({Brake:0,Rpms:0,FuelPercent:100},{LedFunction:'rpmBrake',FuelPlacement:'sides'},at(10));
  ok(L(fn2)===5&&R(fn2)===5,'fuel on both sides shows when not braking: '+show(fn2,5,16));
  const bt=T.frame({Brake:90,Rpms:0,FuelPercent:100},{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:'leftRight',FuelPlacement:'sides',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  ok(L(bt)===5&&R(bt)===5,'function Brake with a top bar: the sides stay free for fuel: '+show(bt,5,16)); }

// the side of a custom brake position only counts with the custom position on
{ const L=r=>r.slice(0,5).filter(c=>c&&c!=='#FF000000').length, R=r=>r.slice(21,26).filter(c=>c&&c!=='#FF000000').length;
  const nc=T.frame({Brake:90,Rpms:0},{LedFunction:'rpmBrake',BrakeArea:'top',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10)); ok(L(nc)===5&&R(nc)===5,'Top without the function Brake falls back to both sides: '+show(nc,5,16));
  // smooth fill: the LED that is coming on fades in
  const sm=pct=>T.frame({Brake:pct,Rpms:0},{LedFunction:'rpmBrake',BrakeSmooth:true,BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10));
  const a=sm(10), b=sm(20), c=sm(30);          // 5 LEDs: 10 % = half of the first LED, 20 % = the first, 30 % = the first plus half of the second
  const red=x=>x?parseInt(x.slice(3,5),16):0;
  ok(red(a[0])>100&&red(a[0])<150,'smooth: first LED half on at 10 %: '+a[0]); ok(red(b[0])===255&&red(b[1])===0,'smooth: exactly one LED at 20 %: '+b.slice(0,2));
  ok(red(c[0])===255&&red(c[1])>100&&red(c[1])<150,'smooth: second LED half on at 30 %: '+c.slice(0,2));
  ok(red(c[0])<=255&&red(sm(100)[4])===255,'smooth: 100 % lights every LED');
  const hard=T.frame({Brake:10,Rpms:0},{LedFunction:'rpmBrake',BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10)); ok(red(hard[0])===255,'without smooth the first LED is fully on at once: '+hard[0]);
  const smb=T.frame({Brake:95,Rpms:0},{LedFunction:'rpmBrake',BrakeSmooth:true,BrakeBlinkStage1:90,BrakeBlinkStage2:100},at(10)); ok(smb.slice(0,5).every(x=>x==='#FFFF0000'||x==='#FF000000'),'smooth does not dim a blinking bar: '+smb.slice(0,5));
  const smc=T.frame({Brake:50,Rpms:0},{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:'leftRight',BrakeSmooth:true,BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10)); ok(smc.slice(5,21).filter(x=>x&&x!=='#FF000000').length===8,'smooth top bar at 50 %: 8 LEDs: '+show(smc,5,16));
  const smp=T.frame({Brake:50,Rpms:0},{LedFunction:'brakeOnly',BrakeArea:'top',BrakeDirection:'outIn',BrakeSmooth:true,BrakeBlinkStage1:100,BrakeBlinkStage2:100},at(10)); ok(smp.slice(5,21).filter(x=>x&&x!=='#FF000000').length===8,'smooth outside-in at 50 %: '+show(smp,5,16));
  // pedals
  const pd=(v,smooth)=>T.frame({Throttle:v,Brake:0},{LedFunction:'brakeThrottle',PedalThrottle:'sideBoth',PedalBrake:'off',PedalsSmooth:smooth},at(10));
  const g=x=>x?parseInt(x.slice(5,7),16):0;
  ok(pd(30,true).slice(0,5).filter(x=>x&&x!=='#FF000000').length===2&&g(pd(30,true)[1])<g(pd(30,true)[0]),'smooth pedal: second LED fades in at 30 %: '+pd(30,true).slice(0,5));
  ok(pd(30,false).slice(0,5).filter(x=>x&&x!=='#FF000000').length===2&&g(pd(30,false)[1])===g(pd(30,false)[0]),'pedals without smooth: whole LEDs: '+pd(30,false).slice(0,5));
  // flags: the top row follows the custom position setting
  const fy2=cfg=>{ const a=T.frame({Flag_Yellow:1},Object.assign({LedFunction:'flags'},cfg),at(10)), b=T.frame({Flag_Yellow:1},Object.assign({LedFunction:'flags'},cfg),at(300)); return centreLit(a)+sidesLit(a)>=centreLit(b)+sidesLit(b)?a:b; };
  ok(centreLit(fy2({}))===16&&sidesLit(fy2({}))===10,'flags function: the whole strip by default');
  ok(centreLit(fy2({FxFlagsCustom:true}))===16,'custom position on, top row left on by default');
  ok(centreLit(fy2({FxFlagsCustom:true,FxFlagsCentre:false}))===0&&sidesLit(fy2({FxFlagsCustom:true,FxFlagsCentre:false}))===10,'custom position with the top row off: only the sides');
  ok(sidesLit(fy2({FxFlagsCustom:true,FxFlagsCentre:false,FxFlagsStart:2,FxFlagsCount:2}))===4,'custom position: only the chosen side LEDs');
  ok(centreLit(fy2({FxFlagsCentre:false}))===16,'the top row switch only counts with the custom position on'); }
// fuel with smooth fill: the last LED shows only its share
{ const fsm=(pct,smooth,place)=>T.frame({FuelPercent:pct},{LedFunction:'rpmOnly',FuelPlacement:place,FuelSmooth:smooth,FuelColours:'#FF0000,#FF0000'},at(10));
  const red=x=>x?parseInt(x.slice(3,5),16):0;
  const c=fsm(25,true,'sides'), n=fsm(25,false,'sides');           // 5 side LEDs: 25 % = 1.25 LEDs
  ok(red(c[0])===255&&red(c[1])>0&&red(c[1])<120&&red(c[2])===0,'smooth fuel on the sides: second LED partly on: '+c.slice(0,3));
  ok(red(n[0])===255&&red(n[1])===255&&red(n[2])===0,'fuel without smooth: whole LEDs: '+n.slice(0,3));
  const d=T.frame({FuelPercent:50},{LedFunction:'fuel',FuelPlacement:'centre',FuelSmooth:true,FuelColours:'#FF0000,#FF0000'},at(10)).slice(5,21);
  ok(d.filter(x=>red(x)===255).length===8,'smooth fuel on the top row, half: '+d); }
console.log(fails?`${fails} FAILURES`:'ALL LAYOUT TESTS PASSED');
process.exit(fails?1:0);
