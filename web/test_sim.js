// GreyZone demo - headless tests for the simulation layer (node, no browser).
// Run: node web/test_sim.js
'use strict';
const S = require('./src/sim.js');

let fails = 0;
function check(name, ok, detail) {
    if (ok) console.log('PASS ' + name);
    else { fails++; console.log('FAIL ' + name + ' : ' + (detail || '')); }
}
const DT = 1 / 64;

function flatWorld() { return S.worldFromBoxes([S.makeBox(S.V(-40, -1, -40), S.V(40, 0, 40))]); }
function worldWith(boxes) { return S.worldFromBoxes([S.makeBox(S.V(-40, -1, -40), S.V(40, 0, 40))].concat(boxes)); }
function player() { const st = S.makeMoveState(S.V(0, 0, 0)); st.onGround = true; return st; }
function input(fwd, yaw) { return { forward: fwd, right: 0, jump: false, duck: false, walk: false, yaw: yaw || 0 }; }
function sim(st, cfg, inp, mult, world, seconds) {
    const steps = Math.round(seconds / DT);
    for (let i = 0; i < steps; i++) S.stepMove(st, cfg, inp, mult, world, DT);
}
function hspeed(st) { return Math.sqrt(st.vel.x * st.vel.x + st.vel.z * st.vel.z); }

// ---------------------------------------------------------------- movement
(function () {
    const cfg = Object.assign({}, S.DEFAULT_MOVE), w = flatWorld(), st = player();
    sim(st, cfg, input(1), 1, w, 2);
    check('move.full_speed', Math.abs(hspeed(st) - cfg.MaxSpeed) < cfg.MaxSpeed * 0.05, 'speed=' + hspeed(st).toFixed(3));
})();

(function () {
    const cfg = Object.assign({}, S.DEFAULT_MOVE), w = flatWorld(), st = player();
    sim(st, cfg, input(1), 1, w, 2);
    const p0 = { x: st.pos.x, y: st.pos.y, z: st.pos.z };
    const idle = { forward: 0, right: 0, jump: false, duck: false, walk: false, yaw: 0 };
    sim(st, cfg, idle, 1, w, 0.5);
    const dist = Math.sqrt(Math.pow(st.pos.x - p0.x, 2) + Math.pow(st.pos.z - p0.z, 2));
    check('move.stop_speed', hspeed(st) < 0.1, 'speed=' + hspeed(st).toFixed(3));
    check('move.stop_distance', dist < 1.3, 'dist=' + dist.toFixed(3));
})();

(function () {
    const cfg = Object.assign({}, S.DEFAULT_MOVE), w = flatWorld();
    const sw = player(); sim(sw, cfg, Object.assign(input(1), { walk: true }), 1, w, 2);
    const expectW = cfg.MaxSpeed * cfg.WalkMultiplier;
    check('move.walk_speed', Math.abs(hspeed(sw) - expectW) < expectW * 0.05, 'speed=' + hspeed(sw).toFixed(3));
    const sd = player(); sim(sd, cfg, Object.assign(input(1), { duck: true }), 1, w, 2);
    const expectD = cfg.MaxSpeed * cfg.DuckMultiplier;
    check('move.duck_speed', Math.abs(hspeed(sd) - expectD) < expectD * 0.05, 'speed=' + hspeed(sd).toFixed(3));
})();

(function () {
    const cfg = Object.assign({}, S.DEFAULT_MOVE), w = flatWorld(), st = player();
    let maxY = 0, doubleJump = false;
    for (let k = 0; k < 200; k++) {
        const inp = { forward: 0, right: 0, jump: k === 0 || (k > 12 && k < 150), duck: false, walk: false, yaw: 0 };
        const wasGrounded = st.onGround, vyBefore = st.vel.y;
        S.stepMove(st, cfg, inp, 1, w, DT);
        if (!wasGrounded && !st.onGround && st.vel.y > vyBefore + 0.01) doubleJump = true;
        if (st.pos.y > maxY) maxY = st.pos.y;
    }
    check('move.jump_apex', maxY > 1.2 && maxY < 1.7, 'apex=' + maxY.toFixed(3));
    check('move.no_double_jump', !doubleJump, 'upward velocity while airborne');
})();

