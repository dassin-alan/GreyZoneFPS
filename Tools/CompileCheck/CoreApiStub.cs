// =====================================================================================
// GreyZone - FROZEN CORE API SURFACE (compile-check aid, NOT part of the Unity build)
// =====================================================================================
// This file exists ONLY so that gameplay scripts can be compile-checked before/while the
// real Core implementation exists. It must never be copied into Assets/.
//
// The real implementation lives in: Assets/GreyZone/Scripts/Core/*.cs
// It must match every public signature below EXACTLY (namespace, type, member name,
// parameters, return type, constants). Adding private/internal helpers is allowed.
// Changing the public surface is NOT allowed without updating this file + docs/SPEC.md.
//
// Coordinate system: Y-up, same axis order/direction as Unity (X right, Y up, Z forward).
// Distance unit: 1 = 1 meter (Unity units). Angle unit: degrees.
// Pure C# only: System + System.Collections.Generic. No UnityEngine, no LINQ in hot paths.
// =====================================================================================

using System;
using System.Collections.Generic;

namespace GreyZone.Core
{
    public enum Team { Terrorists, CTs }

    // ---------------------------------------------------------------------------------
    // Math
    // ---------------------------------------------------------------------------------
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z) { throw new NotImplementedException(); }

        public static Vec3 Zero { get { throw new NotImplementedException(); } }
        public static Vec3 Up { get { throw new NotImplementedException(); } }

        public static Vec3 operator +(Vec3 a, Vec3 b) { throw new NotImplementedException(); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { throw new NotImplementedException(); }
        public static Vec3 operator -(Vec3 a) { throw new NotImplementedException(); }
        public static Vec3 operator *(Vec3 a, float s) { throw new NotImplementedException(); }
        public static Vec3 operator *(float s, Vec3 a) { throw new NotImplementedException(); }
        public static Vec3 operator /(Vec3 a, float s) { throw new NotImplementedException(); }

        public static float Dot(Vec3 a, Vec3 b) { throw new NotImplementedException(); }
        public static Vec3 Cross(Vec3 a, Vec3 b) { throw new NotImplementedException(); }
        public static float Distance(Vec3 a, Vec3 b) { throw new NotImplementedException(); }
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) { throw new NotImplementedException(); }

        public float Length() { throw new NotImplementedException(); }
        public float LengthSq() { throw new NotImplementedException(); }
        public Vec3 Normalized() { throw new NotImplementedException(); }

        public override string ToString() { throw new NotImplementedException(); }
    }

    // ---------------------------------------------------------------------------------
    // Movement (CS-style accelerate / friction / stopspeed / air control)
    // ---------------------------------------------------------------------------------
    public struct MoveConfig
    {
        public float MaxSpeed;              // reference 6.35 m/s (knife run); weapon multiplier applies on top
        public float Accel;                 // 5.5
        public float AirAccel;              // 12
        public float AirMaxWishSpeed;       // 0.762 m/s (30 u/s) - air control cap
        public float Friction;              // 5.2
        public float StopSpeed;             // 2.032 m/s (80 u/s)
        public float Gravity;               // 20.32 m/s^2
        public float JumpImpulse;           // 7.67 m/s
        public float MaxSpeedCap;           // 8.128 m/s (320 u/s) hard clamp (anti bunny-hop exploit)
        public float WalkMultiplier;        // 0.52 (silent walk; there is NO sprint)
        public float DuckMultiplier;        // 0.34
        public float StandHeight;           // 1.83 m
        public float DuckHeight;            // 1.37 m
        public float DuckTransitionTime;    // 0.20 s

        public static MoveConfig CreateDefault() { throw new NotImplementedException(); }
        public void ClampToValid() { throw new NotImplementedException(); }
    }

    public struct MoveInput
    {
        public float MoveForward;   // -1..1, local to YawDeg
        public float MoveRight;     // -1..1, local to YawDeg
        public bool JumpPressed;    // edge trigger
        public bool DuckHeld;
        public bool WalkHeld;
        public float YawDeg;        // view yaw in degrees, 0 = +Z

        public static MoveInput None { get { throw new NotImplementedException(); } }
    }

    public struct MoveState
    {
        public Vec3 Position;       // FEET position (bottom center of the capsule)
        public Vec3 Velocity;       // m/s, Y is vertical
        public bool OnGround;
        public bool Ducked;
        public float DuckFraction;  // 0 = standing, 1 = fully ducked
        public bool JumpQueued;     // jump buffer, consumed by Step
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

    /// <summary>Implemented by the Unity layer (Physics) and by the test harness.</summary>
    public interface ICollisionWorld
    {
        SweepHit SweepCapsule(Vec3 feetPos, float radius, float height, Vec3 delta);
        bool OverlapCapsule(Vec3 feetPos, float radius, float height);
    }

    public static class PlayerMover
    {
        public const float PlayerRadius = 0.4f;
        public const float StepHeight = 0.45f;
        public const float JumpBufferTime = 0.10f;

        /// <summary>Max horizontal speed for this tick (walk / duck multipliers + weapon multiplier).</summary>
        public static float ComputeMaxSpeed(ref MoveConfig cfg, float weaponSpeedMultiplier, bool walking, bool ducked) { throw new NotImplementedException(); }

        /// <summary>Capsule height for the current duck state.</summary>
        public static float CurrentHeight(ref MoveConfig cfg, MoveState state) { throw new NotImplementedException(); }

        /// <summary>
        /// One fixed movement tick (~1/64 s). Applies duck transition, friction, acceleration,
        /// gravity, jump, then moves with capsule sweeps + step-up. Never allows double jump.
        /// </summary>
        public static void Step(ref MoveState state, ref MoveConfig cfg, MoveInput input, float weaponSpeedMultiplier, ICollisionWorld world, float dt) { throw new NotImplementedException(); }
    }

    // ---------------------------------------------------------------------------------
    // Weapons / ballistics
    // ---------------------------------------------------------------------------------
    public enum WeaponKind { Pistol, Rifle, Sniper, Smg, Shotgun }

    public enum HitGroup { Head, Chest, Stomach, Arm, Leg }

    public class WeaponDef
    {
        public string Id;
        public string DisplayName;
        public WeaponKind Kind;
        public int Price;
        public int Damage;
        public float ArmorPen;              // 0..1, CS-style penetration
        public float Rpm;
        public int MagSize;
        public int ReserveAmmo;
        public float ReloadTime;            // seconds
        public float RangeModifier;         // falloff per 12.7 m chunk, e.g. 0.98
        public float MoveSpeedMultiplier;   // relative to MoveConfig.MaxSpeed (knife = 1.0)
        public int KillReward;
        public float SpreadStandDeg;        // base inaccuracy cone half-angle (deg)
        public float SpreadMoveDeg;         // extra at full run speed
        public float SpreadCrouchMultiplier;// e.g. 0.7
        public float SpreadJumpDeg;         // extra while airborne
        public float RecoilPitchScale;      // per-weapon recoil magnitude scale
        public float RecoilYawScale;
        public float RecoilRecoveryDelay;   // s after last shot before recoil decays
        public float RecoilRecoveryRate;    // deg/s decay speed
        public float RecoilResetTime;       // s after last shot to reset ShotIndex
        public float PatternVariance;       // 0..1 jitter applied to pattern offsets
        public float[] PatternPitch;        // per-shot pitch delta in degrees (+ = view up)
        public float[] PatternYaw;          // per-shot yaw delta in degrees (+ = view right)
        public bool HasScope;
        public float ScopeFov;              // degrees, 0 = unscoped

        public static WeaponDef[] All() { throw new NotImplementedException(); }
        public static WeaponDef Find(string id) { throw new NotImplementedException(); }
    }

    public struct WeaponState
    {
        public int AmmoInMag;
        public int ReserveAmmo;
        public int ShotIndex;       // current spray index, resets after RecoilResetTime
        public float NextFireTime;  // earliest allowed next shot (game time)
        public float LastFireTime;
        public float ReloadEndTime;
        public bool Reloading;
        public float RecoilPitch;   // accumulated recoil offset, degrees, + = view up
        public float RecoilYaw;     // accumulated recoil offset, degrees, + = view right
    }

    public struct FireResult
    {
        public bool Fired;
        public int ShotIndex;       // pattern index that was used
    }

    public static class WeaponLogic
    {
        /// <summary>Fresh state: full mag + reserve, no recoil, not reloading.</summary>
        public static WeaponState CreateState(WeaponDef def) { throw new NotImplementedException(); }

        /// <summary>Rate of fire, ammo and reload checks.</summary>
        public static bool CanFire(ref WeaponState st, WeaponDef def, float now) { throw new NotImplementedException(); }

        /// <summary>
        /// Consumes one round, applies the recoil kick of this shot index (pattern + variance
        /// jitter from the two 0..1 randoms), advances ShotIndex and NextFireTime.
        /// Firing while reloading cancels the reload (CS behaviour). Returns false if not fired.
        /// </summary>
        public static bool TryFire(ref WeaponState st, WeaponDef def, float now, float randPitch01, float randYaw01, out FireResult result) { throw new NotImplementedException(); }

        /// <summary>Reload completion + recoil recovery + spray-index reset. Call every tick.</summary>
        public static void Update(ref WeaponState st, WeaponDef def, float now, float dt) { throw new NotImplementedException(); }

        public static bool TryStartReload(ref WeaponState st, WeaponDef def, float now) { throw new NotImplementedException(); }

        /// <summary>Inaccuracy cone half-angle in degrees for the current movement state.</summary>
        public static float ComputeSpreadDeg(WeaponDef def, float speedRatio01, bool onGround, bool ducked) { throw new NotImplementedException(); }
    }

    public static class Ballistics
    {
        public const int MaxHealth = 100;
        public const int MaxArmor = 100;

        public static float HitGroupMultiplier(HitGroup group) { throw new NotImplementedException(); }

        /// <summary>Base damage after distance falloff: Damage * RangeModifier^(dist / 12.7).</summary>
        public static int ComputeDamageAtDistance(WeaponDef def, float distanceMeters) { throw new NotImplementedException(); }

        /// <summary>
        /// CS armour formula. damage = damage after hit-group multiplier.
        /// With armour: health = round(damage * armorPen); armourDamage = round((damage - health) * 0.5).
        /// armour is clamped to &gt;= 0. Without armour the full damage is returned and armourDamage = 0.
        /// </summary>
        public static int ApplyArmor(int damage, float armorPen, ref int armor, out int armorDamage) { throw new NotImplementedException(); }
    }

    // ---------------------------------------------------------------------------------
    // Economy / match rules
    // ---------------------------------------------------------------------------------
    public enum RoundEndReason { BombExploded, BombDefused, TerroristsEliminated, CTsEliminated, TimeExpired }

    public static class Economy
    {
        public const int StartMoney = 800;
        public const int MaxMoney = 16000;
        public const int WinRewardElimination = 3250;
        public const int WinRewardBombExploded = 3500;
        public const int WinRewardBombDefused = 3500;
        public const int WinRewardTimeExpired = 3250;
        public const int LossRewardBase = 1400;
        public const int LossRewardStep = 500;
        public const int LossRewardMax = 3400;
        public const int PlantBonusPlanter = 300;
        public const int PlantBonusTeam = 800;
        public const int DefuseBonusDefuser = 300;
        public const int ArmorPrice = 650;
        public const int HelmetPrice = 350;
        public const int DefuseKitPrice = 400;

        public static int WinReward(RoundEndReason reason) { throw new NotImplementedException(); }

        /// <summary>lossStreak = consecutive losses INCLUDING the current one (1 = first loss =&gt; 1400).</summary>
        public static int LossReward(int lossStreak) { throw new NotImplementedException(); }

        public static int ClampMoney(int money) { throw new NotImplementedException(); }
        public static bool CanAfford(int money, int price) { throw new NotImplementedException(); }
    }

    public static class MatchRules
    {
        public const int RoundsToWin = 8;
        public const int RoundsPerHalf = 8;
        public const int MaxBotsPerTeam = 5;
        public const float FreezeTime = 10f;
        public const float BuyTime = 20f;              // buy allowed during freeze + this long into the round
        public const float RoundTime = 115f;
        public const float BombTimer = 40f;
        public const float PlantTime = 3f;
        public const float DefuseTime = 10f;
        public const float DefuseTimeWithKit = 5f;
        public const float IntermissionTime = 5f;
        public const float BombDamageMax = 500f;
        public const float BombDamageRadius = 12f;
    }
}
