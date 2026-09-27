// GreyZone Core - CS-style movement: accelerate / friction / stopspeed / air control / step-up.
// Pure C#, no UnityEngine. All distances in meters, angles in degrees, Y is up.
using System;

namespace GreyZone.Core
{
    public struct MoveConfig
    {
        public float MaxSpeed;
        public float Accel;
        public float AirAccel;
        public float AirMaxWishSpeed;
        public float Friction;
        public float StopSpeed;
        public float Gravity;
        public float JumpImpulse;
        public float MaxSpeedCap;
        public float WalkMultiplier;
        public float DuckMultiplier;
        public float StandHeight;
        public float DuckHeight;
        public float DuckTransitionTime;

        public static MoveConfig CreateDefault()
        {
            MoveConfig c = new MoveConfig();
            c.MaxSpeed = 6.35f;             // knife run speed (250 u/s)
            c.Accel = 5.5f;
            c.AirAccel = 12f;
            c.AirMaxWishSpeed = 0.762f;     // 30 u/s air-control cap
            c.Friction = 5.2f;
            c.StopSpeed = 2.032f;           // 80 u/s
            c.Gravity = 20.32f;             // 800 u/s^2
            c.JumpImpulse = 7.67f;          // 302 u/s
            c.MaxSpeedCap = 8.128f;         // 320 u/s hard clamp
            c.WalkMultiplier = 0.52f;
            c.DuckMultiplier = 0.34f;
            c.StandHeight = 1.83f;
            c.DuckHeight = 1.37f;
            c.DuckTransitionTime = 0.20f;
            return c;
        }

        public void ClampToValid()
        {
            if (MaxSpeed < 0.1f) MaxSpeed = 0.1f;
            if (Accel < 0.01f) Accel = 0.01f;
            if (AirAccel < 0f) AirAccel = 0f;
            if (AirMaxWishSpeed < 0f) AirMaxWishSpeed = 0f;
            if (Friction < 0f) Friction = 0f;
            if (StopSpeed < 0.01f) StopSpeed = 0.01f;
            if (Gravity < 0f) Gravity = 0f;
            if (JumpImpulse < 0f) JumpImpulse = 0f;
            if (MaxSpeedCap < MaxSpeed) MaxSpeedCap = MaxSpeed;
            if (WalkMultiplier < 0.05f) WalkMultiplier = 0.05f;
            if (WalkMultiplier > 1f) WalkMultiplier = 1f;
            if (DuckMultiplier < 0.05f) DuckMultiplier = 0.05f;
            if (DuckMultiplier > 1f) DuckMultiplier = 1f;
            if (StandHeight < 0.8f) StandHeight = 0.8f;
            if (DuckHeight < 0.5f) DuckHeight = 0.5f;
            if (DuckHeight > StandHeight) DuckHeight = StandHeight;
            if (DuckTransitionTime < 0.01f) DuckTransitionTime = 0.01f;
        }
    }

    public struct MoveInput
    {
        public float MoveForward;
        public float MoveRight;
        public bool JumpPressed;
        public bool DuckHeld;
        public bool WalkHeld;
        public float YawDeg;

        public static MoveInput None { get { return new MoveInput(); } }
    }

    public struct MoveState
    {
        public Vec3 Position;       // feet (bottom centre of capsule)
        public Vec3 Velocity;
        public bool OnGround;
        public bool Ducked;
        public float DuckFraction;  // 0 standing .. 1 ducked
        public bool JumpQueued;

        private float _jumpBuffer;  // internal jump buffer timer

        internal float JumpBufferTimer
        {
            get { return _jumpBuffer; }
            set { _jumpBuffer = value; }
        }
    }

    public struct SweepHit
    {
        public bool Hit;
        public float Fraction;      // 0..1 along the requested delta
        public Vec3 Normal;
    }

    public struct GroundHit
    {
        public bool Hit;
        public float Distance;
        public Vec3 Normal;
    }

    public interface ICollisionWorld
    {
        /// <summary>
        /// Sweep a capsule (given by feet position + radius + height) along delta.
        /// Returns the first blocking hit. Implementations should ignore pure initial
        /// overlap (report Fraction &gt; 0 for real blocking surfaces when possible).
        /// </summary>
        SweepHit SweepCapsule(Vec3 feetPos, float radius, float height, Vec3 delta);

        bool OverlapCapsule(Vec3 feetPos, float radius, float height);
    }

    public static class PlayerMover
    {
        public const float PlayerRadius = 0.4f;
        public const float StepHeight = 0.45f;
        public const float JumpBufferTime = 0.10f;

        private const float GroundSnapDistance = 0.08f;
        private const float GroundProbeLift = 0.02f;
        private const float MinGroundNormalY = 0.6f;
        private const int MaxSlideIterations = 4;
        private const float DegToRad = 0.0174532924f;

        public static float ComputeMaxSpeed(ref MoveConfig cfg, float weaponSpeedMultiplier, bool walking, bool ducked)
        {
            float m = cfg.MaxSpeed * weaponSpeedMultiplier;
            if (walking) m *= cfg.WalkMultiplier;
            if (ducked) m *= cfg.DuckMultiplier;
            return m;
        }

