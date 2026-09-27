// GreyZone - spawns and equips the bots, keeps per round objectives and team alerts.
using System.Collections.Generic;
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class BotManager : MonoBehaviour
    {
        public static BotManager I;

        public const int MaxBots = 9;

        public readonly List<BotActor> Bots = new List<BotActor>(MaxBots);

        private MapData _map;
        private int _spawnCursorT;
        private int _spawnCursorCT;

        public void Build(Transform parent, MapData map)
        {
            I = this;
            _map = map;
            for (int i = 0; i < MaxBots; i++)
            {
                GameObject go = new GameObject("bot" + i);
                go.transform.SetParent(parent, false);
                BotActor b = go.AddComponent<BotActor>();
                b.Init(Team.Terrorists);
                b.Setup(map.Graph, i, 0.04f * i);
                b.gameObject.SetActive(false);
                Bots.Add(b);
            }
        }

        public int AliveCount(Team t)
        {
            int n = 0;
            for (int i = 0; i < Bots.Count; i++)
            {
                BotActor b = Bots[i];
                if (b != null && b.gameObject.activeSelf && b.Alive && b.Team == t) n++;
            }
            return n;
        }

        public void AlertTeam(Team t, Vector3 pos, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < Bots.Count; i++)
            {
                BotActor b = Bots[i];
                if (b == null || !b.gameObject.activeSelf || !b.Alive || b.Team != t) continue;
                Vector3 d = b.FeetPos - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) b.Alert(pos);
            }
        }

        /// <summary>Activates / positions / equips every bot for a new round.</summary>
        public void StartRound(int round, Team playerTeam, int enemyCount, int half)
        {
            Team enemyTeam = playerTeam == Team.Terrorists ? Team.CTs : Team.Terrorists;
            int enemies = Mathf.Clamp(enemyCount, 2, MatchRules.MaxBotsPerTeam);
            int allies = Mathf.Clamp(enemies - 1, 1, MatchRules.MaxBotsPerTeam);

            // start after the player's slot so nobody ever spawns inside the local player
            _spawnCursorT = playerTeam == Team.Terrorists ? 1 : 0;
            _spawnCursorCT = playerTeam == Team.CTs ? 1 : 0;

            for (int i = 0; i < Bots.Count; i++)
            {
                BotActor b = Bots[i];
                bool used = i < enemies + allies;
                b.gameObject.SetActive(used);
                if (!used) continue;

                bool enemy = i < enemies;
                Team team = enemy ? enemyTeam : playerTeam;
                b.SetTeam(team);
                b.Name = GzNames.BotName(enemy ? i : i - enemies);

                Vector3 pos = NextSpawn(team, i);
                float yaw = team == Team.Terrorists ? 0f : 180f;
                b.Respawn(pos, yaw);
                b.ResetForRound();
                Equip(b, round, enemy);
                SetObjective(b);
            }
        }

        private static readonly Vector2[] SpawnOffsets =
        {
            new Vector2(0f, 0f), new Vector2(0.9f, 0.6f), new Vector2(-0.9f, 0.6f),
            new Vector2(0.9f, -0.6f), new Vector2(-0.9f, -0.6f), new Vector2(0f, 1.3f), new Vector2(0f, -1.3f)
        };

        private Vector3 NextSpawn(Team team, int index)
        {
            Vector3[] arr = team == Team.Terrorists ? _map.SpawnsT : _map.SpawnsCT;
            int cursor = team == Team.Terrorists ? _spawnCursorT++ : _spawnCursorCT++;
            int slot = cursor % arr.Length;
            int lap = (cursor / arr.Length) % SpawnOffsets.Length;
            Vector2 off = SpawnOffsets[lap];
            Vector3 p = arr[slot];
            return new Vector3(p.x + off.x, p.y, p.z + off.y);
        }

        private void SetObjective(BotActor b)
        {
            if (b.Team == Team.Terrorists)
            {
                int site = (b.Index % 2 == 0) ? 0 : 1;
                Vector3 anchor = site == 0 ? _map.HoldA[b.Index % _map.HoldA.Length] : _map.HoldB[b.Index % _map.HoldB.Length];
                b.SetObjective(site, anchor);
            }
            else
            {
                int site = (b.Index % 2 == 0) ? 0 : 1;
                Vector3 anchor = site == 0 ? _map.HoldA[b.Index % _map.HoldA.Length] : _map.HoldB[b.Index % _map.HoldB.Length];
                b.SetObjective(site, anchor);
            }
        }

        /// <summary>Round based equipment tiers (no per bot economy, this is a prototype).</summary>
        public static void Equip(BotActor b, int round, bool enemy)
        {
            b.ClearWeapons();
            WeaponDef pistol = WeaponDef.Find(b.Team == Team.Terrorists ? "glock" : "usp");
            if (pistol == null) pistol = WeaponDef.Find("glock");
            b.GiveWeapon(pistol, true);
            b.Armor = 0;
            b.HasHelmet = false;
            b.HasKit = false;

            if (round <= 1) { }
            else if (round <= 3)
            {
                b.Armor = 100;
                if (b.Index % 2 == 0)
                {
                    WeaponDef mid = WeaponDef.Find("p250");
                    if (mid != null) b.GiveWeapon(mid, true);
                }
            }
            else
            {
                b.Armor = 100;
                b.HasHelmet = true;
                bool awp = enemy && b.Index == 0 && round >= 4 && ((round - 4) % 5 == 0);
                WeaponDef rifle = WeaponDef.Find(b.Team == Team.Terrorists ? "ak47" : "m4a4");
                if (awp)
                {
                    WeaponDef awpDef = WeaponDef.Find("awp");
                    if (awpDef != null) rifle = awpDef;
                }
                if (rifle != null) b.GiveWeapon(rifle, true);
                b.GiveWeapon(pistol, false);
            }

            if (b.Slots[0] != null) b.SelectSlot(0);
        }
    }
}
