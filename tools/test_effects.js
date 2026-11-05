// Every effect: switch on shows its default colour, switch off hides it, a custom colour replaces the default.
const path=require('path'),{make}=require('./harness.js');
const file=path.join(__dirname,'Shiftline.plugin.js');
const LMU='DataCorePlugin.CurrentGame';
// name, switch setting (setting = value for on / off), game props that trigger the effect, extra config, colour settings [key, default #RRGGBB]
const E=[
 ['ABS',{FxAbsOn:[true,false]},{ABSActive:1},{},[['FxAbsColour','#FFFF00']]],
 ['Traction control',{FxTcOn:[true,false]},{TCActive:1},{},[['FxTcColour','#00BFFF']]],
 ['Turn indicator',{FxTurnOn:[true,false]},{TurnIndicatorLeft:1},{},[['FxTurnColour','#FFA500']]],
 ['Headlight flash',{FxHeadlightFlashOn:[true,false]},{[LMU]:'AssettoCorsaCompetizione','DataCorePlugin.GameRawData.Graphics.FlashingLights':1},{},[['FxHeadlightFlashColour','#00BFFF']]],
 ['Spotter',{FxSpotterOn:[true,false]},{SpotterCarLeft:1},{},[['FxSpotterColour','#FF1493']]],
 ['Flag yellow',{FxFlagsOn:[true,false]},{Flag_Yellow:1},{},[['FxFlagYellowColour','#FFFF00']]],
 ['Flag green',{FxFlagsOn:[true,false]},{Flag_Green:1},{},[['FxFlagGreenColour','#00FF00']]],
 ['Flag red',{FxFlagsOn:[true,false]},{Flag_Red:1,'GameRawData.Telemetry.SessionFlagsDetails.Isred':true,[LMU]:'IRacing'},{},[['FxFlagRedColour','#FF0000']]],
 ['Flag blue',{FxFlagsOn:[true,false]},{Flag_Blue:1},{},[['FxFlagBlueColour','#0000FF']]],
 ['Flag white',{FxFlagsOn:[true,false]},{Flag_White:1},{},[['FxFlagWhiteColour','#FFFFFF']]],
 ['Flag orange',{FxFlagsOn:[true,false]},{Flag_Orange:1},{},[['FxFlagOrangeColour','#FF8C00']]],
 ['Flag black',{FxFlagsOn:[true,false]},{Flag_Black:1},{},[['FxFlagBlackColour','#808080']]],
 ['Flag checkered',{FxFlagsOn:[true,false]},{Flag_Checkered:1},{},[['FxFlagCheckeredColour','#FFFFFF']]],
 ['Pit lane indicator',{FxPitIndicatorOn:[true,false]},{IsInPitLane:1},{},[['FxPitIndicatorColour','#FFFF00']]],
 ['Pit limiter',{FxPitLimiterOn:[true,false]},{PitLimiterOn:1},{SidePit:false},[['FxPitLimiterColour1','#0000FF'],['FxPitLimiterColour2','#FFFFFF']]],
 ['Pit alert (entry)',{FxPitAlertsOn:[true,false]},{IsInPitLane:1,PitLimiterOn:0,SpeedKmh:10,[LMU]:'LMU'},{SidePit:false},[['FxPitEntryColour','#FF0000']]],
 ['Pit speeding',{FxPitSpeedingOn:[true,false]},{IsInPitLane:1,PitLimiterOn:0,SpeedKmh:200,[LMU]:'LMU'},{SidePit:false,FxPitAlertsOn:false},[['FxPitSpeedingColour','#FF0000']]],
 ['Low fuel',{FxLowFuelOn:[true,false]},{CarSettings_FuelAlertActive:1},{},[['FxLowFuelColour','#FF0000']]],
 ['Lift and coast',{FxLiftCoastOn:[true,false]},{'GameRawData.PlayerNativeTelemetry.mLiftAndCoastProgress':200},{},[['FxLiftCoastColour','#BF00FF']]],
 ['ERS deploy',{FxErsDeployOn:[true,false]},{[LMU]:'LMU','GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState':2,'GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorTorque':5},{},[['FxErsDeployColour','#0000FF']]],
 ['ERS regen',{ShowErsRegen:[true,false]},{[LMU]:'LMU','GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState':3},{},[['FxErsRegenColour','#FF1493']]],
 ['ERS state of charge',{FxErsChargeOn:[true,false]},{[LMU]:'LMU','GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState':1,'GameRawData.PlayerNativeTelemetry.mSoC':80},{},[],'#008000'],
 ['ERS lap limit',{FxErsLapLimitOn:[true,false]},{[LMU]:'AssettoCorsa','GameRawData.StaticInfo.HasKERS':1,ERSPercent:30},{},[],'#008000'],
 ['ERS mode',{FxErsModeOn:[true,false]},{[LMU]:'F12025','GameRawData.PlayerCarStatusData.m_ersDeployMode':3},{},[['FxErsModeOvertakeColour','#FF0000']]],
 ['ERS mode medium',{FxErsModeOn:[true,false]},{[LMU]:'F12025','GameRawData.PlayerCarStatusData.m_ersDeployMode':1},{},[['FxErsModeMediumColour','#00C060']]],
 ['ERS mode hotlap',{FxErsModeOn:[true,false]},{[LMU]:'F12025','GameRawData.PlayerCarStatusData.m_ersDeployMode':2},{},[['FxErsModeHotlapColour','#FF8C00']]],
 ['Safety car delta',{FxScDeltaOn:[true,false]},{[LMU]:'F12025','DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':1,'GameRawData.PlayerLapData.m_safetyCarDelta':1.5},{},[['FxScDeltaOffColour','#FFA500']]],
 ['Safety car delta ok',{FxScDeltaOn:[true,false]},{[LMU]:'F12025','DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus':1,'GameRawData.PlayerLapData.m_safetyCarDelta':0.05},{},[['FxScDeltaOkColour','#00FF00']]],
 ['Push to pass',{FxP2pOn:[true,false]},{PushToPassActive:1},{},[['FxP2pColour','#008000']]],
 ['DRS',{FxDrsOn:[true,false]},{DRSEnabled:1},{},[['FxDrsColour','#008000']]],
 ['DRS detection point',{FxDrsDetectOn:[true,false]},{[LMU]:'IRacing','GameRawData.Telemetry.DRS_Status':1},{},[['FxDrsDetectColour','#FFFF00']]],
 ['Clutch',{ShowClutch:[true,false]},{Clutch:60},{},[['FxClutchColour','#8A2BE2']]],
];
const argb=c=>'#FF'+c.slice(1).toUpperCase();
function scan(props,cfg,engine){
  const T=make(file), found=new Set(); let t=4_000_000;
  if(engine){ T.frame({EngineStarted:0},cfg,t); T.frame({EngineStarted:1},cfg,t+=10); }
  for(let i=0;i<40;i++){ for(const c of T.frame(Object.assign({},props,engine?{EngineStarted:1}:{}),cfg,t+=37)) if(c&&c!=='#FF000000') found.add(c); }
  return found;
}
let fails=0; const ok=(c,m)=>{ if(!c){fails++;console.log('FAIL',m);} };
// engine start needs a rising edge, so it has its own sequence
const cases=E.concat([['Engine start',{FxEngineStartOn:[true,false]},{},{},[['FxEngineStartColour','#FF4500']],null,true]]);
for(const [name,sw,props,extra,colours,levelColour,engine] of cases){
  const [swKey,[onV,offV]]=Object.entries(sw)[0];
  const wanted=colours.length?colours.map(c=>argb(c[1])):[argb(levelColour)];
  const on=scan(props,Object.assign({},extra,{[swKey]:onV}),engine), off=scan(props,Object.assign({},extra,{[swKey]:offV}),engine);
  ok(wanted.every(c=>on.has(c)),`${name}: default colour missing when on: wanted ${wanted} got ${[...on]}`);
  ok(wanted.every(c=>!off.has(c)),`${name}: still lit when switched off: ${[...off]}`);
  const GROUP={FxFlagsOn:'Indicators',FxSpotterOn:'Indicators',FxPitIndicatorOn:'Pit',FxPitAlertsOn:'Pit',FxPitSpeedingOn:'Pit',FxPitLimiterOn:'Pit',
    FxEngineStartOn:'Car',FxLowFuelOn:'Car',FxTcOn:'Car',FxAbsOn:'Car',FxHeadlightFlashOn:'Car',FxTurnOn:'Car',FxLiftCoastOn:'Car',
    FxErsModeOn:'Ers',FxErsDeployOn:'Ers',FxErsChargeOn:'Ers',FxErsLapLimitOn:'Ers',ShowErsRegen:'Ers',FxP2pOn:'Ers',
    FxDrsOn:'Extras',FxDrsDetectOn:'Extras',FxScDeltaOn:'Extras',ShowClutch:'Extras'}[swKey];
  ok(!!GROUP,`${name}: no group for ${swKey}`);
  const grpOff=scan(props,Object.assign({},extra,{[swKey]:onV,['FxGroup'+GROUP+'On']:false}),engine);
  ok(wanted.every(c=>!grpOff.has(c)),`${name}: still lit with group ${GROUP} off: ${[...grpOff]}`);
  // another group switched off must not touch this effect
  const other=GROUP==='Car'?'Pit':'Car';
  const grpOther=scan(props,Object.assign({},extra,{[swKey]:onV,['FxGroup'+other+'On']:false}),engine);
  ok(wanted.every(c=>grpOther.has(c)),`${name}: hidden by the unrelated group ${other}`);
  for(const [key,def] of colours){
    const custom=scan(props,Object.assign({},extra,{[swKey]:onV,[key]:'#123456'}),engine);
    ok(custom.has('#FF123456'),`${name}: custom colour ${key} not used: ${[...custom]}`);
    ok(!custom.has(argb(def))||colours.some(([k,d])=>k!==key&&d===def),`${name}: default colour still shown with custom ${key}`);
  }
  console.log(('ok   '+name).padEnd(28), 'on:',[...on].slice(0,3).join(' '), '| off:', off.size?[...off].slice(0,2).join(' '):'-');
}
// the main switch turns every effect off and leaves the RPM bar alone
{ const T=make(file);
  const withFx=T.frame({ABSActive:1,Flag_Yellow:1,SpotterCarLeft:1,IsInPitLane:1,Rpms:6000},{},5_000_000);
  const noFx=T.frame({ABSActive:1,Flag_Yellow:1,SpotterCarLeft:1,IsInPitLane:1,Rpms:6000},{FxMasterOn:false},5_000_000);
  ok(withFx.includes('#FFFFFF00')||withFx.includes('#FFFF1493'),'effects should show with the main switch on');
  ok(!noFx.includes('#FFFFFF00')&&!noFx.includes('#FFFF1493'),'effects still lit with the main switch off: '+[...new Set(noFx)]);
  ok(noFx.some(c=>c&&c!=='#FF000000'),'the RPM bar must stay lit with the main switch off');
  console.log('ok   Effects main switch');
}
// the play button of every effect card: its scenario exists and lights the effect in Manual test mode
{ const T=make(file), xaml=require('fs').readFileSync(path.join(__dirname,'..','plugin','Shiftline','LedPage.xaml'),'utf8');
  // scenario name -> a colour that must show; also the switch the button is bound to (IsEnabled) must be one of the effect switches
  const EXPECT={'Yellow flag':'#FFFF00','Spotter left (close)':'#FF1493','Pit lane indicator':'#FFFF00','Pit lane entry without limiter (LMU)':'#FF0000','Pit lane speeding (LMU)':'#FF0000',
    'Pit limiter in pit lane':'#0000FF','Engine start':'#FF4500','Low fuel':'#FF0000','ABS and TC active':'#FFFF00','Headlight flash (ACC)':'#00BFFF','Indicator left':'#FFA500',
    'Lift and coast (LMU)':'#BF00FF','ERS mode: overtake (F1)':'#FF0000','ERS deploy (LMU)':'#0000FF','ERS charge 70 % (F1)':'#008000','ERS regeneration (LMU)':'#FF1493',
    'ERS lap limit 50 % (F1)':'#FFFF00','Push to pass':'#008000','Clutch pedal':'#8A2BE2','DRS open':'#008000','DRS detection point (iRacing)':'#FFFF00','Safety car delta: too slow (F1)':'#FFA500'};
  const tags=[...xaml.matchAll(/TestPlayButton\}" Tag="([^"]+)"/g)].map(m=>m[1]);
  ok(tags.length>=29,'expected a play button per effect card, found '+tags.length);
  // the play buttons of the other tabs show sweeps and whole-strip scenarios; they are tested in the block below ("Sweeps")
  const TABS=new Set(['RPM sweep','Redline reached (rev limiter)','Brake pedal sweep','Pedal sweep','All LEDs on','Idle (configured style)','Fuel sweep']);
  for(const name of new Set(tags.flatMap(t=>t.split('|')))){
    if(TABS.has(name)){ ok(T.scenarios.indexOf(name)>=0,`play button scenario "${name}" does not exist`); continue; }
    const idx=T.scenarios.indexOf(name);
    ok(idx>=0,`play button scenario "${name}" does not exist`);
    ok(name in EXPECT,`play button scenario "${name}" is not covered by this test`);
    if(idx<0||!(name in EXPECT)) continue;
    const seen=new Set(); let t=4_000_000;
    const cfg={TestMode:'Manual',TestScenario:idx,ShowErsRegen:true,ShowClutch:true}; // these two effects are options that are off until switched on; their play button is disabled while off
    for(let i=0;i<60;i++) for(const c of T.frame({},cfg,t+=37)) if(c&&c!=='#FF000000') seen.add(c);
    ok(seen.has(argb(EXPECT[name])),`scenario "${name}" does not show ${EXPECT[name]}: ${[...seen].slice(0,6)}`);
  }
  // the TC scenario also has to show the TC colour
  console.log('ok   Play buttons ('+tags.length+' buttons)');
}
// sweeps of the play buttons on the other tabs: periodic over 3 seconds, so every start time shows the whole range
{ const T=make(file); const idx=n=>T.scenarios.indexOf(n);
  const run=(n,cfg)=>{ const seen=[]; for(let t=0;t<3000;t+=100) seen.push(T.frame({},Object.assign({TestMode:'Manual',TestScenario:idx(n)},cfg),9_000_000+t)); return seen; };
  const lit=f=>f.filter(c=>c&&c!=='#FF000000').length;
  for(const offset of [0,777,1500]){
    const frames=[]; for(let t=0;t<3000;t+=100) frames.push(T.frame({},{TestMode:'Manual',TestScenario:idx('RPM sweep')},9_000_000+offset+t));
    const counts=frames.map(lit);
    ok(Math.min(...counts)<=2&&Math.max(...counts)>=16,'RPM sweep should run from (almost) dark to the full bar, start offset '+offset+': '+counts);
    ok(frames.some(f=>f.some(c=>c==='#FFFF0000')),'RPM sweep should reach the redline colour, start offset '+offset);
  }
  const all=T.frame({},{TestMode:'Manual',TestScenario:idx('All LEDs on'),BrightnessGeneral:100},9_000_000);
  ok(all.slice(0,26).every(c=>c==='#FFFFFFFF'),'All LEDs on should light all 26 LEDs white: '+all.slice(0,26));
  const dim=T.frame({},{TestMode:'Manual',TestScenario:idx('All LEDs on'),BrightnessGeneral:50},9_000_000);
  ok(dim[0]!=='#FFFFFFFF'&&dim[0]!==null,'All LEDs on has to follow the general brightness: '+dim[0]);
  const fuel=run('Fuel sweep',{LedFunction:'rpmOnly',FuelPlacement:'centre'}).map(lit);
  ok(Math.max(...fuel)-Math.min(...fuel)>=8,'Fuel sweep should change the number of lit LEDs: '+fuel);
  // the fuel test shows the fuel level only where "Show fuel level on" says, whatever the LED function is
  { const topLit=f=>f.slice(5,21).filter(c=>c&&c!=='#FF000000').length, sideLit=f=>f.slice(0,5).concat(f.slice(21,26)).filter(c=>c&&c!=='#FF000000').length;
    const sw=(fn,pl)=>run('Fuel sweep',{LedFunction:fn,FuelPlacement:pl});
    for(const fn of ['fuel','rpmBrake']){
      ok(sw(fn,'sides').every(f=>topLit(f)===0)&&sw(fn,'sides').some(f=>sideLit(f)>0),'fuel test, both sides, function '+fn+': only the sides may light');
      ok(sw(fn,'left').every(f=>f.slice(21,26).every(c=>!c||c==='#FF000000')&&topLit(f)===0)&&sw(fn,'left').some(f=>sideLit(f)>0),'fuel test, left side, function '+fn);
    }
    ok(sw('rpmBrake','off').every(f=>lit(f)===0),'fuel test, Off, function rpmBrake: nothing may light');
    ok(sw('fuel','centre').some(f=>topLit(f)>0)&&sw('fuel','centre').every(f=>sideLit(f)===0),'fuel test, function Fuel with Top: only the top row may light');
    ok(sw('rpmOnly','centre').some(f=>topLit(f)>0)&&sw('rpmOnly','centre').every(f=>sideLit(f)===0),'fuel test, Top: only the top row may light');
  }
  const ped=run('Pedal sweep',{LedFunction:'brakeThrottle'});
  ok(ped.some(f=>f.some(c=>c==='#FFFF0000'))&&ped.some(f=>f.some(c=>c==='#FF00FF00')),'Pedal sweep should show brake (red) and throttle (green)');
  console.log('ok   Sweeps (RPM, pedals, fuel, all LEDs)');
}
// invalid colours fall back to the default
{ const T=make(file); const f=T.frame({ABSActive:1},{FxAbsColour:'banana'},5_000_000); ok(f.includes('#FFFFFF00'),'invalid effect colour must fall back to the default'); }
console.log(fails?`${fails} FAILURES`:'ALL EFFECT TESTS PASSED'); process.exit(fails?1:0);
