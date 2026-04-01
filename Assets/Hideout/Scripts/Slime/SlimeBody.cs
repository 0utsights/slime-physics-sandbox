cat > "/home/nihil/1BIT/Assets/Hideout/Scripts/Slime/SlimeBody.cs" << 'EOF'
using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Core soft-body slime system.
    /// Generates a spring-mass network at runtime: one center Rigidbody2D
    /// surrounded by N perimeter Rigidbody2Ds connected by SpringJoint2Ds.
    /// Shape matching forces pull nodes back to rest positions each frame.
    /// Pressure forces push nodes outward when volume is lost.
    /// Apply movement forces via AddMovementForce / AddImpulse.
    /// All arrays pre-allocated — zero per-frame GC allocations.
    /// </summary>
    public class SlimeBody : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Body Shape")]
        [Tooltip("Number of perimeter nodes. 8 = performance, 12 = smoother.")]
        [Range(6, 24)]
        public int nodeCount = 8;

        [Tooltip("Radius of the slime body in world units.")]
        public float bodyRadius = 0.5f;

        [Tooltip("Radius of each perimeter node's CircleCollider2D.")]
        public float colliderRadius = 0.12f;

        [Header("Spring Settings")]
        [Tooltip("Higher = stiffer, faster return to shape. 8–15 for firm jelly.")]
        public float springFrequency = 14f;

        [Tooltip("0 = no damping (infinite bounce). 0.5–0.7 = 1–3 bounces.")]
        [Range(0f, 1f)]
        public float springDamping = 0.6f;

        [Header("Shape Matching")]
        [Tooltip("How strongly nodes are pulled back to their rest shape. 10–30 for snappy recovery.")]
        public float shapeMatchStrength = 25f;

        [Header("Mass")]
        public float centerMass = 2f;
        public float perimeterMass = 0.4f;
        public float gravityScale = 1f;

        [Header("Volume Preservation")]
        [Tooltip("Strength of the outward pressure force keeping the slime from flattening.")]
        public float pressureForce = 8f;

        [Header("Debug")]
        public bool showGizmos = true;

        // ── Public state (read by SlimeMesh) ─────────────────────────────────

        /// <summary>World positions of all perimeter nodes, updated every FixedUpdate.</summary>
        public Vector2[] PerimeterPositions { get; private set; }

        /// <summary>World position of the center node.</summary>
        public Vector2 CenterPosition => _centerBody != null
            ? (Vector2)_centerBody.transform.position
            : (Vector2)transform.position;

        // ── Internal ─────────────────────────────────────────────────────────

        private Rigidbody2D _centerBody;
        private Rigidbody2D[] _perimeterBodies;
        private SpringJoint2D[] _centerSprings;
        private SpringJoint2D[] _neighborSprings;

        private float _targetArea;
        private Vector2[] _restOffsets;
        private GameObject _nodesParent;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildBody();
        }

        private void FixedUpdate()
        {
            UpdatePerimeterPositions();
            ApplyPressure();
            ApplyShapeMatching();
        }

        // ── Construction ─────────────────────────────────────────────────────

        private void BuildBody()
        {
            PerimeterPositions = new Vector2[nodeCount];
            _perimeterBodies   = new Rigidbody2D[nodeCount];
            _centerSprings     = new SpringJoint2D[nodeCount];
            _neighborSprings   = new SpringJoint2D[nodeCount];
            _restOffsets       = new Vector2[nodeCount];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _centerBody = CreateNode("Center", Vector2.zero, centerMass, 0f);

            float angleStep = 360f / nodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                _restOffsets[i] = offset;
                _perimeterBodies[i] = CreateNode($"Node_{i}", offset, perimeterMass, colliderRadius);
            }

            for (int i = 0; i < nodeCount; i++)
                _centerSprings[i] = AddSpring(_perimeterBodies[i].gameObject, _centerBody, bodyRadius);

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;
                float neighborDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = AddSpring(_perimeterBodies[i].gameObject, _perimeterBodies[next], neighborDist);
            }

            _targetArea = ComputeRestArea();
        }

        private Rigidbody2D CreateNode(string nodeName, Vector2 localOffset, float mass, float circleRadius)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(_nodesParent.transform);
            go.transform.localPosition = localOffset;
            go.layer = gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = mass;
            rb.gravityScale = gravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (circleRadius > 0f)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = circleRadius;
            }

            return rb;
        }

        private SpringJoint2D AddSpring(GameObject from, Rigidbody2D to, float restLength)
        {
            var spring = from.AddComponent<SpringJoint2D>();
            spring.connectedBody = to;
            spring.distance = restLength;
            spring.frequency = springFrequency;
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

        private void ApplyPressure()
        {
            float area = ComputeCurrentArea();
            float deficit = _targetArea - area;
            if (deficit <= 0f) return;

            float forceMag = deficit * pressureForce;
            Vector2 currentCenter = _centerBody.position;

            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 outward = (_perimeterBodies[i].position - currentCenter).normalized;
                _perimeterBodies[i].AddForce(outward * forceMag);
            }
        }

        private void ApplyShapeMatching()
        {
            Vector2 currentCenter = _centerBody.position;

            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 targetWorld = currentCenter + _restOffsets[i];
                Vector2 currentPos  = _perimeterBodies[i].position;
                Vector2 delta       = targetWorld - currentPos;
                _perimeterBodies[i].AddForce(delta * shapeMatchStrength);
            }
        }

        // ── Geometry ──────────────────────────────────────────────────────────

        private float ComputeRestArea()
        {
            float area = 0f;
            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;
                area += _restOffsets[i].x * _restOffsets[next].y;
                area -= _restOffsets[next].x * _restOffsets[i].y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        private float ComputeCurrentArea()
        {
            float area = 0f;
            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;
                area += PerimeterPositions[i].x * PerimeterPositions[next].y;
                area -= PerimeterPositions[next].x * PerimeterPositions[i].y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Apply a movement force to the center node.</summary>
        public void AddMovementForce(Vector2 force) =>
            _centerBody?.AddForce(force, ForceMode2D.Force);

        /// <summary>Apply an impulse to the center node (e.g. jump).</summary>
        public void AddImpulse(Vector2 impulse) =>
            _centerBody?.AddForce(impulse, ForceMode2D.Impulse);

        /// <summary>Current velocity of the center node.</summary>
        public Vector2 Velocity => _centerBody != null
            ? _centerBody.linearVelocity
            : Vector2.zero;

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