(function () {
    const cfg = Object.assign({}, S.DEFAULT_MOVE);
    const w = worldWith([S.makeBox(S.V(-2, 0, 1), S.V(2, 0.3, 3))]);
    const st = player();
    sim(st, cfg, input(1), 1, w, 0.47);
    check('move.step_up', st.pos.y > 0.25 && st.pos.z > 1.0 && st.pos.z < 2.9, 'pos=' + JSON.stringify(st.pos));
    const w2 = worldWith([S.makeBox(S.V(-2, 0, 1), S.V(2, 1.0, 3))]);
    const st2 = player();
    sim(st2, cfg, input(1), 1, w2, 1.5);
    check('move.wall_block', st2.pos.z < 0.75 && st2.pos.y < 0.1, 'pos=' + JSON.stringify(st2.pos));
    const w3 = worldWith([S.makeBox(S.V(-20, 0, 3), S.V(20, 3, 4))]);
    const st3 = player();
    const diag = { forward: 1, right: 1, jump: false, duck: false, walk: false, yaw: 0 };
    sim(st3, cfg, diag, 1, w3, 2);
    check('move.wall_slide', st3.pos.x > 1.5 && st3.pos.z < 2.75, 'pos=' + JSON.stringify(st3.pos));
})();

// ---------------------------------------------------------------- weapons
(function () {
    const ak = S.WEAPONS.ak47;
    const st = S.makeWeaponState(ak);
    const f1 = S.tryFire(st, 0, 0.5, 0.5);
    const f2 = S.tryFire(st, 0.05, 0.5, 0.5);
    const f3 = S.tryFire(st, 0.11, 0.5, 0.5);
    check('weapon.rate_limit', f1 && !f2 && f3, 'f1=' + f1 + ' f2=' + f2 + ' f3=' + f3);

    let t = 0.3;
    for (let i = 0; i < 40; i++) { S.tryFire(st, t, 0.5, 0.5); t += 0.1; }
    check('weapon.mag_empty', st.ammo === 0, 'ammo=' + st.ammo);
    const started = S.tryReload(st, t);
    S.updateWeapon(st, t + ak.reloadTime + 0.01, 0.02);
    check('weapon.reload', started && st.ammo === ak.magSize && st.reserve === ak.reserveAmmo - ak.magSize,
        'ammo=' + st.ammo + ' reserve=' + st.reserve);
})();

(function () {
    const ak = S.WEAPONS.ak47;
    const st = S.makeWeaponState(ak);
    let t = 0;
    for (let i = 0; i < 10; i++) { S.tryFire(st, t, 0.5, 0.5); t += 0.11; }
    check('recoil.spray_climb', st.recoilPitch > 10, 'pitch=' + st.recoilPitch.toFixed(2));
    let now = t;
    for (let i = 0; i < Math.round(2.5 / DT); i++) { S.updateWeapon(st, now, DT); now += DT; }
    check('recoil.recovery', Math.abs(st.recoilPitch) < 0.01 && Math.abs(st.recoilYaw) < 0.01, 'pitch=' + st.recoilPitch);
    check('recoil.index_reset', st.shotIndex === 0, 'index=' + st.shotIndex);
})();

(function () {
    const ak = S.WEAPONS.ak47;
    const stand = S.computeSpread(ak, 0, true, false);
    const move = S.computeSpread(ak, 1, true, false);
    const air = S.computeSpread(ak, 0, false, false);
    const duck = S.computeSpread(ak, 0, true, true);
    check('spread.states', Math.abs(stand - 0.35) < 0.01 && Math.abs(move - 3.55) < 0.01 &&
        Math.abs(air - 6.35) < 0.02 && Math.abs(duck - 0.245) < 0.01,
        'stand=' + stand + ' move=' + move + ' air=' + air + ' duck=' + duck);
})();

