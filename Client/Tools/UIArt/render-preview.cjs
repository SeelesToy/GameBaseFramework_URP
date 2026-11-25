const fs=require('fs'),path=require('path'),os=require('os'),{spawnSync}=require('child_process'),{pathToFileURL}=require('url');
const edge='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'card-ui-preview-'));
const screenshot=path.resolve('Artifacts/UI/ui-preview.png');
const result=spawnSync(edge,['--headless=new','--no-sandbox','--disable-gpu','--disable-gpu-sandbox','--in-process-gpu','--no-first-run','--no-default-browser-check','--allow-file-access-from-files','--hide-scrollbars','--force-device-scale-factor=1','--window-size=1600,1230','--virtual-time-budget=5000','--user-data-dir='+profile,'--screenshot='+screenshot,pathToFileURL(path.resolve('Artifacts/UI/ui-preview.html')).href],{windowsHide:true,timeout:60000,encoding:'utf8'});
if(!fs.existsSync(screenshot))throw Error(result.error||result.stderr||'Screenshot was not created');
console.log(JSON.stringify({screenshot,bytes:fs.statSync(screenshot).size,exitCode:result.status}));
