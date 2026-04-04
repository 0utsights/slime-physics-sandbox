using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Viscous soft-body slime: center Rigidbody2D + N perimeter nodes connected
    /// by a spring network with cross-bracing and internal pressure.
    ///
    /// Spring network topology (per node i):
    ///   Radial   — node i ↔ center     (DistanceJoint2D, maxDistanceOnly = true)
    ///                                   Tether only — zero compressive force into ground.
    ///   Neighbor — node i ↔ node i+1   (SpringJoint2D — ring integrity + elasticity)
    ///   Brace    — node i ↔ node i+2   (SpringJoint2D — prevents topological inversion)
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
        [Tooltip("Force per unit of area deficit. Higher = harder to compress. Original used ~5.")]
        public float pressureStrength = 5f;

        [Header("Shape Recovery — Idle")]
        [Tooltip("Stiffness of recovery force when resting. Low = wobbly, high = rigid. 50–200.")]
        public float idleRecoveryStiffness = 150f;
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
        [Tooltip("How much to reduce pressure while crouching. 0 = no resistance to shape change, 1 = no change. Keep low so slime can flow into geometry.")]
        [Range(0f, 1f)]
        public float crouchPressureMultiplier = 0.1f;
        [Tooltip("Multiplier for neighbor spring frequency while crouching. Lower = ring reshapes easier. 0.2–0.5.")]
        [Range(0.05f, 1f)]
        public float crouchNeighborFrequencyMultiplier = 0.3f;

        [Header("Mass")]
        public float centerMass    = 2f;
        public float perimeterMass = 0.5f;

        [Header("Gravity")]
        public float gravityScale          = 1f;
        [Tooltip("Separate gravity scale for perimeter nodes. Lower = less downward force per step, easier for solver to hold nodes above terrain.")]
        public float perimeterGravityScale = 0.5f;

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
        [Tooltip("Force scale applied per unit of ground penetration depth to push nodes back out. Passed to SlimeNodeContact.")]
        public float antiSinkForceScale = 150f;

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
        private DistanceJoint2D[]  _radialSprings;
        private SpringJoint2D[]    _neighborSprings;
        private SlimeNodeContact[] _nodeContacts;

        // Shape matching rest state — computed once at build, never modified
        private Vector2[] _restOffsets;   // perimeter node offsets from rest centroid
        private float     _totalMass;

        private Vector2[] _lastValidPositions;
        private float     _restArea;  // polygon area of rest shape, computed once at build

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
            Physics2D.velocityIterations = 16;
            Physics2D.positionIterations = 8;
            Time.fixedDeltaTime          = 0.01f; // 100 Hz — halved timestep improves solver convergence quadratically

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
            UpdateCrouchSprings();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildPhysicsMaterial()
        {
            _physicsMaterial            = new PhysicsMaterial2D("SlimeMaterial");
            _physicsMaterial.friction   = 0.4f;
            _physicsMaterial.bounciness = bounciness;
        }

        private void BuildBody()
        {
            int n = nodeCount;

            PerimeterPositions  = new Vector2[n];
            _perimeterBodies    = new Rigidbody2D[n];
            _radialSprings      = new DistanceJoint2D[n];
            _neighborSprings    = new SpringJoint2D[n];
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
                _nodeContacts[i].Init(this, CornerEscapeForce, maxNodeSpeed, antiSinkForceScale);
            }

            // Total mass for centroid weighting
            _totalMass = centerMass + n * perimeterMass;

            // Rest area — computed once, used by ApplyPressure to detect compression
            _restArea = 0f;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                _restArea += _restOffsets[i].x * _restOffsets[next].y;
                _restArea -= _restOffsets[next].x * _restOffsets[i].y;
            }
            _restArea = Mathf.Abs(_restArea) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                // Radial: node ↔ center — DistanceJoint2D with maxDistanceOnly.
                var dj = _perimeterBodies[i].gameObject.AddComponent<DistanceJoint2D>();
                dj.connectedBody         = _centerBody;
                dj.distance              = bodyRadius;
                dj.maxDistanceOnly       = true;
                dj.autoConfigureDistance = false;
                dj.enableCollision       = false;
                _radialSprings[i]        = dj;

                // Neighbor: node i ↔ node i+1 (SpringJoint2D — bidirectional restoration)
                int   next  = (i + 1) % n;
                float nDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = AddSpring(
                    _perimeterBodies[i].gameObject, _perimeterBodies[next], nDist, neighborFrequency);
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
            rb.mass         = mass;
            rb.gravityScale = hasCollider ? perimeterGravityScale : gravityScale;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping          = damping;
            rb.angularDamping         = 0.05f;
            rb.interpolation          = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode              = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

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
            _centerBody.linearVelocity =
                Vector2.ClampMagnitude(_centerBody.linearVelocity, maxNodeSpeed);

            for (int i = 0; i < nodeCount; i++)
                _perimeterBodies[i].linearVelocity =
                    Vector2.ClampMagnitude(_perimeterBodies[i].linearVelocity, maxNodeSpeed);
        }

        // ── PRESSURE ─────────────────────────────────────────────────────────

        private void ApplyPressure()
        {
            int n = nodeCount;

            float currentArea = 0f;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                currentArea += PerimeterPositions[i].x * PerimeterPositions[next].y;
                currentArea -= PerimeterPositions[next].x * PerimeterPositions[i].y;
            }
            currentArea = Mathf.Abs(currentArea) * 0.5f;

            float deficit = _restArea - currentArea;
            if (deficit <= 0f) return;

            float forceMag = deficit * EffectivePressureStrength;

            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;

                Vector2 edgePrev = PerimeterPositions[i]    - PerimeterPositions[prev];
                Vector2 edgeNext = PerimeterPositions[next] - PerimeterPositions[i];
                Vector2 nPrev    = new Vector2(-edgePrev.y,  edgePrev.x).normalized;
                Vector2 nNext    = new Vector2(-edgeNext.y,  edgeNext.x).normalized;
                Vector2 outward  = ((nPrev + nNext) * 0.5f).normalized;

                // FIX: For grounded nodes, remove the component of pressure force
                // that pushes into the terrain. Pressure should expand the slime
                // outward but never push a grounded node through its contact surface.
                if (_nodeContacts[i].IsGrounded)
                {
                    Vector2 normal = _nodeContacts[i].ContactNormal;
                    float intoTerrain = Vector2.Dot(outward, -normal);
                    if (intoTerrain > 0f)
                        outward += normal * intoTerrain; // cancel the into-terrain component
                    // Re-normalize; if outward is near zero the node is being pushed
                    // directly into terrain — skip it entirely
                    float len = outward.magnitude;
                    if (len < 0.01f) continue;
                    outward /= len;
                }

                _perimeterBodies[i].AddForce(outward * forceMag);
            }
        }

        // ── RECOVERY ─────────────────────────────────────────────────────────

        private void ApplyRecovery()
        {
            float dt = Time.fixedDeltaTime;

            float stiffness, damping;

            if (_jumpActive)
            {
                _jumpTimer += dt;

                if (_centerBody.linearVelocity.y < 0.5f)
                    _jumpActive = false;
                else if (_jumpTimer > jumpRecoveryDelay + jumpRecoveryDuration)
                    _jumpActive = false;

                bool inWindow = _jumpActive && _jumpTimer >= jumpRecoveryDelay;
                stiffness = inWindow ? jumpRecoveryStiffness : EffectiveIdleStiffness;
                damping   = inWindow ? jumpRecoveryDamping   : idleRecoveryDamping;
            }
            else if (_impactRecoveryTimer > 0f)
            {
                _impactRecoveryTimer -= dt;
                float t = _impactRecoveryTimer / impactRecoveryDuration;
                stiffness = Mathf.Lerp(EffectiveIdleStiffness,  impactRecoveryStiffness, t);
                damping   = Mathf.Lerp(idleRecoveryDamping,     impactRecoveryDamping,   t);
            }
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
        ///
        /// GROUNDED-AWARE CLAMPING: For grounded nodes, the recovery force component
        /// that pushes into the terrain (opposing the contact normal) is removed.
        /// This prevents shape matching from fighting the contact solver and eliminates
        /// the primary cause of terrain intersection. The tangential component is kept
        /// so grounded nodes still slide toward their goal positions along the surface.
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

            float theta = Mathf.Atan2(a10 - a01, a00 + a11);
            float cosT  = Mathf.Cos(theta);
            float sinT  = Mathf.Sin(theta);

            Vector2 bodyVelocity = _centerBody.linearVelocity;

            for (int i = 0; i < n; i++)
            {
                Vector2 q       = _restOffsets[i];
                Vector2 goalPos = Centroid + new Vector2(
                    cosT * q.x - sinT * q.y,
                    sinT * q.x + cosT * q.y);

                Vector2 relVelocity = _perimeterBodies[i].linearVelocity - bodyVelocity;

                Vector2 force = stiffness * (goalPos - PerimeterPositions[i])
                              - damping   * relVelocity;

                float mag = force.magnitude;
                if (mag > maxRecoveryForce)
                    force = force * (maxRecoveryForce / mag);

                // FIX: Grounded-aware clamping.
                // If this node is touching terrain, remove the force component that
                // pushes into the surface. Keep the tangential component so the node
                // slides toward its goal along the surface rather than through it.
                if (_nodeContacts[i].IsGrounded)
                {
                    Vector2 normal = _nodeContacts[i].ContactNormal;
                    float intoSurface = Vector2.Dot(force, normal);
                    if (intoSurface < 0f)
                        force -= normal * intoSurface; // remove penetrating component
                }

                _perimeterBodies[i].AddForce(force);
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

        // ── CROUCH SPRING MODULATION ──────────────────────────────────────────
        // Reduces neighbor spring frequency while crouching so the ring can
        // reshape to fit crevice geometry. Restores full frequency on release.

        private void UpdateCrouchSprings()
        {
            float targetFreq = _isCrouching
                ? neighborFrequency * crouchNeighborFrequencyMultiplier
                : neighborFrequency;

            for (int i = 0; i < nodeCount; i++)
                _neighborSprings[i].frequency = targetFreq;
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
            _jumpActive = false;
        }

        /// <summary>
        /// Called by SlimeController each frame with the current crouch state.
        /// While crouching: applies downward force to center + airborne nodes,
        /// and surface-tangent flow force to grounded nodes so they slide along
        /// geometry rather than crushing flat against it.
        /// On release: triggers immediate recovery unless too many nodes are still
        /// in contact (tight crevice — recovery would fight the geometry).
        /// </summary>
        public void SetCrouching(bool crouching)
        {
            bool wascrouching = _isCrouching;
            _isCrouching = crouching;

            if (crouching)
            {
                // Center always gets straight-down force
                _centerBody?.AddForce(Vector2.down * crouchDownForce, ForceMode2D.Force);

                for (int i = 0; i < nodeCount; i++)
                {
                    if (_nodeContacts[i].IsGrounded)
                    {
                        // FIX: Grounded nodes get force projected along the contact
                        // surface tangent. This makes nodes flow along geometry
                        // (into crevices, around corners) instead of crushing flat.
                        Vector2 normal  = _nodeContacts[i].ContactNormal;
                        Vector2 gravity = Vector2.down;

                        // Project gravity onto the surface plane
                        float normalComponent = Vector2.Dot(gravity, normal);
                        Vector2 tangent = gravity - normal * normalComponent;

                        // Only apply if there's meaningful tangential component
                        // (node is on a slope or crevice wall, not flat ground)
                        float tangentMag = tangent.magnitude;
                        if (tangentMag > 0.1f)
                            _perimeterBodies[i].AddForce(tangent / tangentMag * crouchDownForce);
                        // On flat ground: no extra force on grounded nodes.
                        // Gravity + center weight already compress. Pushing down
                        // on nodes that are already on the floor just fights the solver.
                    }
                    else
                    {
                        // Airborne nodes get full downward force to compress into gaps
                        _perimeterBodies[i].AddForce(Vector2.down * crouchDownForce);
                    }
                }
            }
            else if (wascrouching)
            {
                if (GroundedRatio < crouchReleaseGroundedThreshold)
                    NotifyImpact();
            }
        }

        /// <summary>
        /// Returns the effective idle recovery stiffness for this frame.
        /// Crouching multiplies it down so shape matching stops resisting compression.
        /// </summary>
        private float EffectiveIdleStiffness =>
            _isCrouching ? idleRecoveryStiffness * crouchStiffnessMultiplier
                         : idleRecoveryStiffness;

        private float EffectivePressureStrength =>
            _isCrouching ? pressureStrength * crouchPressureMultiplier
                         : pressureStrength;

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (_nodesParent     != null) Destroy(_nodesParent);
            if (_physicsMaterial != null) Destroy(_physicsMaterial);
        }
    }
}