// ---------------------------------------------------------------- ballistics / economy
(function () {
    const ak = S.WEAPONS.ak47;
    const d0 = S.damageAtDistance(ak, 0);
    const armor = { v: 100 };
    const hp = S.applyArmor(36, ak.armorPen, armor);
    check('damage.ak_armor', d0 === 36 && hp === 28 && armor.v === 96, 'd0=' + d0 + ' hp=' + hp + ' armor=' + armor.v);
    const awp = S.WEAPONS.awp;
    const a2 = { v: 100 };
    const hp2 = S.applyArmor(awp.damage, awp.armorPen, a2);
    check('damage.awp_lethal', hp2 >= 100, 'hp=' + hp2);
    check('economy.loss_ladder', S.lossReward(1) === 1400 && S.lossReward(5) === 3400 && S.lossReward(9) === 3400,
        S.lossReward(1) + '/' + S.lossReward(5) + '/' + S.lossReward(9));
    check('economy.clamp', S.clampMoney(20000) === 16000 && S.clampMoney(-5) === 0, S.clampMoney(20000) + '/' + S.clampMoney(-5));
    check('weapon.prices', S.WEAPONS.ak47.price === 2700 && S.WEAPONS.awp.price === 4750 && S.ECON.armor === 650, 'prices');
})();

// ---------------------------------------------------------------- world / bots smoke test
(function () {
    const W = S.makeWorld();
    check('world.actors', W.actors.length === 8, 'actors=' + W.actors.length);
    const idle = { forward: 0, right: 0, jump: false, duck: false, walk: false, yaw: 0 };
    const startPos = W.actors.map(a => ({ x: a.st.pos.x, z: a.st.pos.z }));
    let moved = 0;
    for (let i = 0; i < Math.round(12.5 / DT); i++) W.update(DT, idle);
    for (let i = 0; i < W.actors.length; i++) {
        const a = W.actors[i];
        if (a.isPlayer) continue;
        const d = Math.sqrt(Math.pow(a.st.pos.x - startPos[i].x, 2) + Math.pow(a.st.pos.z - startPos[i].z, 2));
        if (d > 0.5) moved++;
    }
    check('world.bots_move', moved >= 4, 'bots that moved: ' + moved);
    check('world.phase_progressed', W.phase === 'live' || W.phase === 'end', 'phase=' + W.phase);

    // scripted player: look north and fire
    const p = W.player;
    p.yaw = 180; p.pitch = 0;
    const pst = S.stateOf(p);
    check('world.player_has_gun', !!pst, 'weapon state missing');
    const before = pst.ammo;
    W.fireShot(W, p, p.yaw, p.pitch, true);
    check('world.player_fire', pst.ammo === before - 1, 'ammo ' + before + ' -> ' + pst.ammo);

    // plant + resolve path
    W.phase = 'live';
    p.hasBomb = true;
    p.st.pos = S.V(21, 0, 10);   // inside site A
    W.plantBomb(W, p, 'A');
    check('world.plant', W.bomb.planted && W.bomb.timer === S.RULES.bombTimer, 'planted=' + W.bomb.planted);
    let sawResolve = false;
    for (let i = 0; i < Math.round(45 / DT); i++) {
        W.update(DT, idle);
        if (W.bomb.exploded || W.bomb.defused || W.phase === 'end' || W.phase === 'halftime' || W.phase === 'matchover') sawResolve = true;
    }
    check('world.round_resolves', sawResolve, 'phase=' + W.phase + ' score=' + W.scoreT + ':' + W.scoreCT);
})();

console.log('');
if (fails === 0) { console.log('ALL SIM TESTS PASSED'); process.exit(0); }
console.log(fails + ' TEST(S) FAILED');
process.exit(1);
