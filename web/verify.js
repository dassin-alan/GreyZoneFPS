// GreyZone demo - headless browser verification + screenshots (Playwright + installed Edge).
// Run: node web/verify.js
'use strict';
const fs = require('fs');
let pw;
try { pw = require('playwright'); } catch (e) { pw = require('D:/npm_global/node_modules/playwright'); }

const DEMO = 'file:///D:/Projects/GreyZoneFPS/GreyZone_Demo.html';
const SHOTS = 'D:/Projects/GreyZoneFPS/web/shots/';

let fails = 0;
function check(name, ok, detail) {
    if (ok) console.log('PASS ' + name);
    else { fails++; console.log('FAIL ' + name + ' : ' + (detail === undefined ? '' : JSON.stringify(detail))); }
}

(async () => {
    if (!fs.existsSync(SHOTS)) fs.mkdirSync(SHOTS, { recursive: true });
    const browser = await pw.chromium.launch({
        channel: 'msedge', headless: true,
        args: ['--enable-unsafe-swiftshader', '--use-gl=swiftshader', '--disable-gpu-sandbox']
    });
    const errors = [];
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    page.on('pageerror', e => errors.push('pageerror: ' + e.message));
    page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });

    await page.goto(DEMO, { waitUntil: 'load' });
    await page.waitForFunction('window.GZ && window.GZ.ready', null, { timeout: 30000 });
    await page.waitForTimeout(900);

    check('page.no_errors', errors.length === 0, errors);
    await page.screenshot({ path: SHOTS + '01_spawn.png' });

    // ---- deterministic drive (rAF sim paused)
    const boot = await page.evaluate(() => {
        GZ.debug.paused = true;
        const st = GZ.debug.stats();
        const gl = document.getElementById('gl').getContext('webgl');
        return { phase: st.phase, actors: st.aliveT + st.aliveCT, weapon: st.weapon, money: st.money, glError: gl ? gl.getError() : -1, hasGl: !!gl };
    });
    check('boot.gl_context', boot.hasGl && boot.glError === 0, boot);
    check('boot.round_state', boot.phase === 'freeze' && boot.actors === 8, boot);
    check('boot.default_pistol', boot.weapon === 'glock' && boot.money === 800, boot);

    // render sanity: sample the WebGL framebuffer, count distinct-ish colours
    const pixels = await page.evaluate(() => {
        const c = document.getElementById('gl');
        const gl = c.getContext('webgl');
        const w = c.width, h = c.height;
        const buf = new Uint8Array(4 * 200);
        const seen = {};
        let n = 0;
        for (let i = 0; i < 200; i++) {
            const x = Math.floor((i % 20) * w / 20 + 3);
            const y = Math.floor(Math.floor(i / 20) * h / 10 + 3);
            gl.readPixels(x, y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, buf.subarray(i * 4, i * 4 + 4));
        }
        for (let i = 0; i < 200; i++) {
            const key = buf[i * 4] + ',' + buf[i * 4 + 1] + ',' + buf[i * 4 + 2];
            if (!seen[key]) { seen[key] = 1; n++; }
        }
        return n;
    });
    check('render.distinct_colours', pixels > 20, 'distinct=' + pixels);

    // ---- movement (freeze must lock, live must move)
    const move = await page.evaluate(() => {
        GZ.debug.look(0, 0);
        GZ.debug.setInput({ forward: 1, right: 0, walk: false, duck: false, jump: false });
        const z0 = GZ.debug.stats().pos.z;
        GZ.debug.step(64);                       // still freeze
        const zFrozen = GZ.debug.stats().pos.z;
        GZ.debug.phase('live');
        GZ.debug.step(128);                      // 2 s live
        const z1 = GZ.debug.stats().pos.z;
        GZ.debug.setInput({ forward: 0, right: 0 });
        return { z0, zFrozen, z1 };
    });
    check('move.freeze_locks', Math.abs(move.zFrozen - move.z0) < 0.01, move);
    check('move.live_moves', move.z1 - move.zFrozen > 4, move);

    // ---- firing
    const fire = await page.evaluate(() => {
        const a0 = GZ.debug.stats().ammo;
        GZ.debug.fire();
        const a1 = GZ.debug.stats().ammo;
        GZ.debug.fire();                         // too soon: rate limited
        const a2 = GZ.debug.stats().ammo;
        return { a0, a1, a2 };
    });
    check('fire.uses_round', fire.a1 === fire.a0 - 1, fire);
    check('fire.rate_limited', fire.a2 === fire.a1, fire);

    // ---- buying
    const buy = await page.evaluate(() => {
        GZ.debug.money(16000);
        const ok1 = GZ.debug.buy('ak47');
        const ok2 = GZ.debug.buy('armor');
        GZ.debug.buy('kit');
        const st = GZ.debug.stats();
        return { ok1, ok2, weapon: st.weapon, armor: st.armor, money: st.money };
    });
    check('buy.ak_and_armor', buy.ok1 && buy.ok2 && buy.weapon === 'ak47' && buy.armor === 100, buy);

    // ---- shooting a bot (deterministic: stand in front of a CT bot and fire)
    const combat = await page.evaluate(() => {
        GZ.debug.phase('live');
        const W = GZ.W;
        const bot = W.actors.find(a => a.team === 'CT' && a.alive);
        const P = W.player;
        P.st.pos = { x: bot.st.pos.x, y: 0, z: bot.st.pos.z - 6 };
        P.st.vel = { x: 0, y: 0, z: 0 };
        const yaw = Math.atan2(bot.st.pos.x - P.st.pos.x, bot.st.pos.z - P.st.pos.z) * 180 / Math.PI;
        GZ.debug.look(yaw, 0);
        const hp0 = bot.health;
        let fired = 0;
        for (let i = 0; i < 8; i++) { if (GZ.debug.fire()) fired++; GZ.debug.step(8); }
        return { hp0, hp1: bot.health, alive: bot.alive, fired, weapon: GZ.debug.stats().weapon };
    });
    check('combat.bot_takes_damage', combat.alive === false || combat.hp1 < combat.hp0, combat);

    // ---- killfeed / round flow / economy on round end
    const flow = await page.evaluate(() => {
        const W = GZ.W;
        GZ.debug.money(2000);
        W.player.hasBomb = true;
        W.player.st.pos = { x: 21, y: 0, z: 10 };   // site A
        W.plantBomb(W, W.player, 'A');
        const planted = W.bomb.planted;
        GZ.debug.phase('live');
        let sawEnd = false;
        for (let i = 0; i < 64 * 46; i++) { GZ.debug.step(1); if (W.phase === 'end' || W.bomb.exploded || W.bomb.defused) { sawEnd = true; break; } }
        const st = GZ.debug.stats();
        return { planted, sawEnd, score: st.score, money: st.money, killfeed: st.killfeed.slice(0, 3) };
    });
    check('flow.bomb_round_resolves', flow.planted && flow.sawEnd, flow);

    // ---- screenshots: spawn, site A, buy menu, tuner, combat
    await page.evaluate(() => {
        const W = GZ.W;
        W.startRound(true);
        GZ.debug.pause = true;
        GZ.debug.paused = true;
        GZ.debug.look(0, 0);
        GZ.debug.place(0, -16);
        GZ.debug.step(2);
    });
    await page.screenshot({ path: SHOTS + '02_look_north.png' });

    await page.evaluate(() => { GZ.debug.place(20, 4); GZ.debug.look(0, 0); GZ.debug.step(2); });
    await page.screenshot({ path: SHOTS + '03_site_a.png' });

    await page.evaluate(() => { GZ.debug.place(-20, 4); GZ.debug.look(0, -4); GZ.debug.step(2); });
    await page.screenshot({ path: SHOTS + '04_site_b.png' });

    // buy menu (DOM) - force open + fill money
    await page.evaluate(() => { GZ.debug.money(16000); GZ.debug.phase('freeze'); });
    await page.keyboard.press('KeyB');
    await page.waitForTimeout(150);
    await page.screenshot({ path: SHOTS + '05_buy_menu.png' });
    await page.keyboard.press('KeyB');

    // tuner panel
    await page.keyboard.press('F1');
    await page.waitForTimeout(150);
    await page.screenshot({ path: SHOTS + '06_tuner.png' });
    await page.keyboard.press('F1');

    // fight: stand facing CT spawn with bots pushing
    await page.evaluate(() => {
        GZ.debug.phase('live');
        GZ.debug.place(0, 6);
        GZ.debug.look(180, 0);
        GZ.debug.setInput({ forward: -1, right: 0 });
        GZ.debug.step(180);
        GZ.debug.setInput({ forward: 0, right: 0 });
    });
    await page.screenshot({ path: SHOTS + '07_fight.png' });

    // ---- mobile viewport with touch UI
    const mobile = await browser.newPage({ viewport: { width: 844, height: 390 }, hasTouch: true, isMobile: true });
    await mobile.goto(DEMO + '?touch=1', { waitUntil: 'load' });
    await mobile.waitForFunction('window.GZ && window.GZ.ready', null, { timeout: 30000 });
    await mobile.waitForTimeout(800);
    await mobile.screenshot({ path: SHOTS + '08_touch_ui.png' });
    const touchUi = await mobile.evaluate(() => document.getElementById('touch').style.display);
    check('touch.ui_visible', touchUi === 'block', touchUi);

    check('page.no_errors_final', errors.length === 0, errors);
    await browser.close();

    console.log('');
    console.log('screenshots -> ' + SHOTS);
    if (fails === 0) { console.log('ALL BROWSER CHECKS PASSED'); process.exit(0); }
    console.log(fails + ' CHECK(S) FAILED');
    process.exit(1);
})().catch(e => { console.error('verify crashed: ' + (e && e.stack || e)); process.exit(2); });
