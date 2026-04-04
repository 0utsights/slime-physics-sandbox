using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Attached to each perimeter node by SlimeBody.
    ///
    /// Responsibilities:
    ///   1. Track grounded state and contact normals for SlimeBody.GroundedRatio.
    ///   2. Anti-sink: apply a counter-force proportional to penetration depth on
    ///      every grounded contact stay, plus a position-level safety correction in
    ///      FixedUpdate that catches any remaining overlap the solver missed.
    ///   3. Velocity clamp on first contact to absorb impact energy spike.
    ///   4. Corner escape impulse when wedged between two diverging surfaces.
    ///   5. Notify SlimeBody on first contact for impact recovery burst.
    ///
    /// Anti-sink design:
    ///   Box2D resolves joint constraints before contact constraints each velocity
    ///   iteration, so spring forces have "first say" on node velocity. With joints
    ///   per node the solver cannot fully converge against the ground contact in the
    ///   default iteration budget, leaving residual penetration. Two-layer correction:
    ///     Layer 1 (force) — OnCollisionStay2D: proportional force + velocity
    ///                       correction along contact normal each physics step.
    ///     Layer 2 (position) — FixedUpdate: OverlapCircle check + direct position
    ///                          teleport out of any residual overlap. Catches what
    ///                          force correction misses.
    ///
    /// EXECUTION ORDER: -20, runs BEFORE SlimeBody (-10). This ensures anti-sink
    /// position corrections are applied before shape matching forces, so recovery
    /// operates on already-corrected positions rather than fighting the correction.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class SlimeNodeContact : MonoBehaviour
    {
        public bool    IsGrounded    { get; private set; }
        public Vector2 ContactNormal { get; private set; }

        private SlimeBody     _body;
        private Rigidbody2D   _rb;
        private CircleCollider2D _col;
        private float         _escapeForce;
        private float         _maxImpactSpeed;
        private float         _antiSinkForceScale;

        private LayerMask _groundMask;

        // Accumulated normal from OnCollisionStay for the current physics step.
        // Averaged when multiple contacts exist, persists until next OnCollisionExit.
        private Vector2 _accumulatedNormal;
        private bool    _contactThisStep;

        private static readonly ContactPoint2D[] _contacts = new ContactPoint2D[4];

        private const float SkinWidth = 0.01f; // breathing room to prevent contact flickering

        public void Init(SlimeBody body, float escapeForce, float maxImpactSpeed, float antiSinkForceScale)
        {
            _body               = body;
            _escapeForce        = escapeForce;
            _maxImpactSpeed     = maxImpactSpeed;
            _antiSinkForceScale = antiSinkForceScale;
            _rb                 = GetComponent<Rigidbody2D>();
            _col                = GetComponent<CircleCollider2D>();

            int slimeLayer = LayerMask.NameToLayer("Slime");
            _groundMask = slimeLayer != -1
                ? ~(1 << slimeLayer)
                : ~0;
        }

        // ── LAYER 1: Force-based correction ───────────────────────────────────

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_rb.linearVelocity.sqrMagnitude > _maxImpactSpeed * _maxImpactSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * _maxImpactSpeed;

            _body.NotifyImpact();
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            int count = collision.GetContacts(_contacts);
            IsGrounded    = true;
            _contactThisStep = true;

            for (int c = 0; c < count; c++)
            {
                ContactPoint2D contact = _contacts[c];
                _accumulatedNormal = contact.normal; // last-write for this step

                float penetration = -contact.separation;
                if (penetration > 0f)
                    _rb.AddForce(contact.normal * penetration * _antiSinkForceScale);

                float vn = Vector2.Dot(_rb.linearVelocity, contact.normal);
                if (vn < 0f)
                    _rb.AddForce(contact.normal * (-vn) * _rb.mass / Time.fixedDeltaTime);
            }

            // Expose the best normal for SlimeBody's grounded-aware clamping
            ContactNormal = _accumulatedNormal;

            // Corner escape: two diverging normals = wedged between surfaces
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
            IsGrounded    = false;
            ContactNormal = Vector2.zero;
            _accumulatedNormal = Vector2.zero;
            _contactThisStep   = false;
        }

        // ── LAYER 2: Position-based safety net ────────────────────────────────
        // Runs every fixed step BEFORE SlimeBody (-20 vs -10).
        // Catches any residual overlap — directly teleports the node to surface edge.

        private void FixedUpdate()
        {
            if (_col == null) return;

            Collider2D hit = Physics2D.OverlapCircle(_rb.position, _col.radius, _groundMask);
            if (hit == null) return;

            ColliderDistance2D dist = _col.Distance(hit);
            if (!dist.isValid || !dist.isOverlapped) return;

            // Teleport node to surface edge + skin width
            float correction = -dist.distance + SkinWidth;
            _rb.position += dist.normal * correction;

            // Zero out the inward velocity component so the node doesn't re-penetrate
            float vn = Vector2.Dot(_rb.linearVelocity, dist.normal);
            if (vn < 0f)
                _rb.linearVelocity -= dist.normal * vn;

            // Update grounded state from position correction too
            IsGrounded    = true;
            ContactNormal = dist.normal;
        }
    }
}
