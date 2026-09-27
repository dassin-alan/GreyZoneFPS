// GreyZone Core - weapon definitions (data driven, 7 weapons).
using System;

namespace GreyZone.Core
{
    public enum WeaponKind { Pistol, Rifle, Sniper, Smg, Shotgun }

    public class WeaponDef
    {
        public string Id;
        public string DisplayName;
        public WeaponKind Kind;
        public int Price;
        public int Damage;
        public float ArmorPen;
        public float Rpm;
        public int MagSize;
        public int ReserveAmmo;
        public float ReloadTime;
        public float RangeModifier;
        public float MoveSpeedMultiplier;
        public int KillReward;
        public float SpreadStandDeg;
        public float SpreadMoveDeg;
        public float SpreadCrouchMultiplier;
        public float SpreadJumpDeg;
        public float RecoilPitchScale;
        public float RecoilYawScale;
        public float RecoilRecoveryDelay;
        public float RecoilRecoveryRate;
        public float RecoilResetTime;
        public float PatternVariance;
        public float[] PatternPitch;
        public float[] PatternYaw;
        public bool HasScope;
        public float ScopeFov;

        private static WeaponDef[] _catalog;

        public static WeaponDef[] All()
        {
            if (_catalog == null) _catalog = BuildCatalog();
            return _catalog;
        }

