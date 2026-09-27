// GreyZone demo - pure simulation layer (no DOM, no WebGL). Ported from the Unity Core:
// CS-style movement, weapon spray/inaccuracy, armour, economy, bomb rounds and simple bots.
// Runs in the browser (window.GZSim) and in node (module.exports) for headless testing.
(function (root) {
    'use strict';

    // ------------------------------------------------------------------ math
    function V(x, y, z) { return { x: x, y: y, z: z }; }
    function vadd(a, b) { return V(a.x + b.x, a.y + b.y, a.z + b.z); }
    function vsub(a, b) { return V(a.x - b.x, a.y - b.y, a.z - b.z); }
    function vmul(a, s) { return V(a.x * s, a.y * s, a.z * s); }
    function vdot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
    function vlen(a) { return Math.sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
    function vdist(a, b) { return vlen(vsub(a, b)); }
    function vnorm(a) { var l = vlen(a); return l < 1e-6 ? V(0, 0, 0) : vmul(a, 1 / l); }
    function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }
    function clamp01(v) { return clamp(v, 0, 1); }
    function lerp(a, b, t) { return a + (b - a) * t; }
    function wrapAngle(a) { while (a > 180) a -= 360; while (a < -180) a += 360; return a; }
    function moveTowardsAngle(cur, target, maxDelta) {
        var d = wrapAngle(target - cur);
        if (d > maxDelta) d = maxDelta;
        if (d < -maxDelta) d = -maxDelta;
        return cur + d;
    }
    function dirOf(yawDeg, pitchDeg) {
        var y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
        var cp = Math.cos(p);
        return V(Math.sin(y) * cp, Math.sin(p), Math.cos(y) * cp);
    }
    function yawOf(d) { return Math.atan2(d.x, d.z) * 180 / Math.PI; }
    function pitchOf(d) { return Math.atan2(d.y, Math.sqrt(d.x * d.x + d.z * d.z)) * 180 / Math.PI; }

    // ------------------------------------------------------------------ movement config (CS:GO reference)
    var DEFAULT_MOVE = {
        MaxSpeed: 6.35, Accel: 5.5, AirAccel: 12, AirMaxWishSpeed: 0.762,
        Friction: 5.2, StopSpeed: 2.032, Gravity: 20.32, JumpImpulse: 7.67,
        MaxSpeedCap: 8.128, WalkMultiplier: 0.52, DuckMultiplier: 0.34,
        StandHeight: 1.83, DuckHeight: 1.37, DuckTransitionTime: 0.20
    };
    var PLAYER_RADIUS = 0.4, STEP_HEIGHT = 0.45, JUMP_BUFFER_TIME = 0.10;
    var GROUND_SNAP = 0.08, GROUND_LIFT = 0.02, MIN_GROUND_NY = 0.6, MIN_WALL_NY = -0.05;

    function makeMoveState(pos) {
        return { pos: pos || V(0, 0, 0), vel: V(0, 0, 0), onGround: true, ducked: false, duckFrac: 0, jumpQueued: false, jumpBuf: 0 };
    }
    function currentHeight(cfg, st) { return lerp(cfg.StandHeight, cfg.DuckHeight, clamp01(st.duckFrac)); }
    function computeMaxSpeed(cfg, weaponMult, walking, ducked) {
        var m = cfg.MaxSpeed * weaponMult;
        if (walking) m *= cfg.WalkMultiplier;
        if (ducked) m *= cfg.DuckMultiplier;
        return m;
    }

    // ------------------------------------------------------------------ AABB collision world
    function makeBox(min, max) { return { min: min, max: max }; }
    function worldFromBoxes(boxes) {
        return { boxes: boxes };
    }

    // capsule approximated by its centre segment vs boxes expanded by the radius
    function sweepBoxes(world, feet, radius, height, delta) {
        var r = radius;
        var a = V(feet.x, feet.y + r, feet.z);
        var b = V(feet.x, feet.y + Math.max(r * 2 + 0.02, height) - r, feet.z);
        var bestT = 1.0000001, bestN = null, hit = false;
        var dl = vlen(delta);
        if (dl < 1e-6) return { hit: false, fraction: 1, normal: V(0, 1, 0) };
        var dirN = vmul(delta, 1 / dl);

        for (var i = 0; i < world.boxes.length; i++) {
            var box = world.boxes[i];
            var res = sweepSegmentBox(a, b, delta, box.min, box.max, r);
            if (!res) continue;
            if (res.t0 > 1) continue;
            var frac = clamp01(res.t0);
            var n = res.normal;
            if (frac <= 1e-4) {
                // touching: only blocks when the surface faces the motion (normalised!)
                if (vdot(n, dirN) >= -0.01) continue;
                if (frac < bestT) { bestT = frac; bestN = n; hit = true; continue; }
                continue;
            }
            if (frac < bestT) { bestT = frac; bestN = n; hit = true; }
        }
        if (!hit) return { hit: false, fraction: 1, normal: V(0, 1, 0) };
        return { hit: true, fraction: clamp01(bestT), normal: bestN };
    }

    // down-probe that only accepts walkable (up-facing) surfaces, so touching a wall
    // next to you never cancels ground contact
    function probeGroundDown(world, feet, radius, height, dist) {
        var r = radius;
        var a = V(feet.x, feet.y + r, feet.z);
        var b = V(feet.x, feet.y + Math.max(r * 2 + 0.02, height) - r, feet.z);
        var down = V(0, -dist, 0);
        var bestT = 1.0000001, normal = null, hit = false;
        for (var i = 0; i < world.boxes.length; i++) {
            var box = world.boxes[i];
            var res = sweepSegmentBox(a, b, down, box.min, box.max, r);
            if (!res || res.t0 > 1) continue;
            if (!(res.normal.y > MIN_GROUND_NY)) continue;
            if (res.t0 <= 0) return { hit: true, fraction: 0, normal: res.normal };
            if (res.t0 < bestT) { bestT = res.t0; normal = res.normal; hit = true; }
        }
        if (!hit) return { hit: false, fraction: 1, normal: V(0, 1, 0) };
        return { hit: true, fraction: clamp01(bestT), normal: normal };
    }

    function overlapBoxes(world, feet, radius, height) {
        var r = radius;
        var a = V(feet.x, feet.y + r, feet.z);
        var b = V(feet.x, feet.y + Math.max(r * 2 + 0.02, height) - r, feet.z);
        for (var i = 0; i < world.boxes.length; i++) {
            var box = world.boxes[i];
            var mn = V(box.min.x - r, box.min.y - r, box.min.z - r);
            var mx = V(box.max.x + r, box.max.y + r, box.max.z + r);
            if (segIntersectsBox(a, b, mn, mx)) return true;
        }
        return false;
    }

    function segIntersectsBox(a, b, mn, mx) {
        var d = vsub(b, a);
        var t0 = -Infinity, t1 = Infinity;
        for (var ax = 0; ax < 3; ax++) {
            var p = ax === 0 ? a.x : (ax === 1 ? a.y : a.z);
            var dd = ax === 0 ? d.x : (ax === 1 ? d.y : d.z);
            var lo = ax === 0 ? mn.x : (ax === 1 ? mn.y : mn.z);
            var hi = ax === 0 ? mx.x : (ax === 1 ? mx.y : mx.z);
            if (Math.abs(dd) < 1e-8) { if (p < lo || p > hi) return false; }
            else {
                var ta = (lo - p) / dd, tb = (hi - p) / dd;
                if (ta > tb) { var tmp = ta; ta = tb; tb = tmp; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
        }
        return t0 <= 1 && t1 >= 0;
    }

    // returns { t0, normal } or null. t0 may be <= 0 when the segment starts touching/inside.
    function sweepSegmentBox(a, b, delta, bmin, bmax, r) {
        var mn = V(bmin.x - r, bmin.y - r, bmin.z - r);
        var mx = V(bmax.x + r, bmax.y + r, bmax.z + r);
        var t0 = -Infinity, t1 = Infinity, axis = -1;
        for (var ax = 0; ax < 3; ax++) {
            var p = ax === 0 ? a.x : (ax === 1 ? a.y : a.z);
            var dd = ax === 0 ? delta.x : (ax === 1 ? delta.y : delta.z);
            var lo = ax === 0 ? mn.x : (ax === 1 ? mn.y : mn.z);
            var hi = ax === 0 ? mx.x : (ax === 1 ? mx.y : mx.z);
            if (Math.abs(dd) < 1e-8) {
                var pe = ax === 0 ? a.x : (ax === 1 ? a.y : a.z);
                if (pe < lo || pe > hi) return null;
            } else {
                var ta = (lo - p) / dd, tb = (hi - p) / dd;
                if (ta > tb) { var tmp = ta; ta = tb; tb = tmp; }
                if (ta > t0) { t0 = ta; axis = ax; }
                if (tb < t1) t1 = tb;
                if (t0 > t1) return null;
            }
        }
        if (axis < 0 || t1 < 0) return null;
        if (t0 <= 0) {
            // touching / overlapping: push-out normal = smallest normalised penetration
            var cx = (mn.x + mx.x) * 0.5, cy = (mn.y + mx.y) * 0.5, cz = (mn.z + mx.z) * 0.5;
            var ex = Math.max(1e-4, (mx.x - mn.x) * 0.5), ey = Math.max(1e-4, (mx.y - mn.y) * 0.5), ez = Math.max(1e-4, (mx.z - mn.z) * 0.5);
            var rx = (a.x - cx) / ex, ry = (a.y - cy) / ey, rz = (a.z - cz) / ez;
            var ax2 = Math.abs(rx), ay2 = Math.abs(ry), az2 = Math.abs(rz);
            var best = (ax2 >= ay2 && ax2 >= az2) ? 0 : (ay2 >= az2 ? 1 : 2);
            var val = best === 0 ? rx : (best === 1 ? ry : rz);
            var sgn = val >= 0 ? 1 : -1;
            var n = best === 0 ? V(sgn, 0, 0) : (best === 1 ? V(0, sgn, 0) : V(0, 0, sgn));
            return { t0: 0, normal: n };
        }
        var dax = axis === 0 ? delta.x : (axis === 1 ? delta.y : delta.z);
        var s = dax > 0 ? -1 : 1;
        var nn = axis === 0 ? V(s, 0, 0) : (axis === 1 ? V(0, s, 0) : V(0, 0, s));
        return { t0: t0, normal: nn };
    }

    // ray vs boxes, nearest hit up to maxDist (used for bullets / LOS)
    function raycastBoxes(world, origin, dir, maxDist) {
        var bestT = maxDist, bestN = null;
        for (var i = 0; i < world.boxes.length; i++) {
            var box = world.boxes[i];
            var t = rayBox(origin, dir, box.min, box.max);
            if (t === null) continue;
            if (t < bestT) {
                bestT = t;
                bestN = hitNormalFor(origin, dir, t, box.min, box.max);
            }
        }
        return { hit: bestN !== null, dist: bestT, point: vadd(origin, vmul(dir, bestT)), normal: bestN || V(0, 1, 0) };
    }

    function rayBox(o, d, mn, mx) {
        var t0 = 0, t1 = Infinity, axis = -1;
        for (var ax = 0; ax < 3; ax++) {
            var p = ax === 0 ? o.x : (ax === 1 ? o.y : o.z);
            var dd = ax === 0 ? d.x : (ax === 1 ? d.y : d.z);
            var lo = ax === 0 ? mn.x : (ax === 1 ? mn.y : mn.z);
            var hi = ax === 0 ? mx.x : (ax === 1 ? mx.y : mx.z);
            if (Math.abs(dd) < 1e-9) { if (p < lo || p > hi) return null; }
            else {
                var ta = (lo - p) / dd, tb = (hi - p) / dd;
                if (ta > tb) { var tmp = ta; ta = tb; tb = tmp; }
                if (ta > t0) { t0 = ta; axis = ax; }
                if (tb < t1) t1 = tb;
                if (t0 > t1) return null;
            }
        }
        if (axis < 0) return null;
        return t0;
    }

    function hitNormalFor(o, d, t, mn, mx) {
        var p = vadd(o, vmul(d, t));
        var eps = 0.001;
        if (Math.abs(p.x - mn.x) < eps) return V(-1, 0, 0);
        if (Math.abs(p.x - mx.x) < eps) return V(1, 0, 0);
        if (Math.abs(p.y - mn.y) < eps) return V(0, -1, 0);
        if (Math.abs(p.y - mx.y) < eps) return V(0, 1, 0);
        if (Math.abs(p.z - mn.z) < eps) return V(0, 0, -1);
        return V(0, 0, 1);
    }

    // ------------------------------------------------------------------ movement step (port of PlayerMover.Step)
    function stepMove(st, cfg, input, weaponMult, world, dt) {
        if (dt <= 0) return;
        if (dt > 0.1) dt = 0.1;

        // duck transition
        var rate = 1 / cfg.DuckTransitionTime;
        var target = input.duck ? 1 : 0;
        if (st.duckFrac < target) st.duckFrac = Math.min(target, st.duckFrac + rate * dt);
        else if (st.duckFrac > target) {
            var want = Math.max(target, st.duckFrac - rate * dt);
            var hWant = lerp(cfg.StandHeight, cfg.DuckHeight, want);
            if (!overlapBoxes(world, st.pos, PLAYER_RADIUS, hWant)) st.duckFrac = want;
        }
        st.ducked = st.duckFrac > 0.5;

        var yaw = (input.yaw || 0) * Math.PI / 180;
        var sn = Math.sin(yaw), cs = Math.cos(yaw);
        var fwd = V(sn, 0, cs), right = V(cs, 0, -sn);
        var wishLen2 = input.forward * input.forward + input.right * input.right;
        var wishLen = wishLen2 > 1e-6 ? Math.sqrt(wishLen2) : 0;
        wishLen = Math.min(wishLen, 1);
        var wishDir = wishLen > 1e-5 ? vnorm(vadd(vmul(fwd, input.forward), vmul(right, input.right))) : V(0, 0, 0);
        var maxSpeed = computeMaxSpeed(cfg, weaponMult, !!input.walk, st.ducked);

        // friction
        if (st.onGround) {
            var sp = Math.sqrt(st.vel.x * st.vel.x + st.vel.z * st.vel.z);
            if (sp > 1e-4) {
                var control = sp < cfg.StopSpeed ? cfg.StopSpeed : sp;
                var drop = control * cfg.Friction * dt;
                var ns = Math.max(0, sp - drop);
                var scale = ns / sp;
                st.vel.x *= scale; st.vel.z *= scale;
            }
        }
        // accelerate
        if (wishLen > 1e-5) {
            var fullWish = maxSpeed * wishLen;
            var wishSpeed, accel;
            if (st.onGround) { accel = cfg.Accel; wishSpeed = fullWish; }
            else { accel = cfg.AirAccel; wishSpeed = Math.min(fullWish, cfg.AirMaxWishSpeed); }
            var current = st.vel.x * wishDir.x + st.vel.z * wishDir.z;
            var add = wishSpeed - current;
            if (add > 0) {
                var accelSpeed = Math.min(accel * dt * fullWish, add);
                st.vel.x += wishDir.x * accelSpeed;
                st.vel.z += wishDir.z * accelSpeed;
            }
        }
        // gravity
        if (!st.onGround) st.vel.y -= cfg.Gravity * dt;

        // jump (single jump, small buffer)
        if (input.jump) st.jumpBuf = JUMP_BUFFER_TIME;
        if (st.jumpQueued) { st.jumpQueued = false; st.jumpBuf = JUMP_BUFFER_TIME; }
        if (st.jumpBuf > 0) st.jumpBuf -= dt;
        if (st.onGround && st.jumpBuf > 0) { st.vel.y = cfg.JumpImpulse; st.onGround = false; st.jumpBuf = 0; }

        // hard speed cap
        var hs = Math.sqrt(st.vel.x * st.vel.x + st.vel.z * st.vel.z);
        if (hs > cfg.MaxSpeedCap) { var s2 = cfg.MaxSpeedCap / hs; st.vel.x *= s2; st.vel.z *= s2; }

        // move + slide + step-up
        var height = currentHeight(cfg, st);
        var startPos = st.pos;
        var horizDelta = V(st.vel.x * dt, 0, st.vel.z * dt);
        var expectedHoriz = Math.sqrt(horizDelta.x * horizDelta.x + horizDelta.z * horizDelta.z);
        var pos = st.pos, vel = st.vel, timeLeft = dt, touchedGround = false, blocked = false;

        for (var iter = 0; iter < 4 && timeLeft > 1e-5; iter++) {
            var d = vmul(vel, timeLeft);
            if (vdot(d, d) < 1e-8) break;
            var hit = sweepBoxes(world, pos, PLAYER_RADIUS, height, d);
            if (!hit.hit) { pos = vadd(pos, d); timeLeft = 0; break; }
            if (hit.fraction >= 1) {
                pos = vadd(pos, d);
                if (vdot(hit.normal, hit.normal) > 0.25) {
                    var vnE = vdot(vel, hit.normal);
                    if (vnE < 0) vel = vsub(vel, vmul(hit.normal, vnE));
                }
                timeLeft = 0; break;
            }
            var f = clamp01(hit.fraction);
            if (f > 0) { pos = vadd(pos, vmul(d, f)); timeLeft *= (1 - f); }
            var n = hit.normal;
            if (n.y > MIN_GROUND_NY) touchedGround = true;
            else if (n.y > MIN_WALL_NY) blocked = true;
            if (vdot(n, n) > 0.25) {
                var vn = vdot(vel, n);
                if (vn < 0) vel = vsub(vel, vmul(n, vn));
            }
        }

        var wantsMove = (input.forward * input.forward + input.right * input.right) > 0.01;
        if (wantsMove && blocked && (st.onGround || touchedGround)) {
            var achieved = Math.sqrt(Math.pow(pos.x - startPos.x, 2) + Math.pow(pos.z - startPos.z, 2));
            if (achieved < expectedHoriz * 0.6 || expectedHoriz < 0.05) {
                var stepped = tryStepUp(world, startPos, horizDelta, height);
                if (stepped) {
                    var gained = Math.sqrt(Math.pow(stepped.x - startPos.x, 2) + Math.pow(stepped.z - startPos.z, 2));
                    if (gained > achieved) { pos = stepped; touchedGround = true; }
                }
            }
        }

        st.pos = pos; st.vel = vel;

        // ground probe / snap
        var origin = vadd(st.pos, V(0, GROUND_LIFT, 0));
        var down = V(0, -(GROUND_SNAP + GROUND_LIFT), 0);
        var gh = probeGroundDown(world, origin, PLAYER_RADIUS, height, GROUND_SNAP + GROUND_LIFT);
        var grounded = gh.hit && gh.normal.y > MIN_GROUND_NY;
        if (grounded) {
            st.pos = vadd(origin, vmul(down, clamp01(gh.fraction)));
            if (st.vel.y < 0) st.vel.y = 0;
        }
        st.onGround = grounded;
    }

    function tryStepUp(world, start, horizDelta, height) {
        var up = V(0, STEP_HEIGHT, 0);
        var hUp = sweepBoxes(world, start, PLAYER_RADIUS, height, up);
        var fUp = hUp.hit ? hUp.fraction : 1;
        if (fUp * STEP_HEIGHT < 0.20) return null;
        var raised = vadd(start, vmul(up, fUp));
        var hFwd = sweepBoxes(world, raised, PLAYER_RADIUS, height, horizDelta);
        if (hFwd.hit && hFwd.fraction < 0.999) return null;
        var moved = vadd(raised, horizDelta);
        var down = V(0, -STEP_HEIGHT, 0);
        var hDown = probeGroundDown(world, moved, PLAYER_RADIUS, height, STEP_HEIGHT);
        if (!hDown.hit) return null;
        return vadd(moved, vmul(down, hDown.fraction));
    }

    // live tuning hooks (the F1 panel writes here): multipliers applied to the player only
    var TUNE = { recoil: 1, spread: 1 };

    // ------------------------------------------------------------------ weapons (same data as the Unity Core)
    function makePattern(scales, firstFlatten) {
        var src = WEAPON_PATTERNS.ak;
        var p = [], y = [];
        for (var i = 0; i < src.p.length; i++) {
            var s = scales * (i < (firstFlatten || 0) ? 0.9 : 1);
            p.push(src.p[i] * s);
            y.push(src.y[i] * scales);
        }
        return { p: p, y: y };
    }

    var WEAPON_PATTERNS = {
        ak: {
            p: [2.10, 1.75, 1.55, 1.40, 1.30, 1.20, 1.10, 1.05, 1.00, 0.95, 0.75, 0.60, 0.45, 0.35, 0.30, 0.28, 0.25, 0.22, 0.20, 0.18, 0.16, 0.15, 0.14, 0.13, 0.12, 0.11, 0.10, 0.10, 0.09, 0.08],
            y: [0.00, 0.10, 0.22, 0.30, 0.34, 0.30, 0.18, 0.00, -0.22, -0.40, -0.52, -0.45, -0.28, -0.05, 0.18, 0.38, 0.50, 0.52, 0.42, 0.25, 0.02, -0.20, -0.38, -0.50, -0.52, -0.42, -0.25, -0.05, 0.15, 0.30]
        }
    };

    function defWeapon(id, name, kind, price, dmg, pen, rpm, mag, reserve, reload, rangeMod, moveMult, reward, feel, pattern, scope) {        return {
            id: id, name: name, kind: kind, price: price, damage: dmg, armorPen: pen, rpm: rpm,
            magSize: mag, reserveAmmo: reserve, reloadTime: reload, rangeModifier: rangeMod, moveSpeedMultiplier: moveMult,
            killReward: reward, spreadStand: feel[0], spreadMove: feel[1], spreadCrouch: feel[2], spreadJump: feel[3],
            recoilPitchScale: feel[4], recoilYawScale: feel[5], recoilRecoveryDelay: feel[6], recoilRecoveryRate: feel[7],
            recoilResetTime: feel[8], patternVariance: feel[9], pattern: pattern, hasScope: !!scope, scopeFov: scope || 0
        };
    }

    var WEAPONS = (function () {
        var ak = defWeapon('ak47', 'AK-47', 'rifle', 2700, 36, 0.775, 600, 30, 90, 2.5, 0.98, 0.86, 300,
            [0.35, 3.2, 0.70, 6.0, 1.0, 1.0, 0.25, 9, 0.6, 0.12], WEAPON_PATTERNS.ak);
        var m4 = defWeapon('m4a4', 'M4A4', 'rifle', 3100, 33, 0.70, 666, 30, 90, 3.1, 0.99, 0.90, 300,
            [0.32, 3.0, 0.70, 6.0, 0.85, 0.85, 0.25, 9, 0.6, 0.12], makePattern(0.85, 3));
        var awp = defWeapon('awp', 'AWP', 'sniper', 4750, 115, 0.975, 41, 10, 30, 3.7, 0.99, 0.80, 100,
            [0.10, 6.0, 0.50, 10.0, 1.6, 1.2, 0.50, 6, 1.0, 0.05], { p: [3.6, 3.4], y: [0.4, -0.3] }, 15);
        var deagle = defWeapon('deagle', 'Deagle', 'pistol', 700, 53, 0.93, 267, 7, 35, 2.2, 0.99, 0.92, 300,
            [0.45, 4.5, 0.70, 8.0, 1.3, 1.2, 0.30, 11, 0.5, 0.20],
            { p: [2.6, 2.2, 2.0, 1.8, 1.6], y: [0.3, -0.2, 0.3, -0.3, 0.2] });
        var p250 = defWeapon('p250', 'P250', 'pistol', 300, 38, 0.64, 400, 13, 26, 2.2, 0.95, 0.96, 300,
            [0.50, 4.0, 0.70, 7.0, 1.0, 1.0, 0.30, 11, 0.5, 0.20],
            { p: [1.6, 1.4, 1.3, 1.2, 1.1], y: [0.2, -0.2, 0.2, -0.2, 0.1] });
        var glock = defWeapon('glock', 'Glock-18', 'pistol', 200, 30, 0.47, 400, 20, 120, 2.2, 0.95, 0.96, 300,
            [0.55, 4.2, 0.70, 7.0, 0.7, 0.7, 0.30, 11, 0.5, 0.20],
            { p: [1.1, 1.0, 0.9, 0.85, 0.8], y: [0.15, -0.15, 0.15, -0.10, 0.10] });
        var usp = defWeapon('usp', 'USP', 'pistol', 200, 35, 0.50, 352, 12, 24, 2.2, 0.95, 0.96, 300,
            [0.50, 4.0, 0.70, 7.0, 0.8, 0.8, 0.30, 11, 0.5, 0.20],
            { p: [1.3, 1.2, 1.1, 1.0, 0.95], y: [0.2, -0.2, 0.15, -0.15, 0.1] });
        return { ak47: ak, m4a4: m4, awp: awp, deagle: deagle, p250: p250, glock: glock, usp: usp };
    })();
    var WEAPON_LIST = [WEAPONS.ak47, WEAPONS.m4a4, WEAPONS.awp, WEAPONS.deagle, WEAPONS.p250, WEAPONS.glock, WEAPONS.usp];

    function makeWeaponState(def) {
        return { def: def, ammo: def.magSize, reserve: def.reserveAmmo, shotIndex: 0, nextFire: 0, lastFire: -99, reloadEnd: 0, reloading: false, recoilPitch: 0, recoilYaw: 0 };
    }
    function canFire(st, now) { return st.ammo > 0 && now >= st.nextFire; }
    function tryFire(st, now, r1, r2) {
        if (!canFire(st, now)) return false;
        if (st.reloading) st.reloading = false;
        st.ammo--;
        var def = st.def;
        var idx = Math.min(st.shotIndex, def.pattern.p.length - 1);
        var jp = (r1 * 2 - 1) * def.patternVariance;
        var jy = (r2 * 2 - 1) * def.patternVariance;
        st.recoilPitch += def.pattern.p[idx] * def.recoilPitchScale * (1 + jp);
        st.recoilYaw += def.pattern.y[idx] * def.recoilYawScale * (1 + jy);
        st.shotIndex = idx + 1;
        st.nextFire = now + 60 / def.rpm;
        st.lastFire = now;
        return true;
    }
    function updateWeapon(st, now, dt) {
        var def = st.def;
        if (st.reloading && now >= st.reloadEnd) {
            var need = def.magSize - st.ammo;
            var take = Math.min(need, st.reserve);
            st.ammo += take; st.reserve -= take; st.reloading = false;
        }
        if (st.lastFire > 0) {
            var since = now - st.lastFire;
            if (since > def.recoilRecoveryDelay) {
                var step = def.recoilRecoveryRate * dt;
                st.recoilPitch = Math.abs(st.recoilPitch) <= step ? 0 : st.recoilPitch - Math.sign(st.recoilPitch) * step;
                st.recoilYaw = Math.abs(st.recoilYaw) <= step ? 0 : st.recoilYaw - Math.sign(st.recoilYaw) * step;
            }
            if (since > def.recoilResetTime) st.shotIndex = 0;
        }
    }
    function tryReload(st, now) {
        if (st.reloading || st.ammo >= st.def.magSize || st.reserve <= 0) return false;
        st.reloading = true;
        st.reloadEnd = now + st.def.reloadTime;
        return true;
    }
    function computeSpread(def, speedRatio01, onGround, ducked) {
        var r = clamp01(speedRatio01);
        var s = def.spreadStand + def.spreadMove * r;
        if (!onGround) s += def.spreadJump;
        if (ducked) s *= def.spreadCrouch;
        return s;
    }

    // ------------------------------------------------------------------ ballistics
    var HIT_MULT = { head: 4, chest: 1, stomach: 1.25, arm: 1, leg: 0.75 };
    function damageAtDistance(def, dist) {
        var d = def.damage * Math.pow(def.rangeModifier, Math.max(0, dist) / 12.7);
        return Math.max(1, Math.round(d));
    }
    function applyArmor(dmg, pen, armorRef) {
        if (armorRef.v > 0) {
            var health = Math.round(dmg * pen);
            var lose = Math.max(0, Math.round((dmg - health) * 0.5));
            armorRef.v = Math.max(0, armorRef.v - lose);
            return Math.max(1, health);
        }
        return dmg;
    }

    // ------------------------------------------------------------------ economy / rules
    var ECON = {
        startMoney: 800, maxMoney: 16000, winElim: 3250, winBomb: 3500, winDefuse: 3500, winTime: 3250,
        lossBase: 1400, lossStep: 500, lossMax: 3400, plantPlanter: 300, plantTeam: 800, defuseDefuser: 300,
        armor: 650, helmet: 350, kit: 400
    };
    function lossReward(streak) { return ECON.lossBase + ECON.lossStep * (clamp(streak, 1, 5) - 1); }
    function clampMoney(m) { return clamp(m | 0, 0, ECON.maxMoney); }

    var RULES = {
        freeze: 10, buyTime: 20, roundTime: 115, bombTimer: 40, plantTime: 3, defuseTime: 10, defuseKit: 5,
        intermission: 5, roundsToWin: 8, roundsPerHalf: 8, bombDamageMax: 500, bombDamageRadius: 12
    };

    // ------------------------------------------------------------------ demo map (greybox, ~60x48)
    function buildMap() {
        var boxes = [];
        function box(cx, cy, cz, sx, sy, sz, color) {
            boxes.push({ min: V(cx - sx / 2, cy - sy / 2, cz - sz / 2), max: V(cx + sx / 2, cy + sy / 2, cz + sz / 2), color: color });
        }
        function wallZ(z, x0, x1, h) { box((x0 + x1) / 2, h / 2, z, Math.abs(x1 - x0), h, 1, 'wall'); }
        function wallX(x, z0, z1, h) { box(x, h / 2, (z0 + z1) / 2, 1, h, Math.abs(z1 - z0), 'wall'); }

        box(0, -0.5, 0, 80, 1, 80, 'floor');                     // ground
        wallZ(-24, -30, 30, 4); wallZ(24, -30, 30, 4);           // south / north walls
        wallX(-30, -24, 24, 4); wallX(30, -24, 24, 4);           // west / east walls

        // mid divider with two chokes (west gap x -10..-6, east gap x 6..10)
        wallZ(0, -30, -10, 3.2);
        wallZ(0, -6, 6, 3.2);
        wallZ(0, 10, 30, 3.2);

        // site A (east warehouse, north half)
        wallZ(16, 14, 28, 3.6);                                   // north wall
        wallX(28, 4, 16, 3.6);                                    // east wall
        wallZ(4, 14, 20, 3.6); wallZ(4, 24, 28, 3.6);             // south wall with door
        box(20, 0.9, 9, 1.8, 1.8, 1.8, 'crate');                  // A crates
        box(24, 0.6, 12, 1.2, 1.2, 1.2, 'crate');
        box(17, 0.45, 12.5, 0.9, 0.9, 0.9, 'crate');
        box(23, 1.35, 6.5, 1.8, 2.7, 1.8, 'container');           // stacked A cover

        // site B (west yard, north half)
        box(-20, 1.3, 10, 6, 2.6, 2.5, 'container');
        box(-24, 1.3, 6, 2.5, 2.6, 6, 'container');
        box(-16, 0.6, 12, 1.2, 1.2, 1.2, 'crate');
        box(-22, 0.45, 13.5, 0.9, 0.9, 0.9, 'crate');
        wallX(-14, 4, 16, 3.6);                                   // B east wall (faces mid)
        wallZ(16, -26, -14, 3.6);                                 // B north wall

        // south half cover
        box(-8, 0.6, -8, 1.2, 1.2, 1.2, 'crate');
        box(8, 0.9, -10, 1.8, 1.8, 1.8, 'crate');
        box(0, 1.35, -14, 1.8, 2.7, 1.8, 'container');
        box(-12, 0.45, -16, 0.9, 0.9, 0.9, 'crate');
        box(12, 0.45, -16, 0.9, 0.9, 0.9, 'crate');
        wallX(-16, -12, -4, 3.0);                                 // south route shaping
        wallX(16, 4, 12, 3.0);

        // north half mid cover
        box(-6, 0.9, 8, 1.8, 1.8, 1.8, 'crate');
        box(6, 0.6, 10, 1.2, 1.2, 1.2, 'crate');
        box(0, 1.35, 14, 1.8, 2.7, 1.8, 'container');

        var spawnsT = [V(-4, 0, -19), V(0, 0, -19), V(4, 0, -19), V(-2, 0, -21), V(2, 0, -21)];
        var spawnsCT = [V(-4, 0, 19), V(0, 0, 19), V(4, 0, 19), V(-2, 0, 21), V(2, 0, 21)];
        var siteA = { min: V(14, 0, 4), max: V(28, 3, 16) };
        var siteB = { min: V(-26, 0, 4), max: V(-14, 3, 16) };

        var wpRaw = [
            [-4, -18], [0, -18], [4, -18],           // T spawn
            [-18, -16], [-24, -12], [-26, -4],       // west south route
            [18, -16], [24, -12], [26, -4],          // east south route
            [-8, -6], [-8, 2], [-10, 8], [-12, 12],  // west choke
            [8, -6], [8, 2], [10, 8], [12, 12],      // east choke
            [0, -8], [0, 6],                         // mid
            [16, 10], [21, 10], [25, 10],            // site A
            [-16, 10], [-21, 8], [-24, 12],          // site B
            [-4, 18], [0, 18], [4, 18],              // CT spawn
            [-8, 14], [8, 14]                        // CT connectors
        ];
        var nodes = [];
        for (var i = 0; i < wpRaw.length; i++) nodes.push({ p: V(wpRaw[i][0], 0, wpRaw[i][1]), edges: [] });

        // adjacency by clearance (two probe heights, boxes fattened by the actor radius + margin)
        var margin = PLAYER_RADIUS + 0.12;
        for (var a = 0; a < nodes.length; a++) {
            for (var b2 = a + 1; b2 < nodes.length; b2++) {
                var pa = nodes[a].p, pb = nodes[b2].p;
                var d = vdist(pa, pb);
                if (d > 15) continue;
                if (!clearSegment(boxes, pa, pb, margin)) continue;
                nodes[a].edges.push(b2);
                nodes[b2].edges.push(a);
            }
        }
        return { boxes: boxes, spawnsT: spawnsT, spawnsCT: spawnsCT, siteA: siteA, siteB: siteB, nodes: nodes };
    }

    function clearSegment(boxes, a, b, margin) {
        for (var h = 0; h < 2; h++) {
            var hh = h === 0 ? 0.5 : 1.35;
            var o = V(a.x, a.y + hh, a.z);
            var dir = vnorm(vsub(V(b.x, b.y + hh, b.z), o));
            var len = vdist(o, V(b.x, b.y + hh, b.z)) - 0.05;
            if (len < 0) continue;
            for (var i = 0; i < boxes.length; i++) {
                var box = boxes[i];
                var t = rayBox(o, dir, V(box.min.x - margin, box.min.y - margin, box.min.z - margin), V(box.max.x + margin, box.max.y + margin, box.max.z + margin));
                if (t !== null && t < len) return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ bullet trace (map boxes + actor hitboxes)
    function actorHitboxes(a) {
        var f = a.st.pos, duckDrop = (a.cfg.StandHeight - a.cfg.DuckHeight) * a.st.duckFrac;
        var yaw = a.yaw * Math.PI / 180;
        var sn = Math.sin(yaw), cs = Math.cos(yaw);
        function local(x, y, z) { return V(f.x + x * cs + z * sn, f.y + y, f.z + -x * sn + z * cs); }
        function boxAt(cx, cy, cz, sx, sy, sz, group) {
            return { min: local(cx - sx / 2, cy - sy / 2, cz - sz / 2), max: local(cx + sx / 2, cy + sy / 2, cz + sz / 2), group: group };
        }
        return [
            boxAt(0, 1.65 - duckDrop * 0.95, 0, 0.26, 0.26, 0.26, 'head'),
            boxAt(0, 1.30 - duckDrop * 0.8, 0, 0.5, 0.6, 0.35, 'chest'),
            boxAt(0, 0.45 - duckDrop * 0.15, 0, 0.45, 0.85, 0.3, 'leg')
        ];
    }

    function castBullet(world, actors, origin, dir, maxDist, shooter) {
        var mapHit = raycastBoxes(world, origin, dir, maxDist);
        var best = { hit: false, dist: mapHit.hit ? mapHit.dist : maxDist, point: mapHit.point, normal: mapHit.normal, actor: null, group: null };
        best.hit = mapHit.hit;
        for (var i = 0; i < actors.length; i++) {
            var a = actors[i];
            if (a === shooter || !a.alive) continue;
            var hbs = actorHitboxes(a);
            for (var h = 0; h < hbs.length; h++) {
                var t = rayBox(origin, dir, hbs[h].min, hbs[h].max);
                if (t === null || t < 0 || t > maxDist) continue;
                if (t < best.dist) {
                    best.hit = true; best.dist = t; best.actor = a; best.group = hbs[h].group;
                    best.point = vadd(origin, vmul(dir, t));
                    best.normal = vmul(dir, -1);
                }
            }
        }
        return best;
    }

    function canSee(world, actors, from, to, ignoreA, ignoreB, maxDist) {
        var d = vsub(to, from);
        var dist = vlen(d);
        if (dist > maxDist) return false;
        var dir = vnorm(d);
        var mapHit = raycastBoxes(world, from, dir, dist - 0.08);
        if (mapHit.hit) return false;
        for (var i = 0; i < actors.length; i++) {
            var a = actors[i];
            if (a === ignoreA || a === ignoreB || !a.alive) continue;
            var hbs = actorHitboxes(a);
            for (var h = 0; h < 3; h++) {
                var t = rayBox(from, dir, hbs[h].min, hbs[h].max);
                if (t !== null && t >= 0 && t < dist - 0.08) return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ world (rounds, actor, bots)
    function makeActor(team, name, isPlayer) {
        var cfg = Object.assign({}, DEFAULT_MOVE);
        return {
            name: name, team: team, isPlayer: !!isPlayer, alive: true, health: 100, armor: 0, helmet: false, kit: false,
            hasBomb: false, st: makeMoveState(V(0, 0, 0)), cfg: cfg, yaw: team === 'T' ? 0 : 180, pitch: 0,
            slots: [null, null], states: [null, null], slot: 0,
            kills: 0, deaths: 0, lastNoise: -99, lastNoisePos: V(0, 0, 0), spotted: -99,
            bot: null
        };
    }

    function giveWeapon(actor, def, primary) {
        var ix = primary ? 0 : 1;
        actor.slots[ix] = def;
        actor.states[ix] = makeWeaponState(def);
    }
    function defOf(a) { return a.states[a.slot] ? a.states[a.slot].def : null; }
    function stateOf(a) { return a.states[a.slot]; }
    function selectSlot(a, ix) { if (ix >= 0 && ix < 2 && a.slots[ix] && a.slot !== ix) a.slot = ix; }
    function selectBestSlot(a) { a.slot = a.slots[0] ? 0 : (a.slots[1] ? 1 : 0); }

    function makeWorld() {
        var map = buildMap();
        var world = worldFromBoxes(map.boxes);
        var W = {
            map: map, world: world, actors: [], player: null, time: 0,
            phase: 'freeze', phaseTime: RULES.freeze, round: 1, scoreT: 0, scoreCT: 0,
            bomb: { planted: false, pos: V(0, 0, 0), timer: 0, plantProgress: 0, plantSite: null, defuseProgress: 0, defusingBy: null, exploded: false, defused: false },
            killfeed: [], banner: 'ROUND 1', bannerTime: 3, money: ECON.startMoney, lossT: 0, lossCT: 0,
            lastRoundWinner: null, lastRoundReason: '', shots: [], hits: [], events: [],
            duelStart: 0
        };

        // ---------------- actors
        var p = makeActor('T', 'YOU', true);
        W.player = p;
        W.actors.push(p);
        for (var i = 0; i < 3; i++) {
            var b = makeActor('T', 'T-BOT ' + (i + 1), false);
            b.bot = { state: 'idle', path: [], pathIx: 0, goal: -1, target: null, seeTime: -99, react: 0, burst: 0, burstCd: 0, fireT: 0, strafe: 0, strafeDir: 1, plantT: 0, defuseT: 0, think: Math.random() * 0.2, roam: 0 };
            W.actors.push(b);
        }
        for (var j = 0; j < 4; j++) {
            var c = makeActor('CT', 'CT-BOT ' + (j + 1), false);
            c.bot = { state: 'idle', path: [], pathIx: 0, goal: -1, target: null, seeTime: -99, react: 0, burst: 0, burstCd: 0, fireT: 0, strafe: 0, strafeDir: 1, plantT: 0, defuseT: 0, think: Math.random() * 0.2, roam: 0 };
            W.actors.push(c);
        }

        // ---------------- helpers
        W.pushEvent = function (text) {
            W.killfeed.unshift({ text: text, time: W.time });
            if (W.killfeed.length > 5) W.killfeed.pop();
        };
        W.setBanner = function (text, t) { W.banner = text; W.bannerTime = t; };

        W.aliveT = function () { var n = 0; for (var i = 0; i < W.actors.length; i++) if (W.actors[i].alive && W.actors[i].team === 'T') n++; return n; };
        W.aliveCT = function () { var n = 0; for (var i = 0; i < W.actors.length; i++) if (W.actors[i].alive && W.actors[i].team === 'CT') n++; return n; };

        W.giveDefaultLoadout = function (a) { a.slots = [null, null]; a.states = [null, null]; a.slot = 0; giveWeapon(a, a.team === 'T' ? WEAPONS.glock : WEAPONS.usp, false); };

        W.spawnAll = function () {
            var tIx = 0, ctIx = 0;
            for (var i = 0; i < W.actors.length; i++) {
                var a = W.actors[i];
                var pos = a.team === 'T' ? map.spawnsT[tIx++ % map.spawnsT.length] : map.spawnsCT[ctIx++ % map.spawnsCT.length];
                a.st = makeMoveState(V(pos.x, pos.y, pos.z));
                a.yaw = a.team === 'T' ? 0 : 180;
                a.pitch = 0;
                a.alive = true;
                a.health = 100;
                a.hasBomb = false;
                if (a.bot) {
                    a.bot.state = 'idle'; a.bot.path = []; a.bot.pathIx = 0; a.bot.target = null;
                    a.bot.plantT = 0; a.bot.defuseT = 0; a.bot.react = 0; a.bot.burst = 0;
                }
            }
            // bomb to a random T
            var ts = [];
            for (var k = 0; k < W.actors.length; k++) if (W.actors[k].team === 'T' && W.actors[k].alive) ts.push(W.actors[k]);
            if (ts.length) ts[Math.floor(Math.random() * ts.length)].hasBomb = true;
        };

        W.startRound = function (fresh) {
            if (fresh) { W.actors.forEach(function (a) { a.slots = [null, null]; a.states = [null, null]; a.slot = 0; }); }
            // players keep weapons when they survived; bots rebuy by tier
            for (var i = 0; i < W.actors.length; i++) {
                var a = W.actors[i];
                if (!a.alive && !a.isPlayer) a.armor = 0;
                if (a.isPlayer) {
                    if (!a.alive) { a.armor = 0; a.helmet = false; a.kit = false; }
                    if (!a.slots[1]) giveWeapon(a, a.team === 'T' ? WEAPONS.glock : WEAPONS.usp, false);
                } else {
                    W.botBuy(a);
                }
                selectBestSlot(a);
            }
            W.bomb.planted = false; W.bomb.exploded = false; W.bomb.defused = false;
            W.bomb.plantProgress = 0; W.bomb.defuseProgress = 0; W.bomb.defusingBy = null;
            W.spawnAll();
            W.phase = 'freeze';
            W.phaseTime = RULES.freeze;
            W.buyOpen = true;
            W.setBanner('ROUND ' + W.round + '  -  BUY TIME (B)', 3.5);
        };

        W.botBuy = function (a) {
            var r = W.round;
            a.slots = [null, null]; a.states = [null, null]; a.slot = 0;
            if (r === 1) {
                giveWeapon(a, a.team === 'T' ? WEAPONS.glock : WEAPONS.usp, false);
                selectBestSlot(a);
                return;
            }
            var tier = Math.random();
            if (r <= 3) {
                if (tier < 0.5) { giveWeapon(a, WEAPONS.p250, false); a.armor = 100; }
                else { giveWeapon(a, WEAPONS.deagle, false); }
            } else {
                var awp = (r >= 4 && (r - 4) % 5 === 0 && tier < 0.25);
                if (awp) { giveWeapon(a, WEAPONS.awp, false); }
                else { giveWeapon(a, a.team === 'T' ? WEAPONS.ak47 : WEAPONS.m4a4, false); }
                a.armor = 100;
                a.helmet = Math.random() < 0.7;
                a.kit = a.team === 'CT' && Math.random() < 0.4;
            }
            selectBestSlot(a);
        };

        W.setLastRound = function (winner, reason) {
            W.lastRoundWinner = winner;
            W.lastRoundReason = reason;
        };

        W.applyRoundEconomy = function (winner, reason) {
            var winMoney = reason === 'bomb' ? ECON.winBomb : (reason === 'defuse' ? ECON.winDefuse : (reason === 'time' ? ECON.winTime : ECON.winElim));
            var playerWon = W.player.team === winner;
            var reward = playerWon ? winMoney : lossReward(W.player.team === 'T' ? W.lossT : W.lossCT);
            W.money = clampMoney(W.money + reward);
            if (playerWon) { if (W.player.team === 'T') W.lossT = 0; else W.lossCT = 0; }
            else { if (W.player.team === 'T') W.lossT++; else W.lossCT++; }
            if (winner === 'T') W.scoreT++; else W.scoreCT++;
        };

        W.endRound = function (winner, reason, banner) {
            if (W.phase === 'end') return;
            W.phase = 'end';
            W.phaseTime = RULES.intermission;
            W.applyRoundEconomy(winner, reason);
            W.setBanner(banner, RULES.intermission);
            var next = W.round + 1;
            if (W.scoreT >= RULES.roundsToWin || W.scoreCT >= RULES.roundsToWin) {
                W.phase = 'matchover';
                W.setBanner((W.scoreT >= RULES.roundsToWin ? 'T' : 'CT') + ' WINS THE MATCH  ' + W.scoreT + ':' + W.scoreCT, 999);
                return;
            }
            if (next - 1 === RULES.roundsPerHalf) {
                for (var i = 0; i < W.actors.length; i++) {
                    var a = W.actors[i];
                    a.team = a.team === 'T' ? 'CT' : 'T';
                    a.slots = [null, null]; a.states = [null, null]; a.slot = 0;
                    a.armor = 0; a.helmet = false; a.kit = false;
                    a.hasBomb = false;
                }
                W.round = next;
                W.money = ECON.startMoney;
                W.lossT = 0; W.lossCT = 0;
                for (var j = 0; j < W.actors.length; j++) W.botBuy(W.actors[j]);
                W.setBanner('HALF TIME - SWAPPING SIDES', 3.5);
                W.phase = 'halftime';
                W.phaseTime = 3.5;
                return;
            }
            W.round = next;
        };

        W.damage = function (victim, baseDmg, pen, group, attacker) {
            if (!victim.alive) return;
            var dmg = Math.round(baseDmg * HIT_MULT[group]);
            var armorRef = { v: victim.armor };
            var armorApplies = victim.armor > 0 && (group !== 'head' || victim.helmet);
            var applied = armorApplies ? applyArmor(dmg, pen, armorRef) : dmg;
            victim.armor = armorRef.v;
            victim.health -= applied;
            if (attacker && attacker.isPlayer) W.events.push({ type: 'hit', head: group === 'head', dmg: applied, time: W.time });
            if (victim.health <= 0) W.kill(victim, attacker);
        };

        W.kill = function (victim, attacker) {
            if (!victim.alive) return;
            victim.alive = false;
            victim.health = 0;
            victim.deaths++;
            var weaponName = attacker ? (defOf(attacker) ? defOf(attacker).name : '?') : 'BOMB';
            if (attacker && attacker !== victim) {
                attacker.kills++;
                if (attacker.isPlayer) W.money = clampMoney(W.money + (defOf(attacker) ? defOf(attacker).killReward : 300));
            }
            W.pushEvent((attacker ? attacker.name : 'BOMB') + '  ->  ' + victim.name + '   [' + weaponName + ']');
            if (victim.hasBomb) {
                victim.hasBomb = false;
                var others = W.actors.filter(function (x) { return x.alive && x.team === 'T' && x !== victim; });
                if (others.length) others[Math.floor(Math.random() * others.length)].hasBomb = true;
            }
        };

        // ---------------- buying
        W.canBuy = function () {
            return W.player.alive && (W.phase === 'freeze' || (W.phase === 'live' && W.phaseTime > RULES.roundTime - RULES.buyTime));
        };
        W.buy = function (item) {
            if (!W.canBuy()) return false;
            var p2 = W.player;
            if (item === 'armor') {
                var cost = ECON.armor + (p2.helmet ? 0 : ECON.helmet);
                if (W.money < ECON.armor) return false;
                W.money -= ECON.armor;
                p2.armor = 100;
                if (p2.helmet) return true;
                if (W.money >= ECON.helmet) { W.money -= ECON.helmet; p2.helmet = true; }
                return true;
            }
            if (item === 'kit') {
                if (p2.team !== 'CT' || p2.kit || W.money < ECON.kit) return false;
                W.money -= ECON.kit; p2.kit = true; return true;
            }
            var def = WEAPONS[item];
            if (!def || W.money < def.price) return false;
            W.money -= def.price;
            giveWeapon(p2, def, def.kind === 'pistol' ? false : true);
            selectSlot(p2, def.kind === 'pistol' ? 1 : 0);
            return true;
        };

        // ---------------- bots
        function nearestNode(map, pos) {
            var best = 0, bd = Infinity;
            for (var i = 0; i < map.nodes.length; i++) {
                var d = vdist(map.nodes[i].p, pos);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
        W.nearestNode = nearestNode;

        function bfsPath(map, from, to) {
            if (from === to) return [to];
            var prev = new Array(map.nodes.length).fill(-1);
            var seen = new Array(map.nodes.length).fill(false);
            var q = [from]; seen[from] = true;
            while (q.length) {
                var cur = q.shift();
                var edges = map.nodes[cur].edges;
                for (var e = 0; e < edges.length; e++) {
                    var nb = edges[e];
                    if (seen[nb]) continue;
                    seen[nb] = true; prev[nb] = cur;
                    if (nb === to) {
                        var path = [to];
                        var n = to;
                        while (prev[n] !== -1) { n = prev[n]; path.unshift(n); }
                        return path;
                    }
                    q.push(nb);
                }
            }
            return [to];
        }

        function botObjective(W, a) {
            var map = W.map, bot = a.bot;
            if (a.team === 'T') {
                // T: push a site, plant if carrying, otherwise support
                if (W.bomb.planted) return nearestNode(map, W.bomb.pos);
                var carry = null;
                for (var i = 0; i < W.actors.length; i++) if (W.actors[i].team === 'T' && W.actors[i].alive && W.actors[i].hasBomb) carry = W.actors[i];
                var site = (a.hasBomb || !carry) ? (a.bot.site || (a.bot.site = Math.random() < 0.5 ? 'A' : 'B')) : (carry.bot && carry.bot.site) || 'A';
                if (carry && carry !== a) {
                    // escort the carrier: pick the node nearest to the carrier's current site
                    var tn = nearestNode(map, map.siteA.min.x !== undefined && site === 'A' ? V(21, 0, 10) : V(-20, 0, 10));
                    return tn;
                }
                return nearestNode(map, site === 'A' ? V(21, 0, 10) : V(-20, 0, 10));
            }
            // CT: hold near a site, rotate to the bomb once planted
            if (W.bomb.planted) return nearestNode(map, W.bomb.pos);
            if (!a.bot.hold) a.bot.hold = Math.random() < 0.5 ? 0 : 1;
            return nearestNode(map, a.bot.hold === 0 ? V(16, 0, 12) : V(-18, 0, 12));
        }

        function botThink(W, a, dt) {
            var bot = a.bot, map = W.map;
            bot.think -= dt;
            if (bot.think > 0) return;
            bot.think = 0.18 + Math.random() * 0.12;

            // perception
            var eye = vadd(a.st.pos, V(0, currentHeight(a.cfg, a.st) - 0.16, 0));
            var best = null, bestD = Infinity;
            for (var i = 0; i < W.actors.length; i++) {
                var e = W.actors[i];
                if (!e.alive || e.team === a.team) continue;
                var d = vdist(a.st.pos, e.st.pos);
                if (d > 38) continue;
                var aim = vsub(vadd(e.st.pos, V(0, 1.3, 0)), eye);
                var yawTo = yawOf(aim);
                if (Math.abs(wrapAngle(yawTo - a.yaw)) > 110 && d > 6) continue;
                if (!canSee(W.world, W.actors, eye, vadd(e.st.pos, V(0, 1.2, 0)), a, e, 38)) continue;
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best) {
                if (bot.target !== best) { bot.target = best; bot.react = 0; }
                bot.seeTime = W.time;
            } else {
                bot.target = null;
            }

            // objective / path
            var goal = botObjective(W, a);
            if (goal !== bot.goal) {
                bot.goal = goal;
                bot.path = bfsPath(map, nearestNode(map, a.st.pos), goal);
                bot.pathIx = 0;
            }
        }

        function botMove(W, a, dt, input) {
            var bot = a.bot, map = W.map;
            input.forward = 0; input.right = 0;
            if (bot.path.length > bot.pathIx) {
                var node = map.nodes[bot.path[bot.pathIx]].p;
                var d = vsub(node, a.st.pos);
                d.y = 0;
                if (vlen(d) < 1.6) bot.pathIx++;
                else {
                    var want = Math.atan2(d.x, d.z) * 180 / Math.PI;
                    var rel = wrapAngle(want - a.yaw);
                    if (Math.abs(rel) > 100) { input.forward = -1; }
                    else { input.forward = 1; input.right = clamp(rel / 35, -1, 1) * 0.6; }
                }
            }
            input.walk = false;
            input.duck = false;
            input.jump = false;
        }

        function botCombat(W, a, dt, input) {
            var bot = a.bot;
            if (!bot.target || !bot.target.alive) { bot.burst = 0; return; }
            var eye = vadd(a.st.pos, V(0, currentHeight(a.cfg, a.st) - 0.16, 0));
            var aimPoint = vadd(bot.target.st.pos, V(0, 1.15, 0));
            var dir = vnorm(vsub(aimPoint, eye));
            var wantYaw = yawOf(dir), wantPitch = pitchOf(dir);

            var diff = Math.abs(wrapAngle(wantYaw - a.yaw));
            var turnRate = 420 * dt;
            a.yaw = moveTowardsAngle(a.yaw, wantYaw, turnRate);
            a.pitch += clamp(wantPitch - a.pitch, -turnRate, turnRate);

            bot.react += dt;
            if (bot.react < 0.28) return;     // reaction delay

            // aim error shrinking while on target
            var err = 4.2 * Math.max(0.25, 1 - bot.react / 1.6);
            var jitterY = (Math.random() * 2 - 1) * err;
            var jitterP = (Math.random() * 2 - 1) * err * 0.5;
            var st = stateOf(a);
            if (!st) return;

            bot.burstCd -= dt;
            if (bot.burst > 0) {
                if (W.time >= st.nextFire) {
                    bot.burst--;
                    fireShot(W, a, a.yaw + jitterY, a.pitch + jitterP, false);
                }
            } else if (bot.burstCd <= 0 && diff < 12) {
                bot.burst = 3 + Math.floor(Math.random() * 4);
                bot.burstCd = 0.45 + Math.random() * 0.5;
            }

            // stop and shoot, occasionally strafe
            input.forward = 0;
            bot.strafe -= dt;
            if (bot.strafe <= 0) { bot.strafe = 0.4 + Math.random() * 0.9; bot.strafeDir = Math.random() < 0.5 ? -1 : 1; }
            input.right = bot.strafeDir * 0.55;
        }

        function fireShot(W, shooter, yaw, pitch, isPlayer) {
            var st = stateOf(shooter);
            var def = st.def;
            var beforeP = st.recoilPitch, beforeY = st.recoilYaw;
            if (!tryFire(st, W.time, Math.random(), Math.random())) return false;
            if (isPlayer && TUNE.recoil !== 1) {
                st.recoilPitch = beforeP + (st.recoilPitch - beforeP) * TUNE.recoil;
                st.recoilYaw = beforeY + (st.recoilYaw - beforeY) * TUNE.recoil;
            }
            var spread = computeSpread(def, speedRatio(shooter), shooter.st.onGround, shooter.st.ducked);
            if (isPlayer) spread *= TUNE.spread;
            var cone = V((Math.random() * 2 - 1) * spread, 0, 0);
            var dir = dirOf(yaw + cone.x, pitch + (Math.random() * 2 - 1) * spread);
            var eye = vadd(shooter.st.pos, V(0, currentHeight(shooter.cfg, shooter.st) - 0.16, 0));
            var hit = castBullet(W.world, W.actors, eye, dir, 200, shooter);
            W.shots.push({ from: vadd(eye, vmul(dir, 0.5)), to: hit.hit ? hit.point : vadd(eye, vmul(dir, 60)), time: W.time, shooter: shooter });
            if (shooter.alive) { shooter.lastNoise = W.time; shooter.lastNoisePos = shooter.st.pos; }
            if (hit.hit && hit.actor && hit.actor.team !== shooter.team) {
                var dmg = damageAtDistance(def, hit.dist);
                W.damage(hit.actor, dmg, def.armorPen, hit.group, shooter);
                W.hits.push({ point: hit.point, normal: hit.normal, time: W.time, flesh: true, big: hit.group === 'head' });
            } else if (hit.hit) {
                if (shooter.isPlayer) W.events.push({ type: 'impact', time: W.time });
                W.hits.push({ point: hit.point, normal: hit.normal, time: W.time, flesh: false, big: false });
            }
            return true;
        }
        W.fireShot = fireShot;

        function speedRatio(a) {
            var s = Math.sqrt(a.st.vel.x * a.st.vel.x + a.st.vel.z * a.st.vel.z);
            var def = defOf(a);
            var m = Math.max(0.001, a.cfg.MaxSpeed * (def ? def.moveSpeedMultiplier : 1));
            return clamp01(s / m);
        }

        function botBombActions(W, a, dt) {
            var bot = a.bot, map = W.map;
            var inSiteA = pointInBox(a.st.pos, map.siteA), inSiteB = pointInBox(a.st.pos, map.siteB);
            if (a.team === 'T') {
                if (a.hasBomb && !W.bomb.planted && W.phase === 'live' && (inSiteA || inSiteB) && !bot.target) {
                    bot.plantT += dt;
                    if (bot.plantT >= RULES.plantTime) {
                        bot.plantT = 0;
                        plantBomb(W, a, inSiteA ? 'A' : 'B');
                    }
                } else bot.plantT = 0;
            } else if (W.bomb.planted && !W.bomb.exploded && !W.bomb.defused && !bot.target) {
                if (vdist(a.st.pos, W.bomb.pos) < 1.6) {
                    bot.defuseT += dt;
                    W.bomb.defusingBy = a;
                    if (bot.defuseT >= (a.kit ? RULES.defuseKit : RULES.defuseTime)) {
                        bot.defuseT = 0;
                        defuseBomb(W, a);
                    }
                } else bot.defuseT = 0;
            }
        }

        function plantBomb(W, actor, site) {
            if (W.bomb.planted || W.phase !== 'live') return;
            W.bomb.planted = true;
            W.bomb.exploded = false; W.bomb.defused = false;
            W.bomb.pos = V(actor.st.pos.x, 0.05, actor.st.pos.z);
            W.bomb.timer = RULES.bombTimer;
            W.bomb.plantSite = site;
            actor.hasBomb = false;
            if (actor.isPlayer) W.money = clampMoney(W.money + ECON.plantPlanter);
            if (actor.team === W.player.team) W.money = clampMoney(W.money + ECON.plantTeam);
            W.setBanner('BOMB PLANTED AT ' + site, 3);
            W.pushEvent(actor.name + ' planted the bomb at ' + site);
        }
        W.plantBomb = plantBomb;

        function defuseBomb(W, actor) {
            if (!W.bomb.planted) return;
            W.bomb.defused = true;
            W.bomb.planted = false;
            if (actor.isPlayer) W.money = clampMoney(W.money + ECON.defuseDefuser);
            W.pushEvent(actor.name + ' defused the bomb');
            W.endRound('CT', 'defuse', 'CT WIN - BOMB DEFUSED');
        }

        function pointInBox(p, b) {
            return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
        }
        W.pointInBox = pointInBox;

        W.defuseBomb = defuseBomb;

        // ---------------- per-frame update
        W.update = function (dt, playerInput) {
            W.time += dt;
            if (W.bannerTime > 0) W.bannerTime -= dt;
            var i, a;

            // phase timers
            if (W.phase === 'freeze') {
                W.phaseTime -= dt;
                if (W.phaseTime <= 0) { W.phase = 'live'; W.phaseTime = RULES.roundTime; W.setBanner('', 0); }
            } else if (W.phase === 'live') {
                W.phaseTime -= dt;
                if (W.phaseTime <= 0) {
                    if (W.bomb.planted) { W.phaseTime = 0.01; }
                    else W.endRound('CT', 'time', 'CT WIN - TIME EXPIRED');
                }
                if (!W.bomb.planted && W.phase === 'live') {
                    var anyT = false, anyCT = false;
                    for (i = 0; i < W.actors.length; i++) { a = W.actors[i]; if (a.alive) { if (a.team === 'T') anyT = true; else anyCT = true; } }
                    if (!anyT && !W.bomb.planted) W.endRound('CT', 'elim', 'CT WIN - TEAM ELIMINATED');
                    else if (!anyCT) W.endRound('T', 'elim', 'T WIN - TEAM ELIMINATED');
                }
            } else if (W.phase === 'end') {
                W.phaseTime -= dt;
                if (W.phaseTime <= 0) W.startRound(false);
            } else if (W.phase === 'halftime') {
                W.phaseTime -= dt;
                if (W.phaseTime <= 0) W.startRound(false);
            }

            if (W.bomb.planted) {
                W.bomb.timer -= dt;
                if (W.bomb.timer <= 0) {
                    W.bomb.planted = false;
                    W.bomb.exploded = true;
                    for (i = 0; i < W.actors.length; i++) {
                        a = W.actors[i];
                        if (!a.alive) continue;
                        var d = vdist(a.st.pos, W.bomb.pos);
                        if (d > RULES.bombDamageRadius) continue;
                        var dmg = Math.round(RULES.bombDamageMax * (1 - d / RULES.bombDamageRadius));
                        if (dmg > 0) W.damage(a, dmg, 1, 'chest', null);
                    }
                    W.setBanner('BOMB EXPLODED', 3);
                    W.endRound('T', 'bomb', 'T WIN - BOMB EXPLODED');
                }
            }

            // actors
            for (i = 0; i < W.actors.length; i++) {
                a = W.actors[i];
                if (!a.alive) continue;
                var st = stateOf(a);
                if (st) {
                    if (st.reloading) updateWeapon(st, W.time, dt);
                    else updateWeapon(st, W.time, dt);
                }
                if (a.isPlayer) {
                    var inp = playerInput;
                    var canAct = W.phase === 'live';
                    if (!canAct) { inp.forward = 0; inp.right = 0; inp.jump = false; }
                    stepMove(a.st, a.cfg, inp, defOf(a) ? defOf(a).moveSpeedMultiplier : 1, W.world, dt);
                    if (a.st.pos.y < -4 || a.st.pos.x < -40 || a.st.pos.x > 40 || a.st.pos.z < -40 || a.st.pos.z > 40) {
                        a.st.pos = V(0, 0, 0);
                    }
                } else {
                    var botInput = { forward: 0, right: 0, jump: false, duck: false, walk: false, yaw: a.yaw };
                    if (W.phase === 'live') {
                        botThink(W, a, dt);
                        botMove(W, a, dt, botInput);
                        botCombat(W, a, dt, botInput);
                        botBombActions(W, a, dt);
                    }
                    botInput.yaw = a.yaw;
                    stepMove(a.st, a.cfg, botInput, defOf(a) ? defOf(a).moveSpeedMultiplier : 1, W.world, dt);
                }
            }

            // cleanup fx buffers
            W.shots = W.shots.filter(function (s) { return W.time - s.time < 0.06; });
            W.hits = W.hits.filter(function (s) { return W.time - s.time < 0.35; });
            W.killfeed = W.killfeed.filter(function (k) { return W.time - k.time < 6; });
            W.events = W.events.filter(function (e) { return W.time - e.time < 0.4; });
        };

        W.startRound(true);
        W.setBanner('GREYZONE DEMO - PRESS B TO BUY', 4);
        return W;
    }

    // ------------------------------------------------------------------ exports
    var api = {
        V: V, vadd: vadd, vsub: vsub, vmul: vmul, vdot: vdot, vlen: vlen, vdist: vdist, vnorm: vnorm,
        clamp: clamp, clamp01: clamp01, lerp: lerp, wrapAngle: wrapAngle, dirOf: dirOf, yawOf: yawOf, pitchOf: pitchOf,
        DEFAULT_MOVE: DEFAULT_MOVE, PLAYER_RADIUS: PLAYER_RADIUS, STEP_HEIGHT: STEP_HEIGHT,
        makeMoveState: makeMoveState, stepMove: stepMove, computeMaxSpeed: computeMaxSpeed, currentHeight: currentHeight,
        makeBox: makeBox, worldFromBoxes: worldFromBoxes, buildMap: buildMap, sweepBoxes: sweepBoxes, overlapBoxes: overlapBoxes,
        probeGroundDown: probeGroundDown,
        raycastBoxes: raycastBoxes, castBullet: castBullet, canSee: canSee, actorHitboxes: actorHitboxes,
        WEAPONS: WEAPONS, WEAPON_LIST: WEAPON_LIST, makeWeaponState: makeWeaponState, tryFire: tryFire, updateWeapon: updateWeapon,
        tryReload: tryReload, canFire: canFire, computeSpread: computeSpread,
        HIT_MULT: HIT_MULT, damageAtDistance: damageAtDistance, applyArmor: applyArmor,
        ECON: ECON, lossReward: lossReward, clampMoney: clampMoney, RULES: RULES, TUNE: TUNE,
        makeWorld: makeWorld, makeActor: makeActor, giveWeapon: giveWeapon, defOf: defOf, stateOf: stateOf, selectSlot: selectSlot
    };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    root.GZSim = api;
})(typeof window !== 'undefined' ? window : globalThis);
