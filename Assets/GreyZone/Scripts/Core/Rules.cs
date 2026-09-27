// GreyZone Core - teams, round outcome, economy rewards and match constants.
using System;

namespace GreyZone.Core
{
    public enum Team { Terrorists, CTs }

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

        public static int WinReward(RoundEndReason reason)
        {
            switch (reason)
            {
                case RoundEndReason.BombExploded: return WinRewardBombExploded;
                case RoundEndReason.BombDefused: return WinRewardBombDefused;
                default: return WinRewardElimination;
            }
        }

        /// <summary>lossStreak = consecutive losses INCLUDING the current one (1 = first loss).</summary>
        public static int LossReward(int lossStreak)
        {
            int s = lossStreak;
            if (s < 1) s = 1;
            if (s > 5) s = 5;
            return LossRewardBase + LossRewardStep * (s - 1);
        }

        public static int ClampMoney(int money)
        {
            if (money < 0) return 0;
            if (money > MaxMoney) return MaxMoney;
            return money;
        }

        public static bool CanAfford(int money, int price)
        {
            return money >= price;
        }
    }

    public static class MatchRules
    {
        public const int RoundsToWin = 8;
        public const int RoundsPerHalf = 8;
        public const int MaxBotsPerTeam = 5;
        public const float FreezeTime = 10f;
        public const float BuyTime = 20f;
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
