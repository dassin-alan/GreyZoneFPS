// GreyZone - runtime tunables, quality tiers and PlayerPrefs persistence (prefix "gz.").
using System;
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }
    public enum QualityTier { Low = 0, Medium = 1, High = 2 }

    public static class GameSettings
    {
        // ---- movement / weapon feel (F1 panel, persisted) -------------------------------
        public static MoveConfig Move = MoveConfig.CreateDefault();
        public static float RecoilScale = 1f;      // 0..2 multiplier on view kick
        public static float SpreadScale = 1f;      // 0..2 multiplier on inaccuracy cone
        public static float MouseSens = 2.2f;      // degrees per mouse unit
        public static float TouchSens = 0.22f;     // degrees per touch pixel (at 1080p)
        public static float Fov = 70f;
        public static float AimAssist = 0.30f;     // 0..1
        public static bool AutoStop = true;        // firing auto-brake (mobile aid)

        // ---- match / presentation ------------------------------------------------------
        public static BotDifficulty Difficulty = BotDifficulty.Normal;
        public static QualityTier Quality = QualityTier.High;
        public static Team PlayerTeam = Team.Terrorists;
        public static int EnemyCount = 4;          // 2..5, applied from the next round
        public static bool ForceTouch = false;
        public static bool LeftHanded = false;     // mirror the touch layout
        public static bool ShowFps = false;

        // ---- derived ------------------------------------------------------------------
        public static float BotReactionTime
        {
            get { return Difficulty == BotDifficulty.Easy ? 0.45f : (Difficulty == BotDifficulty.Normal ? 0.30f : 0.18f); }
        }
        public static float BotAimErrorDeg
        {
            get { return Difficulty == BotDifficulty.Easy ? 5.0f : (Difficulty == BotDifficulty.Normal ? 3.0f : 1.6f); }
        }
        public static float FarClip
        {
            get { return Quality == QualityTier.Low ? 80f : (Quality == QualityTier.Medium ? 150f : 200f); }
        }
        public static float FogEnd
        {
            get { return Quality == QualityTier.Low ? 60f : (Quality == QualityTier.Medium ? 100f : 140f); }
        }
        public static float FogStart
        {
            get { return Quality == QualityTier.Low ? 12f : (Quality == QualityTier.Medium ? 18f : 25f); }
        }
        public static int TargetFrameRate
        {
            get { return Quality == QualityTier.Low ? 30 : 60; }
        }
        public static float EffectScale
        {
            get { return Quality == QualityTier.Low ? 0.5f : (Quality == QualityTier.Medium ? 0.75f : 1f); }
        }
        public static bool TouchMode
        {
            get { return Application.isMobilePlatform || ForceTouch; }
        }

        // ---- persistence --------------------------------------------------------------
        private const string P = "gz.";

        private static float GetF(string key, float def)
        {
            return PlayerPrefs.GetFloat(P + key, def);
        }
        private static int GetI(string key, int def)
        {
            return PlayerPrefs.GetInt(P + key, def);
        }
        private static bool GetB(string key, bool def)
        {
            return PlayerPrefs.GetInt(P + key, def ? 1 : 0) != 0;
        }

        public static void Load()
        {
            MoveConfig d = MoveConfig.CreateDefault();
            Move = d;

            Move.MaxSpeed = GetF("mv.maxSpeed", d.MaxSpeed);
            Move.Accel = GetF("mv.accel", d.Accel);
            Move.AirAccel = GetF("mv.airAccel", d.AirAccel);
            Move.AirMaxWishSpeed = GetF("mv.airMaxWish", d.AirMaxWishSpeed);
            Move.Friction = GetF("mv.friction", d.Friction);
            Move.StopSpeed = GetF("mv.stopSpeed", d.StopSpeed);
            Move.Gravity = GetF("mv.gravity", d.Gravity);
            Move.JumpImpulse = GetF("mv.jump", d.JumpImpulse);
            Move.MaxSpeedCap = GetF("mv.maxSpeedCap", d.MaxSpeedCap);
            Move.WalkMultiplier = GetF("mv.walk", d.WalkMultiplier);
            Move.DuckMultiplier = GetF("mv.duck", d.DuckMultiplier);
            Move.StandHeight = GetF("mv.standHeight", d.StandHeight);
            Move.DuckHeight = GetF("mv.duckHeight", d.DuckHeight);
            Move.DuckTransitionTime = GetF("mv.duckTime", d.DuckTransitionTime);
            Move.ClampToValid();

            RecoilScale = Mathf.Clamp(GetF("recoilScale", 1f), 0f, 2f);
            SpreadScale = Mathf.Clamp(GetF("spreadScale", 1f), 0f, 2f);
            MouseSens = Mathf.Clamp(GetF("sens.mouse", 2.2f), 0.2f, 12f);
            TouchSens = Mathf.Clamp(GetF("sens.touch", 0.22f), 0.03f, 1.2f);
            Fov = Mathf.Clamp(GetF("fov", 70f), 55f, 110f);
            AimAssist = Mathf.Clamp01(GetF("aimAssist", 0.30f));
            AutoStop = GetB("autoStop", true);
            ForceTouch = GetB("forceTouch", false);
            LeftHanded = GetB("leftHanded", false);
            ShowFps = GetB("showFps", false);

            Difficulty = (BotDifficulty)Mathf.Clamp(GetI("difficulty", 1), 0, 2);
            Quality = (QualityTier)Mathf.Clamp(GetI("quality", 2), 0, 2);
            PlayerTeam = GetI("playerTeam", 0) == 0 ? Team.Terrorists : Team.CTs;
            EnemyCount = Mathf.Clamp(GetI("enemyCount", 4), 2, 5);
        }

        public static void Save()
        {
            SaveMovement();
            SaveOptions();
            PlayerPrefs.Save();
        }

        public static void SaveMovement()
        {
            PlayerPrefs.SetFloat(P + "mv.maxSpeed", Move.MaxSpeed);
            PlayerPrefs.SetFloat(P + "mv.accel", Move.Accel);
            PlayerPrefs.SetFloat(P + "mv.airAccel", Move.AirAccel);
            PlayerPrefs.SetFloat(P + "mv.airMaxWish", Move.AirMaxWishSpeed);
            PlayerPrefs.SetFloat(P + "mv.friction", Move.Friction);
            PlayerPrefs.SetFloat(P + "mv.stopSpeed", Move.StopSpeed);
            PlayerPrefs.SetFloat(P + "mv.gravity", Move.Gravity);
            PlayerPrefs.SetFloat(P + "mv.jump", Move.JumpImpulse);
            PlayerPrefs.SetFloat(P + "mv.maxSpeedCap", Move.MaxSpeedCap);
            PlayerPrefs.SetFloat(P + "mv.walk", Move.WalkMultiplier);
            PlayerPrefs.SetFloat(P + "mv.duck", Move.DuckMultiplier);
            PlayerPrefs.SetFloat(P + "mv.standHeight", Move.StandHeight);
            PlayerPrefs.SetFloat(P + "mv.duckHeight", Move.DuckHeight);
            PlayerPrefs.SetFloat(P + "mv.duckTime", Move.DuckTransitionTime);
            PlayerPrefs.SetFloat(P + "recoilScale", RecoilScale);
            PlayerPrefs.SetFloat(P + "spreadScale", SpreadScale);
        }

        public static void SaveOptions()
        {
            PlayerPrefs.SetFloat(P + "sens.mouse", MouseSens);
            PlayerPrefs.SetFloat(P + "sens.touch", TouchSens);
            PlayerPrefs.SetFloat(P + "fov", Fov);
            PlayerPrefs.SetFloat(P + "aimAssist", AimAssist);
            PlayerPrefs.SetInt(P + "autoStop", AutoStop ? 1 : 0);
            PlayerPrefs.SetInt(P + "forceTouch", ForceTouch ? 1 : 0);
            PlayerPrefs.SetInt(P + "leftHanded", LeftHanded ? 1 : 0);
            PlayerPrefs.SetInt(P + "showFps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(P + "difficulty", (int)Difficulty);
            PlayerPrefs.SetInt(P + "quality", (int)Quality);
            PlayerPrefs.SetInt(P + "playerTeam", PlayerTeam == Team.Terrorists ? 0 : 1);
            PlayerPrefs.SetInt(P + "enemyCount", EnemyCount);
        }

        public static void ResetDefaults()
        {
            Move = MoveConfig.CreateDefault();
            RecoilScale = 1f;
            SpreadScale = 1f;
            MouseSens = 2.2f;
            TouchSens = 0.22f;
            Fov = 70f;
            AimAssist = 0.30f;
            AutoStop = true;
            ForceTouch = false;
            LeftHanded = false;
            ShowFps = false;
            Difficulty = BotDifficulty.Normal;
            Quality = QualityTier.High;
            PlayerTeam = Team.Terrorists;
            EnemyCount = 4;
            Save();
        }

        /// <summary>Applies quality tier to engine level settings (render settings part lives in GameBootstrap).</summary>
        public static void ApplyQuality()
        {
            Application.targetFrameRate = TargetFrameRate;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.fogColor = GZ.Col(0x6A, 0x6F, 0x73);
        }
    }
}
