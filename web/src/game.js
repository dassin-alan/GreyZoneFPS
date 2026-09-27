// GreyZone demo - game glue: input (desktop + touch), HUD, buy menu, F1 tuner, render loop.
(function (root) {
    'use strict';
    var S = root.GZSim;
    var G = root.GZGL;

    // ------------------------------------------------------------------ settings
    var settings = {
        sensMouse: 0.12, sensTouch: 0.22, fov: 70, recoilScale: 1, spreadScale: 1,
        autoStop: true, leftHanded: false, showFps: true,
        move: {}, targetFps: 60
    };
    (function load() {
        try {
            var raw = localStorage.getItem('gz.settings');
            if (raw) {
                var o = JSON.parse(raw);
                for (var k in o) if (k !== 'move') settings[k] = o[k];
                settings.move = o.move || {};
            }
        } catch (e) { }
    })();
    function saveSettings() {
        try { localStorage.setItem('gz.settings', JSON.stringify(settings)); } catch (e) { }
    }
    function baseCfg() {
        var cfg = Object.assign({}, S.DEFAULT_MOVE);
        for (var k in settings.move) if (cfg.hasOwnProperty(k)) cfg[k] = settings.move[k];
        return cfg;
    }
    var assistCfg = (function () {
        var c = baseCfg();
        c.Friction *= 3.5; c.StopSpeed *= 2;
        return c;
    })();

    // ------------------------------------------------------------------ world / renderer
    var W = S.makeWorld();
    var P = W.player;
    var canvas = document.getElementById('gl');
    var hud = document.getElementById('hud');
    var hctx = hud.getContext('2d');
    var R = new G.Renderer(canvas);
    R.buildMapMesh(W.map.boxes);

    var view = { yaw: P.yaw, pitch: P.pitch };
    var scoped = false;
    var useProgress = 0;
    var hitMarker = 0;
    var lastAmmo = -1;
    var fps = 0, fpsT = 0, fpsN = 0;

    // ------------------------------------------------------------------ input
    var input = { forward: 0, right: 0, jump: false, duck: false, walk: false, yaw: 0 };
    var keys = {};
    var fireHeld = false;
    var useHeld = false;
    var isTouch = (typeof matchMedia !== 'undefined' && matchMedia('(pointer: coarse)').matches) || ('ontouchstart' in window) ||
        (typeof location !== 'undefined' && location.search.indexOf('touch=1') >= 0);

    function sens() { return (isTouch ? settings.sensTouch : settings.sensMouse) * (scoped ? 0.4 : 1); }
    function applyLook(dx, dy) {
        view.yaw += dx * sens();
        view.pitch -= dy * sens();
        view.pitch = Math.max(-89, Math.min(89, view.pitch));
    }

    document.addEventListener('keydown', function (e) {
        keys[e.code] = true;
        if (e.code === 'KeyR') reload();
        if (e.code === 'Digit1') S.selectSlot(P, 0);
        if (e.code === 'Digit2') S.selectSlot(P, 1);
        if (e.code === 'KeyB') toggleBuy();
        if (e.code === 'KeyE') useHeld = true;
        if (e.code === 'F1') { toggleTuner(); e.preventDefault(); }
        if (e.code === 'Escape') togglePause();
        if (e.code === 'KeyQ') cycleSlot();
    });
    document.addEventListener('keyup', function (e) {
        keys[e.code] = false;
        if (e.code === 'KeyE') useHeld = false;
    });

    canvas.addEventListener('mousedown', function (e) {
        if (!pointerLocked() && !isTouch) { canvas.requestPointerLock(); return; }
        if (e.button === 0) fireHeld = true;
        if (e.button === 2) toggleScope();
    });
    document.addEventListener('mouseup', function (e) { if (e.button === 0) fireHeld = false; });
    document.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    document.addEventListener('mousemove', function (e) {
        if (pointerLocked()) applyLook(e.movementX || 0, e.movementY || 0);
    });
    document.addEventListener('pointerlockchange', function () { });
    function pointerLocked() { return document.pointerLockElement === canvas; }

    // ---- touch
    var stick = { active: false, id: -1, dx: 0, dy: 0 };
    var lookTouch = { active: false, id: -1, x: 0, y: 0 };
    function touchHandlers() {
        var stickEl = document.getElementById('stick');
        var knob = document.getElementById('knob');
        stickEl.addEventListener('pointerdown', function (e) {
            stick.active = true; stick.id = e.pointerId;
            stickEl.setPointerCapture(e.pointerId);
            stickMove(e);
        });
        stickEl.addEventListener('pointermove', function (e) { if (stick.active && e.pointerId === stick.id) stickMove(e); });
        function stickEnd(e) {
            if (e.pointerId !== stick.id) return;
            stick.active = false; stick.id = -1; stick.dx = 0; stick.dy = 0;
            knob.style.transform = 'translate(-50%,-50%)';
        }
        stickEl.addEventListener('pointerup', stickEnd);
        stickEl.addEventListener('pointercancel', stickEnd);
        function stickMove(e) {
            var r = stickEl.getBoundingClientRect();
            var cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            var max = r.width * 0.42;
            var dx = (e.clientX - cx) / max, dy = (e.clientY - cy) / max;
            var len = Math.hypot(dx, dy);
            if (len > 1) { dx /= len; dy /= len; }
            stick.dx = dx; stick.dy = dy;
            knob.style.transform = 'translate(-50%,-50%) translate(' + (dx * max) + 'px,' + (dy * max) + 'px)';
        }
        canvas.addEventListener('pointerdown', function (e) {
            if (e.pointerType === 'mouse') return;
            lookTouch.active = true; lookTouch.id = e.pointerId; lookTouch.x = e.clientX; lookTouch.y = e.clientY;
        });
        canvas.addEventListener('pointermove', function (e) {
            if (!lookTouch.active || e.pointerId !== lookTouch.id) return;
            applyLook((e.clientX - lookTouch.x) * 1.4, (e.clientY - lookTouch.y) * 1.4);
            lookTouch.x = e.clientX; lookTouch.y = e.clientY;
        });
        function lookEnd(e) { if (e.pointerId === lookTouch.id) { lookTouch.active = false; lookTouch.id = -1; } }
        canvas.addEventListener('pointerup', lookEnd);
        canvas.addEventListener('pointercancel', lookEnd);
    }

    function bindButton(id, onDown, onUp) {
        var el = document.getElementById(id);
        if (!el) return;
        el.addEventListener('pointerdown', function (e) { e.preventDefault(); el.classList.add('on'); if (onDown) onDown(); });
        el.addEventListener('pointerup', function (e) { e.preventDefault(); el.classList.remove('on'); if (onUp) onUp(); });
        el.addEventListener('pointercancel', function () { el.classList.remove('on'); if (onUp) onUp(); });
        el.addEventListener('pointerleave', function () { el.classList.remove('on'); if (onUp) onUp(); });
    }

    function setupTouchUi() {
        if (!isTouch) return;
        document.getElementById('touch').style.display = 'block';
        if (settings.leftHanded) document.getElementById('touch').classList.add('left');
        touchHandlers();
        bindButton('btn-fire', function () { fireHeld = true; }, function () { fireHeld = false; });
        bindButton('btn-scope', toggleScope);
        bindButton('btn-jump', function () { input.jump = true; setTimeout(function () { input.jump = false; }, 90); });
        bindButton('btn-duck', function () { input.duck = !input.duck; document.getElementById('btn-duck').classList.toggle('on', input.duck); });
        bindButton('btn-walk', function () { input.walk = !input.walk; document.getElementById('btn-walk').classList.toggle('on', input.walk); });
        bindButton('btn-reload', reload);
        bindButton('btn-use', function () { useHeld = true; }, function () { useHeld = false; });
        bindButton('btn-swap', cycleSlot);
        bindButton('btn-buy', toggleBuy);
        bindButton('btn-pause', togglePause);
    }

    function reload() { var st = S.stateOf(P); if (st) S.tryReload(st, W.time); }
    function cycleSlot() { S.selectSlot(P, P.slot === 0 ? 1 : 0); }
    function toggleScope() {
        var st = S.stateOf(P);
        if (!st || !st.def.hasScope) return;
        scoped = !scoped;
    }

    // ------------------------------------------------------------------ buy menu / tuner / pause
    function byId(id) { return document.getElementById(id); }
    function toggleBuy() {
        var el = byId('buy');
        var open = el.style.display !== 'block';
        if (open && !W.canBuy()) { W.setBanner('BUY TIME IS OVER', 1.5); return; }
        el.style.display = open ? 'block' : 'none';
        if (open) { if (!isTouch) document.exitPointerLock(); renderBuy(); }
        else if (!isTouch) canvas.requestPointerLock();
    }
    function renderBuy() {
        var list = byId('buy-list');
        list.innerHTML = '';
        function row(label, price, enabled, cb) {
            var d = document.createElement('div');
            d.className = 'buy-row' + (enabled ? '' : ' dim');
            d.textContent = label + '   $' + price;
            if (enabled) d.addEventListener('click', cb);
            list.appendChild(d);
        }
        row('护甲 ARMOR' + (P.helmet ? ' + 头盔' : ' + 头盔 HELMET'), S.ECON.armor + (P.helmet ? 0 : S.ECON.helmet), W.money >= S.ECON.armor, function () { W.buy('armor'); renderBuy(); });
        if (P.team === 'CT' && !P.kit) row('拆弹器 DEFUSE KIT', S.ECON.kit, W.money >= S.ECON.kit, function () { W.buy('kit'); renderBuy(); });
        S.WEAPON_LIST.forEach(function (d) {
            row((d.kind === 'pistol' ? '[手枪] ' : '[主武器] ') + d.name, d.price, W.money >= d.price, function () { W.buy(d.id); renderBuy(); });
        });
        byId('buy-money').textContent = '资金 $' + W.money + '   (B 关闭 / 点击购买)';
        var st = S.stateOf(P);
        byId('buy-have').textContent = st ? ('当前: ' + st.def.name + '  ' + st.ammo + '/' + st.reserve) : '当前: 无武器';
    }

    function toggleTuner() {
        var el = byId('tuner');
        var open = el.style.display !== 'block';
        el.style.display = open ? 'block' : 'none';
        if (open) { if (!isTouch) document.exitPointerLock(); syncTuner(); }
    }
    var tunerDefs = [
        ['friction', '急停 · 摩擦 Friction', 0, 12, 0.1],
        ['accel', '加速 Accel', 1, 12, 0.1],
        ['stopspeed', 'StopSpeed', 0.5, 6, 0.05],
        ['jump', '跳跃初速 JumpImpulse', 4, 10, 0.05],
        ['gravity', '重力 Gravity', 8, 30, 0.1],
        ['maxspeed', '最大速度 MaxSpeed', 4, 9, 0.05],
        ['recoilScale', '后坐力 RecoilScale', 0, 2, 0.05],
        ['spreadScale', '扩散 SpreadScale', 0, 2, 0.05],
        ['sensMouse', '鼠标灵敏度', 0.02, 0.5, 0.01],
        ['fov', 'FOV', 55, 110, 1]
    ];
    function buildTuner() {
        var el = byId('tuner');
        el.innerHTML = '<div class="t-title">F1 调参 · 实时生效（急停 / 后坐力）</div>';
        tunerDefs.forEach(function (def) {
            var wrap = document.createElement('div');
            wrap.className = 't-row';
            var lab = document.createElement('label');
            lab.textContent = def[1];
            var val = document.createElement('span');
            valueSpan(def, val);
            var sl = document.createElement('input');
            sl.type = 'range'; sl.min = def[2]; sl.max = def[3]; sl.step = def[4];
            sl.value = getTune(def[0]);
            sl.addEventListener('input', function () { setTune(def[0], parseFloat(sl.value)); valueSpan(def, val); });
            wrap.appendChild(lab); wrap.appendChild(sl); wrap.appendChild(val);
            el.appendChild(wrap);
        });
        var opt = document.createElement('div');
        opt.className = 't-row';
        opt.innerHTML = '<label>开火自动急停（触控辅助）</label>';
        var cb = document.createElement('input'); cb.type = 'checkbox'; cb.checked = settings.autoStop;
        cb.addEventListener('change', function () { settings.autoStop = cb.checked; saveSettings(); });
        opt.appendChild(cb);
        el.appendChild(opt);
        var lh = document.createElement('div');
        lh.className = 't-row';
        lh.innerHTML = '<label>左手模式</label>';
        var cb2 = document.createElement('input'); cb2.type = 'checkbox'; cb2.checked = settings.leftHanded;
        cb2.addEventListener('change', function () {
            settings.leftHanded = cb2.checked; saveSettings();
            var t = byId('touch'); if (t) t.classList.toggle('left', settings.leftHanded);
        });
        lh.appendChild(cb2);
        el.appendChild(lh);
        var btns = document.createElement('div');
        btns.className = 't-row';
        var reset = document.createElement('button'); reset.textContent = '恢复默认';
        reset.addEventListener('click', function () {
            settings.move = {}; settings.recoilScale = 1; settings.spreadScale = 1;
            settings.friction = undefined;
            saveSettings(); syncTuner(); applyTuning();
        });
        var restart = document.createElement('button'); restart.textContent = '重开比赛';
        restart.addEventListener('click', function () { W.scoreT = 0; W.scoreCT = 0; W.round = 1; W.money = S.ECON.startMoney; W.startRound(true); });
        btns.appendChild(reset); btns.appendChild(restart);
        el.appendChild(btns);
    }
    function getTune(key) {
        if (key === 'recoilScale') return settings.recoilScale;
        if (key === 'spreadScale') return settings.spreadScale;
        if (key === 'sensMouse') return settings.sensMouse;
        if (key === 'fov') return settings.fov;
        var map = { friction: 'Friction', accel: 'Accel', stopspeed: 'StopSpeed', jump: 'JumpImpulse', gravity: 'Gravity', maxspeed: 'MaxSpeed' };
        var cfg = baseCfg();
        return cfg[map[key]];
    }
    function setTune(key, v) {
        if (key === 'recoilScale') { settings.recoilScale = v; }
        else if (key === 'spreadScale') { settings.spreadScale = v; }
        else if (key === 'sensMouse') { settings.sensMouse = v; }
        else if (key === 'fov') { settings.fov = v; }
        else {
            var map = { friction: 'Friction', accel: 'Accel', stopspeed: 'StopSpeed', jump: 'JumpImpulse', gravity: 'Gravity', maxspeed: 'MaxSpeed' };
            settings.move[map[key]] = v;
        }
        saveSettings();
        applyTuning();
    }
    function valueSpan(def, span) { span.textContent = getTune(def[0]).toFixed(def[4] < 0.05 ? 2 : 1); }
    function syncTuner() {
        var rows = byId('tuner').querySelectorAll('.t-row');
        var sliders = byId('tuner').querySelectorAll('input[type=range]');
        for (var i = 0; i < sliders.length && i < tunerDefs.length; i++) {
            sliders[i].value = getTune(tunerDefs[i][0]);
            var span = sliders[i].parentElement.querySelector('span');
            if (span) span.textContent = getTune(tunerDefs[i][0]).toFixed(tunerDefs[i][4] < 0.05 ? 2 : 1);
        }
    }
    function applyTuning() {
        P.cfg = baseCfg();
        assistCfg = (function () { var c = baseCfg(); c.Friction *= 3.5; c.StopSpeed *= 2; return c; })();
        S.TUNE.recoil = settings.recoilScale;
        S.TUNE.spread = settings.spreadScale;
    }

    function togglePause() {
        var el = byId('pause');
        var open = el.style.display !== 'block';
        el.style.display = open ? 'block' : 'none';
        if (open && !isTouch) document.exitPointerLock();
    }

    // ------------------------------------------------------------------ HUD
    function drawHud() {
        var w = hud.width, h = hud.height;
        hctx.clearRect(0, 0, w, h);
        var s = Math.max(0.7, w / 1280);
        hctx.textBaseline = 'top';
        var st = S.stateOf(P);
        var spreadDeg = st ? S.computeSpread(st.def, speedRatio(), P.st.onGround, P.st.ducked) * settings.spreadScale : 1;

        // crosshair (dynamic gap)
        if (!scoped) {
            var gap = (5 + spreadDeg * 6) * s;
            var len = 7 * s;
            hctx.strokeStyle = 'rgba(140,235,140,0.95)';
            hctx.lineWidth = Math.max(1, 1.4 * s);
            hctx.beginPath();
            hctx.moveTo(w / 2, h / 2 - gap); hctx.lineTo(w / 2, h / 2 - gap - len);
            hctx.moveTo(w / 2, h / 2 + gap); hctx.lineTo(w / 2, h / 2 + gap + len);
            hctx.moveTo(w / 2 - gap, h / 2); hctx.lineTo(w / 2 - gap - len, h / 2);
            hctx.moveTo(w / 2 + gap, h / 2); hctx.lineTo(w / 2 + gap + len, h / 2);
            hctx.stroke();
        } else {
            hctx.fillStyle = 'rgba(0,0,0,0.92)';
            hctx.beginPath();
            hctx.rect(0, 0, w, h * 0.5 - 1);
            hctx.rect(0, h * 0.5 + 1, w, h * 0.5);
            hctx.fill();
            hctx.strokeStyle = 'rgba(0,0,0,0.9)';
            hctx.lineWidth = 2 * s;
            hctx.beginPath(); hctx.moveTo(0, h / 2); hctx.lineTo(w, h / 2); hctx.stroke();
            hctx.beginPath(); hctx.moveTo(w / 2, 0); hctx.lineTo(w / 2, h); hctx.stroke();
        }

        // hit marker
        if (hitMarker > 0) {
            hctx.strokeStyle = 'rgba(255,255,255,' + Math.min(1, hitMarker * 2) + ')';
            hctx.lineWidth = 2 * s;
            var d = 8 * s;
            hctx.beginPath();
            hctx.moveTo(w / 2 - d, h / 2 - d); hctx.lineTo(w / 2 + d, h / 2 + d);
            hctx.moveTo(w / 2 + d, h / 2 - d); hctx.lineTo(w / 2 - d, h / 2 + d);
            hctx.stroke();
        }

        // bottom-left: health / armour
        hctx.font = (26 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#d8e8d8';
        hctx.textAlign = 'left';
        hctx.fillText('HP ' + Math.max(0, Math.round(P.health)), 24 * s, h - 96 * s);
        hctx.fillStyle = P.armor > 0 ? '#9fc4e8' : '#6a6f73';
        hctx.fillText('AP ' + Math.round(P.armor) + (P.helmet ? ' +HELM' : ''), 24 * s, h - 62 * s);

        // bottom-right: ammo
        hctx.textAlign = 'right';
        hctx.font = (32 * s) + 'px Consolas, monospace';
        hctx.fillStyle = st ? (st.reloading ? '#e8c46a' : '#e8e8e8') : '#888';
        hctx.fillText(st ? (st.ammo + ' / ' + st.reserve) : '-', w - 24 * s, h - 76 * s);
        hctx.font = (16 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#9aa39a';
        hctx.fillText(st ? st.def.name + (st.reloading ? '  RELOADING' : '') : 'NO WEAPON', w - 24 * s, h - 40 * s);

        // money (green), bottom-centre
        hctx.textAlign = 'center';
        hctx.font = (24 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#78d878';
        hctx.fillText('$' + W.money, w / 2, h - 58 * s);

        // top-centre: timer + score
        hctx.font = (30 * s) + 'px Consolas, monospace';
        var timeText, timeColor = '#e8e8e8';
        if (W.bomb.planted) { timeText = W.bomb.timer.toFixed(1); timeColor = (Math.floor(W.time * 4) % 2) ? '#e85050' : '#ff9a9a'; }
        else if (W.phase === 'freeze') timeText = 'FREEZE ' + Math.ceil(W.phaseTime);
        else if (W.phase === 'live') {
            var t = Math.max(0, W.phaseTime);
            timeText = Math.floor(t / 60) + ':' + (t % 60 < 10 ? '0' : '') + Math.floor(t % 60);
        } else timeText = W.phase.toUpperCase();
        hctx.fillStyle = timeColor;
        hctx.fillText(timeText, w / 2, 16 * s);
        hctx.font = (20 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#cfd8cf';
        hctx.fillText('T ' + W.scoreT + ' : ' + W.scoreCT + ' CT     Round ' + W.round, w / 2, 52 * s);

        // banner
        if (W.bannerTime > 0 && W.banner) {
            hctx.font = (26 * s) + 'px Consolas, monospace';
            hctx.fillStyle = 'rgba(255,235,180,0.95)';
            hctx.fillText(W.banner, w / 2, h * 0.34);
        }
        if (!P.alive) {
            hctx.font = (34 * s) + 'px Consolas, monospace';
            hctx.fillStyle = 'rgba(255,120,120,0.95)';
            hctx.fillText('ELIMINATED', w / 2, h * 0.42);
        }

        // use / plant / defuse progress
        if (useProgress > 0) {
            var barW = 260 * s;
            hctx.fillStyle = 'rgba(0,0,0,0.5)';
            hctx.fillRect(w / 2 - barW / 2, h * 0.6, barW, 10 * s);
            hctx.fillStyle = '#e8c46a';
            hctx.fillRect(w / 2 - barW / 2, h * 0.6, barW * Math.min(1, useProgress), 10 * s);
        }

        // killfeed (top right)
        hctx.textAlign = 'right';
        hctx.font = (15 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#cfd8cf';
        for (var i = 0; i < W.killfeed.length; i++) {
            hctx.fillText(W.killfeed[i].text, w - 18 * s, 100 * s + i * 20 * s);
        }

        // radar (top left)
        drawRadar(s);

        // fps
        if (settings.showFps) {
            hctx.textAlign = 'left';
            hctx.font = (13 * s) + 'px Consolas, monospace';
            hctx.fillStyle = '#9aa39a';
            hctx.fillText(fps.toFixed(0) + ' fps', 14 * s, 12 * s);
        }
        hctx.textAlign = 'left';
    }

    function speedRatio() {
        var st = P.st, def = S.defOf(P);
        var sp = Math.sqrt(st.vel.x * st.vel.x + st.vel.z * st.vel.z);
        var m = Math.max(0.001, P.cfg.MaxSpeed * (def ? def.moveSpeedMultiplier : 1));
        return Math.min(1, sp / m);
    }

    function drawRadar(s) {
        var size = 150 * s, pad = 14 * s;
        var w = W.map;
        var minX = -32, maxX = 32, minZ = -26, maxZ = 26;
        var sc = size / (maxX - minX);
        hctx.save();
        hctx.globalAlpha = 0.72;
        hctx.fillStyle = '#181c1a';
        hctx.fillRect(pad, 34 * s, size, (maxZ - minZ) * sc);
        function tx(x) { return pad + (x - minX) * sc; }
        function tz(z) { return 34 * s + (z - minZ) * sc; }
        hctx.fillStyle = '#3c443c';
        for (var i = 0; i < w.boxes.length; i++) {
            var b = w.boxes[i];
            var hgt = b.max.y - b.min.y;
            if (hgt < 1.2) continue;
            var bw = (b.max.x - b.min.x) * sc, bh = (b.max.z - b.min.z) * sc;
            if (bw * bh < 3) continue;
            hctx.fillRect(tx(b.min.x), tz(b.min.z), bw, bh);
        }
        hctx.fillStyle = 'rgba(200,80,70,0.5)';
        hctx.fillRect(tx(w.siteA.min.x), tz(w.siteA.min.z), (w.siteA.max.x - w.siteA.min.x) * sc, (w.siteA.max.z - w.siteA.min.z) * sc);
        hctx.fillRect(tx(w.siteB.min.x), tz(w.siteB.min.z), (w.siteB.max.x - w.siteB.min.x) * sc, (w.siteB.max.z - w.siteB.min.z) * sc);
        hctx.font = (11 * s) + 'px Consolas, monospace';
        hctx.fillStyle = '#e8a0a0';
        hctx.fillText('A', tx(20), tz(9));
        hctx.fillText('B', tx(-22), tz(9));
        function dot(x, z, color) { hctx.fillStyle = color; hctx.fillRect(tx(x) - 2.5 * s, tz(z) - 2.5 * s, 5 * s, 5 * s); }
        var eye = { x: P.st.pos.x, y: P.st.pos.y + 1.6, z: P.st.pos.z };
        for (var a = 0; a < W.actors.length; a++) {
            var act = W.actors[a];
            if (!act.alive || act === P) continue;
            if (act.team === P.team) dot(act.st.pos.x, act.st.pos.z, '#7fa8e0');
            else {
                if (S.canSee(W.world, W.actors, eye, { x: act.st.pos.x, y: act.st.pos.y + 1.2, z: act.st.pos.z }, P, act, 45)) {
                    dot(act.st.pos.x, act.st.pos.z, '#e06060');
                }
            }
        }
        if (W.bomb.planted) dot(W.bomb.pos.x, W.bomb.pos.z, '#e0a040');
        dot(P.st.pos.x, P.st.pos.z, '#f0f0f0');
        hctx.restore();
    }

    // ------------------------------------------------------------------ plant / defuse (player)
    function tickUse(dt) {
        useProgress = 0;
        if (!P.alive || W.phase !== 'live') return;
        var p = P.st.pos;
        if (P.team === 'T' && P.hasBomb && !W.bomb.planted) {
            var inA = W.pointInBox(p, W.map.siteA), inB = W.pointInBox(p, W.map.siteB);
            if ((inA || inB) && (useHeld || keys['KeyE'])) {
                useProgress = Math.min(1, (P._useT = (P._useT || 0) + dt) / S.RULES.plantTime);
                if (P._useT >= S.RULES.plantTime) { P._useT = 0; useProgress = 0; W.plantBomb(W, P, inA ? 'A' : 'B'); }
                return;
            }
            P._useT = 0;
        }
        if (P.team === 'CT' && W.bomb.planted) {
            var d = S.vdist(p, W.bomb.pos);
            if (d < 2.2 && (useHeld || keys['KeyE'])) {
                var need = P.kit ? S.RULES.defuseKit : S.RULES.defuseTime;
                useProgress = Math.min(1, (P._useT = (P._useT || 0) + dt) / need);
                if (P._useT >= need) { P._useT = 0; useProgress = 0; W.defuseBomb(W, P); }
                return;
            }
            P._useT = 0;
        }
    }

    // ------------------------------------------------------------------ main loop
    var acc = 0, last = performance.now();
    var FIXED = 1 / 64;
    function frame(now) {
        requestAnimationFrame(frame);
        var dt = Math.min(0.1, (now - last) / 1000);
        last = now;
        fpsT += dt; fpsN++;
        if (fpsT > 0.5) { fps = fpsN / fpsT; fpsT = 0; fpsN = 0; }

        if (!GZ.debug.paused) {
            // desktop movement keys
            if (!isTouch) {
                input.forward = (keys['KeyW'] ? 1 : 0) - (keys['KeyS'] ? 1 : 0);
                input.right = (keys['KeyD'] ? 1 : 0) - (keys['KeyA'] ? 1 : 0);
                input.walk = !!keys['ShiftLeft'] || !!keys['ShiftRight'];
                input.duck = !!keys['ControlLeft'] || !!keys['ControlRight'];
                input.jump = !!keys['Space'];
            } else {
                input.forward = -stick.dy;
                input.right = stick.dx;
            }
            input.yaw = view.yaw;

            acc += dt;
            var steps = 0;
            while (acc >= FIXED && steps < 6) {
                acc -= FIXED;
                steps++;
                if (settings.autoStop && fireHeld && P.st.onGround) P.cfg = assistCfg; else P.cfg = baseCfg();
                var st0 = S.stateOf(P);
                var vy = view.yaw + (st0 ? st0.recoilYaw : 0);
                var vp = view.pitch + (st0 ? st0.recoilPitch : 0);
                P.yaw = vy; P.pitch = vp;
                W.update(FIXED, input);
                tickUse(FIXED);
                if (fireHeld) W.fireShot(W, P, view.yaw + (st0 ? st0.recoilYaw : 0), view.pitch + (st0 ? st0.recoilPitch : 0), true);
            }
            if (steps >= 6) acc = 0;
        }

        // hit marker / feedback
        for (var i = 0; i < W.events.length; i++) if (W.events[i].type === 'hit') hitMarker = 0.35;
        if (hitMarker > 0) hitMarker -= dt;

        render(now);
    }

    function render(now) {
        var st = S.stateOf(P);
        var camYaw = view.yaw + (st ? st.recoilYaw : 0);
        var camPitch = view.pitch + (st ? st.recoilPitch : 0);
        var eyeY = P.st.pos.y + S.currentHeight(P.cfg, P.st) - 0.16;
        var w = Math.floor(canvas.clientWidth * Math.min(1.5, window.devicePixelRatio || 1));
        var h = Math.floor(canvas.clientHeight * Math.min(1.5, window.devicePixelRatio || 1));
        if (hud.width !== w || hud.height !== h) { hud.width = w; hud.height = h; }
        var cam = {
            pos: { x: P.st.pos.x, y: eyeY + (P.alive ? 0 : -0.4), z: P.st.pos.z },
            yaw: camYaw, pitch: camPitch,
            fov: scoped ? (st ? st.def.scopeFov : 15) : settings.fov
        };
        R.beginFrame(cam, w, h);
        R.drawMap();
        R.drawActors(W.actors, P);
        R.drawFxWorld(W, W.actors);
        if (P.alive) {
            var kind = 'rifle';
            if (st) {
                if (st.def.kind === 'pistol') kind = 'pistol';
                else if (st.def.kind === 'sniper') kind = 'sniper';
            }
            R.drawViewmodel(kind, Math.max(0, (st ? st.recoilPitch : 0)) * 0.6, stick.dx * 0.3, scoped);
        }
        drawHud();
    }

    // ------------------------------------------------------------------ debug API (used by headless verification)
    var GZ = {
        ready: true, W: W, S: S, settings: settings, input: input, view: view,
        debug: {
            paused: false,
            step: function (n, dt) {
                dt = dt || FIXED;
                for (var i = 0; i < n; i++) {
                    P.yaw = view.yaw; P.pitch = view.pitch;
                    W.update(dt, input);
                    tickUse(dt);
                }
                render(performance.now());
            },
            setInput: function (o) { for (var k in o) input[k] = o[k]; },
            look: function (yaw, pitch) { view.yaw = yaw; if (pitch !== undefined) view.pitch = pitch; },
            fire: function () {
                var st = S.stateOf(P);
                return W.fireShot(W, P, view.yaw + (st ? st.recoilYaw : 0), view.pitch + (st ? st.recoilPitch : 0), true);
            },
            buy: function (id) { return W.buy(id); },
            money: function (m) { W.money = m; },
            phase: function (p, t) { W.phase = p; W.phaseTime = t === undefined ? 999 : t; },
            place: function (x, z) { P.st.pos = S.V(x, 0, z); P.st.vel = S.V(0, 0, 0); },
            stats: function () {
                var st = S.stateOf(P);
                return {
                    phase: W.phase, phaseTime: W.phaseTime, round: W.round, score: W.scoreT + ':' + W.scoreCT,
                    money: W.money, health: P.health, armor: P.armor, alive: P.alive,
                    ammo: st ? st.ammo : -1, reserve: st ? st.reserve : -1,
                    weapon: st ? st.def.id : null, pos: P.st.pos, yaw: view.yaw, onGround: P.st.onGround,
                    aliveT: W.aliveT(), aliveCT: W.aliveCT(), bombPlanted: W.bomb.planted, bombTimer: W.bomb.timer,
                    killfeed: W.killfeed.map(function (k) { return k.text; }),
                    shots: W.shots.length, hits: W.hits.length, fps: fps
                };
            }
        }
    };
    root.GZ = GZ;

    // ------------------------------------------------------------------ boot
    buildTuner();
    setupTouchUi();
    applyTuning();
    var qs = location.search;
    if (qs.indexOf('hud=0') >= 0) hud.style.display = 'none';
    requestAnimationFrame(frame);
    console.log('[GreyZone] demo ready (WebGL, procedural, sim ported from the Unity Core)');
})(typeof window !== 'undefined' ? window : globalThis);
