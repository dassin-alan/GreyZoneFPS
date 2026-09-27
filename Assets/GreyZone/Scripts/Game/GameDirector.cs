// GreyZone - match flow: freeze / live / round end / match over, economy, bomb objective, kill feed.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public enum MatchPhase { Freeze, Live, RoundEnd, MatchOver }

    public struct FeedEntry
    {
        public string Text;
        public float Time;
        public bool Headshot;
        public bool KillerIsPlayer;
    }

    public class GameDirector : MonoBehaviour
    {
        public static GameDirector I;

        public MapData Map;
        public PlayerActor Player;
        public BotManager Bots;
        public BombEntity Bomb;

        public MatchPhase Phase = MatchPhase.Freeze;
        public float PhaseTimer;
        public float RoundClock;
        public float BombTimer;
        public bool BombPlanted;
        public Vector3 BombPos;
        public bool BuyWindow;
        public bool BuyMenuOpen;
        public bool Paused;
        public int Money;
        public int Round = 1;
        public int ScoreT;
        public int ScoreCT;
        public RoundEndReason LastReason;
        public string Banner = "";
        public float BannerTime;
        public float HitMarkerTime = -10f;
        public bool HitMarkerHead;

        private int _lossStreakT;
        private int _lossStreakCT;
        private int _half;
        private bool _playerSurvived;
        private int _playerSpawnCursor;
        private float _cursorCheck;

        public readonly FeedEntry[] Feed = new FeedEntry[5];
        public int FeedCount;

        private readonly Actor[] _actors = new Actor[16];
        private int _actorCount;

        // ------------------------------------------------------------------ setup
        public void Init(Transform root)
        {
            I = this;

            GameObject mapRoot = new GameObject("Map");
            mapRoot.transform.SetParent(root, false);
            Map = MapBuilder.Build(mapRoot.transform);

            GameObject bombGo = new GameObject("Bomb");
            bombGo.transform.SetParent(root, false);
            Bomb = bombGo.AddComponent<BombEntity>();
            Bomb.Build(root);

            GameObject playerGo = new GameObject("Player");
            playerGo.transform.SetParent(root, false);
            Player = playerGo.AddComponent<PlayerActor>();
            Player.Init(GameSettings.PlayerTeam);

            GameObject botsRoot = new GameObject("Bots");
            botsRoot.transform.SetParent(root, false);
            Bots = botsRoot.AddComponent<BotManager>();
            Bots.Build(botsRoot.transform, Map);

            GameObject touchGo = new GameObject("TouchInput");
            touchGo.transform.SetParent(root, false);
            TouchControls tc = touchGo.AddComponent<TouchControls>();
            tc.Init();

            GameObject hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(root, false);
            hudGo.AddComponent<Hud>();
            hudGo.AddComponent<TunerPanel>();

            StartMatch();
        }

        public void StartMatch()
        {
            Round = 1;
            ScoreT = 0;
            ScoreCT = 0;
            Money = Economy.StartMoney;
            _lossStreakT = 0;
            _lossStreakCT = 0;
            _half = 0;
            _playerSurvived = false;
            _playerSpawnCursor = 0;
            Paused = false;
            BuyMenuOpen = false;
            StartRound();
        }

        public void RestartMatch()
        {
            StartMatch();
        }

        private void StartRound()
        {
            Phase = MatchPhase.Freeze;
            PhaseTimer = MatchRules.FreezeTime;
            RoundClock = MatchRules.RoundTime;
            BuyWindow = true;
            BuyMenuOpen = false;
            BombPlanted = false;
            BombTimer = 0f;
            Bomb.Reset();
            Banner = "";
            BannerTime = 0f;

            Player.SetTeam(GameSettings.PlayerTeam);
            Vector3 spawn = NextPlayerSpawn(Player.Team);
            float yaw = Player.Team == Team.Terrorists ? 0f : 180f;
            Player.Respawn(spawn, yaw);
            Player.HasBomb = false;

            if (!_playerSurvived)
            {
                Player.ClearWeapons();
                Player.Armor = 0;
                Player.HasHelmet = false;
                Player.HasKit = false;
                WeaponDef pistol = WeaponDef.Find(Player.Team == Team.Terrorists ? "glock" : "usp");
                if (pistol == null) pistol = WeaponDef.Find("glock");
                if (pistol != null) Player.GiveWeapon(pistol, false);
                Player.SelectSlot(1);
            }
            else
            {
                if (Player.Slots[0] == null && Player.Slots[1] != null) Player.SelectSlot(1);
                else if (Player.Slots[0] != null) Player.SelectSlot(0);
            }

            Bots.StartRound(Round, Player.Team, GameSettings.EnemyCount, _half);
            AssignBombCarrier();
            RebuildActors();
            if (FxSystem.I != null) FxSystem.I.ClearProjectiles();

            Debug.Log("[GreyZone] round " + Round + " start (T " + ScoreT + " : " + ScoreCT + " CT)");
        }

        private void AssignBombCarrier()
        {
            if (Player.Team == Team.Terrorists)
            {
                Player.HasBomb = true;
                return;
            }
            for (int i = 0; i < Bots.Bots.Count; i++)
            {
                BotActor b = Bots.Bots[i];
                if (b != null && b.gameObject.activeSelf && b.Alive && b.Team == Team.Terrorists)
                {
                    b.HasBomb = true;
                    return;
                }
            }
        }

        private void TransferBomb(Team team, Actor dead)
        {
            if (Player.Team == team && Player.Alive) { Player.HasBomb = true; return; }
            for (int i = 0; i < Bots.Bots.Count; i++)
            {
                BotActor b = Bots.Bots[i];
                if (b != null && b != dead && b.gameObject.activeSelf && b.Alive && b.Team == team)
                {
                    b.HasBomb = true;
                    return;
                }
            }
        }

        private Vector3 NextPlayerSpawn(Team team)
        {
            Vector3[] arr = team == Team.Terrorists ? Map.SpawnsT : Map.SpawnsCT;
            int i = _playerSpawnCursor++ % arr.Length;
            return arr[i];
        }

        public void RebuildActors()
        {
            _actorCount = 0;
            if (Player != null && _actorCount < _actors.Length) _actors[_actorCount++] = Player;
            for (int i = 0; i < Bots.Bots.Count; i++)
            {
                BotActor b = Bots.Bots[i];
                if (b == null || !b.gameObject.activeSelf) continue;
                if (_actorCount < _actors.Length) _actors[_actorCount++] = b;
            }
        }

        public int ActorCount { get { return _actorCount; } }

        public Actor GetActor(int i)
        {
            return (i >= 0 && i < _actorCount) ? _actors[i] : null;
        }

        public int AliveOn(Team t)
        {
            int n = Bots.AliveCount(t);
            if (Player != null && Player.Team == t && Player.Alive) n++;
            return n;
        }

        // ------------------------------------------------------------------ frame
        private void Update()
        {
            float dt = Paused ? 0f : Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            GameClock.Delta = dt;
            GameClock.Now += dt;

            if (FxSystem.I != null) FxSystem.I.Tick(dt);
            Tick(dt);
            TickCursor(Time.unscaledDeltaTime);
        }

        private void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (BannerTime > 0f) BannerTime -= dt;

            switch (Phase)
            {
                case MatchPhase.Freeze:
                    PhaseTimer -= dt;
                    if (PhaseTimer <= 0f)
                    {
                        Phase = MatchPhase.Live;
                        PhaseTimer = 0f;
                    }
                    break;

                case MatchPhase.Live:
                    RoundClock -= dt;
                    if (RoundClock <= 0f)
                    {
                        RoundClock = 0f;
                        if (!BombPlanted) EndRound(RoundEndReason.TimeExpired);
                    }
                    if (BuyWindow && RoundClock <= MatchRules.RoundTime - MatchRules.BuyTime)
                    {
                        BuyWindow = false;
                        BuyMenuOpen = false;
                    }
                    if (!Player.Alive && BuyMenuOpen) BuyMenuOpen = false;
                    if (BombPlanted)
                    {
                        BombTimer -= dt;
                        Bomb.Tick(dt, BombTimer / MatchRules.BombTimer);
                        if (BombTimer <= 0f) ExplodeBomb();
                    }
                    break;

                case MatchPhase.RoundEnd:
                    PhaseTimer -= dt;
                    if (PhaseTimer <= 0f)
                    {
                        PhaseTimer = 0f;
                        AdvanceRound();
                    }
                    break;
            }
        }

        private void TickCursor(float dt)
        {
            _cursorCheck -= dt;
            if (_cursorCheck > 0f) return;
            _cursorCheck = 0.2f;

            bool wantLock = !Paused && !BuyMenuOpen && !TunerPanel.IsOpen && !GameSettings.TouchMode;
            if (wantLock)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            else if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // ------------------------------------------------------------------ round end
        private static Team WinnerOf(RoundEndReason r)
        {
            if (r == RoundEndReason.BombExploded || r == RoundEndReason.CTsEliminated) return Team.Terrorists;
            return Team.CTs;
        }

        public void EndRound(RoundEndReason reason)
        {
            if (Phase == MatchPhase.RoundEnd || Phase == MatchPhase.MatchOver) return;

            LastReason = reason;
            Phase = MatchPhase.RoundEnd;
            PhaseTimer = MatchRules.IntermissionTime;
            BuyWindow = false;
            BuyMenuOpen = false;
            _playerSurvived = Player.Alive;

            Team winner = WinnerOf(reason);
            if (winner == Team.Terrorists) ScoreT++; else ScoreCT++;

            int reward;
            if (winner == Player.Team)
            {
                reward = Economy.WinReward(reason);
                if (Player.Team == Team.Terrorists) _lossStreakT = 0; else _lossStreakCT = 0;
            }
            else
            {
                int streak = (Player.Team == Team.Terrorists ? _lossStreakT : _lossStreakCT) + 1;
                if (streak > 5) streak = 5;
                reward = Economy.LossReward(streak);
                if (Player.Team == Team.Terrorists) _lossStreakT = streak; else _lossStreakCT = streak;
            }
            Money = Economy.ClampMoney(Money + reward);

            Banner = BannerText(reason, winner);
            BannerTime = MatchRules.IntermissionTime + 1.5f;

            if (GzAudio.I != null)
            {
                AudioClip clip = winner == Player.Team ? GzAudio.I.Win : GzAudio.I.Lose;
                GzAudio.I.Play(clip, Player.CamTf != null ? Player.CamTf.position : Player.FeetPos, 0.8f, 1f);
            }

            Debug.Log("[GreyZone] round " + Round + " end: " + reason + " -> " + winner);
        }

        private static string BannerText(RoundEndReason reason, Team winner)
        {
            string who = winner == Team.Terrorists ? "TERRORISTS WIN" : "CT WIN";
            switch (reason)
            {
                case RoundEndReason.BombExploded: return "BOMB DETONATED - " + who;
                case RoundEndReason.BombDefused: return "BOMB DEFUSED - " + who;
                case RoundEndReason.TerroristsEliminated: return "TERRORISTS ELIMINATED - " + who;
                case RoundEndReason.CTsEliminated: return "CT ELIMINATED - " + who;
                default: return "TIME EXPIRED - " + who;
            }
        }

        private void AdvanceRound()
        {
            if (ScoreT >= MatchRules.RoundsToWin || ScoreCT >= MatchRules.RoundsToWin)
            {
                Phase = MatchPhase.MatchOver;
                Banner = ScoreT >= MatchRules.RoundsToWin ? "TERRORISTS TAKE THE MATCH" : "CT TAKE THE MATCH";
                BannerTime = 999f;
                return;
            }

            if (Round == MatchRules.RoundsPerHalf && _half == 0) SwapSides();

            Round++;
            StartRound();
        }

        private void SwapSides()
        {
            _half = 1;
            GameSettings.PlayerTeam = GameSettings.PlayerTeam == Team.Terrorists ? Team.CTs : Team.Terrorists;
            GameSettings.SaveOptions();
            Money = Economy.StartMoney;
            _lossStreakT = 0;
            _lossStreakCT = 0;
            Banner = "HALF TIME - SWAPPING SIDES";
            BannerTime = 2.5f;
        }

        // ------------------------------------------------------------------ bomb
        public void PlantBomb(Actor planter, Vector3 pos, BombSite site)
        {
            if (BombPlanted || Phase != MatchPhase.Live) return;
            BombPlanted = true;
            BombPos = pos;
            BombTimer = MatchRules.BombTimer;
            Bomb.Place(pos);
            if (planter != null) planter.HasBomb = false;

            if (planter == Player) Money = Economy.ClampMoney(Money + Economy.PlantBonusPlanter);
            if (planter != null && planter.Team == Player.Team) Money = Economy.ClampMoney(Money + Economy.PlantBonusTeam);

            Banner = "BOMB HAS BEEN PLANTED";
            BannerTime = 3.5f;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Plant, pos, 0.9f, 1f);
            Debug.Log("[GreyZone] bomb planted at " + pos + " by " + (planter != null ? planter.Name : "?"));
        }

        public void DefuseBomb(Actor defuser)
        {
            if (!BombPlanted) return;
            BombPlanted = false;
            Bomb.Done = true;
            Bomb.SetVisible(false);

            if (defuser == Player) Money = Economy.ClampMoney(Money + Economy.DefuseBonusDefuser);
            // no team bonus on defuse: the defusing team is paid through the round-win reward

            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Defuse, BombPos, 0.9f, 1f);
            EndRound(RoundEndReason.BombDefused);
        }

        private void ExplodeBomb()
        {
            BombPlanted = false;
            Bomb.Exploded();
            Vector3 p = BombPos;
            FxSystem.I.Explosion(p);
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Explosion, p, 1f, 1f);

            float radius = MatchRules.BombDamageRadius;
            for (int i = 0; i < _actorCount; i++)
            {
                Actor a = _actors[i];
                if (a == null || !a.Alive) continue;
                Vector3 d = a.FeetPos - p;
                float dist = d.magnitude;
                if (dist > radius) continue;
                int dmg = Mathf.RoundToInt(MatchRules.BombDamageMax * (1f - dist / radius));
                if (dmg > 0) a.TakeDamage(dmg, 1f, HitGroup.Chest, null, null, false);
            }
            EndRound(RoundEndReason.BombExploded);
        }

        // ------------------------------------------------------------------ events
        public void OnActorDeath(Actor victim, Actor killer, WeaponDef weapon, bool headshot)
        {
            AddFeed(victim, killer, weapon, headshot);

            if (killer != null && killer != victim && weapon != null)
            {
                if (killer.IsPlayer) Money = Economy.ClampMoney(Money + weapon.KillReward);
                Bots.AlertTeam(killer.Team, victim.FeetPos, 25f);
            }
            Bots.AlertTeam(victim.Team, victim.FeetPos, 35f);

            if (victim.HasBomb)
            {
                victim.HasBomb = false;
                TransferBomb(victim.Team, victim);
            }

            CheckEliminations();
        }

        public void CheckEliminations()
        {
            if (Phase != MatchPhase.Live) return;
            if (AliveOn(Team.Terrorists) == 0 && !BombPlanted)
            {
                EndRound(RoundEndReason.TerroristsEliminated);
                return;
            }
            if (AliveOn(Team.CTs) == 0)
            {
                EndRound(RoundEndReason.CTsEliminated);
            }
        }

        public void PlayerHitFeedback(bool headshot)
        {
            HitMarkerTime = GameClock.Now;
            HitMarkerHead = headshot;
            if (GzAudio.I != null)
                GzAudio.I.Play(headshot ? GzAudio.I.Headshot : GzAudio.I.HitMarker, Player.CamTf.position, 0.55f, 1f);
        }

        public void ReportGunshot(Vector3 pos, Team shooterTeam)
        {
            Team enemy = shooterTeam == Team.Terrorists ? Team.CTs : Team.Terrorists;
            Bots.AlertTeam(enemy, pos, 42f);
        }

        public void NotifyFootstep(Vector3 pos, Team team)
        {
            Team enemy = team == Team.Terrorists ? Team.CTs : Team.Terrorists;
            Bots.AlertTeam(enemy, pos, 16f);
        }

        private void AddFeed(Actor victim, Actor killer, WeaponDef weapon, bool headshot)
        {
            string k = killer == null ? "WORLD" : killer.Name;
            string v = victim == null ? "?" : victim.Name;
            string txt = k + "  >  " + v;
            for (int i = Feed.Length - 1; i > 0; i--) Feed[i] = Feed[i - 1];
            Feed[0].Text = txt;
            Feed[0].Time = GameClock.Now;
            Feed[0].Headshot = headshot;
            Feed[0].KillerIsPlayer = killer != null && killer.IsPlayer;
            if (FeedCount < Feed.Length) FeedCount++;
        }

        // ------------------------------------------------------------------ ui
        public bool CanAct { get { return !Paused && Phase == MatchPhase.Live && Player.Alive; } }

        public bool Frozen { get { return Phase == MatchPhase.Freeze; } }

        public bool MatchOver { get { return Phase == MatchPhase.MatchOver; } }

        public void TogglePause()
        {
            if (BuyMenuOpen)
            {
                BuyMenuOpen = false;
                return;
            }
            Paused = !Paused;
            if (Paused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void ToggleBuy()
        {
            if (!BuyWindow || !Player.Alive) return;
            BuyMenuOpen = !BuyMenuOpen;
            if (BuyMenuOpen && !GameSettings.TouchMode)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void CloseBuy()
        {
            BuyMenuOpen = false;
        }

        // ------------------------------------------------------------------ purchasing
        public bool TryBuyWeapon(WeaponDef def)
        {
            if (def == null || !BuyWindow || !Player.Alive) return false;
            if (Player.Slots[0] == def || Player.Slots[1] == def) return false;
            if (!Economy.CanAfford(Money, def.Price)) return false;
            Money -= def.Price;
            bool primary = def.Kind != WeaponKind.Pistol;
            Player.GiveWeapon(def, primary);
            Player.SelectSlot(primary ? 0 : 1);
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Click, Player.CamTf.position, 0.6f, 1.1f);
            return true;
        }

        public bool TryBuyArmor()
        {
            if (!BuyWindow || !Player.Alive) return false;
            if (Player.Armor > 0) return false;
            if (!Economy.CanAfford(Money, Economy.ArmorPrice)) return false;
            Money -= Economy.ArmorPrice;
            Player.Armor = Ballistics.MaxArmor;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Click, Player.CamTf.position, 0.6f, 1f);
            return true;
        }

        public bool TryBuyHelmet()
        {
            if (!BuyWindow || !Player.Alive) return false;
            if (Player.HasHelmet) return false;
            if (Player.Armor > 0)
            {
                if (!Economy.CanAfford(Money, Economy.HelmetPrice)) return false;
                Money -= Economy.HelmetPrice;
            }
            else
            {
                int total = Economy.ArmorPrice + Economy.HelmetPrice;
                if (!Economy.CanAfford(Money, total)) return false;
                Money -= total;
                Player.Armor = Ballistics.MaxArmor;
            }
            Player.HasHelmet = true;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Click, Player.CamTf.position, 0.6f, 1f);
            return true;
        }

        public bool TryBuyKit()
        {
            if (!BuyWindow || !Player.Alive) return false;
            if (Player.Team != Team.CTs || Player.HasKit) return false;
            if (!Economy.CanAfford(Money, Economy.DefuseKitPrice)) return false;
            Money -= Economy.DefuseKitPrice;
            Player.HasKit = true;
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Click, Player.CamTf.position, 0.6f, 1f);
            return true;
        }
    }
}
