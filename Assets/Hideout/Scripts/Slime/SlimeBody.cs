using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Viscous soft-body slime: center Rigidbody2D + N perimeter nodes connected
    /// by a spring network with cross-bracing and internal pressure.
    ///
    /// Spring network topology (per node i):
    ///   Radial   — node i ↔ center     (maintains body radius)
    ///   Neighbor — node i ↔ node i+1   (ring integrity)
    ///   Brace    — node i ↔ node i+2   (prevents topological inversion on impact)
    ///
    /// Elasticity model:
    ///   springDamping (0.2) is underdamped — produces 2–3 natural oscillations
    ///   after impact before settling. linearDamping stays near zero so it does
    ///   not fight spring restoration. groundedDamping (1.0) provides viscous
    ///   settling AFTER the bounce completes, not during it.
    ///
    /// Structural integrity:
    ///   Pressure constraint prevents volume collapse by applying outward normal
    ///   forces proportional to how much polygon area has shrunk (gasAmount / area).
    ///   Cross-brace springs resist topological inversion that ring springs cannot.
    ///   Shape matching keeps nodes near rest positions at low constant strength.
    ///   Velocity clamping and NaN recovery prevent cascade decomposition.
    ///
    /// Zero per-frame GC allocation.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class SlimeBody : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Body Shape")]
        [Range(6, 24)]
        public int nodeCount = 12;
        public float bodyRadius     = 0.5f;
        public float colliderRadius = 0.05f;

        [Header("Springs")]
        [Tooltip("Radial spring frequency (node↔center). Safe range 3–12 Hz at 50 Hz physics.")]
        public float radialFrequency = 6f;
        [Tooltip("Neighbor/brace spring frequency (node↔node).")]
        public float neighborFrequency = 4f;
        [Range(0f, 1f)]
        [Tooltip("Spring damping ratio. 0.2 = underdamped (2–3 bounces). 1.0 = no overshoot.")]
        public float springDamping = 0.2f;

        [Header("Elasticity")]
        [Range(0f, 1f)]
        [Tooltip("Physics material bounciness. Additive to spring rebound. Keep ≤ 0.5.")]
        public float bounciness = 0.3f;

        [Header("Pressure")]
        [Tooltip("Target internal volume (polygon area). Higher = puffier resistance to compression.")]
        public float gasAmount = 1f;
        [Tooltip("Scales outward pressure force. Higher = harder to compress.")]
        public float pressureStrength = 8f;

        [Header("Shape Matching")]
        [Tooltip("Mild constant force toward rest shape. Keep low — pressure handles volume. 0.05–0.2.")]
        public float shapeMatchStrength = 0.15f;

        [Header("Spread")]
        [Tooltip("Rate rest lengths follow node stretch outward.")]
        public float spreadRate = 1.2f;
        [Tooltip("Rate rest lengths return to original when nodes compress.")]
        public float recoveryRate = 0.3f;
        [Tooltip("Maximum rest length as a multiplier of original.")]
        public float maxSpreadMultiplier = 1.5f;

        [Header("Mass")]
        public float centerMass    = 2f;
        public float perimeterMass = 0.5f;

        [Header("Gravity")]
        public float gravityScale = 1f;

        [Header("Damping — Airborne")]
        [Tooltip("Near-zero so spring oscillation is the only damping in freefall.")]
        public float airborneDamping       = 0.05f;
        public float airborneCenterDamping = 0.1f;

        [Header("Damping — Grounded")]
        [Tooltip("Low enough that spring bounce can still express. Viscous settling after the bounce.")]
        public float groundedDamping       = 1.0f;
        public float groundedCenterDamping = 1.5f;

        [Tooltip("How quickly linearDamping transitions. Low = bounce has room before settling kicks in.")]
        public float dampingTransitionSpeed = 4f;

        [Header("Angular Separation")]
        [Tooltip("Minimum angular gap between adjacent nodes as fraction of ideal spacing. 0.5–0.7.")]
        public float minAngularSeparation = 0.6f;
        [Tooltip("Force pushing nodes apart when gap falls below minimum.")]
        public float separationForce = 5f;

        [Header("Safety")]
        [Tooltip("Max speed any perimeter node can reach. Clamps velocity to prevent cascade decomposition.")]
        public float maxNodeSpeed = 20f;

        [Header("Debug")]
        public bool showGizmos = true;

        // ── Public ────────────────────────────────────────────────────────────

        public Vector2[] PerimeterPositions { get; private set; }

        public Vector2 CenterPosition => _centerBody != null
            ? (Vector2)_centerBody.position
            : (Vector2)transform.position;

        public Vector2 Velocity => _centerBody != null
            ? _centerBody.linearVelocity
            : Vector2.zero;

        /// <summary>0 = fully airborne, 1 = all nodes grounded. Smoothed over time.</summary>
        public float GroundedRatio { get; private set; }

        /// <summary>Centroid of all bodies. Cached once per FixedUpdate.</summary>
        public Vector2 Centroid { get; private set; }

        // ── Internal ──────────────────────────────────────────────────────────

        private Rigidbody2D       _centerBody;
        private Rigidbody2D[]     _perimeterBodies;
        private SpringJoint2D[]   _radialSprings;
        private SpringJoint2D[]   _neighborSprings;
        private SpringJoint2D[]   _braceSprings;
        private SlimeNodeContact[] _nodeContacts;

        private float[]   _radialRestDist;
        private float[]   _neighborRestDist;
        private Vector2[] _restOffsets;
        private Vector2[] _lastValidPositions;

        private float _perimeterDamping;
        private float _centerDamping;

        private PhysicsMaterial2D _physicsMaterial;
        private GameObject        _nodesParent;

        private const float CornerEscapeForce = 5f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            Physics2D.velocityIterations = 12;
            Physics2D.positionIterations = 6;

            BuildPhysicsMaterial();
            BuildBody();

            _perimeterDamping = airborneDamping;
            _centerDamping    = airborneCenterDamping;
        }

        private void FixedUpdate()
        {
            ClampVelocities();        // safety net before any force is applied

            CachePositions();         // physics → PerimeterPositions (NaN-guarded)
            CacheCentroid();
            CacheGroundedRatio();

            ApplyPressure();
            UpdateSpread();
            ApplyShapeMatching();
            EnforceAngularSeparation();
            UpdateDamping();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildPhysicsMaterial()
        {
            _physicsMaterial            = new PhysicsMaterial2D("SlimeMaterial");
            _physicsMaterial.friction   = 0f;
            _physicsMaterial.bounciness = bounciness;
        }

        private void BuildBody()
        {
            int n = nodeCount;

            PerimeterPositions  = new Vector2[n];
            _perimeterBodies    = new Rigidbody2D[n];
            _radialSprings      = new SpringJoint2D[n];
            _neighborSprings    = new SpringJoint2D[n];
            _braceSprings       = new SpringJoint2D[n];
            _nodeContacts       = new SlimeNodeContact[n];
            _radialRestDist     = new float[n];
            _neighborRestDist   = new float[n];
            _restOffsets        = new Vector2[n];
            _lastValidPositions = new Vector2[n];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _centerBody = CreateNode("Center", Vector2.zero, centerMass, airborneCenterDamping);

            float angleStep = 360f / n;
            for (int i = 0; i < n; i++)
            {
                float   angle  = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                _restOffsets[i]        = offset;
                _lastValidPositions[i] = (Vector2)transform.position + offset;
                _perimeterBodies[i]    = CreateNode($"Node_{i}", offset, perimeterMass, airborneDamping);

                _nodeContacts[i] = _perimeterBodies[i].gameObject.AddComponent<SlimeNodeContact>();
                _nodeContacts[i].Init(this, CornerEscapeForce, maxNodeSpeed);
            }

            for (int i = 0; i < n; i++)
            {
                // Radial: node ↔ center
                _radialSprings[i]  = AddSpring(_perimeterBodies[i].gameObject, _centerBody, bodyRadius, radialFrequency);
                _radialRestDist[i] = bodyRadius;

                // Neighbor: node i ↔ node i+1
                int   next  = (i + 1) % n;
                float nDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i]  = AddSpring(_perimeterBodies[i].gameObject, _perimeterBodies[next], nDist, neighborFrequency);
                _neighborRestDist[i] = nDist;

                // Brace: node i ↔ node i+2 — structural, fixed rest length
                int   skip  = (i + 2) % n;
                float bDist = Vector2.Distance(_restOffsets[i], _restOffsets[skip]);
                _braceSprings[i] = AddSpring(_perimeterBodies[i].gameObject, _perimeterBodies[skip], bDist, neighborFrequency);
                // brace rest distances are fixed — not tracked for spread
            }
        }

        private Rigidbody2D CreateNode(string nodeName, Vector2 localOffset, float mass, float damping)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(_nodesParent.transform);
            go.transform.localPosition = localOffset;
            go.layer = gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass                   = mass;
            rb.gravityScale           = gravityScale;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping          = damping;
            rb.angularDamping         = 5f;
            rb.interpolation          = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode              = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col           = go.AddComponent<CircleCollider2D>();
            col.radius         = colliderRadius;
            col.sharedMaterial = _physicsMaterial;

            return rb;
        }

        private SpringJoint2D AddSpring(GameObject from, Rigidbody2D to, float restLength, float frequency)
        {
            var spring = from.AddComponent<SpringJoint2D>();
            spring.connectedBody         = to;
            spring.distance              = restLength;
            spring.frequency             = frequency;
            spring.dampingRatio          = springDamping;
            spring.autoConfigureDistance = false;
            spring.enableCollision       = false;
            return spring;
        }

        // ── SENSE ─────────────────────────────────────────────────────────────

        private void CachePositions()
        {
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 pos = _perimeterBodies[i].position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y))
                {
                    // Solver produced garbage — reset node to last known good state
                    _perimeterBodies[i].position       = _lastValidPositions[i];
                    _perimeterBodies[i].linearVelocity = Vector2.zero;
                    PerimeterPositions[i]              = _lastValidPositions[i];
                }
                else
                {
                    PerimeterPositions[i]  = pos;
                    _lastValidPositions[i] = pos;
                }
            }
        }

        private void CacheCentroid()
        {
            Vector2 sum = _centerBody.position;
            for (int i = 0; i < nodeCount; i++)
                sum += PerimeterPositions[i];
            Centroid = sum / (nodeCount + 1);
        }

        private void CacheGroundedRatio()
        {
            int grounded = 0;
            for (int i = 0; i < nodeCount; i++)
                if (_nodeContacts[i].IsGrounded) grounded++;

            float target = grounded / (float)nodeCount;
            GroundedRatio = Mathf.MoveTowards(
                GroundedRatio, target,
                dampingTransitionSpeed * Time.fixedDeltaTime);
        }

        // ── SAFETY ────────────────────────────────────────────────────────────

        private void ClampVelocities()
        {
            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearVelocity =
                    Vector2.ClampMagnitude(_perimeterBodies[i].linearVelocity, maxNodeSpeed);
        }

        // ── PRESSURE ─────────────────────────────────────────────────────────
        // Computes current polygon area via shoelace, then applies outward normal
        // force per edge scaled by gasAmount / area. Force naturally increases
        // when the slime is compressed, mimicking internal fluid pressure.

        private void ApplyPressure()
        {
            int n = nodeCount;

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = PerimeterPositions[i];
                Vector2 b = PerimeterPositions[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            area = Mathf.Abs(area) * 0.5f;
            if (area < 0.001f) return;

            float pressure = gasAmount / area * pressureStrength;

            for (int i = 0; i < n; i++)
            {
                int     next    = (i + 1) % n;
                Vector2 edge    = PerimeterPositions[next] - PerimeterPositions[i];
                float   edgeLen = edge.magnitude;
                if (edgeLen < 0.0001f) continue;

                // Outward normal for CCW polygon: rotate edge 90° clockwise
                Vector2 normal = new Vector2(edge.y, -edge.x) / edgeLen;
                Vector2 force  = normal * (pressure * edgeLen * 0.5f);

                _perimeterBodies[i].AddForce(force);
                _perimeterBodies[next].AddForce(force);
            }
        }

        // ── SPREAD ────────────────────────────────────────────────────────────
        // Radial and neighbor rest lengths follow node stretch outward (up to cap)
        // and recover toward original when nodes compress.
        // Brace springs have fixed rest lengths — structural role only.

        private void UpdateSpread()
        {
            Vector2 center = _centerBody.position;
            float   dt     = Time.fixedDeltaTime;
            int     n      = nodeCount;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;

                float origR    = _radialRestDist[i];
                float currentR = _radialSprings[i].distance;
                float actualR  = Vector2.Distance(_perimeterBodies[i].position, center);
                _radialSprings[i].distance = actualR > currentR
                    ? Mathf.MoveTowards(currentR, Mathf.Min(actualR, origR * maxSpreadMultiplier), spreadRate * dt)
                    : Mathf.MoveTowards(currentR, origR, recoveryRate * dt);

                float origN    = _neighborRestDist[i];
                float currentN = _neighborSprings[i].distance;
                float actualN  = Vector2.Distance(_perimeterBodies[i].position, _perimeterBodies[next].position);
                _neighborSprings[i].distance = actualN > currentN
                    ? Mathf.MoveTowards(currentN, Mathf.Min(actualN, origN * maxSpreadMultiplier), spreadRate * dt)
                    : Mathf.MoveTowards(currentN, origN, recoveryRate * dt);
            }
        }

        // ── SHAPE MATCHING ────────────────────────────────────────────────────
        // Estimates body rotation from average angular drift, applies mild constant
        // force toward each node's rotated rest position. Prevents corner trapping.

        private void ApplyShapeMatching()
        {
            float totalAngle = 0f;
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 offset  = _perimeterBodies[i].position - Centroid;
                float   current = Mathf.Atan2(offset.y, offset.x);
                float   rest    = Mathf.Atan2(_restOffsets[i].y, _restOffsets[i].x);
                totalAngle += Mathf.DeltaAngle(rest * Mathf.Rad2Deg, current * Mathf.Rad2Deg);
            }

            float rot = totalAngle / nodeCount * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rot);
            float sin = Mathf.Sin(rot);

            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 r    = _restOffsets[i];
                Vector2 goal = Centroid + new Vector2(
                    r.x * cos - r.y * sin,
                    r.x * sin + r.y * cos);
                _perimeterBodies[i].AddForce(
                    (goal - _perimeterBodies[i].position) * shapeMatchStrength);
            }
        }

        // ── ANGULAR SEPARATION ────────────────────────────────────────────────

        private void EnforceAngularSeparation()
        {
            Vector2 center     = _centerBody.position;
            float   idealAngle = (2f * Mathf.PI) / nodeCount;
            float   minAngle   = idealAngle * minAngularSeparation;

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;

                Vector2 dirA = _perimeterBodies[i].position    - center;
                Vector2 dirB = _perimeterBodies[next].position - center;

                float diff = Mathf.DeltaAngle(
                    Mathf.Atan2(dirA.y, dirA.x) * Mathf.Rad2Deg,
                    Mathf.Atan2(dirB.y, dirB.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;

                if (Mathf.Abs(diff) < minAngle)
                {
                    float   violation = minAngle - Mathf.Abs(diff);
                    float   sign      = diff >= 0f ? 1f : -1f;
                    Vector2 tangA     = new Vector2(-dirA.normalized.y,  dirA.normalized.x);
                    Vector2 tangB     = new Vector2(-dirB.normalized.y,  dirB.normalized.x);

                    _perimeterBodies[i].AddForce(   -tangA * sign * violation * separationForce);
                    _perimeterBodies[next].AddForce( tangB * sign * violation * separationForce);
                }
            }
        }

        // ── DAMPING ───────────────────────────────────────────────────────────
        // MoveTowards-smoothed linearDamping per body type, driven by GroundedRatio.
        // Spring oscillation (from springDamping) runs its course; this transitions
        // slowly enough that a 2–3 cycle bounce completes before damping peaks.

        private void UpdateDamping()
        {
            float targetPerimeter = Mathf.Lerp(airborneDamping,       groundedDamping,       GroundedRatio);
            float targetCenter    = Mathf.Lerp(airborneCenterDamping, groundedCenterDamping, GroundedRatio);

            _perimeterDamping = Mathf.MoveTowards(
                _perimeterDamping, targetPerimeter,
                dampingTransitionSpeed * Time.fixedDeltaTime);
            _centerDamping = Mathf.MoveTowards(
                _centerDamping, targetCenter,
                dampingTransitionSpeed * Time.fixedDeltaTime);

            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearDamping = _perimeterDamping;
            _centerBody.linearDamping = _centerDamping;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void AddMovementForce(Vector2 force) =>
            _centerBody?.AddForce(force, ForceMode2D.Force);

        public void AddImpulse(Vector2 impulse) =>
            _centerBody?.AddForce(impulse, ForceMode2D.Impulse);

        // ── Gizmos ────────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showGizmos || Application.isPlaying) return;

            float     angleStep = 360f / nodeCount;
            Vector3[] pts       = new Vector3[nodeCount];

            Gizmos.color = Color.green;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                pts[i] = transform.position +
                         new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                Gizmos.DrawWireSphere(pts[i], colliderRadius);
                Gizmos.DrawLine(transform.position, pts[i]);
            }

            for (int i = 0; i < nodeCount; i++)
                Gizmos.DrawLine(pts[i], pts[(i + 1) % nodeCount]);

            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            for (int i = 0; i < nodeCount; i++)
                Gizmos.DrawLine(pts[i], pts[(i + 2) % nodeCount]);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.05f);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (_nodesParent     != null) Destroy(_nodesParent);
            if (_physicsMaterial != null) Destroy(_physicsMaterial);
        }
    }
}
