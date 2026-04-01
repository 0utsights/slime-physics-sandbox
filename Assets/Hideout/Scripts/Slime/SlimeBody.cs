using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Viscous soft-body slime with contact-aware damping.
    /// During freefall: low damping + high gravity = real acceleration.
    /// On landing: high damping = viscous settling.
    /// Shape matching at low stiffness prevents corner trapping.
    /// Dynamic rest lengths drive spreading behavior.
    /// Zero per-frame GC allocation.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class SlimeBody : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Body Shape")]
        [Range(6, 24)]
        public int nodeCount = 12;
        public float bodyRadius = 0.5f;
        public float colliderRadius = 0.05f;

        [Header("Springs")]
        public float radialFrequency = 6f;
        public float neighborFrequency = 2.5f;
        [Range(0f, 1f)]
        public float springDamping = 0.95f;

        [Header("Shape Matching")]
        [Tooltip("Keep low (0.05–0.5) — prevents corner trapping without fighting spread.")]
        public float shapeMatchStrength = 0.3f;

        [Header("Dynamic Rest Lengths")]
        public float spreadRate = 1.2f;
        public float recoveryRate = 0.3f;
        public float maxSpreadMultiplier = 1.5f;

        [Header("Mass")]
        public float centerMass = 3f;
        public float perimeterMass = 0.8f;

        [Header("Gravity")]
        [Tooltip("Higher = faster fall, more impact. 1 recommended.")]
        public float gravityScale = 1f;

        [Header("Damping — Airborne")]
        [Tooltip("Low damping during freefall so gravity can actually accelerate the slime.")]
        public float airborneDamping = 0.5f;
        public float airborneCenterDamping = 0.3f;

        [Header("Damping — Grounded")]
        [Tooltip("High damping when settled for viscous feel.")]
        public float groundedDamping = 6f;
        public float groundedCenterDamping = 3f;

        [Header("Angular Separation")]
        [Tooltip("Minimum angular gap between nodes as fraction of ideal. 0.5–0.7 recommended.")]
        public float minAngularSeparation = 0.6f;
        [Tooltip("Force pushing nodes apart when too close angularly.")]
        public float separationForce = 5f;

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

        /// <summary>0 = fully airborne, 1 = fully grounded. Used by SlimeMesh.</summary>
        public float GroundedRatio { get; private set; }

        // ── Internal ─────────────────────────────────────────────────────────

        private Rigidbody2D _centerBody;
        private Rigidbody2D[] _perimeterBodies;
        private SpringJoint2D[] _radialSprings;
        private SpringJoint2D[] _neighborSprings;
        private SlimeNodeContact[] _nodeContacts;

        private float[] _originalRadialDist;
        private float[] _originalNeighborDist;
        private Vector2[] _restOffsets;

        private const float DampingTransitionSpeed = 8f;
        private const float CornerEscapeForce = 5f;

        private float _currentPerimeterDamping;
        private float _currentCenterDamping;

        private PhysicsMaterial2D _slipperyMaterial;
        private GameObject _nodesParent;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            CreatePhysicsMaterial();
            BuildBody();
            _currentPerimeterDamping = airborneDamping;
            _currentCenterDamping    = airborneCenterDamping;
        }

        private void FixedUpdate()
        {
            UpdatePerimeterPositions();
            UpdateGroundedRatio();
            UpdateDamping();
            UpdateRestLengths();
            ApplyShapeMatching();
            EnforceAngularSeparation();
        }

        // ── Construction ─────────────────────────────────────────────────────

        private void CreatePhysicsMaterial()
        {
            _slipperyMaterial = new PhysicsMaterial2D("SlimeNode");
            _slipperyMaterial.friction = 0f;
            _slipperyMaterial.bounciness = 0.05f;
        }

        private void BuildBody()
        {
            PerimeterPositions    = new Vector2[nodeCount];
            _perimeterBodies      = new Rigidbody2D[nodeCount];
            _radialSprings        = new SpringJoint2D[nodeCount];
            _neighborSprings      = new SpringJoint2D[nodeCount];
            _nodeContacts         = new SlimeNodeContact[nodeCount];
            _originalRadialDist   = new float[nodeCount];
            _originalNeighborDist = new float[nodeCount];
            _restOffsets          = new Vector2[nodeCount];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _centerBody = CreateNode("Center", Vector2.zero, centerMass, colliderRadius, airborneCenterDamping);

            float angleStep = 360f / nodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                _restOffsets[i] = offset;
                _perimeterBodies[i] = CreateNode(
                    $"Node_{i}", offset, perimeterMass, colliderRadius, airborneDamping);

                _nodeContacts[i] = _perimeterBodies[i].gameObject.AddComponent<SlimeNodeContact>();
                _nodeContacts[i].Init(this, CornerEscapeForce);
            }

            for (int i = 0; i < nodeCount; i++)
            {
                _radialSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _centerBody,
                    bodyRadius, radialFrequency);
                _originalRadialDist[i] = bodyRadius;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;
                float dist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _perimeterBodies[next],
                    dist, neighborFrequency);
                _originalNeighborDist[i] = dist;
            }
        }

        private Rigidbody2D CreateNode(
            string nodeName, Vector2 localOffset,
            float mass, float circleRadius, float damping)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(_nodesParent.transform);
            go.transform.localPosition = localOffset;
            go.layer = gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = mass;
            rb.gravityScale = gravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping = damping;
            rb.angularDamping = 5f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (circleRadius > 0f)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = circleRadius;
                col.sharedMaterial = _slipperyMaterial;
            }

            return rb;
        }

        private SpringJoint2D AddSpring(
            GameObject from, Rigidbody2D to,
            float restLength, float frequency)
        {
            var spring = from.AddComponent<SpringJoint2D>();
            spring.connectedBody = to;
            spring.distance = restLength;
            spring.frequency = frequency;
            spring.dampingRatio = springDamping;
            spring.autoConfigureDistance = false;
            spring.enableCollision = false;
            return spring;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void UpdatePerimeterPositions()
        {
            for (int i = 0; i < nodeCount; i++)
                PerimeterPositions[i] = _perimeterBodies[i].position;
        }

        private void UpdateGroundedRatio()
        {
            int groundedCount = 0;
            for (int i = 0; i < nodeCount; i++)
                if (_nodeContacts[i].IsGrounded) groundedCount++;

            float targetRatio = groundedCount / (float)nodeCount;
            GroundedRatio = Mathf.MoveTowards(
                GroundedRatio, targetRatio,
                DampingTransitionSpeed * Time.fixedDeltaTime);
        }

        private void UpdateDamping()
        {
            float targetPerimeter = Mathf.Lerp(airborneDamping, groundedDamping, GroundedRatio);
            float targetCenter    = Mathf.Lerp(airborneCenterDamping, groundedCenterDamping, GroundedRatio);

            _currentPerimeterDamping = Mathf.Lerp(
                _currentPerimeterDamping, targetPerimeter,
                DampingTransitionSpeed * Time.fixedDeltaTime);
            _currentCenterDamping = Mathf.Lerp(
                _currentCenterDamping, targetCenter,
                DampingTransitionSpeed * Time.fixedDeltaTime);

            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearDamping = _currentPerimeterDamping;
            _centerBody.linearDamping = _currentCenterDamping;
        }

        private void UpdateRestLengths()
        {
            Vector2 center = _centerBody.position;
            float dt = Time.fixedDeltaTime;

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;

                float currentR = _radialSprings[i].distance;
                float actualR  = Vector2.Distance(_perimeterBodies[i].position, center);
                float maxR     = _originalRadialDist[i] * maxSpreadMultiplier;
                float targetR  = Mathf.Clamp(actualR, _originalRadialDist[i], maxR);
                float newR     = Mathf.MoveTowards(currentR, targetR, spreadRate * dt);
                if (actualR < currentR)
                    newR = Mathf.MoveTowards(currentR, _originalRadialDist[i], recoveryRate * dt);
                _radialSprings[i].distance = newR;

                float currentN = _neighborSprings[i].distance;
                float actualN  = Vector2.Distance(
                    _perimeterBodies[i].position, _perimeterBodies[next].position);
                float maxN     = _originalNeighborDist[i] * maxSpreadMultiplier;
                float targetN  = Mathf.Clamp(actualN, _originalNeighborDist[i], maxN);
                float newN     = Mathf.MoveTowards(currentN, targetN, spreadRate * dt);
                if (actualN < currentN)
                    newN = Mathf.MoveTowards(currentN, _originalNeighborDist[i], recoveryRate * dt);
                _neighborSprings[i].distance = newN;
            }
        }

        private void ApplyShapeMatching()
        {
            Vector2 centroid = _centerBody.position;
            for (int i = 0; i < nodeCount; i++)
                centroid += _perimeterBodies[i].position;
            centroid /= (nodeCount + 1);

            float totalAngle = 0f;
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 offset = _perimeterBodies[i].position - centroid;
                float current  = Mathf.Atan2(offset.y, offset.x);
                float rest     = Mathf.Atan2(_restOffsets[i].y, _restOffsets[i].x);
                totalAngle    += Mathf.DeltaAngle(
                    rest * Mathf.Rad2Deg, current * Mathf.Rad2Deg);
            }

            float rot = totalAngle / nodeCount * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rot);
            float sin = Mathf.Sin(rot);

            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 r    = _restOffsets[i];
                Vector2 goal = centroid + new Vector2(
                    r.x * cos - r.y * sin,
                    r.x * sin + r.y * cos);
                _perimeterBodies[i].AddForce((goal - _perimeterBodies[i].position) * shapeMatchStrength);
            }
        }

        private void EnforceAngularSeparation()
        {
            Vector2 center = _centerBody.position;
            float idealAngle = (2f * Mathf.PI) / nodeCount;
            float minAngle = idealAngle * minAngularSeparation;

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;

                Vector2 dirA = _perimeterBodies[i].position - center;
                Vector2 dirB = _perimeterBodies[next].position - center;

                float angleA = Mathf.Atan2(dirA.y, dirA.x);
                float angleB = Mathf.Atan2(dirB.y, dirB.x);

                float diff = Mathf.DeltaAngle(
                    angleA * Mathf.Rad2Deg,
                    angleB * Mathf.Rad2Deg) * Mathf.Deg2Rad;

                if (Mathf.Abs(diff) < minAngle)
                {
                    float violation = minAngle - Mathf.Abs(diff);
                    float sign = diff >= 0 ? 1f : -1f;

                    Vector2 tangA = new Vector2(-dirA.normalized.y,  dirA.normalized.x);
                    Vector2 tangB = new Vector2(-dirB.normalized.y,  dirB.normalized.x);

                    _perimeterBodies[i].AddForce(
                        -tangA * sign * violation * separationForce);
                    _perimeterBodies[next].AddForce(
                        tangB * sign * violation * separationForce);
                }
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public Vector2 GetCentroid()
        {
            Vector2 c = _centerBody.position;
            for (int i = 0; i < nodeCount; i++) c += _perimeterBodies[i].position;
            return c / (nodeCount + 1);
        }

        public void AddMovementForce(Vector2 force) =>
            _centerBody?.AddForce(force, ForceMode2D.Force);

        public void AddImpulse(Vector2 impulse) =>
            _centerBody?.AddForce(impulse, ForceMode2D.Impulse);

        // ── Gizmos ────────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showGizmos || Application.isPlaying) return;

            Gizmos.color = Color.green;
            float angleStep = 360f / nodeCount;
            Vector3[] pts = new Vector3[nodeCount];

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

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.05f);
        }

        private void OnDestroy()
        {
            if (_nodesParent != null) Destroy(_nodesParent);
            if (_slipperyMaterial != null) Destroy(_slipperyMaterial);
        }
    }
}
