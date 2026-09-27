// GreyZone - Unity side physics: ICollisionWorld for GreyZone.Core movement + shared non-alloc hitscan.
// Everything here runs with Physics.autoSimulation == false: queries only, no rigidbodies anywhere.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    /// <summary>
    /// Capsule sweep / overlap world for one actor. The actor's own solid capsule is switched off for
    /// the duration of every query so it never collides with itself; triggers (hitboxes, bomb sites)
    /// are ignored, which keeps the terrain boxes and the other actors' capsules solid.
    /// </summary>
    public sealed class ActorWorld : ICollisionWorld
    {
        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private readonly Collider[] _overlaps = new Collider[8];
        private Collider _self;

        public void Bind(Collider self) { _self = self; }

        public SweepHit SweepCapsule(Vec3 feetPos, float radius, float height, Vec3 delta)
        {
            SweepHit r = new SweepHit();
            r.Hit = false;
            r.Fraction = 1f;
            r.Normal = Vec3.Up;

            float dist = delta.Length();
            if (dist < 1e-5f) return r;

            float rad = Mathf.Max(0.05f, radius);
            float h = Mathf.Max(rad * 2f + 0.02f, height);
            Vector3 p1 = new Vector3(feetPos.x, feetPos.y + rad, feetPos.z);
            Vector3 p2 = new Vector3(feetPos.x, feetPos.y + h - rad, feetPos.z);
            Vector3 dir = new Vector3(delta.x, delta.y, delta.z) / dist;

            bool wasEnabled = true;
            if (_self != null) { wasEnabled = _self.enabled; _self.enabled = false; }
            int n = Physics.CapsuleCastNonAlloc(p1, p2, rad, dir, _hits, dist, GZ.SolidMask, QueryTriggerInteraction.Ignore);
            if (_self != null) _self.enabled = wasEnabled;

            float best = dist;
            Vector3 bestN = Vector3.up;
            bool blocked = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit hh = _hits[i];
                if (hh.collider == null) continue;
                if (hh.distance <= 1e-4f)
                {
                    // Already touching / overlapping: only a hard stop when the surface actually
                    // faces the sweep. A touching floor during a horizontal sweep must not freeze
                    // movement, while a touching wall must block and feed the mover a real normal
                    // so sliding and step-up keep working.
                    Vector3 nn = hh.normal;
                    if (nn.sqrMagnitude < 0.25f) nn = -dir;
                    if (Vector3.Dot(nn, dir) >= -0.01f) continue;
                    r.Hit = true;
                    r.Fraction = 0f;
                    r.Normal = GZ.C3(nn);
                    return r;
                }
                if (hh.distance < best)
                {
                    best = hh.distance;
                    bestN = hh.normal;
                    blocked = true;
                }
            }

            if (!blocked) return r;
            best = Mathf.Max(0f, best - 0.001f);
            r.Hit = true;
            r.Fraction = Mathf.Clamp01(best / dist);
            r.Normal = GZ.C3(bestN);
            return r;
        }

        public bool OverlapCapsule(Vec3 feetPos, float radius, float height)
        {
            float rad = Mathf.Max(0.05f, radius);
            float h = Mathf.Max(rad * 2f + 0.02f, height);
            Vector3 p1 = new Vector3(feetPos.x, feetPos.y + rad + 0.02f, feetPos.z);
            Vector3 p2 = new Vector3(feetPos.x, feetPos.y + h - rad, feetPos.z);

            bool wasEnabled = true;
            if (_self != null) { wasEnabled = _self.enabled; _self.enabled = false; }
            bool over = Physics.CheckCapsule(p1, p2, rad - 0.02f, GZ.SolidMask, QueryTriggerInteraction.Ignore);
            if (_self != null) _self.enabled = wasEnabled;
            return over;
        }
    }

    /// <summary>Result of one hitscan bullet trace.</summary>
    public struct ShotHit
    {
        public bool Valid;
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public Actor Victim;
        public HitGroup Group;
        public bool Head;
    }

    /// <summary>Shared bullet trace. Nearest non-trigger hit wins; actor capsules are skipped so the
    /// inner hitboxes decide the damage group, every other trigger (bomb sites) is ignored.</summary>
    public static class Combat
    {
        private static readonly RaycastHit[] Buf = new RaycastHit[24];

        public static bool Fire(Vector3 origin, Vector3 dir, float range, Actor shooter, out ShotHit hit)
        {
            hit = new ShotHit();
            hit.Group = HitGroup.Chest;
            hit.Point = origin + dir * range;
            hit.Normal = -dir;
            hit.Distance = range;

            int n = Physics.RaycastNonAlloc(new Ray(origin, dir), Buf, range, GZ.SolidMask, QueryTriggerInteraction.Collide);
            float best = range + 1f;
            int bestIx = -1;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Buf[i];
                Collider c = h.collider;
                if (c == null) continue;
                if (c.GetComponent<ActorBody>() != null) continue;      // body capsule: let hitboxes answer
                BotHitbox box = c.GetComponent<BotHitbox>();
                if (box == null)
                {
                    if (c.isTrigger) continue;                          // bomb site volumes etc.
                }
                else
                {
                    Actor owner = box.Owner;
                    if (owner == null || !owner.Alive || owner == shooter) continue;
                }
                if (h.distance < best)
                {
                    best = h.distance;
                    bestIx = i;
                }
            }

            if (bestIx < 0) { hit.Valid = false; return false; }

            RaycastHit bh = Buf[bestIx];
            hit.Valid = true;
            hit.Point = bh.point;
            hit.Normal = bh.normal;
            hit.Distance = bh.distance;
            BotHitbox hb = bh.collider.GetComponent<BotHitbox>();
            if (hb != null && hb.Owner != null && hb.Owner.Alive)
            {
                hit.Victim = hb.Owner;
                hit.Group = hb.Group;
                hit.Head = hb.Group == HitGroup.Head;
            }
            return true;
        }

        /// <summary>Line of sight between two points (used by bot perception). Non-alloc.</summary>
        public static bool LineOfSight(Vector3 from, Vector3 to, Actor ignoreA, Actor ignoreB)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            int n = Physics.RaycastNonAlloc(new Ray(from, d / dist), Buf, dist - 0.05f, GZ.SolidMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Buf[i];
                Collider c = h.collider;
                if (c == null) continue;
                BotHitbox box = c.GetComponent<BotHitbox>();
                if (box != null)
                {
                    Actor o = box.Owner;
                    if (o == ignoreA || o == ignoreB) continue;
                    return false;
                }
                if (c.GetComponent<ActorBody>() != null) continue;
                if (c.isTrigger) continue;
                return false;
            }
            return true;
        }
    }
}
