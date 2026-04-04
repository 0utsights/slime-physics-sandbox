using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Per-node terrain contact tracker and anti-sink corrector.
    ///
    /// Runs at execution order -20, before SlimeBody (-10). This ordering is
    /// load-bearing: position corrections must land before shape matching reads
    /// node positions, otherwise recovery computes goal forces from penetrating
    /// positions and drives nodes deeper into terrain every step.
    ///
    /// Exposes grounded state, contact normal, and surface tangent as read-only
    /// properties for SlimeBody to use in force clamping and crouch flow.
    ///
    /// Anti-sink is two layers:
    ///   Layer 1 (reactive)  — OnCollisionStay2D: proportional force along contact
    ///                         normal scaled by penetration depth, plus inward
    ///                         velocity cancellation. Handles steady-state contact.
    ///   Layer 2 (proactive) — FixedUpdate: OverlapCircle sweep catches residual
    ///                         overlap the force layer missed and teleports the
    ///                         node to the surface edge. Nuclear option that
    ///                         guarantees no frame ends with penetration.
    ///
    /// Why both layers: Box2D resolves joint constraints before contact constraints.
    /// Spring forces get "first say" on velocity each iteration. The solver can't
    /// fully converge the contact constraint in its iteration budget when joints
    /// pull against it, leaving residual penetration that accumulates over frames.
    /// Force correction reduces it; position correction eliminates it.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class SlimeNodeContact : MonoBehaviour
    {
        // ── Public state ──────────────────────────────────────────────────────

        /// <summary>True when this node has active contact with non-slime geometry.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Outward-facing normal of the contact surface. Zero when airborne.</summary>
        public Vector2 ContactNormal { get; private set; }

        /// <summary>
        /// Surface tangent derived from contact normal. Points in whichever
        /// tangent direction has a downward component (gravity-biased), so
        /// crouch flow forces naturally slide nodes downhill along surfaces.
        /// Zero when airborne or on perfectly flat ground.
        /// </summary>
        public Vector2 SurfaceTangent { get; private set; }

        // ── Private ───────────────────────────────────────────────────────────

        private SlimeBody        _body;
        private Rigidbody2D      _rb;
        private CircleCollider2D _col;

        private float     _escapeForce;
        private float     _maxImpactSpeed;
        private float     _antiSinkForceScale;
        private LayerMask _groundMask;

        private static readonly ContactPoint2D[] _contacts = new ContactPoint2D[4];

        // Small gap left after position correction to prevent the node from
        // sitting exactly on the surface edge, which causes contact flickering
        // as the solver alternates between "overlapping" and "separated".
        private const float SkinWidth = 0.01f;

        // ── Init ──────────────────────────────────────────────────────────────

        public void Init(SlimeBody body, float escapeForce, float maxImpactSpeed, float antiSinkForceScale)
        {
            _body               = body;
            _escapeForce        = escapeForce;
            _maxImpactSpeed     = maxImpactSpeed;
            _antiSinkForceScale = antiSinkForceScale;
            _rb                 = GetComponent<Rigidbody2D>();
            _col                = GetComponent<CircleCollider2D>();

            int slimeLayer = LayerMask.NameToLayer("Slime");
            _groundMask = slimeLayer != -1 ? ~(1 << slimeLayer) : ~0;
        }

        // ── Layer 2: Position correction (runs first each frame) ──────────────
        //
        // FixedUpdate at -20 fires before SlimeBody.FixedUpdate at -10.
        // Residual overlap from the previous step is resolved here so that when
        // SlimeBody reads PerimeterPositions, every node is already outside
        // terrain. This breaks the feedback loop where recovery → penetration →
        // anti-sink → recovery fought each other frame-over-frame.

        private void FixedUpdate()
        {
            if (_col == null) return;

            Collider2D hit = Physics2D.OverlapCircle(_rb.position, _col.radius, _groundMask);
            if (hit == null) return;

            ColliderDistance2D dist = _col.Distance(hit);
            if (!dist.isValid || !dist.isOverlapped) return;

            _rb.position += dist.normal * (-dist.distance + SkinWidth);

            float vn = Vector2.Dot(_rb.linearVelocity, dist.normal);
            if (vn < 0f)
                _rb.linearVelocity -= dist.normal * vn;

            // Position correction proves contact even without a collision callback
            SetGrounded(dist.normal);
        }

        // ── Layer 1: Force correction (collision callbacks) ───────────────────

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Absorb impact energy spike before the spring network amplifies it
            if (_rb.linearVelocity.sqrMagnitude > _maxImpactSpeed * _maxImpactSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * _maxImpactSpeed;

            _body.NotifyImpact();
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            int count = collision.GetContacts(_contacts);
            if (count == 0) return;

            // Track the deepest penetration for the authoritative normal
            float   deepest    = 0f;
            Vector2 bestNormal = _contacts[0].normal;

            for (int c = 0; c < count; c++)
            {
                ContactPoint2D contact = _contacts[c];
                float penetration = -contact.separation;

                if (penetration > deepest)
                {
                    deepest    = penetration;
                    bestNormal = contact.normal;
                }

                // Proportional push-out force scaled by penetration depth
                if (penetration > 0f)
                    _rb.AddForce(contact.normal * penetration * _antiSinkForceScale);

                // Cancel inward velocity along this contact normal
                float vn = Vector2.Dot(_rb.linearVelocity, contact.normal);
                if (vn < 0f)
                    _rb.AddForce(contact.normal * (-vn) * _rb.mass / Time.fixedDeltaTime);
            }

            SetGrounded(bestNormal);

            // Corner escape: two diverging normals means wedged between surfaces.
            // Impulse along the averaged escape direction biased toward body center.
            if (count >= 2)
            {
                Vector2 n0 = _contacts[0].normal;
                Vector2 n1 = _contacts[1].normal;
                if (Vector2.Dot(n0, n1) < 0.3f)
                {
                    Vector2 escape   = (n0 + n1).normalized;
                    Vector2 toCenter = (_body.CenterPosition - (Vector2)transform.position).normalized;
                    _rb.AddForce((escape + toCenter * 0.5f).normalized * _escapeForce, ForceMode2D.Impulse);
                }
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            IsGrounded     = false;
            ContactNormal  = Vector2.zero;
            SurfaceTangent = Vector2.zero;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sets grounded state and derives the gravity-biased surface tangent.
        /// Called from both collision callbacks and position correction so
        /// grounded data is always consistent.
        /// </summary>
        private void SetGrounded(Vector2 normal)
        {
            IsGrounded    = true;
            ContactNormal = normal;

            // Perpendicular to normal, biased so tangent y-component points
            // downward. On flat ground both tangents are horizontal — bias
            // toward body center for stable crouch behavior.
            Vector2 tangent = new Vector2(-normal.y, normal.x);

            if (Mathf.Abs(tangent.y) < 0.01f)
            {
                Vector2 toCenter = _body.CenterPosition - (Vector2)transform.position;
                if (Vector2.Dot(tangent, toCenter) < 0f)
                    tangent = -tangent;
            }
            else if (tangent.y > 0f)
            {
                tangent = -tangent;
            }

            SurfaceTangent = tangent;
        }
    }
}
