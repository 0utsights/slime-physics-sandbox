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
    /// Recovery model (Müller et al. 2005 shape matching):
    ///   Every frame, compute the best-fit rigid rotation of the rest shape onto
    ///   the current deformed shape using the 2×2 cross-covariance matrix (reduces
    ///   to a single atan2 in 2D). Apply spring-like forces pulling each node toward
    ///   its goal position in the rotated rest frame.
    ///
    ///   A single RecoverShape(stiffness, damping) function handles all recovery
    ///   contexts — idle settling, post-impact, and post-jump — by varying only
    ///   the stiffness and damping parameters. This avoids duplicated logic and
    ///   the failed spring-distance-manipulation approach (which has no global
    ///   shape awareness and is overridden by autoConfigureDistance).
    ///
    /// Springs are set once at build time and never modified at runtime.
    /// Springs provide elasticity and structural connectivity.
    /// Shape matching provides global recovery — these two do complementary jobs.
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

        [Header("Shape Recovery — Idle")]
        [Tooltip("Stiffness of recovery force when resting. Low = wobbly, high = rigid. 50–200.")]
        public float idleRecoveryStiffness = 80f;
        [Tooltip("Damping of recovery force when resting. Reduces oscillation. 8–20.")]
        public float idleRecoveryDamping   = 4f;

        [Header("Shape Recovery — Impact")]
        [Tooltip("Stiffness boost on landing. Snaps shape back faster after squash.")]
        public float impactRecoveryStiffness = 250f;
        [Tooltip("Damping during impact recovery. Higher = less post-impact wobble.")]
        public float impactRecoveryDamping   = 8f;
        [Tooltip("How long after landing the impact recovery runs before fading to idle. Seconds.")]
        public float impactRecoveryDuration  = 0.25f;

        [Header("Shape Recovery — Jump")]
        [Tooltip("Stiffness during jump unfurl window. Pulls nodes toward rest shape as slime rises.")]
        public float jumpRecoveryStiffness = 180f;
        [Tooltip("Damping during jump unfurl.")]
        public float jumpRecoveryDamping   = 6f;
        [Tooltip("Seconds after jump before unfurl begins.")]
        public float jumpRecoveryDelay     = 0.05f;
        [Tooltip("Duration of the unfurl window.")]
        public float jumpRecoveryDuration  = 0.3f;

        [Header("Crouch")]
        [Tooltip("Continuous downward force applied while crouching. Weaker than a jump.")]
        public float crouchDownForce = 6f;
        [Tooltip("How much to reduce idle recovery stiffness while crouching. 0 = fully limp, 1 = no change.")]
        [Range(0f, 1f)]
        public float crouchStiffnessMultiplier = 0.05f;
        [Tooltip("Grounded ratio threshold below which release-recovery is suppressed (tight crevice).")]
        [Range(0f, 1f)]
        public float crouchReleaseGroundedThreshold = 0.4f;

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
        [Tooltip("Viscous settling after bounce. Spring oscillation runs first.")]
        public float groundedDamping       = 1.0f;
        public float groundedCenterDamping = 1.5f;

        [Tooltip("How quickly linearDamping transitions between airborne and grounded.")]
        public float dampingTransitionSpeed = 4f;

        [Header("Angular Separation")]
        [Tooltip("Minimum angular gap between adjacent nodes as fraction of ideal spacing. 0.5–0.7.")]
        public float minAngularSeparation = 0.6f;
        [Tooltip("Force pushing nodes apart when gap falls below minimum.")]
        public float separationForce = 5f;

        [Header("Safety")]
        [Tooltip("Max speed any perimeter node can reach. Prevents cascade decomposition.")]
        public float maxNodeSpeed = 20f;
        [Tooltip("Max recovery force magnitude per node. Prevents explosion on extreme deformation.")]
        public float maxRecoveryForce = 500f;

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

        /// <summary>Mass-weighted centroid of all bodies. Cached once per FixedUpdate.</summary>
        public Vector2 Centroid { get; private set; }

        // ── Internal ──────────────────────────────────────────────────────────

        private Rigidbody2D        _centerBody;
        private Rigidbody2D[]      _perimeterBodies;
        private SpringJoint2D[]    _radialSprings;
        private SpringJoint2D[]    _neighborSprings;
        private SpringJoint2D[]    _braceSprings;
        private SlimeNodeContact[] _nodeContacts;

        // Shape matching rest state — computed once at build, never modified
        private Vector2[] _restOffsets;   // perimeter node offsets from rest centroid
        private float     _totalMass;

        private Vector2[] _lastValidPositions;

        private float _perimeterDamping;
        private float _centerDamping;

        // Recovery state
        private bool  _isCrouching         = false;
        private float _impactRecoveryTimer = 0f;  // counts down from impactRecoveryDuration
        private float _jumpTimer           = 0f;  // counts up from jump moment
        private bool  _jumpActive          = false;

        private PhysicsMaterial2D _physicsMaterial;
        private GameObject        _nodesParent;

        private const float CornerEscapeForce = 5f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            Physics2D.velocityIterations = 12;
            Physics2D.positionIterations = 6;

            int slimeLayer = LayerMask.NameToLayer("Slime");
            if (slimeLayer != -1)
                Physics2D.IgnoreLayerCollision(slimeLayer, slimeLayer, true);
            else
                Debug.LogWarning("[SlimeBody] No 'Slime' layer found. Add it in Project Settings → Tags and Layers. Nodes will collide with each other.");

            BuildPhysicsMaterial();
            BuildBody();

            _perimeterDamping = airborneDamping;
            _centerDamping    = airborneCenterDamping;
        }

        private void FixedUpdate()
        {
            CachePositions();
            CacheCentroid();
            CacheGroundedRatio();

            ClampVelocities();   // after cache so we act on fresh positions

            ApplyPressure();
            ApplyRecovery();
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
            _restOffsets        = new Vector2[n];
            _lastValidPositions = new Vector2[n];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _centerBody = CreateNode("Center", Vector2.zero, centerMass, airborneCenterDamping, hasCollider: false);

            float angleStep = 360f / n;
            for (int i = 0; i < n; i++)
            {
                float   angle  = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;
                _restOffsets[i]        = offset; // offset from rest centroid (origin)
                _lastValidPositions[i] = (Vector2)transform.position + offset;
                _perimeterBodies[i]    = CreateNode($"Node_{i}", offset, perimeterMass, airborneDamping);

                _nodeContacts[i] = _perimeterBodies[i].gameObject.AddComponent<SlimeNodeContact>();
                _nodeContacts[i].Init(this, CornerEscapeForce, maxNodeSpeed);
            }

            // Total mass for centroid weighting
            _totalMass = centerMass + n * perimeterMass;

            for (int i = 0; i < n; i++)
            {
                // Radial: node ↔ center — set once, never changed at runtime
                _radialSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _centerBody, bodyRadius, radialFrequency);

                // Neighbor: node i ↔ node i+1
                int   next  = (i + 1) % n;
                float nDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _perimeterBodies[next], nDist, neighborFrequency);

                // Brace: node i ↔ node i+2
                int   skip  = (i + 2) % n;
                float bDist = Vector2.Distance(_restOffsets[i], _restOffsets[skip]);
                _braceSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _perimeterBodies[skip], bDist, neighborFrequency);
            }
        }

        private Rigidbody2D CreateNode(string nodeName, Vector2 localOffset, float mass, float damping, bool hasCollider = true)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(_nodesParent.transform);
            go.transform.localPosition = localOffset;

            int slimeLayer = LayerMask.NameToLayer("Slime");
            go.layer = slimeLayer != -1 ? slimeLayer : gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass                   = mass;
            rb.gravityScale           = gravityScale;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping          = damping;
            rb.angularDamping         = 5f;
            rb.interpolation          = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode              = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (hasCollider)
            {
                var col           = go.AddComponent<CircleCollider2D>();
                col.radius         = colliderRadius;
                col.sharedMaterial = _physicsMaterial;
            }

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
            // Mass-weighted centroid: matches the shape matching precomputation
            Vector2 sum = _centerBody.position * centerMass;
            for (int i = 0; i < nodeCount; i++)
                sum += PerimeterPositions[i] * perimeterMass;
            Centroid = sum / _totalMass;
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
            // Clamp center body too — movement force goes there
            _centerBody.linearVelocity =
                Vector2.ClampMagnitude(_centerBody.linearVelocity, maxNodeSpeed);

            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearVelocity =
                    Vector2.ClampMagnitude(_perimeterBodies[i].linearVelocity, maxNodeSpeed);
        }

        // ── PRESSURE ─────────────────────────────────────────────────────────
        // Shoelace polygon area → outward normal force per edge.
        // Uses signed area to determine winding so force is always outward.

        private void ApplyPressure()
        {
            int n = nodeCount;

            float signedArea = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = PerimeterPositions[i];
                Vector2 b = PerimeterPositions[(i + 1) % n];
                signedArea += a.x * b.y - b.x * a.y;
            }
            float area = Mathf.Abs(signedArea) * 0.5f;
            if (area < 0.001f) return;

            float normalSign = signedArea > 0f ? 1f : -1f;
            float pressure   = gasAmount / area * pressureStrength;

            for (int i = 0; i < n; i++)
            {
                int     next    = (i + 1) % n;
                Vector2 edge    = PerimeterPositions[next] - PerimeterPositions[i];
                float   edgeLen = edge.magnitude;
                if (edgeLen < 0.0001f) continue;

                Vector2 normal = new Vector2(edge.y * normalSign, -edge.x * normalSign) / edgeLen;
                Vector2 force  = normal * (pressure * edgeLen * 0.5f);

                _perimeterBodies[i].AddForce(force);
                _perimeterBodies[next].AddForce(force);
            }
        }

        // ── RECOVERY ─────────────────────────────────────────────────────────
        // Müller et al. 2005 shape matching for 2D.
        //
        // Three recovery contexts share one RecoverShape() function:
        //   Idle    — constant low-stiffness pull toward rest shape
        //   Impact  — high-stiffness burst after landing, fades over impactRecoveryDuration
        //   Jump    — medium-stiffness unfurl during the post-jump rise window
        //
        // The algorithm:
        //   1. Compute current mass-weighted centroid (already in Centroid)
        //   2. Build 2×2 cross-covariance matrix Apq from current vs rest offsets
        //   3. Extract best-fit rotation via atan2(Apq[1,0]-Apq[0,1], Apq[0,0]+Apq[1,1])
        //   4. Derive goal positions by rotating rest offsets by θ around current centroid
        //   5. Apply PD force: F = stiffness*(goal-pos) - damping*velocity
        //
        // Springs never have their .distance modified at runtime.
        // Springs handle elasticity; shape matching handles recovery. Different jobs.

        private void ApplyRecovery()
        {
            float dt = Time.fixedDeltaTime;

            // ── Determine stiffness/damping for this frame ─────────────────────
            float stiffness, damping;

            // Jump unfurl: check first since it has highest priority during ascent
            if (_jumpActive)
            {
                _jumpTimer += dt;

                // Cancel if upward momentum is gone (ceiling or apex)
                if (_centerBody.linearVelocity.y < 0.5f)
                    _jumpActive = false;
                // Cancel if window has elapsed
                else if (_jumpTimer > jumpRecoveryDelay + jumpRecoveryDuration)
                    _jumpActive = false;

                bool inWindow = _jumpActive && _jumpTimer >= jumpRecoveryDelay;
                stiffness = inWindow ? jumpRecoveryStiffness : EffectiveIdleStiffness;
                damping   = inWindow ? jumpRecoveryDamping   : idleRecoveryDamping;
            }
            // Impact burst: fades linearly over impactRecoveryDuration
            else if (_impactRecoveryTimer > 0f)
            {
                _impactRecoveryTimer -= dt;
                float t = _impactRecoveryTimer / impactRecoveryDuration; // 1→0
                stiffness = Mathf.Lerp(EffectiveIdleStiffness,  impactRecoveryStiffness, t);
                damping   = Mathf.Lerp(idleRecoveryDamping,     impactRecoveryDamping,   t);
            }
            // Idle: constant baseline recovery
            else
            {
                stiffness = EffectiveIdleStiffness;
                damping   = idleRecoveryDamping;
            }

            RecoverShape(stiffness, damping);
        }

        /// <summary>
        /// Applies Müller shape matching forces to all perimeter nodes.
        /// Pulls each node toward its goal position in the best-fit rotated rest frame.
        /// </summary>
        private void RecoverShape(float stiffness, float damping)
        {
            int n = nodeCount;

            // Build cross-covariance matrix Apq
            float a00 = 0f, a01 = 0f, a10 = 0f, a11 = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = PerimeterPositions[i] - Centroid;
                Vector2 q = _restOffsets[i];
                float   m = perimeterMass;
                a00 += m * p.x * q.x;
                a01 += m * p.x * q.y;
                a10 += m * p.y * q.x;
                a11 += m * p.y * q.y;
            }

            // Best-fit rotation: atan2 simplification of 2D polar decomposition
            float theta = Mathf.Atan2(a10 - a01, a00 + a11);
            float cosT  = Mathf.Cos(theta);
            float sinT  = Mathf.Sin(theta);

            // Shared body velocity (gravity, movement, jump) — we must NOT damp this.
            // Only damp each node's velocity relative to the shared motion so recovery
            // doesn't fight locomotion and gravity.
            Vector2 bodyVelocity = _centerBody.linearVelocity;

            for (int i = 0; i < n; i++)
            {
                Vector2 q       = _restOffsets[i];
                Vector2 goalPos = Centroid + new Vector2(
                    cosT * q.x - sinT * q.y,
                    sinT * q.x + cosT * q.y);

                // Relative velocity: how fast this node moves away from body-frame rest
                Vector2 relVelocity = _perimeterBodies[i].linearVelocity - bodyVelocity;

                // Stiffness pulls toward goal; damping only suppresses oscillation,
                // not shared translation from gravity/movement
                Vector2 force = stiffness * (goalPos - PerimeterPositions[i])
                              - damping   * relVelocity;

                // Clamp stiffness force only — let damping act freely on oscillation
                float mag = force.magnitude;
                if (mag > maxRecoveryForce)
                    force = force * (maxRecoveryForce / mag);

                _perimeterBodies[i].AddForce(force);
            }
        }

        // ── ANGULAR SEPARATION ────────────────────────────────────────────────
        // Keeps nodes evenly distributed around the ring.

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
        // linearDamping driven by GroundedRatio. Spring oscillation resolves first;
        // damping transitions slowly enough not to kill the bounce on landing.

        private void UpdateDamping()
        {
            float targetPerimeter = Mathf.Lerp(airborneDamping,       groundedDamping,       GroundedRatio);
            float targetCenter    = Mathf.Lerp(airborneCenterDamping, groundedCenterDamping, GroundedRatio);

            _perimeterDamping = Mathf.MoveTowards(
                _perimeterDamping, targetPerimeter, dampingTransitionSpeed * Time.fixedDeltaTime);
            _centerDamping = Mathf.MoveTowards(
                _centerDamping, targetCenter, dampingTransitionSpeed * Time.fixedDeltaTime);

            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearDamping = _perimeterDamping;
            _centerBody.linearDamping = _centerDamping;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void AddMovementForce(Vector2 force) =>
            _centerBody?.AddForce(force, ForceMode2D.Force);

        /// <summary>
        /// Applies an impulse to all bodies proportional to their mass.
        /// Resets damping instantly so it doesn't eat the impulse.
        /// Triggers the jump recovery unfurl window.
        /// </summary>
        public void AddImpulse(Vector2 impulse)
        {
            if (_centerBody == null) return;

            _perimeterDamping = airborneDamping;
            _centerDamping    = airborneCenterDamping;
            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearDamping = _perimeterDamping;
            _centerBody.linearDamping = _centerDamping;

            _centerBody.AddForce(impulse * centerMass, ForceMode2D.Impulse);
            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].AddForce(impulse * perimeterMass, ForceMode2D.Impulse);

            _jumpActive = true;
            _jumpTimer  = 0f;
        }

        /// <summary>
        /// Called by SlimeNodeContact on collision enter to trigger impact recovery burst.
        /// </summary>
        public void NotifyImpact()
        {
            _impactRecoveryTimer = impactRecoveryDuration;
            _jumpActive = false; // landing cancels jump unfurl
        }

        /// <summary>
        /// Called by SlimeController each frame with the current crouch state.
        /// While crouching: applies downward force and reduces recovery stiffness so
        /// the slime can deform into crevices without bouncing back out.
        /// On release: triggers immediate recovery unless too many nodes are still
        /// in contact (tight crevice — recovery would fight the geometry).
        /// </summary>
        public void SetCrouching(bool crouching)
        {
            bool wascrouching = _isCrouching;
            _isCrouching = crouching;

            if (crouching)
            {
                // Continuous downward push — weaker than a jump, feels like pressing down
                _centerBody?.AddForce(Vector2.down * crouchDownForce, ForceMode2D.Force);
            }
            else if (wascrouching)
            {
                // Released crouch — recover immediately unless we're still wedged
                // in a crevice (most nodes still grounded = no room to expand)
                if (GroundedRatio < crouchReleaseGroundedThreshold)
                    NotifyImpact(); // reuse impact burst: fast stiffness recovery
            }
        }

        /// <summary>
        /// Returns the effective idle recovery stiffness for this frame.
        /// Crouching multiplies it down so shape matching stops resisting compression.
        /// </summary>
        private float EffectiveIdleStiffness =>
            _isCrouching ? idleRecoveryStiffness * crouchStiffnessMultiplier
                         : idleRecoveryStiffness;

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
