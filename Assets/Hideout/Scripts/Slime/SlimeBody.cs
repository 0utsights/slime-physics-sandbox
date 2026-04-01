using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Viscous soft-body slime. Feels heavy and settles like jello — no bounce,
    /// no elastic snapping. Spreading is driven by gravity and dynamic rest lengths.
    /// 
    /// Core idea: spring rest lengths slowly follow node positions (spread),
    /// then slowly return to original (recovery). The slime stays where it lands
    /// and reforms lazily. High damping kills all oscillation.
    /// 
    /// No shape matching. No pressure. Just damped springs with adaptive rest lengths.
    /// Zero per-frame GC allocation.
    /// </summary>
    public class SlimeBody : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Body Shape")]
        [Range(6, 24)]
        public int nodeCount = 12;
        public float bodyRadius = 0.5f;
        public float colliderRadius = 0.000001f;

        [Header("Springs")]
        [Tooltip("Radial spring frequency. Keep low for viscous feel. 3–6.")]
        public float radialFrequency = 4f;
        [Tooltip("Neighbor spring frequency. Lower = more lateral spread. 2–4.")]
        public float neighborFrequency = 2.5f;
        [Tooltip("Damping ratio. 0.8–1.0 = barely any oscillation.")]
        [Range(0f, 1f)]
        public float springDamping = 0.95f;

        [Header("Dynamic Rest Lengths")]
        [Tooltip("How fast rest lengths follow node positions (spreading). Lower = slower spread.")]
        public float spreadRate = 1.2f;
        [Tooltip("How fast rest lengths recover toward original. Lower = slower recovery.")]
        public float recoveryRate = 0.3f;
        [Tooltip("Max rest length as multiplier of original. 1.5 = 50% max spread.")]
        public float maxSpreadMultiplier = 1.5f;

        [Header("Mass & Damping")]
        public float centerMass = 3f;
        public float perimeterMass = 0.8f;
        public float gravityScale = 1f;
        [Tooltip("High linear damping kills velocity fast — key to viscous feel. 4–10.")]
        public float linearDamping = 6f;
        [Tooltip("Center node damping. Slightly lower so movement stays responsive.")]
        public float centerDamping = 2f;

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

        // ── Internal ─────────────────────────────────────────────────────────

        private Rigidbody2D _centerBody;
        private Rigidbody2D[] _perimeterBodies;
        private SpringJoint2D[] _radialSprings;
        private SpringJoint2D[] _neighborSprings;

        private float[] _originalRadialDist;
        private float[] _originalNeighborDist;
        private Vector2[] _restOffsets;

        private GameObject _nodesParent;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake() => BuildBody();

        private void FixedUpdate()
        {
            UpdatePerimeterPositions();
            UpdateRestLengths();
        }

        // ── Construction ─────────────────────────────────────────────────────

        private void BuildBody()
        {
            PerimeterPositions    = new Vector2[nodeCount];
            _perimeterBodies      = new Rigidbody2D[nodeCount];
            _radialSprings        = new SpringJoint2D[nodeCount];
            _neighborSprings      = new SpringJoint2D[nodeCount];
            _originalRadialDist   = new float[nodeCount];
            _originalNeighborDist = new float[nodeCount];
            _restOffsets          = new Vector2[nodeCount];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _centerBody = CreateNode("Center", Vector2.zero, centerMass, 0f, centerDamping);

            float angleStep = 360f / nodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                _restOffsets[i] = offset;
                _perimeterBodies[i] = CreateNode($"Node_{i}", offset, perimeterMass, colliderRadius, linearDamping);
            }

            for (int i = 0; i < nodeCount; i++)
            {
                float radialDist = bodyRadius;
                _radialSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _centerBody,
                    radialDist, radialFrequency);
                _originalRadialDist[i] = radialDist;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;
                float neighborDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _perimeterBodies[next],
                    neighborDist, neighborFrequency);
                _originalNeighborDist[i] = neighborDist;
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
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (circleRadius > 0f)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = circleRadius;
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

        private void UpdateRestLengths()
        {
            Vector2 center = _centerBody.position;
            float dt = Time.fixedDeltaTime;

            for (int i = 0; i < nodeCount; i++)
            {
                // ── Radial spring ─────────────────────────────────────────────
                float currentRadial  = _radialSprings[i].distance;
                float actualRadial   = Vector2.Distance(_perimeterBodies[i].position, center);
                float maxRadial      = _originalRadialDist[i] * maxSpreadMultiplier;

                // Spread: rest length follows actual distance (node moved away from center)
                float targetRadial = Mathf.Clamp(actualRadial, _originalRadialDist[i], maxRadial);
                float newRadial    = Mathf.MoveTowards(currentRadial, targetRadial, spreadRate * dt);

                // Recovery: when actual dist is less than rest, slowly recover toward original
                if (actualRadial < currentRadial)
                    newRadial = Mathf.MoveTowards(currentRadial, _originalRadialDist[i], recoveryRate * dt);

                _radialSprings[i].distance = newRadial;

                // ── Neighbor spring ───────────────────────────────────────────
                int next = (i + 1) % nodeCount;
                float currentNeighbor = _neighborSprings[i].distance;
                float actualNeighbor  = Vector2.Distance(
                    _perimeterBodies[i].position,
                    _perimeterBodies[next].position);
                float maxNeighbor     = _originalNeighborDist[i] * maxSpreadMultiplier;

                float targetNeighbor = Mathf.Clamp(actualNeighbor, _originalNeighborDist[i], maxNeighbor);
                float newNeighbor    = Mathf.MoveTowards(currentNeighbor, targetNeighbor, spreadRate * dt);

                if (actualNeighbor < currentNeighbor)
                    newNeighbor = Mathf.MoveTowards(currentNeighbor, _originalNeighborDist[i], recoveryRate * dt);

                _neighborSprings[i].distance = newNeighbor;
            }
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
            if (_nodesParent != null)
                Destroy(_nodesParent);
        }
    }
}

