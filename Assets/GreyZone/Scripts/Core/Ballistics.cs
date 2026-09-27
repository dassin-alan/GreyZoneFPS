// GreyZone Core - hit groups, distance falloff and CS-style armour formula.
using System;

namespace GreyZone.Core
{
    public enum HitGroup { Head, Chest, Stomach, Arm, Leg }

    public static class Ballistics
    {
        public const int MaxHealth = 100;
        public const int MaxArmor = 100;

        private const float RangeChunkMeters = 12.7f;   // damage falloff per 500 units

        public static float HitGroupMultiplier(HitGroup group)
        {
            switch (group)
            {
                case HitGroup.Head: return 4f;
                case HitGroup.Stomach: return 1.25f;
                case HitGroup.Leg: return 0.75f;
                default: return 1f;   // chest / arms
            }
        }

        public static int ComputeDamageAtDistance(WeaponDef def, float distanceMeters)
        {
            if (distanceMeters < 0f) distanceMeters = 0f;
            double chunks = distanceMeters / RangeChunkMeters;
            float dmg = def.Damage * (float)Math.Pow(def.RangeModifier, chunks);
            int v = (int)Math.Round((double)dmg);
            return v < 1 ? 1 : v;
        }

        public static int ApplyArmor(int damage, float armorPen, ref int armor, out int armorDamage)
        {
            if (armor > 0)
            {
                int health = (int)Math.Round(damage * (double)armorPen);
                if (health < 0) health = 0;
                int lose = (int)Math.Round((damage - health) * 0.5);
                if (lose < 0) lose = 0;
                armor -= lose;
                if (armor < 0) armor = 0;
                armorDamage = lose;
                return health;
            }
            armorDamage = 0;
            return damage;
        }
    }
}
