// GreyZone Core - weapon runtime logic: fire rate, magazine, reload, recoil pattern, spray recovery.
using System;

namespace GreyZone.Core
{
    public struct WeaponState
    {
        public int AmmoInMag;
        public int ReserveAmmo;
        public int ShotIndex;
        public float NextFireTime;
        public float LastFireTime;
        public float ReloadEndTime;
        public bool Reloading;
        public float RecoilPitch;   // accumulated kick, degrees, + = view up
        public float RecoilYaw;     // accumulated kick, degrees, + = view right
    }

    public struct FireResult
    {
        public bool Fired;
        public int ShotIndex;
    }

    public static class WeaponLogic
    {
        public static WeaponState CreateState(WeaponDef def)
        {
            WeaponState st = new WeaponState();
            st.AmmoInMag = def.MagSize;
            st.ReserveAmmo = def.ReserveAmmo;
            return st;
        }

        public static bool CanFire(ref WeaponState st, WeaponDef def, float now)
        {
            if (st.AmmoInMag <= 0) return false;
            if (now < st.NextFireTime) return false;
            return true;
        }

        public static bool TryFire(ref WeaponState st, WeaponDef def, float now, float randPitch01, float randYaw01, out FireResult result)
        {
            result = new FireResult();
            if (!CanFire(ref st, def, now)) return false;

            if (st.Reloading) st.Reloading = false;   // firing cancels a reload (CS behaviour)

            st.AmmoInMag--;

            int idx = st.ShotIndex;
            if (idx < 0) idx = 0;
            int patternLen = (def.PatternPitch == null) ? 0 : def.PatternPitch.Length;
            if (patternLen == 0) idx = -1;
            else if (idx >= patternLen) idx = patternLen - 1;

            if (idx >= 0)
            {
                float kickPitch = def.PatternPitch[idx] * def.RecoilPitchScale;
                float kickYaw = def.PatternYaw[idx] * def.RecoilYawScale;
                float jp = (randPitch01 * 2f - 1f) * def.PatternVariance;
                float jy = (randYaw01 * 2f - 1f) * def.PatternVariance;
                st.RecoilPitch += kickPitch * (1f + jp);
                st.RecoilYaw += kickYaw * (1f + jy);
            }

            st.ShotIndex = (idx < 0 ? 0 : idx) + 1;
            st.NextFireTime = now + (def.Rpm > 0.01f ? 60f / def.Rpm : 1f);
            st.LastFireTime = now;

            result.Fired = true;
            result.ShotIndex = idx < 0 ? 0 : idx;
            return true;
        }

        public static void Update(ref WeaponState st, WeaponDef def, float now, float dt)
        {
            if (st.Reloading && now >= st.ReloadEndTime)
            {
                int need = def.MagSize - st.AmmoInMag;
                if (need > 0)
                {
                    int take = need < st.ReserveAmmo ? need : st.ReserveAmmo;
                    st.AmmoInMag += take;
                    st.ReserveAmmo -= take;
                }
                st.Reloading = false;
            }

            if (st.LastFireTime > 0f)
            {
                float since = now - st.LastFireTime;
                if (since > def.RecoilRecoveryDelay)
                {
                    float step = def.RecoilRecoveryRate * dt;
                    st.RecoilPitch = MoveTowardZero(st.RecoilPitch, step);
                    st.RecoilYaw = MoveTowardZero(st.RecoilYaw, step);
                }
                if (since > def.RecoilResetTime) st.ShotIndex = 0;
            }
        }

        public static bool TryStartReload(ref WeaponState st, WeaponDef def, float now)
        {
            if (st.Reloading) return false;
            if (st.AmmoInMag >= def.MagSize) return false;
            if (st.ReserveAmmo <= 0) return false;
            st.Reloading = true;
            st.ReloadEndTime = now + def.ReloadTime;
            return true;
        }

        public static float ComputeSpreadDeg(WeaponDef def, float speedRatio01, bool onGround, bool ducked)
        {
            float ratio = speedRatio01;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;
            float s = def.SpreadStandDeg + def.SpreadMoveDeg * ratio;
            if (!onGround) s += def.SpreadJumpDeg;
            if (ducked) s *= def.SpreadCrouchMultiplier;
            return s;
        }

        private static float MoveTowardZero(float v, float step)
        {
            if (v > step) return v - step;
            if (v < -step) return v + step;
            return 0f;
        }
    }
}
