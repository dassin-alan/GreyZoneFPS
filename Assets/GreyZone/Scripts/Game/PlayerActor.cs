// GreyZone - local player: desktop + touch input, Core PlayerMover stepping, weapon handling,
// scope, planting/defusing, death camera. Recoil is added straight to the view so the player
// has to counter it, exactly like the original game does.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class PlayerActor : Actor
    {
        public Camera Cam;
        public Transform CamTf;
        public ViewModel View;

        public float Yaw;
        public float Pitch;
        public bool Scoped;

        // plant / defuse progress for the HUD
        public float UseProgress01;
        public bool UseActive;
        public bool UseIsDefuse;

        private float _useTime;
        private float _accum;
        private float _stepTimer;
        private float _autoStopUntil;
        private float _switchTime;
        private float _scopeLock;
        private float _fovCurrent = 70f;
        private MoveConfig _brakeCfg;
        private bool _useHeld;

        public override void Init(Team team)
        {
            base.Init(team);
            Name = "YOU";
            IsPlayer = true;
            gameObject.name = "Player";

            Cam = Camera.main;
            if (Cam == null)
            {
                GameObject go = new GameObject("GzCamera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            CamTf = Cam.transform;
            GameBootstrap.ApplyCameraQuality(Cam);
            _fovCurrent = GameSettings.Fov;

            View = CamTf.gameObject.GetComponent<ViewModel>();
            if (View == null) View = CamTf.gameObject.AddComponent<ViewModel>();
            View.Init(CamTf);

            _brakeCfg = GameSettings.Move;
            Yaw = 0f;
            Pitch = 0f;
        }

        // ------------------------------------------------------------------ view helpers
        public float ViewYawDeg { get { return Yaw + States[Slot].RecoilYaw; } }
        public float ViewPitchDeg { get { return Pitch + States[Slot].RecoilPitch; } }

        public void AddView(float dYaw, float dPitch)
        {
            Yaw = GZ.WrapAngle(Yaw + dYaw);
            Pitch = Mathf.Clamp(Pitch + dPitch, -89f, 89f);
        }

        public void ResetView(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        // ------------------------------------------------------------------ lifecycle
        public override void Respawn(Vector3 pos, float yaw)
        {
            base.Respawn(pos, yaw);
            ResetView(yaw, 0f);
            Scoped = false;
            UseProgress01 = 0f;
            UseActive = false;
            _useHeld = false;
            _useTime = 0f;
            _accum = 0f;
            _autoStopUntil = 0f;
            _switchTime = 0f;
            if (View != null)
            {
                View.SetWeapon(Def);
                View.Hide(false);
            }
        }

        public override void Die(Actor killer, WeaponDef weapon, bool headshot)
        {
            base.Die(killer, weapon, headshot);
            Scoped = false;
            UseProgress01 = 0f;
            UseActive = false;
            if (View != null) View.Hide(true);
        }

        protected override void Update()
        {
            base.Update();

            GameDirector gd = GameDirector.I;
            bool paused = gd != null && gd.Paused;

            if (!GameSettings.TouchMode)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && gd != null) gd.TogglePause();
                if (Input.GetKeyDown(KeyCode.F1)) TunerPanel.Toggle();
            }

            float dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            if (paused) dt = 0f;

            if (GameSettings.TouchMode && TouchControls.I != null)
            {
                TouchControls.I.Tick(dt);
                if (gd != null && gd.Paused)
                {
                    TouchControls.I.ClearState();
                    TickCamera(0f);
                    return;
                }
            }

            if (!Alive)
            {
                DeadLook(dt);
                return;
            }

            float now = GameClock.Now;
            bool canAct = gd == null || gd.CanAct;

            MoveInput mi = new MoveInput();
            mi.YawDeg = ViewYawDeg;
            float yawDelta = 0f;
            float pitchDelta = 0f;

            if (dt > 0f) ReadInput(ref mi, dt, ref yawDelta, ref pitchDelta);

            if (View != null)
            {
                View.SetWeapon(Def);
                View.Tick(dt, SpeedRatio01, yawDelta, pitchDelta, Move.OnGround, Scoped);
            }

            // ---- fixed step movement (Core PlayerMover)
            if (canAct && dt > 0f)
            {
                _accum += dt;
                int guard = 0;
                MoveConfig cfg = GameSettings.Move;
                bool braking = GameSettings.AutoStop && now < _autoStopUntil;
                if (braking)
                {
                    _brakeCfg = GameSettings.Move;
                    _brakeCfg.Friction = GameSettings.Move.Friction * 3.5f;
                    _brakeCfg.StopSpeed = GameSettings.Move.StopSpeed * 2f;
                    _brakeCfg.ClampToValid();
                    cfg = _brakeCfg;
                }
                float mult = Def == null ? 1f : Def.MoveSpeedMultiplier;
                while (_accum >= GZ.FixedStep && guard++ < 4)
                {
                    _accum -= GZ.FixedStep;
                    StepMove(mi, mult, GZ.FixedStep, cfg);
                }
                if (guard >= 4) _accum = 0f;
            }
            else
            {
                _accum = 0f;
            }

            TickWeapon(dt, now);
            TickUse(dt, canAct);
            TickFootsteps(dt);
            TickCamera(dt);
            MarkVisibleEnemies();
        }

        // ------------------------------------------------------------------ input
        private void ReadInput(ref MoveInput mi, float dt, ref float yawDelta, ref float pitchDelta)
        {
            GameDirector gd = GameDirector.I;
            bool frozen = gd != null && gd.Frozen;
            bool uiOpen = gd != null && (gd.Paused || gd.BuyMenuOpen);
            bool blockAct = uiOpen || TunerPanel.IsOpen;

            if (GameSettings.TouchMode && TouchControls.I != null)
            {
                TouchControls tc = TouchControls.I;
                if (tc.BuyDown && gd != null) gd.ToggleBuy();
                float aa = AimAssistFactor();
                yawDelta = tc.LookDelta.x * aa;
                pitchDelta = tc.LookDelta.y * aa;
                AddView(yawDelta, pitchDelta);
                if (!frozen && !uiOpen)
                {
                    mi.MoveForward = tc.MoveForward;
                    mi.MoveRight = tc.MoveRight;
                    mi.JumpPressed = tc.JumpDown;
                    mi.DuckHeld = tc.CrouchHeld;
                    mi.WalkHeld = tc.WalkHeld;
                }
                ApplyActions(tc.SwapDown, tc.ReloadDown, tc.ScopeDown, tc.FireHeld, tc.UseHeld, frozen, blockAct);
                return;
            }

            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y");
            if (!blockAct)
            {
                float sens = GameSettings.MouseSens * (Scoped ? 0.4f : 1f);
                yawDelta = mx * sens;
                pitchDelta = -my * sens;
                AddView(yawDelta, pitchDelta);
            }

            // buying is allowed during the freeze phase, so this runs outside the movement gate
            if (Input.GetKeyDown(KeyCode.B) && gd != null) gd.ToggleBuy();

            if (!frozen && !uiOpen)
            {
                float f = 0f, r = 0f;
                if (Input.GetKey(KeyCode.W)) f += 1f;
                if (Input.GetKey(KeyCode.S)) f -= 1f;
                if (Input.GetKey(KeyCode.D)) r += 1f;
                if (Input.GetKey(KeyCode.A)) r -= 1f;
                mi.MoveForward = f;
                mi.MoveRight = r;
                mi.JumpPressed = Input.GetKeyDown(KeyCode.Space);
                mi.DuckHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
                mi.WalkHeld = Input.GetKey(KeyCode.LeftShift);

                bool fire = Input.GetMouseButton(0);
                bool scopeDown = Input.GetMouseButtonDown(1);
                bool reloadDown = Input.GetKeyDown(KeyCode.R);
                bool swapDown = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Q);
                bool useHeld = Input.GetKey(KeyCode.E);

                ApplyActions(swapDown, reloadDown, scopeDown, fire, useHeld, false, blockAct);
            }
            else
            {
                ApplyActions(false, false, false, false, false, frozen, true);
            }
        }

        private void ApplyActions(bool swapDown, bool reloadDown, bool scopeDown, bool fireHeld, bool useHeld,
                                  bool frozen, bool uiOpen)
        {
            _useHeld = useHeld && !frozen && !uiOpen;
            if (frozen || uiOpen)
            {
                return;
            }
            float now = GameClock.Now;

            if (scopeDown) ToggleScope();
            if (reloadDown) StartReload();
            if (swapDown) SwitchNext();

            if (fireHeld && Def != null && States[Slot].AmmoInMag <= 0 && !States[Slot].Reloading) StartReload();

            if (fireHeld && now >= _switchTime) FireOnce();
            if (fireHeld && GameSettings.AutoStop) _autoStopUntil = now + 0.25f;
        }

        private void DeadLook(float dt)
        {
            if (dt <= 0f) return;
            if (GameDirector.I != null && GameDirector.I.Paused) return;
            if (GameSettings.TouchMode && TouchControls.I != null)
            {
                AddView(TouchControls.I.LookDelta.x, TouchControls.I.LookDelta.y);
            }
            else
            {
                float mx = Input.GetAxisRaw("Mouse X");
                float my = Input.GetAxisRaw("Mouse Y");
                AddView(mx * GameSettings.MouseSens, -my * GameSettings.MouseSens);
            }
            TickCamera(dt);
        }

        /// <summary>Aim assist: slows the look only while a target is inside a 6 degree cone (never pulls).</summary>
        private float AimAssistFactor()
        {
            if (GameSettings.AimAssist <= 0.001f) return 1f;
            GameDirector gd = GameDirector.I;
            if (gd == null || CamTf == null) return 1f;
            Vector3 fwd = GZ.DirOf(ViewYawDeg, ViewPitchDeg);
            Vector3 eye = CamTf.position;
            float factor = 1f;
            for (int i = 0; i < gd.ActorCount; i++)
            {
                Actor a = gd.GetActor(i);
                if (a == null || !a.Alive || a.Team == Team) continue;
                Vector3 to = a.EyePos - eye;
                float dist = to.magnitude;
                if (dist > 55f) continue;
                if (Vector3.Angle(fwd, to) < 6f) factor = 0.65f;
            }
            return Mathf.Lerp(1f, factor, GameSettings.AimAssist);
        }

        public void ToggleScope()
        {
            WeaponDef def = Def;
            if (def == null || !def.HasScope) return;
            float now = GameClock.Now;
            if (now < _scopeLock) return;
            _scopeLock = now + 0.18f;
            Scoped = !Scoped;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Scope, CamTf.position, 0.35f, Scoped ? 1f : 0.9f);
        }

        public void SwitchNext()
        {
            int next = Slot == 0 ? 1 : 0;
            if (Slots[next] == null || Slot == next) return;
            Slot = next;
            Scoped = false;
            _switchTime = GameClock.Now + 0.35f;
            if (View != null) View.SetWeapon(Def);
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Reload, CamTf.position, 0.3f, 1.2f);
        }

        public void StartReload()
        {
            WeaponDef def = Def;
            if (def == null) return;
            int ix = Slot;
            if (!WeaponLogic.TryStartReload(ref States[ix], def, GameClock.Now)) return;
            Scoped = false;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Reload, CamTf.position, 0.5f, 1f);
        }

        // ------------------------------------------------------------------ weapon tick
        private void TickWeapon(float dt, float now)
        {
            WeaponDef def = Def;
            if (def == null) return;
            int ix = Slot;
            WeaponLogic.Update(ref States[ix], def, now, dt);
        }

        private void FireOnce()
        {
            WeaponDef def = Def;
            if (def == null) return;
            int ix = Slot;
            float now = GameClock.Now;

            float beforeP = States[ix].RecoilPitch;
            float beforeY = States[ix].RecoilYaw;
            FireResult res;
            if (!WeaponLogic.TryFire(ref States[ix], def, now, Random.value, Random.value, out res)) return;

            float s = GameSettings.RecoilScale;
            States[ix].RecoilPitch = beforeP + (States[ix].RecoilPitch - beforeP) * s;
            States[ix].RecoilYaw = beforeY + (States[ix].RecoilYaw - beforeY) * s;

            if (View != null) View.Kick();

            float spread = WeaponLogic.ComputeSpreadDeg(def, SpeedRatio01, Move.OnGround, Move.Ducked) * GameSettings.SpreadScale;
            Vector2 cone = Random.insideUnitCircle * spread;
            Vector3 dir = GZ.DirOf(ViewYawDeg + cone.x, ViewPitchDeg + cone.y);
            Vector3 origin = CamTf.position;

            ShotHit hit;
            Combat.Fire(origin, dir, GZ.MaxShotRange, this, out hit);

            Vector3 end = hit.Valid ? hit.Point : origin + dir * GZ.MaxShotRange;
            Vector3 muzzle = Scoped ? origin + dir * 0.6f : View.MuzzlePos;
            FxSystem.I.Tracer(muzzle, end);
            if (FxSystem.Near(origin, 40f)) FxSystem.I.Muzzle(muzzle, dir, def.Kind == WeaponKind.Sniper ? 1.35f : 1f);

            if (hit.Valid)
            {
                bool flesh = false;
                if (hit.Victim != null)
                {
                    if (hit.Victim.Team != Team)
                    {
                        int dmg = Ballistics.ComputeDamageAtDistance(def, hit.Distance);
                        hit.Victim.TakeDamage(dmg, def.ArmorPen, hit.Group, this, def, hit.Head);
                        flesh = true;
                    }
                }
                FxSystem.I.Impact(hit.Point, hit.Normal, flesh);
            }

            if (GzAudio.I != null) GzAudio.I.PlayShot(def, transform.position, true);
            if (GameDirector.I != null) GameDirector.I.ReportGunshot(transform.position, Team);
        }

        // ------------------------------------------------------------------ use (plant / defuse)
        private void TickUse(float dt, bool canAct)
        {
            UseActive = false;
            GameDirector gd = GameDirector.I;
            if (gd == null || !canAct)
            {
                UseProgress01 = 0f;
                _useTime = 0f;
                return;
            }

            bool wantUse = _useHeld && !Scoped;
            if (!wantUse)
            {
                if (UseProgress01 > 0f) UseProgress01 = Mathf.Max(0f, UseProgress01 - dt * 2.5f);
                _useTime = 0f;
                return;
            }

            if (Team == Team.Terrorists && HasBomb && !gd.BombPlanted)
            {
                BombSite site = gd.Map != null ? gd.Map.SiteOf(FeetPos) : null;
                if (site == null)
                {
                    if (UseProgress01 > 0f) UseProgress01 = Mathf.Max(0f, UseProgress01 - dt * 2.5f);
                    _useTime = 0f;
                    return;
                }
                UseActive = true;
                UseIsDefuse = false;
                _useTime += dt;
                UseProgress01 = Mathf.Clamp01(_useTime / MatchRules.PlantTime);
                if (UseProgress01 >= 1f)
                {
                    gd.PlantBomb(this, FeetPos, site);
                    _useTime = 0f;
                    UseProgress01 = 0f;
                }
                return;
            }

            if (Team == Team.CTs && gd.BombPlanted)
            {
                Vector3 d = gd.BombPos - FeetPos;
                d.y = 0f;
                if (d.sqrMagnitude > 2.4f * 2.4f)
                {
                    if (UseProgress01 > 0f) UseProgress01 = Mathf.Max(0f, UseProgress01 - dt * 2.5f);
                    _useTime = 0f;
                    return;
                }
                UseActive = true;
                UseIsDefuse = true;
                float need = HasKit ? MatchRules.DefuseTimeWithKit : MatchRules.DefuseTime;
                _useTime += dt;
                UseProgress01 = Mathf.Clamp01(_useTime / need);
                if (UseProgress01 >= 1f)
                {
                    gd.DefuseBomb(this);
                    _useTime = 0f;
                    UseProgress01 = 0f;
                }
                return;
            }

            if (UseProgress01 > 0f) UseProgress01 = Mathf.Max(0f, UseProgress01 - dt * 2.5f);
            _useTime = 0f;
        }

        // ------------------------------------------------------------------ footsteps
        private void TickFootsteps(float dt)
        {
            float speed = Mathf.Sqrt(Move.Velocity.x * Move.Velocity.x + Move.Velocity.z * Move.Velocity.z);
            if (!Move.OnGround || speed < 1.4f) return;
            _stepTimer -= dt * (0.6f + speed * 0.14f);
            if (_stepTimer > 0f) return;
            _stepTimer = 1f;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Footstep, FeetPos, 0.45f, Random.Range(0.9f, 1.15f));
            if (GameDirector.I != null && speed > 3.2f) GameDirector.I.NotifyFootstep(transform.position, Team);
        }

        // ------------------------------------------------------------------ camera
        private void TickCamera(float dt)
        {
            Vec3 p = Move.Position;
            CamTf.position = new Vector3(p.x, p.y + EyeHeight, p.z);
            CamTf.rotation = Quaternion.Euler(ViewPitchDeg, ViewYawDeg, 0f);

            WeaponDef def = Def;
            bool canScope = def != null && def.HasScope;
            float target = Scoped && canScope ? def.ScopeFov : GameSettings.Fov;
            _fovCurrent = dt <= 0f ? target : Mathf.Lerp(_fovCurrent, target, Mathf.Clamp01(dt * 14f));
            Cam.fieldOfView = _fovCurrent;
            Cam.farClipPlane = GameSettings.FarClip;
        }

        /// <summary>Radar support: mark enemies the player can see (cheap cone test, no raycast).</summary>
        private void MarkVisibleEnemies()
        {
            GameDirector gd = GameDirector.I;
            if (gd == null || CamTf == null) return;
            Vector3 eye = CamTf.position;
            Vector3 fwd = CamTf.forward;
            for (int i = 0; i < gd.ActorCount; i++)
            {
                Actor a = gd.GetActor(i);
                if (a == null || !a.Alive || a.Team == Team) continue;
                Vector3 to = a.EyePos - eye;
                float dist = to.magnitude;
                if (dist > 80f || dist < 0.001f) continue;
                if (Vector3.Dot(fwd, to / dist) < 0.93f) continue;
                a.SpottedTime = GameClock.Now;
            }
        }

        public bool IsScoped { get { return Scoped; } }
    }
}
