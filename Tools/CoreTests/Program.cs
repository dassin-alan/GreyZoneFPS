// GreyZone Core - console assertion harness (no Unity needed).
// Exit code 0 = all checks passed.
using System;
using System.Collections.Generic;
using GreyZone.Core;

namespace GreyZone.CoreTests
{
    internal static class Program
    {
        private const float Dt = 1f / 64f;
        private static int _failures;

        private static int Main()
        {
            Console.WriteLine("GreyZone Core tests");
            Console.WriteLine("-------------------");

            TestMovementFullSpeed();
            TestStopDistance();
            TestWalkAndDuck();
            TestJump();
            TestSpeedCap();
            TestStepUp();
            TestWallSlide();
            TestWeaponFireAndReload();
            TestRecoilRecovery();
            TestSpread();
            TestDamage();
            TestEconomy();

            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("ALL TESTS PASSED");
                return 0;
            }
            Console.WriteLine(_failures + " TEST(S) FAILED");
            return 1;
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) Console.WriteLine("PASS " + name);
            else
            {
                _failures++;
                Console.WriteLine("FAIL " + name + " : " + detail);
            }
        }

        // ---------------------------------------------------------------- movement tests

        private static void TestMovementFullSpeed()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            MoveState s = Player();
            Sim(ref s, ref c, Forward(0f, false), 1f, w, 2f);
            float speed = HSpeed(s);
            Check("move.full_speed", Math.Abs(speed - c.MaxSpeed) < c.MaxSpeed * 0.05f,
                  "speed=" + speed.ToString("0.###") + " expected=" + c.MaxSpeed);
        }

        private static void TestStopDistance()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            MoveState s = Player();
            Sim(ref s, ref c, Forward(0f, false), 1f, w, 2f);
            Vec3 p0 = s.Position;
            Sim(ref s, ref c, MoveInput.None, 1f, w, 0.5f);
            float speed = HSpeed(s);
            float dist = (s.Position - p0).Length();
            Check("move.stop_speed", speed < 0.1f, "speed=" + speed.ToString("0.###"));
            Check("move.stop_distance", dist < 1.3f, "dist=" + dist.ToString("0.###"));
        }

        private static void TestWalkAndDuck()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();

            MoveInput wi = Forward(0f, false);
            wi.WalkHeld = true;
            MoveState sw = Player();
            Sim(ref sw, ref c, wi, 1f, w, 2f);
            float walkExpected = c.MaxSpeed * c.WalkMultiplier;
            float walkSpeed = HSpeed(sw);
            Check("move.walk_speed", Math.Abs(walkSpeed - walkExpected) < walkExpected * 0.05f,
                  "speed=" + walkSpeed.ToString("0.###") + " expected=" + walkExpected.ToString("0.###"));

            MoveInput di = Forward(0f, false);
            di.DuckHeld = true;
            MoveState sd = Player();
            Sim(ref sd, ref c, di, 1f, w, 2f);
            float duckExpected = c.MaxSpeed * c.DuckMultiplier;
            float duckSpeed = HSpeed(sd);
            Check("move.duck_speed", Math.Abs(duckSpeed - duckExpected) < duckExpected * 0.05f,
                  "speed=" + duckSpeed.ToString("0.###") + " expected=" + duckExpected.ToString("0.###"));
        }

        private static void TestJump()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            MoveState s = Player();
            s.OnGround = true;

            float maxY = 0f;
            bool doubleJump = false;
            for (int k = 0; k < 200; k++)
            {
                MoveInput i = MoveInput.None;
                if (k == 0) i.JumpPressed = true;
                if (k > 12 && k < 150) i.JumpPressed = true;   // spam in air (must not double jump)

                bool wasGrounded = s.OnGround;
                float vyBefore = s.Velocity.y;
                PlayerMover.Step(ref s, ref c, i, 1f, w, Dt);
                if (!wasGrounded && !s.OnGround && s.Velocity.y > vyBefore + 0.01f) doubleJump = true;
                if (s.Position.y > maxY) maxY = s.Position.y;
            }

            Check("move.jump_apex", maxY > 1.2f && maxY < 1.7f, "apex=" + maxY.ToString("0.###"));
            Check("move.no_double_jump", !doubleJump, "upward velocity appeared while airborne");
        }

        private static void TestSpeedCap()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            MoveState s = Player();
            s.Velocity = new Vec3(20f, 0f, 0f);
            PlayerMover.Step(ref s, ref c, MoveInput.None, 1f, w, Dt);
            float speed = HSpeed(s);
            Check("move.speed_cap", speed <= c.MaxSpeedCap + 0.01f,
                  "speed=" + speed.ToString("0.###") + " cap=" + c.MaxSpeedCap);
        }

        private static void TestStepUp()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            w.AddBox(new Vec3(-2f, 0f, 1.0f), new Vec3(2f, 0.3f, 3f));   // 0.3 m step
            MoveState s = Player();
            Sim(ref s, ref c, Forward(0f, false), 1f, w, 0.47f);
            Check("move.step_up", s.Position.y > 0.25f && s.Position.z > 1.0f && s.Position.z < 2.9f,
                  "pos=" + s.Position);

            FlatWorld w2 = new FlatWorld();
            w2.AddBox(new Vec3(-2f, 0f, 1.0f), new Vec3(2f, 1.0f, 3f));  // 1.0 m wall
            MoveState s2 = Player();
            Sim(ref s2, ref c, Forward(0f, false), 1f, w2, 1.5f);
            Check("move.wall_block", s2.Position.z < 0.75f && s2.Position.y < 0.1f,
                  "pos=" + s2.Position);
        }

        private static void TestWallSlide()
        {
            MoveConfig c = MoveConfig.CreateDefault();
            FlatWorld w = new FlatWorld();
            w.AddBox(new Vec3(-20f, 0f, 3.0f), new Vec3(20f, 3f, 4f));     // wall across +Z
            MoveState s = Player();
            MoveInput diag = new MoveInput();
            diag.MoveForward = 1f;
            diag.MoveRight = 1f;
            diag.YawDeg = 0f;
            Sim(ref s, ref c, diag, 1f, w, 2f);
            Check("move.wall_slide", s.Position.x > 1.5f && s.Position.z < 2.75f, "pos=" + s.Position);
        }

        // ---------------------------------------------------------------- weapon tests

        private static void TestWeaponFireAndReload()
        {
            WeaponDef ak = WeaponDef.Find("ak47");
            Check("weapon.catalog", ak != null && WeaponDef.All().Length == 7 && WeaponDef.Find("AWP") != null,
                  "catalog size=" + (WeaponDef.All() == null ? -1 : WeaponDef.All().Length));

            WeaponState ws = WeaponLogic.CreateState(ak);
            FireResult r;
            bool f1 = WeaponLogic.TryFire(ref ws, ak, 0f, 0.5f, 0.5f, out r);
            bool f2 = WeaponLogic.TryFire(ref ws, ak, 0.05f, 0.5f, 0.5f, out r);
            bool f3 = WeaponLogic.TryFire(ref ws, ak, 0.11f, 0.5f, 0.5f, out r);
            Check("weapon.rate_limit", f1 && !f2 && f3, "f1=" + f1 + " f2=" + f2 + " f3=" + f3);

            float t = 0.3f;
            for (int i = 0; i < 40; i++)
            {
                WeaponLogic.TryFire(ref ws, ak, t, 0.5f, 0.5f, out r);
                t += 0.1f;
            }
            Check("weapon.mag_empty", ws.AmmoInMag == 0, "ammo=" + ws.AmmoInMag);

            bool reloadStarted = WeaponLogic.TryStartReload(ref ws, ak, t);
            WeaponLogic.Update(ref ws, ak, t + ak.ReloadTime + 0.01f, 0.02f);
            Check("weapon.reload", reloadStarted && ws.AmmoInMag == ak.MagSize && ws.ReserveAmmo == ak.ReserveAmmo - ak.MagSize,
                  "mag=" + ws.AmmoInMag + " reserve=" + ws.ReserveAmmo);
        }

        private static void TestRecoilRecovery()
        {
            WeaponDef ak = WeaponDef.Find("ak47");
            WeaponState ws = WeaponLogic.CreateState(ak);
            FireResult r;
            float t = 0f;
            for (int i = 0; i < 10; i++)
            {
                WeaponLogic.TryFire(ref ws, ak, t, 0.5f, 0.5f, out r);
                t += 0.11f;
            }
            Check("recoil.spray_climb", ws.RecoilPitch > 10f, "pitch=" + ws.RecoilPitch.ToString("0.##"));

            float now = t;
            int steps = (int)Math.Round(2.5f / Dt);
            for (int i = 0; i < steps; i++)
            {
                WeaponLogic.Update(ref ws, ak, now, Dt);
                now += Dt;
            }
            Check("recoil.recovery", Math.Abs(ws.RecoilPitch) < 0.01f && Math.Abs(ws.RecoilYaw) < 0.01f,
                  "pitch=" + ws.RecoilPitch.ToString("0.###") + " yaw=" + ws.RecoilYaw.ToString("0.###"));
            Check("recoil.index_reset", ws.ShotIndex == 0, "index=" + ws.ShotIndex);
        }

        private static void TestSpread()
        {
            WeaponDef ak = WeaponDef.Find("ak47");
            float stand = WeaponLogic.ComputeSpreadDeg(ak, 0f, true, false);
            float move = WeaponLogic.ComputeSpreadDeg(ak, 1f, true, false);
            float air = WeaponLogic.ComputeSpreadDeg(ak, 0f, false, false);
            float duck = WeaponLogic.ComputeSpreadDeg(ak, 0f, true, true);
            bool ok = Math.Abs(stand - 0.35f) < 0.01f && Math.Abs(move - 3.55f) < 0.01f
                      && Math.Abs(air - 6.35f) < 0.02f && Math.Abs(duck - 0.245f) < 0.01f;
            Check("spread.states", ok,
                  "stand=" + stand.ToString("0.###") + " move=" + move.ToString("0.###") +
                  " air=" + air.ToString("0.###") + " duck=" + duck.ToString("0.###"));
        }

        private static void TestDamage()
        {
            WeaponDef ak = WeaponDef.Find("ak47");
            int d0 = Ballistics.ComputeDamageAtDistance(ak, 0f);
            int armor = 100;
            int armorDmg;
            int hp = Ballistics.ApplyArmor(36, ak.ArmorPen, ref armor, out armorDmg);
            Check("damage.ak_armor", d0 == 36 && hp == 28 && armorDmg == 4 && armor == 96,
                  "d0=" + d0 + " hp=" + hp + " armorDmg=" + armorDmg + " armor=" + armor);

            WeaponDef awp = WeaponDef.Find("awp");
            int a2 = 100;
            int hp2 = Ballistics.ApplyArmor(awp.Damage, awp.ArmorPen, ref a2, out armorDmg);
            Check("damage.awp_lethal", hp2 >= 100, "hp=" + hp2);

            Check("damage.hitgroups",
                  Math.Abs(Ballistics.HitGroupMultiplier(HitGroup.Head) - 4f) < 0.001f &&
                  Math.Abs(Ballistics.HitGroupMultiplier(HitGroup.Stomach) - 1.25f) < 0.001f &&
                  Math.Abs(Ballistics.HitGroupMultiplier(HitGroup.Leg) - 0.75f) < 0.001f,
                  "hitgroup multipliers wrong");
        }

        private static void TestEconomy()
        {
            Check("economy.loss_ladder",
                  Economy.LossReward(1) == 1400 && Economy.LossReward(5) == 3400 && Economy.LossReward(9) == 3400,
                  "1=" + Economy.LossReward(1) + " 5=" + Economy.LossReward(5) + " 9=" + Economy.LossReward(9));
            Check("economy.clamp", Economy.ClampMoney(20000) == 16000 && Economy.ClampMoney(-5) == 0,
                  "20000->" + Economy.ClampMoney(20000) + " -5->" + Economy.ClampMoney(-5));
            Check("economy.win",
                  Economy.WinReward(RoundEndReason.BombExploded) == 3500 &&
                  Economy.WinReward(RoundEndReason.TimeExpired) == 3250 &&
                  Economy.WinReward(RoundEndReason.BombDefused) == 3500,
                  "win rewards wrong");
            Check("economy.prices",
                  WeaponDef.Find("ak47").Price == 2700 && WeaponDef.Find("awp").Price == 4750 && Economy.ArmorPrice == 650,
                  "prices wrong");
        }

        // ---------------------------------------------------------------- helpers

        private static void Sim(ref MoveState s, ref MoveConfig c, MoveInput input, float weaponMult, ICollisionWorld world, float seconds)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) PlayerMover.Step(ref s, ref c, input, weaponMult, world, Dt);
        }

        private static MoveState Player()
        {
            MoveState s = new MoveState();
            s.OnGround = true;
            return s;
        }

        private static MoveInput Forward(float yawDeg, bool jump)
        {
            MoveInput i = new MoveInput();
            i.MoveForward = 1f;
            i.YawDeg = yawDeg;
            i.JumpPressed = jump;
            return i;
        }

        private static float HSpeed(MoveState s)
        {
            return (float)Math.Sqrt(s.Velocity.x * s.Velocity.x + s.Velocity.z * s.Velocity.z);
        }

        /// <summary>Test collision world: ground plane at y=0 plus axis aligned boxes.</summary>
        private sealed class FlatWorld : ICollisionWorld
        {
            private readonly List<Vec3[]> _boxes = new List<Vec3[]>();

            public void AddBox(Vec3 min, Vec3 max) { _boxes.Add(new Vec3[] { min, max }); }

            public SweepHit SweepCapsule(Vec3 feetPos, float radius, float height, Vec3 delta)
            {
                SweepHit best = new SweepHit();
                best.Hit = false;
                best.Fraction = 1f;

                if (delta.y < -1e-6f && feetPos.y + delta.y <= 0f)
                {
                    float f = (-feetPos.y) / delta.y;
                    if (f >= 0f && f <= 1f)
                    {
                        best.Hit = true;
                        best.Fraction = f;
                        best.Normal = new Vec3(0f, 1f, 0f);
                    }
                }

                for (int i = 0; i < _boxes.Count; i++)
                {
                    float f;
                    Vec3 n;
                    if (RaySegmentBox(feetPos, radius, height, delta, _boxes[i][0], _boxes[i][1], out f, out n))
                    {
                        if (!best.Hit || f < best.Fraction)
                        {
                            best.Hit = true;
                            best.Fraction = f;
                            best.Normal = n;
                        }
                    }
                }
                return best;
            }

            public bool OverlapCapsule(Vec3 feetPos, float radius, float height)
            {
                return feetPos.y < 0f;
            }

            private static bool RaySegmentBox(Vec3 feetPos, float radius, float height, Vec3 delta, Vec3 bmin, Vec3 bmax,
                                              out float fraction, out Vec3 normal)
            {
                fraction = 0f;
                normal = Vec3.Zero;

                float px = feetPos.x;
                float py = feetPos.y + radius;   // bottom sphere centre
                float pz = feetPos.z;

                float lo0 = bmin.x - radius, hi0 = bmax.x + radius;
                float lo1 = bmin.y - radius, hi1 = bmax.y + radius;
                float lo2 = bmin.z - radius, hi2 = bmax.z + radius;

                float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
                int axis = -1;
                for (int ax = 0; ax < 3; ax++)
                {
                    float p = ax == 0 ? px : (ax == 1 ? py : pz);
                    float d = ax == 0 ? delta.x : (ax == 1 ? delta.y : delta.z);
                    float lo = ax == 0 ? lo0 : (ax == 1 ? lo1 : lo2);
                    float hi = ax == 0 ? hi0 : (ax == 1 ? hi1 : hi2);
                    if (Math.Abs(d) < 1e-8f)
                    {
                        if (p < lo || p > hi) return false;
                    }
                    else
                    {
                        float inv = 1f / d;
                        float ta = (lo - p) * inv;
                        float tb = (hi - p) * inv;
                        if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                        if (ta > t0) { t0 = ta; axis = ax; }
                        if (tb < t1) t1 = tb;
                        if (t0 > t1) return false;
                    }
                }
                if (axis < 0 || t1 < 0f || t0 > 1f) return false;

                if (t0 <= 0f)
                {
                    // Already touching / overlapping: push-out normal = axis of smallest
                    // normalised penetration; only a hit if the motion goes into that face.
                    float cx = (lo0 + hi0) * 0.5f, cy = (lo1 + hi1) * 0.5f, cz = (lo2 + hi2) * 0.5f;
                    float ex = Math.Max(1e-4f, (hi0 - lo0) * 0.5f);
                    float ey = Math.Max(1e-4f, (hi1 - lo1) * 0.5f);
                    float ez = Math.Max(1e-4f, (hi2 - lo2) * 0.5f);
                    float rx = (px - cx) / ex, ry = (py - cy) / ey, rz = (pz - cz) / ez;
                    float pxa = Math.Abs(rx), pya = Math.Abs(ry), pza = Math.Abs(rz);
                    int a = (pxa >= pya && pxa >= pza) ? 0 : (pya >= pza ? 1 : 2);
                    float val = a == 0 ? rx : (a == 1 ? ry : rz);
                    float sgnIn = val >= 0f ? 1f : -1f;
                    normal = a == 0 ? new Vec3(sgnIn, 0f, 0f) : (a == 1 ? new Vec3(0f, sgnIn, 0f) : new Vec3(0f, 0f, sgnIn));
                    if (Vec3.Dot(delta, normal) >= 0f) return false;   // not moving into the surface
                    fraction = 0f;
                    return true;
                }

                fraction = t0;
                float dd = axis == 0 ? delta.x : (axis == 1 ? delta.y : delta.z);
                float sgn = dd > 0f ? -1f : 1f;
                normal = axis == 0 ? new Vec3(sgn, 0f, 0f) : (axis == 1 ? new Vec3(0f, sgn, 0f) : new Vec3(0f, 0f, sgn));
                return true;
            }
        }
    }
}
