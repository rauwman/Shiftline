// Every value the settings page can set must be accepted by the LED script (valid frame, no exception).
const fs=require('fs'),path=require('path');
const {make}=require('./harness.js');
const T=make(path.join(__dirname,'Shiftline.plugin.js'));
const ui=JSON.parse(fs.readFileSync(path.join(__dirname,'ui_values.json'),'utf8'));
const re=/^#[0-9A-F]{8}$/;
let clk=3_000_000,n=0,bad=0;
for(const [prop,values] of Object.entries(ui)){
  for(const v of values){
    for(const game of [1,0]){
      try{
        const r=T.frame({Rpms:7000,Brake:50,Throttle:60,FuelPercent:40,'DataCorePlugin.GameRunning':game},{[prop]:v},clk+=37); n++;
        if(r.length!==48||!r.every(c=>c===null||re.test(c))){bad++;console.log('BAD frame for',prop,v);}
      }catch(e){bad++;console.log('EXC',prop,v,e.message);}
    }
  }
}
console.log({uiValuesTried:n,bad});
process.exit(bad?1:0);
