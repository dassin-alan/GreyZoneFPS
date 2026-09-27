<#
    GreyZone demo - headless verification via playwright-cli (Edge) + screenshots.
    Serves the demo over http (playwright-cli blocks file://), drives the debug API, captures shots.
    Usage: powershell -ExecutionPolicy Bypass -File web\verify_cli.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
$cli = 'D:\npm_global\playwright-cli.cmd'
$root = Split-Path -Parent $PSScriptRoot
$url = 'http://127.0.0.1:8777/GreyZone_Demo.html'
$shots = 'D:/Projects/GreyZoneFPS/web/shots'
if (-not (Test-Path -LiteralPath $shots)) { New-Item -ItemType Directory -Force -Path $shots | Out-Null }

function RunCli {
    param([string[]]$argv)
    $out = & $cli @argv 2>&1 | Out-String
    return $out.Trim()
}

Write-Host '--- start static server ---'
$srv = Start-Process -FilePath 'node' -ArgumentList (Join-Path $PSScriptRoot 'serve.js') -PassThru -WindowStyle Hidden
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 250
    try { $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2; if ($r.StatusCode -eq 200) { $ready = $true; break } } catch { }
}
Write-Host "server ready: $ready (pid $($srv.Id))"

Write-Host '--- open + load ---'
RunCli @('open', '--browser=msedge') | Write-Host
RunCli @('goto', $url) | Write-Host
Start-Sleep -Seconds 4
RunCli @('eval', '!!(window.GZ && window.GZ.ready)') | Write-Host
RunCli @('eval', 'JSON.stringify(GZ.debug.stats())') | Write-Host

Write-Host '--- screenshot: spawn ---'
RunCli @('screenshot', "--filename=$shots/10_spawn.png") | Write-Host

Write-Host '--- movement (freeze lock vs live) ---'
RunCli @('eval', '(()=>{GZ.debug.paused=true;GZ.debug.look(0,0);GZ.debug.setInput({forward:1,right:0});const z0=GZ.debug.stats().pos.z;GZ.debug.step(64);const zf=GZ.debug.stats().pos.z;GZ.debug.phase("live");GZ.debug.step(128);const z1=GZ.debug.stats().pos.z;GZ.debug.setInput({forward:0,right:0});return JSON.stringify({z0:z0,zFrozen:zf,z1:z1});})()') | Write-Host

Write-Host '--- firing ---'
RunCli @('eval', '(()=>{const a0=GZ.debug.stats().ammo;const f1=GZ.debug.fire();const a1=GZ.debug.stats().ammo;const f2=GZ.debug.fire();const a2=GZ.debug.stats().ammo;return JSON.stringify({a0:a0,f1:f1,a1:a1,f2:f2,a2:a2});})()') | Write-Host

Write-Host '--- buying ---'
RunCli @('eval', '(()=>{GZ.debug.money(16000);const ok1=GZ.debug.buy("ak47");const ok2=GZ.debug.buy("armor");const st=GZ.debug.stats();return JSON.stringify({ok1:ok1,ok2:ok2,weapon:st.weapon,armor:st.armor,money:st.money});})()') | Write-Host

Write-Host '--- combat vs bot ---'
RunCli @('eval', '(()=>{GZ.debug.phase("live");const W=GZ.W;const bot=W.actors.find(a=>a.team==="CT"&&a.alive);const P=W.player;P.st.pos={x:bot.st.pos.x,y:0,z:bot.st.pos.z-6};P.st.vel={x:0,y:0,z:0};const yaw=Math.atan2(bot.st.pos.x-P.st.pos.x,bot.st.pos.z-P.st.pos.z)*180/Math.PI;GZ.debug.look(yaw,0);const hp0=bot.health;let fired=0;for(let i=0;i<8;i++){if(GZ.debug.fire())fired++;GZ.debug.step(8);}return JSON.stringify({hp0:hp0,hp1:bot.health,alive:bot.alive,fired:fired});})()') | Write-Host

Write-Host '--- round flow (plant -> resolve) ---'
RunCli @('eval', '(()=>{const W=GZ.W;GZ.debug.money(2000);W.player.hasBomb=true;W.player.st.pos={x:21,y:0,z:10};W.plantBomb(W,W.player,"A");const planted=W.bomb.planted;let saw=false;for(let i=0;i<64*46;i++){GZ.debug.step(1);if(W.phase==="end"||W.bomb.exploded||W.bomb.defused){saw=true;break;}}const st=GZ.debug.stats();return JSON.stringify({planted:planted,sawResolve:saw,score:st.score,money:st.money});})()') | Write-Host

Write-Host '--- screenshots: site A / site B / fight ---'
RunCli @('eval', '(()=>{GZ.W.startRound(true);GZ.debug.paused=true;GZ.debug.place(20,4);GZ.debug.look(0,-2);GZ.debug.step(2);return 1;})()') | Write-Host
RunCli @('screenshot', "--filename=$shots/11_site_a.png") | Write-Host
RunCli @('eval', '(()=>{GZ.debug.place(-20,4);GZ.debug.look(0,-2);GZ.debug.step(2);return 1;})()') | Write-Host
RunCli @('screenshot', "--filename=$shots/12_site_b.png") | Write-Host
RunCli @('eval', '(()=>{GZ.debug.phase("live");GZ.debug.place(0,8);GZ.debug.look(180,0);GZ.debug.setInput({forward:-1,right:0});GZ.debug.step(200);GZ.debug.setInput({forward:0,right:0});GZ.debug.step(2);return JSON.stringify(GZ.debug.stats());})()') | Write-Host
RunCli @('screenshot', "--filename=$shots/13_fight.png") | Write-Host

Write-Host '--- buy menu + tuner screenshots ---'
RunCli @('eval', '(()=>{GZ.debug.money(16000);GZ.debug.phase("freeze");return 1;})()') | Write-Host
RunCli @('press', 'b') | Write-Host
Start-Sleep -Milliseconds 400
RunCli @('screenshot', "--filename=$shots/14_buy_menu.png") | Write-Host
RunCli @('press', 'b') | Write-Host
RunCli @('press', 'F1') | Write-Host
Start-Sleep -Milliseconds 400
RunCli @('screenshot', "--filename=$shots/15_tuner.png") | Write-Host

Write-Host '--- console errors ---'
RunCli @('console', 'error') | Write-Host

Write-Host '--- close + stop server ---'
RunCli @('close') | Write-Host
Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue
