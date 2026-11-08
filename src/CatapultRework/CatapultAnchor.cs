using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Holds a catapult still while its legs are down. Vanilla only raises its mass, so the arm swing,
    /// players bumping into it and physics jitter make it creep a little with every shot, which ruins
    /// aiming. Here the owner freezes the rigidbody once the legs have settled; other clients simply
    /// follow the synced position.
    ///
    /// The ground under the legs is sampled periodically: if it changes (digging, raising, a floor
    /// destroyed under it) physics is re-enabled until the catapult settles again, then it is re-frozen.
    /// </summary>
    public class CatapultAnchor : MonoBehaviour
    {
        private const float SettleDelay = 1f;        // after the legs finish moving
        private const float GroundCheckInterval = 0.5f;
        private const float GroundTolerance = 0.1f;  // metres
        private const float RestSpeed = 0.05f;
        private const float MaxSettleTime = 6f;

        private Catapult _catapult;
        private Rigidbody _body;
        private RigidbodyConstraints _originalConstraints;
        private bool _frozen;
        private bool _settling;
        private float _settleStart;
        private float _readySince = -1f;
        private float _nextGroundCheck;
        private float[] _groundRef;
        private static int s_groundMask;

        internal void Init(Catapult c)
        {
            _catapult = c;
            _body = c.m_rigidBody != null ? c.m_rigidBody : c.GetComponent<Rigidbody>();
            if (_body != null) _originalConstraints = _body.constraints;
            if (s_groundMask == 0)
                s_groundMask = LayerMask.GetMask("terrain", "static_solid", "Default", "Default_small", "piece", "vehicle");
        }

        private bool ShouldHold()
        {
            if (_catapult == null || _body == null || !ModConfig.AnchorWhenLegsDown.Value) return false;
            ZNetView nv = _catapult.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return false;
            return _catapult.m_lockedLegs && !_catapult.m_movingLegs;
        }

        private void FixedUpdate()
        {
            if (!ShouldHold())
            {
                _readySince = -1f;
                _settling = false;
                if (_frozen) Release("legs raised, ownership moved or anchoring disabled");
                return;
            }

            float now = Time.time;
            if (_readySince < 0f) _readySince = now;

            if (_settling)
            {
                bool atRest = _body.linearVelocity.magnitude < RestSpeed && _body.angularVelocity.magnitude < RestSpeed;
                if ((now - _settleStart > SettleDelay && atRest) || now - _settleStart > MaxSettleTime)
                {
                    _settling = false;
                    Freeze();
                }
                return;
            }

            if (!_frozen)
            {
                if (now - _readySince >= SettleDelay) Freeze();
                return;
            }

            if (now >= _nextGroundCheck)
            {
                _nextGroundCheck = now + GroundCheckInterval;
                if (GroundChanged(out string where))
                {
                    Release("ground changed under the " + where);
                    _settling = true;
                    _settleStart = now;
                }
            }
        }

        private void Freeze()
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.constraints = RigidbodyConstraints.FreezeAll;
            _frozen = true;
            _groundRef = SampleGround();
            _nextGroundCheck = Time.time + GroundCheckInterval;
            ModLog.Debug($"Catapult anchored at {ModLog.F(_body.position)}");
        }

        private void Release(string reason)
        {
            if (_body != null)
            {
                _body.constraints = _originalConstraints;
                _body.WakeUp();
            }
            _frozen = false;
            ModLog.Debug("Catapult anchor released: " + reason);
        }

        private void OnDestroy()
        {
            if (_frozen && _body != null) _body.constraints = _originalConstraints;
        }

        // Ground height under each leg tip and under the body. NaN = nothing found.
        private float[] SampleGround()
        {
            int legs = _catapult.m_legs != null ? _catapult.m_legs.Count : 0;
            var h = new float[legs + 1];
            for (int i = 0; i < legs; i++)
            {
                Transform leg = _catapult.m_legs[i].transform;
                Vector3 tip = leg.childCount > 0 ? leg.GetChild(0).position : leg.position;
                h[i] = GroundAt(tip);
            }
            h[legs] = GroundAt(_body.worldCenterOfMass);
            return h;
        }

        private bool GroundChanged(out string where)
        {
            where = null;
            float[] now = SampleGround();
            if (_groundRef == null || _groundRef.Length != now.Length) { _groundRef = now; return false; }
            for (int i = 0; i < now.Length; i++)
            {
                float a = _groundRef[i], b = now[i];
                bool changed = float.IsNaN(a) != float.IsNaN(b) || (!float.IsNaN(a) && Mathf.Abs(a - b) > GroundTolerance);
                if (changed)
                {
                    where = i == now.Length - 1 ? "body" : "leg " + (i + 1);
                    return true;
                }
            }
            return false;
        }

        private float GroundAt(Vector3 p)
        {
            Vector3 from = p + Vector3.up * 1.5f;
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 6f, s_groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NaN, bestDist = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.attachedRigidbody == _body || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue; // players, carts, loose items
                if (hit.distance < bestDist) { bestDist = hit.distance; best = hit.point.y; }
            }
            return best;
        }
    }
}
