// GreyZone - shared actor layer: capsule + hitboxes + health/armour + Core PlayerMover stepping.
// Player and bots both use this; there is no Rigidbody and no Animator anywhere.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    /// <summary>Marker on the solid (non trigger) body capsule so bullets can skip it.</summary>
    public class ActorBody : MonoBehaviour
    {
        public Actor Actor;
    }

    /// <summary>Marker on the three trigger hitboxes that resolve the damage group.</summary>
    public class BotHitbox : MonoBehaviour
    {
        public Actor Owner;
        public HitGroup Group;
    }

    public class Actor : MonoBehaviour
    {
        public string Name = "bot";
        public Team Team = Team.Terrorists;
        public int Health = Ballistics.MaxHealth;
        public int Armor;
        public bool HasHelmet;
        public bool HasKit;
        public bool Alive = true;

        public CapsuleCollider Capsule;
        public Transform Visual;
        public Transform UpperTf;
        public MeshRenderer LegsRend;
        public MeshRenderer UpperRend;

        public Transform HeadBox;
        public Transform ChestBox;
        public Transform LegsBox;

        public readonly ActorWorld World = new ActorWorld();
        public MoveState Move;
        public MoveConfig Cfg;

        // two weapon slots: 0 = primary, 1 = secondary
        public readonly WeaponDef[] Slots = new WeaponDef[2];
        public readonly WeaponState[] States = new WeaponState[2];
        public int Slot;
        public bool HasBomb;

        public float SpottedTime = -999f;      // radar: last time a friendly spotted this actor
        public float LastDamageTime = -999f;
        public float LastFireTime = -999f;
        public float LastNoiseTime = -999f;
        public Vector3 LastNoisePos;
        public bool IsPlayer;

        private Transform _shadowTf;
        private float _deathStart = -1f;
        private bool _hidden;
        private float _lastDuck = -1f;
        protected float _yaw;

        private const float DeathFallTime = 0.45f;
        private const float CorpseTime = 3.0f;

        // ------------------------------------------------------------------ setup
        public virtual void Init(Team team)
        {
            Team = team;
            gameObject.layer = GZ.ActorLayer;

            Capsule = gameObject.AddComponent<CapsuleCollider>();
            Capsule.direction = 1;
            Capsule.radius = PlayerMover.PlayerRadius;
            Capsule.height = 1.83f;
            Capsule.center = new Vector3(0f, 0.915f, 0f);
            Capsule.isTrigger = false;
            Capsule.enabled = true;
            ActorBody body = gameObject.AddComponent<ActorBody>();
            body.Actor = this;
            World.Bind(Capsule);

            BuildVisual();
            BuildHitboxes();

            Move = new MoveState();
            Move.Position = GZ.C3(transform.position);
            Move.OnGround = true;
            Move.JumpQueued = false;
            Cfg = GameSettings.Move;
        }

        private void BuildVisual()
        {
            GameObject visual = new GameObject("Visual");
            visual.layer = GZ.ActorLayer;
            visual.transform.SetParent(transform, false);
            Visual = visual.transform;

            GameObject legs = new GameObject("legs");
            legs.layer = GZ.ActorLayer;
            legs.transform.SetParent(Visual, false);
            legs.transform.localPosition = Vector3.zero;
            MeshFilter lf = legs.AddComponent<MeshFilter>();
            LegsRend = legs.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(LegsRend, ProcAssets.ActorMat);
            lf.sharedMesh = ProcAssets.ActorLegsT;

            GameObject upper = new GameObject("upper");
            upper.layer = GZ.ActorLayer;
            upper.transform.SetParent(Visual, false);
            upper.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            UpperTf = upper.transform;
            MeshFilter uf = upper.AddComponent<MeshFilter>();
            UpperRend = upper.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(UpperRend, ProcAssets.ActorMat);
            uf.sharedMesh = ProcAssets.ActorUpperT;

            ApplyTeamVisual();

            if (FxSystem.I != null) _shadowTf = FxSystem.I.AddShadow(transform);
        }

        public void SetTeam(Team team)
        {
            Team = team;
            ApplyTeamVisual();
        }

        protected void ApplyTeamVisual()
        {
            bool ct = Team == Team.CTs;
            Transform legs = Visual.GetChild(0);
            Transform upper = Visual.GetChild(1);
            legs.GetComponent<MeshFilter>().sharedMesh = ct ? ProcAssets.ActorLegsCT : ProcAssets.ActorLegsT;
            upper.GetComponent<MeshFilter>().sharedMesh = ct ? ProcAssets.ActorUpperCT : ProcAssets.ActorUpperT;
        }

        private void BuildHitboxes()
        {
            HeadBox = MakeBox("hb_head", new Vector3(0f, 1.65f, 0f), new Vector3(0.26f, 0.26f, 0.26f), HitGroup.Head, 1);
            ChestBox = MakeBox("hb_chest", new Vector3(0f, 1.30f, 0f), new Vector3(0.5f, 0.6f, 0.35f), HitGroup.Chest, 0);
            LegsBox = MakeBox("hb_legs", new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.85f, 0.3f), HitGroup.Leg, 0);
        }

        private Transform MakeBox(string name, Vector3 local, Vector3 size, HitGroup group, int sphere)
        {
            GameObject go = new GameObject(name);
            go.layer = GZ.ActorLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            if (sphere == 1)
            {
                SphereCollider sc = go.AddComponent<SphereCollider>();
                sc.radius = size.x * 0.5f;
                sc.isTrigger = true;
            }
            else
            {
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.size = size;
                bc.isTrigger = true;
            }
            BotHitbox hb = go.AddComponent<BotHitbox>();
            hb.Owner = this;
            hb.Group = group;
            return go.transform;
        }

        // ------------------------------------------------------------------ state
        public float CurrentHeight { get { return PlayerMover.CurrentHeight(ref Cfg, Move); } }

        public float EyeHeight { get { return CurrentHeight - GZ.EyeOffset; } }

        public Vector3 FeetPos
        {
            get { Vec3 p = Move.Position; return new Vector3(p.x, p.y, p.z); }
        }

        public Vector3 EyePos
        {
            get { Vec3 p = Move.Position; return new Vector3(p.x, p.y + EyeHeight, p.z); }
        }

        public float ViewYaw
        {
            get { return _yaw; }
            set { _yaw = value; }
        }

        public WeaponDef Def { get { return Slots[Slot]; } }

        public bool HasAnyWeapon { get { return Slots[0] != null || Slots[1] != null; } }

        public void GiveWeapon(WeaponDef def, bool primary)
        {
            int ix = primary ? 0 : 1;
            Slots[ix] = def;
            States[ix] = WeaponLogic.CreateState(def);
        }

        public void ClearWeapons()
        {
            Slots[0] = null;
            Slots[1] = null;
            Slot = 0;
        }

        public void SelectSlot(int ix)
        {
            if (ix < 0 || ix > 1) return;
            if (Slots[ix] == null) return;
            if (Slot == ix) return;
            Slot = ix;
        }

        /// <summary>Keeps the capsule and hitboxes aligned with the Core duck fraction.</summary>
        protected void ApplyTransform()
        {
            Vec3 p = Move.Position;
            transform.position = new Vector3(p.x, p.y, p.z);

            float f = Move.DuckFraction;
            if (Mathf.Abs(f - _lastDuck) > 0.01f) { _lastDuck = f; UpdateBodyShape(f); }
        }

        private void UpdateBodyShape(float f)
        {
            float h = Mathf.Lerp(Cfg.StandHeight, Cfg.DuckHeight, f);
            if (Capsule != null)
            {
                Capsule.height = h;
                Capsule.center = new Vector3(0f, h * 0.5f, 0f);
            }
            float drop = (Cfg.StandHeight - Cfg.DuckHeight) * f;
            if (HeadBox != null) HeadBox.localPosition = new Vector3(0f, 1.65f - drop * 0.95f, 0f);
            if (ChestBox != null) ChestBox.localPosition = new Vector3(0f, 1.30f - drop * 0.8f, 0f);
            if (LegsBox != null) LegsBox.localPosition = new Vector3(0f, 0.45f - drop * 0.15f, 0f);
        }

        /// <summary>One fixed movement tick driven by GreyZone.Core.</summary>
        public void StepMove(MoveInput input, float weaponSpeedMult, float dt)
        {
            Cfg = GameSettings.Move;
            PlayerMover.Step(ref Move, ref Cfg, input, weaponSpeedMult, World, dt);
            ApplyTransform();
        }

        public void StepMove(MoveInput input, float weaponSpeedMult, float dt, MoveConfig cfg)
        {
            PlayerMover.Step(ref Move, ref cfg, input, weaponSpeedMult, World, dt);
            Cfg = GameSettings.Move;
            ApplyTransform();
        }

        public float SpeedRatio01
        {
            get
            {
                float s = Mathf.Sqrt(Move.Velocity.x * Move.Velocity.x + Move.Velocity.z * Move.Velocity.z);
                float m = Mathf.Max(0.001f, Cfg.MaxSpeed * (Def == null ? 1f : Def.MoveSpeedMultiplier));
                return Mathf.Clamp01(s / m);
            }
        }

        // ------------------------------------------------------------------ combat
        public void TakeDamage(int baseDamage, float armorPen, HitGroup group, Actor attacker, WeaponDef weapon, bool headshot)
        {
            if (!Alive) return;

            int dmg = Mathf.RoundToInt(baseDamage * Ballistics.HitGroupMultiplier(group));
            int armorDamage;
            int applied;
            // body armour never protects a bare head in CS - a helmet is required for head hits
            bool armorApplies = Armor > 0 && (group != HitGroup.Head || HasHelmet);
            if (armorApplies)
            {
                applied = Ballistics.ApplyArmor(dmg, armorPen, ref Armor, out armorDamage);
            }
            else
            {
                armorDamage = 0;
                applied = dmg;
            }
            if (applied < 1) applied = 1;
            Health -= applied;
            LastDamageTime = GameClock.Now;
            if (AttackerIsPlayer(attacker)) GameDirector.I.PlayerHitFeedback(headshot);

            if (Health <= 0)
            {
                Health = 0;
                Die(attacker, weapon, headshot);
            }
        }

        private static bool AttackerIsPlayer(Actor a)
        {
            return a != null && a.IsPlayer;
        }

        public virtual void Die(Actor killer, WeaponDef weapon, bool headshot)
        {
            if (!Alive) return;
            Alive = false;
            Health = 0;
            _deathStart = GameClock.Now;
            _hidden = false;

            Capsule.enabled = false;
            if (HeadBox != null) HeadBox.gameObject.SetActive(false);
            if (ChestBox != null) ChestBox.gameObject.SetActive(false);
            if (LegsBox != null) LegsBox.gameObject.SetActive(false);
            if (_shadowTf != null) _shadowTf.gameObject.SetActive(false);

            if (FxSystem.I != null) FxSystem.I.Impact(transform.position + Vector3.up * 1.1f, Vector3.up, true);
            if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.Death, transform.position, 0.7f, 1f);

            if (GameDirector.I != null) GameDirector.I.OnActorDeath(this, killer, weapon, headshot);
        }

        public virtual void Respawn(Vector3 pos, float yaw)
        {
            Alive = true;
            Health = Ballistics.MaxHealth;
            Armor = 0;
            HasHelmet = false;
            HasKit = false;
            HasBomb = false;
            _deathStart = -1f;
            _hidden = false;
            _lastDuck = -1f;

            Visual.gameObject.SetActive(true);
            Visual.localRotation = Quaternion.identity;
            if (_shadowTf != null) _shadowTf.gameObject.SetActive(true);
            Capsule.enabled = true;
            if (HeadBox != null) HeadBox.gameObject.SetActive(true);
            if (ChestBox != null) ChestBox.gameObject.SetActive(true);
            if (LegsBox != null) LegsBox.gameObject.SetActive(true);

            Move = new MoveState();
            Move.Position = GZ.C3(pos);
            Move.OnGround = true;
            Cfg = GameSettings.Move;
            _yaw = yaw;
            UpdateBodyShape(0f);
            ApplyTransform();
            ViewYaw = yaw;
        }

        public void SetFrozenPose(Vector3 pos, float yaw)
        {
            Move.Velocity = Vec3.Zero;
            Move.Position = GZ.C3(pos);
            _yaw = yaw;
            ApplyTransform();
        }

        /// <summary>Body/weapon facing used by the blocky character meshes.</summary>
        protected void UpdateVisualAim(float aimYaw)
        {
            if (UpperTf == null) return;
            UpperTf.localRotation = Quaternion.Euler(0f, GZ.WrapAngle(aimYaw - _yaw), 0f);
        }

        protected virtual void Update()
        {
            if (!Alive)
            {
                TickDeath();
                return;
            }
            UpdateVisualAim(_yaw);
        }

        private void TickDeath()
        {
            if (_deathStart < 0f) return;
            float t = (GameClock.Now - _deathStart) / DeathFallTime;
            if (Visual != null)
            {
                if (t < 1f)
                {
                    Visual.localRotation = Quaternion.Euler(-90f * Mathf.Clamp01(t), 0f, 0f);
                }
                else if (!_hidden && GameClock.Now - _deathStart > CorpseTime)
                {
                    _hidden = true;
                    Visual.gameObject.SetActive(false);
                }
            }
        }

        protected void OnDestroy()
        {
            if (FxSystem.I != null) FxSystem.I.RemoveShadow(transform);
        }
    }
}