        public static float CurrentHeight(ref MoveConfig cfg, MoveState state)
        {
            float f = state.DuckFraction;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;
            return cfg.StandHeight + (cfg.DuckHeight - cfg.StandHeight) * f;
        }

        public static void Step(ref MoveState state, ref MoveConfig cfg, MoveInput input, float weaponSpeedMultiplier, ICollisionWorld world, float dt)
        {
            if (dt <= 0f) return;
            if (dt > 0.1f) dt = 0.1f;

            UpdateDuck(ref state, ref cfg, input.DuckHeld, world, dt);

            float yaw = input.YawDeg * DegToRad;
            float sin = (float)Math.Sin(yaw);
            float cos = (float)Math.Cos(yaw);
            Vec3 forward = new Vec3(sin, 0f, cos);
            Vec3 right = new Vec3(cos, 0f, -sin);

            float wishLenSq = input.MoveForward * input.MoveForward + input.MoveRight * input.MoveRight;
            float wishLen = wishLenSq > 1e-6f ? (float)Math.Sqrt(wishLenSq) : 0f;
            Vec3 wishDir = Vec3.Zero;
            if (wishLen > 1e-5f)
            {
                wishDir = (forward * input.MoveForward + right * input.MoveRight) / wishLen;
                if (wishLen > 1f) wishLen = 1f;
            }

            float maxSpeed = ComputeMaxSpeed(ref cfg, weaponSpeedMultiplier, input.WalkHeld, state.Ducked);

            // ---- friction (ground only)
            if (state.OnGround)
            {
                float speed = (float)Math.Sqrt(state.Velocity.x * state.Velocity.x + state.Velocity.z * state.Velocity.z);
                if (speed > 1e-4f)
                {
                    float control = speed < cfg.StopSpeed ? cfg.StopSpeed : speed;
                    float drop = control * cfg.Friction * dt;
                    float newSpeed = speed - drop;
                    if (newSpeed < 0f) newSpeed = 0f;
                    float scale = newSpeed / speed;
                    state.Velocity.x *= scale;
                    state.Velocity.z *= scale;
                }
            }

            // ---- acceleration
            if (wishLen > 1e-5f)
            {
                float fullWish = maxSpeed * wishLen;
                float wishSpeed;
                float accel;
                if (state.OnGround)
                {
                    accel = cfg.Accel;
                    wishSpeed = fullWish;
                }
                else
                {
                    accel = cfg.AirAccel;
                    wishSpeed = fullWish < cfg.AirMaxWishSpeed ? fullWish : cfg.AirMaxWishSpeed;
                }
                float current = state.Velocity.x * wishDir.x + state.Velocity.z * wishDir.z;
                float add = wishSpeed - current;
                if (add > 0f)
                {
                    float accelSpeed = accel * dt * fullWish;
                    if (accelSpeed > add) accelSpeed = add;
                    state.Velocity.x += wishDir.x * accelSpeed;
                    state.Velocity.z += wishDir.z * accelSpeed;
                }
            }

            // ---- gravity
            if (!state.OnGround) state.Velocity.y -= cfg.Gravity * dt;

            // ---- jump (single jump only, small buffer, no coyote time)
            if (input.JumpPressed) state.JumpBufferTimer = JumpBufferTime;
            if (state.JumpQueued)
            {
                state.JumpQueued = false;
                state.JumpBufferTimer = JumpBufferTime;
            }
            if (state.JumpBufferTimer > 0f) state.JumpBufferTimer -= dt;
            if (state.OnGround && state.JumpBufferTimer > 0f)
            {
                state.Velocity.y = cfg.JumpImpulse;
                state.OnGround = false;
                state.JumpBufferTimer = 0f;
            }

            // ---- hard horizontal speed cap (anti bunny-hop exploit)
            float hSpeed = (float)Math.Sqrt(state.Velocity.x * state.Velocity.x + state.Velocity.z * state.Velocity.z);
            if (hSpeed > cfg.MaxSpeedCap && hSpeed > 1e-4f)
            {
                float scale = cfg.MaxSpeedCap / hSpeed;
                state.Velocity.x *= scale;
                state.Velocity.z *= scale;
            }

            // ---- move with swept capsule + sliding + step-up
            float height = CurrentHeight(ref cfg, state);
            Vec3 startPos = state.Position;
            bool wantsMove = (input.MoveForward * input.MoveForward + input.MoveRight * input.MoveRight) > 0.01f;
            MoveAndCollide(ref state, world, height, dt, startPos, wantsMove);

            // ---- ground probe / snap
            ProbeGround(ref state, world, height);
        }

