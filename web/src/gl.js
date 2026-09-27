// GreyZone demo - minimal WebGL1 renderer: vertex colours, one directional light, linear fog.
// Everything is box geometry built at runtime (no textures, no shadows, no post processing).
(function (root) {
    'use strict';
    var S = root.GZSim;

    // ------------------------------------------------------------------ mat4 (column major, like GL)
    function m4() { var m = new Float32Array(16); m[0] = m[5] = m[10] = m[15] = 1; return m; }
    function m4mul(a, b, out) {
        out = out || new Float32Array(16);
        for (var c = 0; c < 4; c++) {
            for (var r = 0; r < 4; r++) {
                out[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
            }
        }
        return out;
    }
    function m4perspective(fovyDeg, aspect, near, far) {
        var m = new Float32Array(16);
        var f = 1 / Math.tan(fovyDeg * Math.PI / 360);
        m[0] = f / aspect; m[5] = f; m[10] = (far + near) / (near - far); m[11] = -1;
        m[14] = 2 * far * near / (near - far);
        return m;
    }
    function m4view(pos, yawDeg, pitchDeg) {
        // V = Rx(-pitch) * Ry(-yaw) * T(-pos)
        var y = -yawDeg * Math.PI / 180, p = -pitchDeg * Math.PI / 180;
        var cy = Math.cos(y), sy = Math.sin(y), cp = Math.cos(p), sp = Math.sin(p);
        var m = m4();
        // Ry(-yaw)
        var ry = [cy, 0, -sy, 0, 0, 1, 0, 0, sy, 0, cy, 0, 0, 0, 0, 1];
        // Rx(-pitch)
        var rx = [1, 0, 0, 0, 0, cp, sp, 0, 0, -sp, cp, 0, 0, 0, 0, 1];
        var t = m4(); t[12] = -pos.x; t[13] = -pos.y; t[14] = -pos.z;
        m4mul(ry, m4mul(rx, t, m), m);
        return m;
    }
    function m4model(x, y, z, yawDeg, yaw2Deg) {
        var m = m4();
        var y = (yawDeg + (yaw2Deg || 0)) * Math.PI / 180;
        var c = Math.cos(y), s = Math.sin(y);
        m[0] = c; m[2] = -s; m[8] = s; m[10] = c;
        m[12] = x; m[13] = y; m[14] = z;
        return m;
    }

    // ------------------------------------------------------------------ shaders
    var LIT_VS = [
        'attribute vec3 aPos; attribute vec3 aNormal; attribute vec3 aColor;',
        'uniform mat4 uMVP; uniform vec3 uCamPos; uniform vec3 uFogColor;',
        'uniform float uFogStart; uniform float uFogEnd; uniform vec3 uLightDir; uniform float uAmbient;',
        'varying vec3 vColor;',
        'void main(){',
        '  float lam = max(dot(normalize(aNormal), uLightDir), 0.0) * 0.72 + uAmbient;',
        '  vec3 c = aColor * lam;',
        '  float d = distance(aPos, uCamPos);',
        '  float f = clamp((d - uFogStart) / max(1.0, uFogEnd - uFogStart), 0.0, 1.0);',
        '  vColor = mix(c, uFogColor, f);',
        '  gl_Position = uMVP * vec4(aPos, 1.0);',
        '}'
    ].join('\n');
    var LIT_FS = 'precision mediump float; varying vec3 vColor; void main(){ gl_FragColor = vec4(vColor, 1.0); }';
    var FX_VS = [
        'attribute vec3 aPos; attribute vec4 aColor;',
        'uniform mat4 uMVP;',
        'varying vec4 vColor;',
        'void main(){ vColor = aColor; gl_Position = uMVP * vec4(aPos, 1.0); }'
    ].join('\n');
    var FX_FS = 'precision mediump float; varying vec4 vColor; void main(){ gl_FragColor = vColor; }';

    function compile(gl, vsSrc, fsSrc) {
        function sh(type, src) {
            var s = gl.createShader(type);
            gl.shaderSource(s, src);
            gl.compileShader(s);
            if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error('shader: ' + gl.getShaderInfoLog(s));
            return s;
        }
        var p = gl.createProgram();
        gl.attachShader(p, sh(gl.VERTEX_SHADER, vsSrc));
        gl.attachShader(p, sh(gl.FRAGMENT_SHADER, fsSrc));
        gl.linkProgram(p);
        if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error('link: ' + gl.getProgramInfoLog(p));
        return p;
    }

    // ------------------------------------------------------------------ mesh helpers
    function MeshBuilder() { this.v = []; }
    MeshBuilder.prototype.box = function (cx, cy, cz, sx, sy, sz, c, yawDeg) {
        var hx = sx / 2, hy = sy / 2, hz = sz / 2;
        var yaw = (yawDeg || 0) * Math.PI / 180, cs = Math.cos(yaw), sn = Math.sin(yaw);
        var r = c[0], g = c[1], b = c[2];
        function P(x, y, z) { return [cx + x * cs + z * sn, cy + y, cz + (-x * sn + z * cs)]; }
        var p = [P(-hx, -hy, -hz), P(hx, -hy, -hz), P(hx, -hy, hz), P(-hx, -hy, hz), P(-hx, hy, -hz), P(hx, hy, -hz), P(hx, hy, hz), P(-hx, hy, hz)];
        var faces = [
            [4, 7, 6, 5, [0, 1, 0]], [0, 1, 2, 3, [0, -1, 0]],
            [0, 4, 5, 1, [0, 0, -1]], [3, 2, 6, 7, [0, 0, 1]],
            [1, 5, 6, 2, [1, 0, 0]], [0, 3, 7, 4, [-1, 0, 0]]
        ];
        for (var i = 0; i < faces.length; i++) {
            var f = faces[i];
            var n = f[4];
            var quad = [p[f[0]], p[f[1]], p[f[2]], p[f[0]], p[f[2]], p[f[3]]];
            for (var k = 0; k < 6; k++) {
                var v = quad[k];
                this.v.push(v[0], v[1], v[2], n[0], n[1], n[2], r, g, b);
            }
        }
    };
    MeshBuilder.prototype.data = function () { return new Float32Array(this.v); };

    // palette (desaturated early-CSGO greybox)
    var PAL = {
        floor: [0.42, 0.43, 0.41], wall: [0.48, 0.49, 0.47], crate: [0.43, 0.35, 0.25],
        container: [0.36, 0.37, 0.35], dark: [0.30, 0.31, 0.30], site: [0.50, 0.26, 0.23],
        tLight: [0.44, 0.39, 0.28], tDark: [0.34, 0.30, 0.22], cLight: [0.31, 0.36, 0.44], cDark: [0.24, 0.28, 0.34],
        skin: [0.72, 0.58, 0.45], gun: [0.17, 0.17, 0.18], bomb: [0.35, 0.33, 0.25]
    };

    // blocky soldier: legs + upper (upper pivot 1.05, gun held to the right)
    function actorBoxes(ct) {
        var cloth = ct ? PAL.cLight : PAL.tLight, vest = ct ? PAL.cDark : PAL.tDark;
        return {
            legs: [
                [-0.115, 0.44, 0, 0.17, 0.88, 0.21, cloth], [0.115, 0.44, 0, 0.17, 0.88, 0.21, cloth],
                [-0.115, 0.06, 0.02, 0.19, 0.12, 0.27, PAL.dark], [0.115, 0.06, 0.02, 0.19, 0.12, 0.27, PAL.dark]
            ],
            upper: (function () {
                var piv = 1.05;
                return [
                    [0, 1.12 - piv, 0, 0.44, 0.56, 0.26, cloth],
                    [0, 1.16 - piv, 0, 0.47, 0.36, 0.30, vest],
                    [0, 1.585 - piv, 0, 0.21, 0.23, 0.23, PAL.skin],
                    [0, 1.70 - piv, 0, 0.25, 0.12, 0.26, ct ? PAL.cDark : PAL.tDark],
                    [-0.27, 1.13 - piv, 0, 0.12, 0.48, 0.14, cloth],
                    [0.27, 1.13 - piv, 0, 0.12, 0.48, 0.14, cloth],
                    [0.17, 1.22 - piv, 0.26, 0.07, 0.13, 0.52, PAL.gun],
                    [0.17, 1.26 - piv, 0.62, 0.05, 0.06, 0.28, PAL.gun],
                    [0.17, 1.33 - piv, 0.32, 0.04, 0.05, 0.14, PAL.dark]
                ];
            })(),
            pivot: 1.05
        };
    }

    // viewmodel meshes in camera space (x right, y up, z forward)
    function viewmodelBoxes(kind) {
        if (kind === 'pistol') {
            return [
                [0.02, -0.02, 0.34, 0.06, 0.10, 0.34, PAL.gun],
                [0.02, -0.13, 0.30, 0.05, 0.16, 0.10, PAL.dark],
                [0.02, 0.03, 0.20, 0.04, 0.03, 0.10, PAL.dark]
            ];
        }
        if (kind === 'sniper') {
            return [
                [0.03, -0.01, 0.52, 0.07, 0.12, 0.70, PAL.gun],
                [0.03, -0.14, 0.44, 0.06, 0.20, 0.12, PAL.dark],
                [0.03, 0.10, 0.42, 0.05, 0.06, 0.34, PAL.dark],
                [0.03, 0.16, 0.44, 0.04, 0.05, 0.16, PAL.gun],
                [0.03, -0.03, 0.92, 0.04, 0.04, 0.26, PAL.dark]
            ];
        }
        // rifle
        return [
            [0.03, -0.01, 0.46, 0.07, 0.13, 0.58, PAL.gun],
            [0.03, -0.16, 0.40, 0.06, 0.24, 0.12, PAL.dark],
            [0.03, -0.10, 0.62, 0.05, 0.10, 0.14, PAL.dark],
            [0.03, 0.09, 0.40, 0.04, 0.05, 0.20, PAL.dark],
            [0.03, 0.01, 0.80, 0.04, 0.04, 0.24, PAL.gun]
        ];
    }

    // ------------------------------------------------------------------ renderer
    function Renderer(canvas) {
        this.canvas = canvas;
        this.gl = canvas.getContext('webgl', { antialias: false, alpha: false, depth: true, powerPreference: 'high-performance' });
        if (!this.gl) throw new Error('WebGL not available');
        var gl = this.gl;
        this.lit = compile(gl, LIT_VS, LIT_FS);
        this.fx = compile(gl, FX_VS, FX_FS);
        // static map mesh
        this.mapMesh = new MeshBuilder();
        this.mapDirty = false;
        this.staticBuf = gl.createBuffer();
        this.actorBuf = gl.createBuffer();
        this.alphaBuf = gl.createBuffer();
        this.addBuf = gl.createBuffer();
        this.lineBuf = gl.createBuffer();
        this.actorMesh = new MeshBuilder();
        this.alphaMesh = new MeshBuilder();
        this.addMesh = new MeshBuilder();
        this.lineVerts = [];
        gl.enable(gl.DEPTH_TEST);
        gl.disable(gl.CULL_FACE);
        this.litLoc = {
            aPos: gl.getAttribLocation(this.lit, 'aPos'), aNormal: gl.getAttribLocation(this.lit, 'aNormal'),
            aColor: gl.getAttribLocation(this.lit, 'aColor'), uMVP: gl.getUniformLocation(this.lit, 'uMVP'),
            uCamPos: gl.getUniformLocation(this.lit, 'uCamPos'), uFogColor: gl.getUniformLocation(this.lit, 'uFogColor'),
            uFogStart: gl.getUniformLocation(this.lit, 'uFogStart'), uFogEnd: gl.getUniformLocation(this.lit, 'uFogEnd'),
            uLightDir: gl.getUniformLocation(this.lit, 'uLightDir'), uAmbient: gl.getUniformLocation(this.lit, 'uAmbient')
        };
        this.fxLoc = {
            aPos: gl.getAttribLocation(this.fx, 'aPos'), aColor: gl.getAttribLocation(this.fx, 'aColor'),
            uMVP: gl.getUniformLocation(this.fx, 'uMVP')
        };
    }

    Renderer.prototype.buildMapMesh = function (boxes) {
        var mb = new MeshBuilder();
        for (var i = 0; i < boxes.length; i++) {
            var b = boxes[i];
            var c = PAL.floor;
            if (b.color === 'wall') c = PAL.wall;
            else if (b.color === 'crate') c = PAL.crate;
            else if (b.color === 'container') c = PAL.container;
            mb.box((b.min.x + b.max.x) / 2, (b.min.y + b.max.y) / 2, (b.min.z + b.max.z) / 2,
                b.max.x - b.min.x, b.max.y - b.min.y, b.max.z - b.min.z, c);
        }
        this.mapMesh = mb;
        var gl = this.gl;
        gl.bindBuffer(gl.ARRAY_BUFFER, this.staticBuf);
        gl.bufferData(gl.ARRAY_BUFFER, mb.data(), gl.STATIC_DRAW);
        this.mapVertCount = mb.v.length / 9;
    };

    Renderer.prototype.beginFrame = function (cam, w, h) {
        var gl = this.gl;
        if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
        gl.viewport(0, 0, w, h);
        gl.clearColor(0.44, 0.47, 0.49, 1);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        this.proj = m4perspective(cam.fov, w / h, 0.05, 250);
        this.view = m4view(cam.pos, cam.yaw, cam.pitch);
        this.vp = m4mul(this.proj, this.view, new Float32Array(16));
        this.projOnly = this.proj;
        this.camPos = cam.pos;
    };

    Renderer.prototype.bindLit = function (mvp) {
        var gl = this.gl, L = this.litLoc;
        gl.useProgram(this.lit);
        gl.uniformMatrix4fv(L.uMVP, false, mvp);
        gl.uniform3f(L.uCamPos, this.camPos.x, this.camPos.y, this.camPos.z);
        gl.uniform3f(L.uFogColor, 0.42, 0.44, 0.46);
        gl.uniform1f(L.uFogStart, 25);
        gl.uniform1f(L.uFogEnd, 140);
        gl.uniform3f(L.uLightDir, 0.55, 0.72, 0.42);
        gl.uniform1f(L.uAmbient, 0.34);
    };

    Renderer.prototype.drawArray = function (buf, count, stride) {
        var gl = this.gl;
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.vertexAttribPointer(this.litLoc.aPos, 3, gl.FLOAT, false, stride, 0);
        gl.enableVertexAttribArray(this.litLoc.aPos);
        gl.vertexAttribPointer(this.litLoc.aNormal, 3, gl.FLOAT, false, stride, 12);
        gl.enableVertexAttribArray(this.litLoc.aNormal);
        gl.vertexAttribPointer(this.litLoc.aColor, 3, gl.FLOAT, false, stride, 24);
        gl.enableVertexAttribArray(this.litLoc.aColor);
        gl.drawArrays(gl.TRIANGLES, 0, count);
    };

    Renderer.prototype.drawMap = function () {
        this.bindLit(this.vp);
        this.drawArray(this.staticBuf, this.mapVertCount, 36);
    };

    // actors are rebuilt into one world-space vertex buffer each frame (1 draw call)
    Renderer.prototype.drawActors = function (actors, playerActor) {
        var mb = this.actorMesh;
        mb.v.length = 0;
        var gl = this.gl;
        for (var i = 0; i < actors.length; i++) {
            var a = actors[i];
            if (!a.alive) continue;
            var look = a.isPlayer ? playerActor : null;
            var y = a.yaw;
            if (look) y = look.yaw;
            var ct = a.team === 'CT';
            var model = actorBoxes(ct);
            var f = a.st.pos;
            var duckDrop = (a.cfg.StandHeight - a.cfg.DuckHeight) * a.st.duckFrac;
            var j, b;
            for (j = 0; j < model.legs.length; j++) {
                b = model.legs[j];
                mb.box(f.x + rotX(b[0], b[2], y), f.y + b[1], f.z + rotZ(b[0], b[2], y), b[3], b[4], b[5], b[6], y);
            }
            var upY = f.y + model.pivot - duckDrop;
            for (j = 0; j < model.upper.length; j++) {
                b = model.upper[j];
                if (a.isPlayer) {
                    if (j < 6) continue;             // don't render your own body in first person
                }
                var y2 = a.isPlayer ? a.yaw : a.yaw;   // upper faces view yaw
                mb.box(f.x + rotX(b[0], b[2], y2), upY + b[1], f.z + rotZ(b[0], b[2], y2), b[3], b[4], b[5], b[6], y2);
            }
        }
        if (this.actorDebug) { }
        gl.bindBuffer(gl.ARRAY_BUFFER, this.actorBuf);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(mb.v), gl.DYNAMIC_DRAW);
        this.bindLit(this.vp);
        this.drawArray(this.actorBuf, mb.v.length / 9, 36);
    };

    function rotX(x, z, yawDeg) { var y = yawDeg * Math.PI / 180; return x * Math.cos(y) + z * Math.sin(y); }
    function rotZ(x, z, yawDeg) { var y = yawDeg * Math.PI / 180; return -x * Math.sin(y) + z * Math.cos(y); }

    Renderer.prototype.drawViewmodel = function (kind, kick, sway, scoped) {
        if (scoped) return;
        var gl = this.gl;
        var mb = new MeshBuilder();
        var boxes = viewmodelBoxes(kind);
        var k = kick || 0, s = sway || 0;
        for (var i = 0; i < boxes.length; i++) {
            var b = boxes[i];
            mb.box(b[0], b[1] - k * 0.03 + s * 0.01, b[2] - k * 0.06, b[3], b[4], b[5], b[6]);
        }
        gl.clear(gl.DEPTH_BUFFER_BIT);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.actorBuf);
        gl.bufferData(gl.ARRAY_BUFFER, mb.data(), gl.DYNAMIC_DRAW);
        this.bindLit(this.projOnly);
        this.drawArray(this.actorBuf, mb.v.length / 9, 36);
    };

    Renderer.prototype.bindFx = function (mvp) {
        var gl = this.gl;
        gl.useProgram(this.fx);
        gl.uniformMatrix4fv(this.fxLoc.uMVP, false, mvp);
        gl.enable(gl.BLEND);
    };

    Renderer.prototype.drawFxWorld = function (world, actors) {
        var gl = this.gl;
        // ---- additive: tracers + muzzle-ish flashes
        var add = this.addMesh;
        add.v.length = 0;
        var i, k;
        for (i = 0; i < world.shots.length; i++) {
            var s = world.shots[i];
            var d = S.vsub(s.to, s.from);
            var len = S.vlen(d);
            if (len < 0.05) continue;
            var dn = S.vmul(d, 1 / len);
            var side = S.vnorm(S.vcross(dn, { x: 0, y: 1, z: 0 }));
            if (!isFinite(side.x) || S.vlen(side) < 0.1) side = { x: 1, y: 0, z: 0 };
            var w = 0.03;
            addQuad(add, S.vadd(s.from, S.vmul(side, -w)), S.vadd(s.from, S.vmul(side, w)),
                S.vadd(s.to, S.vmul(side, w)), S.vadd(s.to, S.vmul(side, -w)), [1.0, 0.88, 0.5, 0.75]);
        }
        this.uploadAndDraw(add, this.addBuf, gl.ONE);
        // ---- alpha: impacts, blood, decals, blobs
        var al = this.alphaMesh;
        al.v.length = 0;
        for (i = 0; i < world.hits.length; i++) {
            var h = world.hits[i];
            var size = h.flesh ? (h.big ? 0.24 : 0.16) : 0.10;
            var c = h.flesh ? [0.55, 0.10, 0.10, 0.85] : [0.30, 0.30, 0.30, 0.75];
            var n = h.normal;
            var up = Math.abs(n.y) > 0.9 ? { x: 1, y: 0, z: 0 } : { x: 0, y: 1, z: 0 };
            var right = S.vnorm(S.vcross(up, n));
            var up2 = S.vnorm(S.vcross(n, right));
            var o = S.vadd(h.point, S.vmul(n, 0.02));
            addQuad(al,
                S.vadd(o, S.vadd(S.vmul(right, -size), S.vmul(up2, -size))),
                S.vadd(o, S.vadd(S.vmul(right, size), S.vmul(up2, -size))),
                S.vadd(o, S.vadd(S.vmul(right, size), S.vmul(up2, size))),
                S.vadd(o, S.vadd(S.vmul(right, -size), S.vmul(up2, size))), c);
        }
        for (i = 0; i < actors.length; i++) {
            var a = actors[i];
            if (!a.alive) continue;
            var p = a.st.pos;
            var r = 0.45;
            addQuad(al, { x: p.x - r, y: p.y + 0.02, z: p.z - r }, { x: p.x + r, y: p.y + 0.02, z: p.z - r },
                { x: p.x + r, y: p.y + 0.02, z: p.z + r }, { x: p.x - r, y: p.y + 0.02, z: p.z + r }, [0, 0, 0, 0.35]);
        }
        if (world.bomb.planted) {
            var bp = world.bomb.pos;
            addQuad(al, { x: bp.x - 0.3, y: bp.y + 0.02, z: bp.z - 0.3 }, { x: bp.x + 0.3, y: bp.y + 0.02, z: bp.z - 0.3 },
                { x: bp.x + 0.3, y: bp.y + 0.02, z: bp.z + 0.3 }, { x: bp.x - 0.3, y: bp.y + 0.02, z: bp.z + 0.3 }, [1.0, 0.2, 0.15, 0.5]);
        }
        this.uploadAndDraw(al, this.alphaBuf, gl.SRC_ALPHA);
    };

    Renderer.prototype.uploadAndDraw = function (mb, buf, srcFactor) {
        var gl = this.gl;
        if (mb.v.length === 0) return;
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(mb.v), gl.DYNAMIC_DRAW);
        this.bindFx(this.vp);
        gl.blendFunc(srcFactor, gl.ONE_MINUS_SRC_ALPHA);
        if (srcFactor === gl.ONE) gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
        gl.disableVertexAttribArray(this.litLoc.aNormal);
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.vertexAttribPointer(this.fxLoc.aPos, 3, gl.FLOAT, false, 28, 0);
        gl.enableVertexAttribArray(this.fxLoc.aPos);
        gl.vertexAttribPointer(this.fxLoc.aColor, 4, gl.FLOAT, false, 28, 12);
        gl.enableVertexAttribArray(this.fxLoc.aColor);
        gl.drawArrays(gl.TRIANGLES, 0, mb.v.length / 7);
    };

    function addQuad(mb, a, b, c, d, color) {
        var n = [0, 1, 0];
        var tri = [a, b, c, a, c, d];
        for (var i = 0; i < 6; i++) {
            mb.v.push(tri[i].x, tri[i].y, tri[i].z, color[0], color[1], color[2], color[3] !== undefined ? color[3] : 1);
        }
    }

    root.GZGL = { Renderer: Renderer, PAL: PAL, actorBoxes: actorBoxes, MeshBuilder: MeshBuilder };
})(typeof window !== 'undefined' ? window : globalThis);