        public static WeaponDef Find(string id)
        {
            if (id == null) return null;
            WeaponDef[] all = All();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase)) return all[i];
            }
            return null;
        }

        private static WeaponDef New(string id, string name, WeaponKind kind, int price, int damage, float armorPen, float rpm,
                                     int magSize, int reserve, float reloadTime, float rangeModifier, float moveMult, int killReward)
        {
            WeaponDef d = new WeaponDef();
            d.Id = id;
            d.DisplayName = name;
            d.Kind = kind;
            d.Price = price;
            d.Damage = damage;
            d.ArmorPen = armorPen;
            d.Rpm = rpm;
            d.MagSize = magSize;
            d.ReserveAmmo = reserve;
            d.ReloadTime = reloadTime;
            d.RangeModifier = rangeModifier;
            d.MoveSpeedMultiplier = moveMult;
            d.KillReward = killReward;
            return d;
        }

        private static void SetFeel(WeaponDef d, float stand, float move, float crouch, float jump,
                                    float recPitch, float recYaw, float recDelay, float recRate, float recReset, float variance)
        {
            d.SpreadStandDeg = stand;
            d.SpreadMoveDeg = move;
            d.SpreadCrouchMultiplier = crouch;
            d.SpreadJumpDeg = jump;
            d.RecoilPitchScale = recPitch;
            d.RecoilYawScale = recYaw;
            d.RecoilRecoveryDelay = recDelay;
            d.RecoilRecoveryRate = recRate;
            d.RecoilResetTime = recReset;
            d.PatternVariance = variance;
        }

        private static WeaponDef[] BuildCatalog()
        {
            // ---------------------------------------------------------------- AK-47 (reference pattern)
            float[] akPitch =
            {
                2.10f, 1.75f, 1.55f, 1.40f, 1.30f, 1.20f, 1.10f, 1.05f, 1.00f, 0.95f,
                0.75f, 0.60f, 0.45f, 0.35f, 0.30f, 0.28f, 0.25f, 0.22f, 0.20f, 0.18f,
                0.16f, 0.15f, 0.14f, 0.13f, 0.12f, 0.11f, 0.10f, 0.10f, 0.09f, 0.08f
            };
            float[] akYaw =
            {
                0.00f, 0.10f, 0.22f, 0.30f, 0.34f, 0.30f, 0.18f, 0.00f, -0.22f, -0.40f,
                -0.52f, -0.45f, -0.28f, -0.05f, 0.18f, 0.38f, 0.50f, 0.52f, 0.42f, 0.25f,
                0.02f, -0.20f, -0.38f, -0.50f, -0.52f, -0.42f, -0.25f, -0.05f, 0.15f, 0.30f
            };
            WeaponDef ak = New("ak47", "AK-47", WeaponKind.Rifle, 2700, 36, 0.775f, 600f, 30, 90, 2.5f, 0.98f, 0.86f, 300);
            SetFeel(ak, 0.35f, 3.2f, 0.70f, 6.0f, 1.0f, 1.0f, 0.25f, 9f, 0.6f, 0.12f);
            ak.PatternPitch = akPitch;
            ak.PatternYaw = akYaw;

            // ---------------------------------------------------------------- M4A4 (AK pattern x0.85, first 3 flatter)
            float[] m4Pitch = new float[akPitch.Length];
            float[] m4Yaw = new float[akYaw.Length];
            for (int i = 0; i < akPitch.Length; i++)
            {
                float ps = 0.85f * (i < 3 ? 0.9f : 1f);
                m4Pitch[i] = akPitch[i] * ps;
                m4Yaw[i] = akYaw[i] * 0.85f;
            }
            WeaponDef m4 = New("m4a4", "M4A4", WeaponKind.Rifle, 3100, 33, 0.70f, 666f, 30, 90, 3.1f, 0.99f, 0.90f, 300);
            SetFeel(m4, 0.32f, 3.0f, 0.70f, 6.0f, 0.85f, 0.85f, 0.25f, 9f, 0.6f, 0.12f);
            m4.PatternPitch = m4Pitch;
            m4.PatternYaw = m4Yaw;

            // ---------------------------------------------------------------- AWP
            WeaponDef awp = New("awp", "AWP", WeaponKind.Sniper, 4750, 115, 0.975f, 41f, 10, 30, 3.7f, 0.99f, 0.80f, 100);
            SetFeel(awp, 0.10f, 6.0f, 0.50f, 10.0f, 1.6f, 1.2f, 0.50f, 6f, 1.0f, 0.05f);
            awp.PatternPitch = new float[] { 3.6f, 3.4f };
            awp.PatternYaw = new float[] { 0.4f, -0.3f };
            awp.HasScope = true;
            awp.ScopeFov = 15f;

            // ---------------------------------------------------------------- Deagle
            WeaponDef deagle = New("deagle", "沙漠之鹰", WeaponKind.Pistol, 700, 53, 0.93f, 267f, 7, 35, 2.2f, 0.99f, 0.92f, 300);
            SetFeel(deagle, 0.45f, 4.5f, 0.70f, 8.0f, 1.3f, 1.2f, 0.30f, 11f, 0.5f, 0.20f);
            deagle.PatternPitch = new float[] { 2.6f, 2.2f, 2.0f, 1.8f, 1.6f };
            deagle.PatternYaw = new float[] { 0.3f, -0.2f, 0.3f, -0.3f, 0.2f };

            // ---------------------------------------------------------------- P250
            WeaponDef p250 = New("p250", "P250", WeaponKind.Pistol, 300, 38, 0.64f, 400f, 13, 26, 2.2f, 0.95f, 0.96f, 300);
            SetFeel(p250, 0.50f, 4.0f, 0.70f, 7.0f, 1.0f, 1.0f, 0.30f, 11f, 0.5f, 0.20f);
            p250.PatternPitch = new float[] { 1.6f, 1.4f, 1.3f, 1.2f, 1.1f };
            p250.PatternYaw = new float[] { 0.2f, -0.2f, 0.2f, -0.2f, 0.1f };

            // ---------------------------------------------------------------- Glock-18 (T default)
            WeaponDef glock = New("glock", "Glock-18", WeaponKind.Pistol, 200, 30, 0.47f, 400f, 20, 120, 2.2f, 0.95f, 0.96f, 300);
            SetFeel(glock, 0.55f, 4.2f, 0.70f, 7.0f, 0.7f, 0.7f, 0.30f, 11f, 0.5f, 0.20f);
            glock.PatternPitch = new float[] { 1.1f, 1.0f, 0.9f, 0.85f, 0.8f };
            glock.PatternYaw = new float[] { 0.15f, -0.15f, 0.15f, -0.10f, 0.10f };

            // ---------------------------------------------------------------- USP (CT default)
            WeaponDef usp = New("usp", "USP", WeaponKind.Pistol, 200, 35, 0.50f, 352f, 12, 24, 2.2f, 0.95f, 0.96f, 300);
            SetFeel(usp, 0.50f, 4.0f, 0.70f, 7.0f, 0.8f, 0.8f, 0.30f, 11f, 0.5f, 0.20f);
            usp.PatternPitch = new float[] { 1.3f, 1.2f, 1.1f, 1.0f, 0.95f };
            usp.PatternYaw = new float[] { 0.2f, -0.2f, 0.15f, -0.15f, 0.1f };

            return new WeaponDef[] { ak, m4, awp, deagle, p250, glock, usp };
        }
    }
}