        private static void UpdateDuck(ref MoveState state, ref MoveConfig cfg, bool duckHeld, ICollisionWorld world, float dt)
        {
            float rate = 1f / cfg.DuckTransitionTime;
            float target = duckHeld ? 1f : 0f;
            if (state.DuckFraction < target)
            {
                state.DuckFraction += rate * dt;
                if (state.DuckFraction > target) state.DuckFraction = target;
            }
            else if (state.DuckFraction > target)
            {
                float want = state.DuckFraction - rate * dt;
                if (want < target) want = target;
                float hWant = cfg.StandHeight + (cfg.DuckHeight - cfg.StandHeight) * want;
                if (!world.OverlapCapsule(state.Position, PlayerRadius, hWant))
                    state.DuckFraction = want;
            }
            state.Ducked = state.DuckFraction > 0.5f;
        }

        private static void MoveAndCollide(ref MoveState state, ICollisionWorld world, float height, float dt, Vec3 startPos, bool wantsMove)
        {
            Vec3 pos = state.Position;
            Vec3 vel = state.Velocity;
            Vec3 horizDelta = new Vec3(vel.x * dt, 0f, vel.z * dt);
            float expectedHoriz = (float)Math.Sqrt(horizDelta.x * horizDelta.x + horizDelta.z * horizDelta.z);
            float timeLeft = dt;
            bool touchedGround = false;
            bool blocked = false;

            for (int iter = 0; iter < MaxSlideIterations && timeLeft > 1e-5f; iter++)
            {
                Vec3 d = vel * timeLeft;
                if (d.LengthSq() < 1e-8f) break;

                SweepHit hit = world.SweepCapsule(pos, PlayerRadius, height, d);
                if (!hit.Hit)
                {
                    pos = pos + d;
                    timeLeft = 0f;
                    break;
                }
                if (hit.Fraction >= 1f)
                {
                    // ends exactly on a surface: move there but kill the velocity into it
                    pos = pos + d;
                    Vec3 nEnd = hit.Normal;
                    if (nEnd.LengthSq() > 0.25f)
                    {
                        float vnEnd = Vec3.Dot(vel, nEnd);
                        if (vnEnd < 0f) vel = vel - nEnd * vnEnd;
                    }
                    timeLeft = 0f;
                    break;
                }

                float f = hit.Fraction;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                if (f > 0f)
                {
                    pos = pos + d * f;
                    timeLeft *= (1f - f);
                }

                Vec3 n = hit.Normal;
                if (n.y > MinGroundNormalY) touchedGround = true;
                else if (n.y > -0.05f) blocked = true;   // vertical-ish wall
                if (n.LengthSq() > 0.25f)
                {
                    float vn = Vec3.Dot(vel, n);
                    if (vn < 0f) vel = vel - n * vn;   // slide along the surface
                }
            }

            // step-up: attempted when the player wants to move but a wall defeated the move
            if (wantsMove && blocked && (state.OnGround || touchedGround))
            {
                float achieved = HorizontalDistance(startPos, pos);
                if (achieved < expectedHoriz * 0.6f || expectedHoriz < 0.05f)
                {
                    Vec3 stepped;
                    if (TryStepUp(startPos, horizDelta, world, height, out stepped))
                    {
                        if (HorizontalDistance(startPos, stepped) > achieved)
                        {
                            pos = stepped;
                            touchedGround = true;
                        }
                    }
                }
            }

            state.Position = pos;
            state.Velocity = vel;
        }

        private static float HorizontalDistance(Vec3 a, Vec3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool TryStepUp(Vec3 start, Vec3 horizDelta, ICollisionWorld world, float height, out Vec3 result)
        {
            result = start;
            Vec3 up = new Vec3(0f, StepHeight, 0f);
            SweepHit hUp = world.SweepCapsule(start, PlayerRadius, height, up);
            float fUp = hUp.Hit ? hUp.Fraction : 1f;
            if (fUp * StepHeight < 0.20f) return false;

            Vec3 raised = start + up * fUp;
            SweepHit hFwd = world.SweepCapsule(raised, PlayerRadius, height, horizDelta);
            if (hFwd.Hit && hFwd.Fraction < 0.999f) return false;

            Vec3 moved = raised + horizDelta;
            Vec3 down = new Vec3(0f, -StepHeight, 0f);
            SweepHit hDown = world.SweepCapsule(moved, PlayerRadius, height, down);
            if (!hDown.Hit) return false;   // stepped out over a ledge, not a step

            result = moved + down * hDown.Fraction;
            return true;
        }

        private static void ProbeGround(ref MoveState state, ICollisionWorld world, float height)
        {
            // probe from a tiny lift so surfaces we are already touching report a clean hit
            Vec3 origin = state.Position + new Vec3(0f, GroundProbeLift, 0f);
            Vec3 down = new Vec3(0f, -(GroundSnapDistance + GroundProbeLift), 0f);
            SweepHit hit = world.SweepCapsule(origin, PlayerRadius, height, down);
            bool grounded = hit.Hit && hit.Normal.y > MinGroundNormalY;
            if (grounded)
            {
                float f = hit.Fraction;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                state.Position = origin + down * f;
            }

            state.OnGround = grounded;
            if (grounded && state.Velocity.y < 0f) state.Velocity.y = 0f;
        }
    }
}
