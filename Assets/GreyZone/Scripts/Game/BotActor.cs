// GreyZone - bot brain: waypoint navigation, throttled perception, burst fire, plant / defuse.
// Behaviour is intentionally simple and readable: patrol -> investigate -> engage -> objective.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class BotActor : Actor
    {
        public enum St { Patrol, Investigate, Engage, Plant, Defuse, Hold }

        public St State = St.Patrol;
        public int Index;
        public Vector3 Anchor;
        public int SiteIndex;

        // HUD progress (bots also plant / defuse)
        public float UseProgress01;
        public bool UseActive;
        public bool UseIsDefuse;

        private WaypointGraph _graph;
        private readonly int[] _path = new int[64];
        private int _pathLen;
        private int _pathIx;
        private float _repathTimer;
        private Vector3 _goal;
        private bool _hasGoal;
        private Vector3 _lastGoalRequest;

        private float _senseTimer;
        private float _sensePhase;
        private Actor _target;
        private float _targetSeenTime = -999f;
        private Vector3 _lastSeenPos;
        private float _reactAt;
        private float _trackTime;
        private float _burstLeft;
        private float _burstPause;
        private float _useTime;
        private float _accum;
        private float _stuckTimer;
        private Vector3 _stuckPos;
        private float _sweepYaw;
        private float _fireCooldown;

        public void Setup(WaypointGraph graph, int index, float sensePhase)
        {
            _graph = graph;
            Index = index;
            _sensePhase = sensePhase;
            _senseTimer = sensePhase;
            _stuckPos = transform.position;
            _sweepYaw = 0f;
        }

        /// <summary>Clears transient brain state between rounds (stale targets, bursts, paths).</summary>
        public void ResetForRound()
        {
            _target = null;
            _targetSeenTime = -999f;
            _reactAt = 0f;
            _trackTime = 0f;
            _burstLeft = 0f;
            _burstPause = 0f;
            _pathLen = 0;
            _pathIx = 0;
            _repathTimer = 0f;
            _hasGoal = false;
            _useTime = 0f;
            _accum = 0f;
            _stuckTimer = 0f;
            _stuckPos = transform.position;
            _fireCooldown = 0f;
            UseProgress01 = 0f;
            UseActive = false;
            State = St.Patrol;
        }

        public void SetObjective(int siteIndex, Vector3 anchor)
        {
            SiteIndex = siteIndex;
            Anchor = anchor;
            _hasGoal = false;
            _repathTimer = 0f;
            State = St.Patrol;
        }

        public void Alert(Vector3 pos)
        {
            if (!Alive) return;
            if (_target != null && GameClock.Now - _targetSeenTime < 1f) return;
            _lastSeenPos = pos;
            _targetSeenTime = GameClock.Now - 4.5f;   // "heard something" memory, not a full sighting
            if (State == St.Patrol || State == St.Hold) State = St.Investigate;
        }

        public bool IsEngaging { get { return State == St.Engage || _target != null; } }

        // ------------------------------------------------------------------ per frame
        protected override void Update()
        {
            base.Update();
            if (!Alive) return;

            GameDirector gd = GameDirector.I;
            if (gd == null || gd.Paused) return;

            float dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            float now = GameClock.Now;

            if (!gd.CanAct)
            {
                MoveInput idle = new MoveInput();
                idle.YawDeg = _yaw;
                StepMoveSmart(idle, 1f, dt);
                ViewYaw = _yaw;
                return;
            }

            _senseTimer -= dt;
            if (_senseTimer <= 0f)
            {
                _senseTimer = 0.15f;
                Sense(now);
            }
            if (_target != null) _trackTime += dt; else _trackTime = 0f;

            float aimYaw = _yaw;
            float aimPitch = 0f;
            Decide(now, dt, ref aimYaw, ref aimPitch);

            MoveInput mi = new MoveInput();
            mi.YawDeg = _yaw;
            MoveToward(dt, ref mi);

            float mult = Def == null ? 1f : Def.MoveSpeedMultiplier;
            StepMoveSmart(mi, mult, dt);

            _yaw = GZ.MoveTowardsAngle(_yaw, aimYaw, 900f * dt);
            ViewYaw = _yaw;
            UpdateVisualAim(_yaw);
            TickWeapon(dt, now);
            TickUse(dt, now, gd);
            TickStuck(dt);
        }

        private void StepMoveSmart(MoveInput mi, float mult, float dt)
        {
            _accum += dt;
            int guard = 0;
            while (_accum >= GZ.FixedStep && guard++ < 4)
            {
                _accum -= GZ.FixedStep;
                StepMove(mi, mult, GZ.FixedStep);
            }
            if (guard >= 4) _accum = 0f;
        }

        // ------------------------------------------------------------------ perception
        private void Sense(float now)
        {
            GameDirector gd = GameDirector.I;
            Vector3 eye = EyePos;
            Vector3 fwd = GZ.DirOf(_yaw, 0f);
            float bestDist = float.MaxValue;
            Actor best = null;

            for (int i = 0; i < gd.ActorCount; i++)
            {
                Actor a = gd.GetActor(i);
                if (a == null || !a.Alive || a.Team == Team) continue;
                Vector3 to = a.EyePos - eye;
                float dist = to.magnitude;
                if (dist > 75f || dist < 0.01f) continue;
                if (dist > 4.5f)
                {
                    float ang = Vector3.Angle(fwd, to);
                    if (ang > 60f) continue;   // 120 degree FOV
                }
                if (!Combat.LineOfSight(eye, a.EyePos, this, a)) continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = a;
                }
            }

            if (best != _target && best != null)
            {
                _reactAt = now + GameSettings.BotReactionTime;
                _trackTime = 0f;
            }
            _target = best;
            if (_target != null)
            {
                _lastSeenPos = _target.EyePos;
                _targetSeenTime = now;
                _target.SpottedTime = now;      // radar
            }
        }

        // ------------------------------------------------------------------ decisions
        private void Decide(float now, float dt, ref float aimYaw, ref float aimPitch)
        {
            GameDirector gd = GameDirector.I;
            bool t = Team == Team.Terrorists;

            bool inSite = false;
            BombSite site = gd.Map != null ? gd.Map.SiteOf(FeetPos) : null;
            if (site != null) inSite = true;

            bool wantPlant = t && HasBomb && !gd.BombPlanted;
            bool wantDefuse = !t && gd.BombPlanted;
            float bombDist = 0f;
            if (wantDefuse)
            {
                Vector3 bd = gd.BombPos - FeetPos;
                bd.y = 0f;
                bombDist = bd.magnitude;
            }

            bool seesTarget = _target != null && now - _targetSeenTime < 0.3f;

            if (wantPlant && inSite)
            {
                State = St.Plant;
                _goal = FeetPos;
                _hasGoal = false;
                if (_target != null) FaceTarget(ref aimYaw, ref aimPitch, true);
                return;
            }
            if (wantDefuse && bombDist < 2.0f)
            {
                State = St.Defuse;
                _goal = FeetPos;
                _hasGoal = false;
                if (_target != null) FaceTarget(ref aimYaw, ref aimPitch, true);
                return;
            }

            if (seesTarget && now >= _reactAt)
            {
                State = St.Engage;
                FaceTarget(ref aimYaw, ref aimPitch, false);
                _goal = _target.FeetPos;
                _hasGoal = true;
                return;
            }

            if (now - _targetSeenTime < 5f)
            {
                State = St.Investigate;
                _goal = _lastSeenPos;
                aimYaw = _yaw;
                _hasGoal = true;
                return;
            }

            // objective navigation
            Vector3 objGoal = Anchor;
            if (wantPlant) objGoal = SiteCenter(gd, SiteIndex);
            if (wantDefuse) objGoal = gd.BombPos;

            State = St.Patrol;
            _goal = objGoal;
            _hasGoal = true;

            Vector3 flat = objGoal - FeetPos;
            flat.y = 0f;
            if (flat.sqrMagnitude < 3.2f * 3.2f)
            {
                State = St.Hold;
                // slow security sweep while holding
                _sweepYaw += dt * 26f;
                aimYaw = AnchorYaw() + Mathf.Sin(_sweepYaw * Mathf.Deg2Rad) * 55f;
                _goal = Anchor;
            }
            else
            {
                aimYaw = _yaw;
            }
        }

        private void FaceTarget(ref float aimYaw, ref float aimPitch, bool quick)
        {
            Vector3 eye = EyePos;
            Vector3 aimPoint = _target.EyePos - Vector3.up * 0.22f;
            Vector3 dir = aimPoint - eye;
            float err = GameSettings.BotAimErrorDeg * Mathf.Lerp(1f, 0.32f, Mathf.Clamp01(_trackTime / 1.1f));
            Vector2 e = Random.insideUnitCircle * err;
            aimYaw = GZ.YawOf(dir) + e.x;
            aimPitch = Mathf.Clamp(GZ.PitchOf(dir) + e.y, -60f, 60f);
            if (quick) aimPitch *= 0.4f;
        }

        private float AnchorYaw()
        {
            Vector3 d = Anchor - FeetPos;
            d.y = 0f;
            if (d.sqrMagnitude < 0.04f) return _yaw;
            return GZ.YawOf(d);
        }

        private Vector3 SiteCenter(GameDirector gd, int siteIx)
        {
            if (gd.Map == null) return Anchor;
            BombSite s = siteIx == 0 ? gd.Map.SiteA : gd.Map.SiteB;
            if (s == null) return Anchor;
            Vector3 c = s.Area.center;
            return new Vector3(c.x, 0f, c.z);
        }

        // ------------------------------------------------------------------ navigation
        private void MoveToward(float dt, ref MoveInput mi)
        {
            if (!_hasGoal) return;

            Vector3 pos = FeetPos;
            Vector3 goal = _goal;

            Vector3 flatGoal = goal - pos;
            flatGoal.y = 0f;
            float goalDist = flatGoal.magnitude;

            Vector3 steer = goal;

            if (goalDist > 3.0f && _graph != null && _graph.Count > 0)
            {
                if (_repathTimer <= 0f || (goal - _lastGoalRequest).sqrMagnitude > 6f)
                {
                    RecomputePath(pos, goal);
                    _lastGoalRequest = goal;
                    _repathTimer = 1.1f;
                }

                while (_pathIx < _pathLen)
                {
                    Vector3 np = _graph.Nodes[_path[_pathIx]].Pos;
                    Vector3 d2 = np - pos;
                    d2.y = 0f;
                    if (d2.sqrMagnitude < 1.6f * 1.6f) { _pathIx++; continue; }
                    break;
                }
                if (_pathIx < _pathLen) steer = _graph.Nodes[_path[_pathIx]].Pos;
            }
            else
            {
                _pathLen = 0;
            }
            _repathTimer -= dt;

            Vector3 to = steer - pos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            Vector3 dir = to.normalized;

            float yawRad = _yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
            float f = Vector3.Dot(dir, fwd);
            float r = Vector3.Dot(dir, right);
            mi.MoveForward = Mathf.Clamp(f * 1.6f, -1f, 1f);
            mi.MoveRight = Mathf.Clamp(r * 1.6f, -1f, 1f);

            // stop and shoot in close engagements
            if (State == St.Engage && _target != null)
            {
                mi.MoveForward = 0f;
                mi.MoveRight = 0f;
            }
            if (State == St.Plant || State == St.Defuse)
            {
                mi.MoveForward = 0f;
                mi.MoveRight = 0f;
            }

            float desired = GZ.YawOf(dir);
            _yaw = GZ.MoveTowardsAngle(_yaw, desired, (State == St.Engage ? 300f : 560f) * dt);
        }

        private void RecomputePath(Vector3 pos, Vector3 goal)
        {
            _pathLen = 0;
            _pathIx = 0;
            if (_graph == null) return;
            int start = _graph.NearestNode(pos, 255);
            int end = _graph.NearestNode(goal, 255);
            if (start < 0 || end < 0) return;
            _pathLen = _graph.FindPath(start, end, _path);
        }

        private void TickStuck(float dt)
        {
            if (State == St.Engage || State == St.Plant || State == St.Defuse || State == St.Hold) { _stuckTimer = 0f; return; }
            Vector3 d = FeetPos - _stuckPos;
            d.y = 0f;
            if (d.sqrMagnitude > 0.25f)
            {
                _stuckTimer = 0f;
                _stuckPos = FeetPos;
                return;
            }
            _stuckTimer += dt;
            if (_stuckTimer > 1.6f)
            {
                _stuckTimer = 0f;
                _stuckPos = FeetPos;
                _repathTimer = 0f;
                _lastGoalRequest = new Vector3(9999f, 0f, 9999f);
                _yaw = GZ.WrapAngle(_yaw + Random.Range(-90f, 90f));
            }
        }

        // ------------------------------------------------------------------ weapons
        private void TickWeapon(float dt, float now)
        {
            WeaponDef def = Def;
            if (def == null) return;
            int ix = Slot;
            WeaponLogic.Update(ref States[ix], def, now, dt);
            if (_fireCooldown > 0f) _fireCooldown -= dt;

            if (States[ix].AmmoInMag <= 0)
            {
                if (!States[ix].Reloading) WeaponLogic.TryStartReload(ref States[ix], def, now);
                return;
            }
            if (States[ix].Reloading) return;

            if (State != St.Engage || _target == null || !_target.Alive) { _burstLeft = 0f; _burstPause = 0f; return; }
            if (now - _targetSeenTime > 0.35f) { _burstLeft = 0f; return; }
            if (_fireCooldown > 0f) return;

            // aim tolerance check
            Vector3 eye = EyePos;
            Vector3 to = _target.EyePos - eye;
            Vector3 aim = GZ.DirOf(_yaw, 0f);
            float angle = Vector3.Angle(aim, new Vector3(to.x, 0f, to.z));
            if (angle > 4.5f) { _burstLeft = 0f; return; }
            if (!Combat.LineOfSight(eye, _target.EyePos, this, _target)) return;

            if (_burstPause > 0f) { _burstPause -= dt; return; }
            if (_burstLeft <= 0f)
            {
                _burstLeft = Random.Range(3f, 6.99f);
            }

            // aim pitch toward the chest
            Vector3 aimTarget = _target.EyePos - Vector3.up * 0.22f;
            Vector3 dir = (aimTarget - eye).normalized;
            float err = GameSettings.BotAimErrorDeg * Mathf.Lerp(1f, 0.32f, Mathf.Clamp01(_trackTime / 1.1f));
            Vector2 e = Random.insideUnitCircle * err;
            dir = Quaternion.Euler(e.y, e.x, 0f) * dir;

            FireBotShot(dir, eye);

            if (_burstLeft <= 1f)
            {
                _burstLeft = 0f;
                _burstPause = Random.Range(0.26f, 0.62f);
            }
            else
            {
                _burstLeft -= 1f;
            }
        }

        private void FireBotShot(Vector3 dir, Vector3 eye)
        {
            WeaponDef def = Def;
            int ix = Slot;
            float now = GameClock.Now;
            FireResult res;
            float bp = States[ix].RecoilPitch;
            if (!WeaponLogic.TryFire(ref States[ix], def, now, Random.value, Random.value, out res)) return;

            ShotHit hit;
            Combat.Fire(eye, dir, GZ.MaxShotRange, this, out hit);

            Vector3 muzzle = eye + dir * 0.45f + new Vector3(0f, -0.12f, 0f);
            if (FxSystem.Near(eye, 45f))
            {
                FxSystem.I.Tracer(muzzle, hit.Valid ? hit.Point : eye + dir * GZ.MaxShotRange);
                FxSystem.I.Muzzle(muzzle, dir, 1f);
            }
            if (hit.Valid)
            {
                bool flesh = false;
                if (hit.Victim != null && hit.Victim.Team != Team)
                {
                    int dmg = Ballistics.ComputeDamageAtDistance(def, hit.Distance);
                    hit.Victim.TakeDamage(dmg, def.ArmorPen, hit.Group, this, def, hit.Head);
                    flesh = true;
                }
                if (FxSystem.Near(hit.Point, 60f)) FxSystem.I.Impact(hit.Point, hit.Normal, flesh);
            }

            if (GzAudio.I != null) GzAudio.I.PlayShot(def, transform.position, false);
            if (GameDirector.I != null) GameDirector.I.ReportGunshot(transform.position, Team);
        }

        // ------------------------------------------------------------------ plant / defuse
        private void TickUse(float dt, float now, GameDirector gd)
        {
            UseActive = false;
            if (State == St.Plant)
            {
                UseActive = true;
                UseIsDefuse = false;
                _useTime += dt;
                UseProgress01 = Mathf.Clamp01(_useTime / MatchRules.PlantTime);
                if (UseProgress01 >= 1f)
                {
                    BombSite site = gd.Map != null ? gd.Map.SiteOf(FeetPos) : null;
                    if (site != null) gd.PlantBomb(this, FeetPos, site);
                    _useTime = 0f;
                    UseProgress01 = 0f;
                }
                return;
            }
            if (State == St.Defuse)
            {
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
            _useTime = 0f;
            UseProgress01 = 0f;
        }
    }
}
