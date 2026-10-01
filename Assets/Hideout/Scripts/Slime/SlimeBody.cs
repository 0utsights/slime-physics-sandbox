using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Viscous soft-body slime built on Unity's 2D physics solver.
    ///
    /// Architecture:
    ///   One heavy center Rigidbody2D (no collider, full gravity) anchors the body.
    ///   N lightweight perimeter Rigidbody2D nodes with CircleCollider2Ds form the
    ///   surface, on a dedicated "Slime" layer with self-collision disabled.
    ///
    /// Spring topology (per node i, set once at build, never modified at runtime):
    ///   Radial   — node i ↔ center   : DistanceJoint2D, maxDistanceOnly = true.
    ///              Tether only. Prevents outward explosion but exerts zero
    ///              compressive force into terrain.
    ///   Neighbor — node i ↔ node i+1 : SpringJoint2D. Ring connectivity + elasticity.
    ///
    /// Shape recovery (Müller et al. 2005 shape matching, 2D specialization):
    ///   Cross-covariance matrix Apq → best-fit rotation via atan2 → PD force
    ///   pulling each node toward its goal position in the rotated rest frame.
    ///   Damping acts on velocity relative to body-frame motion so it never
    ///   fights gravity or locomotion.
    ///
    ///   TERRAIN-AWARE CLAMPING: For grounded nodes, the recovery force component
    ///   that opposes the contact normal is stripped. The tangential component is
    ///   preserved so nodes slide toward goal positions along surfaces. This
    ///   prevents the fundamental failure mode where goal positions below terrain
    ///   generate forces that push nodes through the ground.
    ///
    /// Pressure (deficit-only):
    ///   Fires only when polygon area drops below rest area. Force = deficit *
    ///   strength along averaged adjacent edge normals. Same terrain-aware
    ///   clamping applied to prevent pressure from pushing grounded nodes into
    ///   surfaces.
    ///
    /// Crouch:
    ///   Recovery stiffness and pressure are multiplied down so the body goes
    ///   limp. Neighbor spring frequency is temporarily reduced so the ring can
    ///   reshape. Grounded nodes receive force along their surface tangent
    ///   (gravity-biased, computed by SlimeNodeContact) so they flow along
    ///   geometry into crevices rather than crushing flat.
    ///
    /// Cached simulation buffers; allocation behavior still needs profiling.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class SlimeBody : MonoBehaviour
    {
        // ── Inspector: Shape ──────────────────────────────────────────────────

        [Header("Body Shape")]
        [Range(6, 24)]
        public int   nodeCount     = 12;
        public float bodyRadius    = 0.5f;
        public float colliderRadius = 0.05f;

        // ── Inspector: Springs ────────────────────────────────────────────────

        [Header("Springs")]
        [Tooltip("Neighbor ring spring frequency (node↔node).")]
        public float neighborFrequency = 4f;

        [Range(0f, 1f)]
        [Tooltip("Spring damping ratio. 0.2 = bouncy, 1.0 = critically damped.")]
        public float springDamping = 0.2f;

        // ── Inspector: Elasticity ─────────────────────────────────────────────

        [Header("Elasticity")]
        [Range(0f, 1f)]
        [Tooltip("Physics material bounciness. Keep ≤ 0.5.")]
        public float bounciness = 0.3f;

        // ── Inspector: Pressure ───────────────────────────────────────────────

        [Header("Pressure")]
        [Tooltip("Force per unit area deficit. Higher = harder to compress.")]
        public float pressureStrength = 5f;

        // ── Inspector: Recovery ───────────────────────────────────────────────

        [Header("Shape Recovery — Idle")]
        public float idleRecoveryStiffness = 150f;
        public float idleRecoveryDamping   = 4f;

        [Header("Shape Recovery — Impact")]
        public float impactRecoveryStiffness = 250f;
        public float impactRecoveryDamping   = 8f;
        public float impactRecoveryDuration  = 0.25f;

        [Header("Shape Recovery — Jump")]
        public float jumpRecoveryStiffness = 180f;
        public float jumpRecoveryDamping   = 6f;
        public float jumpRecoveryDelay     = 0.05f;
        public float jumpRecoveryDuration  = 0.3f;

        // ── Inspector: Crouch ─────────────────────────────────────────────────

        [Header("Crouch")]
        [Tooltip("Downward force applied while crouching.")]
        public float crouchDownForce = 6f;

        [Range(0f, 1f)]
        [Tooltip("Idle stiffness multiplier while crouching. 0.05 = nearly limp.")]
        public float crouchStiffnessMultiplier = 0.05f;

        [Range(0f, 1f)]
        [Tooltip("Pressure multiplier while crouching. Low = flows freely.")]
        public float crouchPressureMultiplier = 0.1f;

        [Range(0.05f, 1f)]
        [Tooltip("Neighbor spring frequency multiplier while crouching. Low = ring reshapes easily.")]
        public float crouchNeighborFrequencyMultiplier = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("GroundedRatio below which crouch release triggers recovery burst.")]
        public float crouchReleaseGroundedThreshold = 0.4f;

        // ── Inspector: Mass & Gravity ─────────────────────────────────────────

        [Header("Mass")]
        public float centerMass    = 2f;
        public float perimeterMass = 0.5f;

        [Header("Gravity")]
        public float gravityScale          = 1f;
        [Tooltip("Perimeter gravity scale. Lower = less downward force per step, easier for solver.")]
        public float perimeterGravityScale = 0.5f;

        // ── Inspector: Damping ────────────────────────────────────────────────

        [Header("Damping — Airborne")]
        public float airborneDamping       = 0.05f;
        public float airborneCenterDamping = 0.1f;

        [Header("Damping — Grounded")]
        public float groundedDamping       = 1.0f;
        public float groundedCenterDamping = 1.5f;
        public float dampingTransitionSpeed = 4f;

        // ── Inspector: Angular Separation ─────────────────────────────────────

        [Header("Angular Separation")]
        [Tooltip("Min angular gap as fraction of ideal spacing. 0.5–0.7.")]
        public float minAngularSeparation = 0.6f;
        public float separationForce = 5f;

        // ── Inspector: Safety ─────────────────────────────────────────────────

        [Header("Safety")]
        public float maxNodeSpeed       = 20f;
        public float maxRecoveryForce   = 500f;
        [Tooltip("Anti-sink force scale passed to SlimeNodeContact.")]
        public float antiSinkForceScale = 150f;

        // ── Public API (read) ─────────────────────────────────────────────────

        /// <summary>World positions of all perimeter nodes. Updated each FixedUpdate.</summary>
        public Vector2[] PerimeterPositions { get; private set; }

        /// <summary>World position of the center body.</summary>
        public Vector2 CenterPosition => _center != null
            ? (Vector2)_center.position
            : (Vector2)transform.position;

        /// <summary>Velocity of the center body.</summary>
        public Vector2 Velocity => _center != null
            ? _center.linearVelocity
            : Vector2.zero;

        /// <summary>0 = fully airborne, 1 = all nodes grounded. Smoothed.</summary>
        public float GroundedRatio { get; private set; }

        // Jump eligibility uses current upward-facing contacts, not the smoothed
        // deformation ratio (which can stay positive after leaving the floor).
        public bool HasSupport
        {
            get
            {
                if (_contacts == null) return false;
                foreach (var contact in _contacts)
                    if (contact.IsGrounded && contact.ContactNormal.y >= 0.5f)
                        return true;
                return false;
            }
        }

        /// <summary>Mass-weighted centroid. Cached each FixedUpdate.</summary>
        public Vector2 Centroid { get; private set; }

        // ── Internal state ────────────────────────────────────────────────────

        private Rigidbody2D        _center;
        private Rigidbody2D[]      _nodes;
        private SpringJoint2D[]    _neighborSprings;
        private SlimeNodeContact[] _contacts;

        private Vector2[] _restOffsets;        // rest-shape offsets from centroid
        private Vector2[] _lastValidPositions; // NaN recovery fallback
        private float     _totalMass;
        private float     _restArea;

        private float _perimeterDamping;
        private float _centerDamping;

        // State machines
        private bool  _isCrouching;
        private float _impactTimer;   // counts down from impactRecoveryDuration
        private float _jumpTimer;     // counts up from jump moment
        private bool  _jumpActive;

        private PhysicsMaterial2D _material;
        private GameObject        _nodesParent;

        private const float CornerEscapeForce = 5f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            nodeCount = Mathf.Clamp(nodeCount, 6, 24);
            bodyRadius = Mathf.Max(0.05f, bodyRadius);
            centerMass = Mathf.Max(0.01f, centerMass);
            perimeterMass = Mathf.Max(0.01f, perimeterMass);
            ConfigurePhysics();
            BuildBody();
            _perimeterDamping = airborneDamping;
            _centerDamping    = airborneCenterDamping;
        }

        private void FixedUpdate()
        {
            Sense();
            Actuate();
        }

        // ── Physics configuration ─────────────────────────────────────────────

        private void ConfigurePhysics()
        {
            int slimeLayer = LayerMask.NameToLayer("Slime");
            if (slimeLayer != -1)
                Physics2D.IgnoreLayerCollision(slimeLayer, slimeLayer, true);
            else
                Debug.LogWarning("[SlimeBody] 'Slime' layer missing. Add it in Tags and Layers.");

            _material            = new PhysicsMaterial2D("SlimeMat");
            _material.friction   = 0.4f;
            _material.bounciness = bounciness;
        }

        // ── Body construction ─────────────────────────────────────────────────

        private void BuildBody()
        {
            int n = nodeCount;

            PerimeterPositions  = new Vector2[n];
            _nodes              = new Rigidbody2D[n];
            _neighborSprings    = new SpringJoint2D[n];
            _contacts           = new SlimeNodeContact[n];
            _restOffsets        = new Vector2[n];
            _lastValidPositions = new Vector2[n];

            _nodesParent = new GameObject("SlimeNodes");
            _nodesParent.transform.SetParent(transform);
            _nodesParent.transform.localPosition = Vector3.zero;

            _center = MakeRigidbody("Center", Vector2.zero, centerMass,
                airborneCenterDamping, gravityScale, collider: false);

            float step = 2f * Mathf.PI / n;
            for (int i = 0; i < n; i++)
            {
                float angle   = i * step;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * bodyRadius;

                _restOffsets[i]        = offset;
                _lastValidPositions[i] = (Vector2)transform.position + offset;
                _nodes[i]              = MakeRigidbody($"Node_{i}", offset, perimeterMass,
                    airborneDamping, perimeterGravityScale, collider: true);

                _contacts[i] = _nodes[i].gameObject.AddComponent<SlimeNodeContact>();
                _contacts[i].Init(this, CornerEscapeForce, maxNodeSpeed, antiSinkForceScale);
            }

            _totalMass = centerMass + n * perimeterMass;

            // Rest area via shoelace formula
            _restArea = 0f;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                _restArea += _restOffsets[i].x * _restOffsets[next].y
                           - _restOffsets[next].x * _restOffsets[i].y;
            }
            _restArea = Mathf.Abs(_restArea) * 0.5f;

            // Wire joints
            for (int i = 0; i < n; i++)
            {
                // Radial tether
                var dj = _nodes[i].gameObject.AddComponent<DistanceJoint2D>();
                dj.connectedBody        = _center;
                dj.distance             = bodyRadius;
                dj.maxDistanceOnly      = true;
                dj.autoConfigureDistance = false;
                dj.enableCollision      = false;

                // Neighbor spring
                int next    = (i + 1) % n;
                float nDist = Vector2.Distance(_restOffsets[i], _restOffsets[next]);
                _neighborSprings[i] = MakeSpring(
                    _nodes[i].gameObject, _nodes[next], nDist, neighborFrequency);
            }
        }

        private Rigidbody2D MakeRigidbody(string name, Vector2 localPos, float mass,
            float damping, float gravity, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_nodesParent.transform);
            go.transform.localPosition = localPos;

            int slimeLayer = LayerMask.NameToLayer("Slime");
            go.layer = slimeLayer != -1 ? slimeLayer : gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass                   = mass;
            rb.gravityScale           = gravity;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping          = damping;
            rb.angularDamping         = 0.05f;
            rb.interpolation          = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode              = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

            if (collider)
            {
                var col        = go.AddComponent<CircleCollider2D>();
                col.radius         = colliderRadius;
                col.sharedMaterial = _material;
            }

            return rb;
        }

        private SpringJoint2D MakeSpring(GameObject from, Rigidbody2D to,
            float restLength, float frequency)
        {
            var s = from.AddComponent<SpringJoint2D>();
            s.connectedBody        = to;
            s.distance             = restLength;
            s.frequency            = frequency;
            s.dampingRatio         = springDamping;
            s.autoConfigureDistance = false;
            s.enableCollision      = false;
            return s;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SENSE — read world state into cached arrays
        // ══════════════════════════════════════════════════════════════════════

        private void Sense()
        {
            SensePositions();
            SenseCentroid();
            SenseGroundedRatio();
        }

        private void SensePositions()
        {
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 pos = _nodes[i].position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y))
                {
                    _nodes[i].position       = _lastValidPositions[i];
                    _nodes[i].linearVelocity = Vector2.zero;
                    PerimeterPositions[i]    = _lastValidPositions[i];
                }
                else
                {
                    PerimeterPositions[i]  = pos;
                    _lastValidPositions[i] = pos;
                }
            }
        }

        private void SenseCentroid()
        {
            Vector2 sum = _center.position * centerMass;
            for (int i = 0; i < nodeCount; i++)
                sum += PerimeterPositions[i] * perimeterMass;
            Centroid = sum / _totalMass;
        }

        private void SenseGroundedRatio()
        {
            int grounded = 0;
            for (int i = 0; i < nodeCount; i++)
                if (_contacts[i].IsGrounded) grounded++;

            float target = grounded / (float)nodeCount;
            GroundedRatio = Mathf.MoveTowards(
                GroundedRatio, target,
                dampingTransitionSpeed * Time.fixedDeltaTime);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ACTUATE — apply forces and constraints
        // ══════════════════════════════════════════════════════════════════════

        private void Actuate()
        {
            ClampVelocities();
            ApplyPressure();
            ApplyRecovery();
            EnforceAngularSeparation();
            UpdateDamping();
            UpdateCrouchSprings();
        }

        // ── Velocity clamping ─────────────────────────────────────────────────

        private void ClampVelocities()
        {
            _center.linearVelocity =
                Vector2.ClampMagnitude(_center.linearVelocity, maxNodeSpeed);

            for (int i = 0; i < nodeCount; i++)
                _nodes[i].linearVelocity =
                    Vector2.ClampMagnitude(_nodes[i].linearVelocity, maxNodeSpeed);
        }

        // ── Pressure ──────────────────────────────────────────────────────────
        //
        // Deficit-only model: outward force proportional to how much the polygon
        // area has shrunk below rest. No force when at or above rest area.
        //
        // Terrain-aware: for grounded nodes, the pressure force component that
        // would push into the contact surface is removed. Pressure should expand
        // the body outward into free space, not through solid geometry.

        private void ApplyPressure()
        {
            int n = nodeCount;

            // Current polygon area via shoelace
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                area += PerimeterPositions[i].x * PerimeterPositions[next].y
                      - PerimeterPositions[next].x * PerimeterPositions[i].y;
            }
            area = Mathf.Abs(area) * 0.5f;

            float deficit = _restArea - area;
            if (deficit <= 0f) return;

            float strength = deficit * EffectivePressure;

            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;

                // Outward normal at node: average of adjacent edge perpendiculars
                Vector2 ePrev   = PerimeterPositions[i]    - PerimeterPositions[prev];
                Vector2 eNext   = PerimeterPositions[next] - PerimeterPositions[i];
                // Nodes are built counterclockwise, so the right-hand edge
                // perpendicular points outward. Left-hand normals compress it.
                Vector2 nPrev   = SlimeMath.OutwardNormal(ePrev);
                Vector2 nNext   = SlimeMath.OutwardNormal(eNext);
                Vector2 outward = ((nPrev + nNext) * 0.5f).normalized;

                // Terrain clamp: strip the into-surface component
                if (_contacts[i].IsGrounded)
                    outward = ClampAwayFromSurface(outward, _contacts[i].ContactNormal);

                if (outward.sqrMagnitude < 0.0001f) continue;

                _nodes[i].AddForce(outward * strength);
            }
        }

        // ── Shape recovery ────────────────────────────────────────────────────
        //
        // Müller et al. 2005 shape matching, 2D polar decomposition.
        //
        // Three contexts share one function with different stiffness/damping:
        //   Idle    — constant low-stiffness baseline
        //   Impact  — high-stiffness burst after landing, fades linearly
        //   Jump    — medium-stiffness unfurl during ascent window
        //
        // TERRAIN-AWARE CLAMPING: For grounded nodes, the component of the
        // recovery force that opposes the contact normal is removed. This
        // preserves tangential sliding toward goal positions while preventing
        // forces from driving nodes through terrain — the root cause of
        // intersection in the original implementation.

        private void ApplyRecovery()
        {
            float dt = Time.fixedDeltaTime;

            float stiffness, damping;

            if (_jumpActive)
            {
                _jumpTimer += dt;

                if (_center.linearVelocity.y < 0.5f)
                    _jumpActive = false;
                else if (_jumpTimer > jumpRecoveryDelay + jumpRecoveryDuration)
                    _jumpActive = false;

                bool inWindow = _jumpActive && _jumpTimer >= jumpRecoveryDelay;
                stiffness = inWindow ? jumpRecoveryStiffness : EffectiveStiffness;
                damping   = inWindow ? jumpRecoveryDamping   : idleRecoveryDamping;
            }
            else if (_impactTimer > 0f)
            {
                _impactTimer -= dt;
                float t = _impactTimer / impactRecoveryDuration;
                stiffness = Mathf.Lerp(EffectiveStiffness, impactRecoveryStiffness, t);
                damping   = Mathf.Lerp(idleRecoveryDamping, impactRecoveryDamping,  t);
            }
            else
            {
                stiffness = EffectiveStiffness;
                damping   = idleRecoveryDamping;
            }

            RecoverShape(stiffness, damping);
        }

        private void RecoverShape(float stiffness, float damping)
        {
            int n = nodeCount;

            // Cross-covariance matrix Apq
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

            // Best-fit rotation via 2D polar decomposition
            float theta = Mathf.Atan2(a10 - a01, a00 + a11);
            float cosT  = Mathf.Cos(theta);
            float sinT  = Mathf.Sin(theta);

            // Body-frame velocity: damp only relative motion so recovery doesn't
            // fight gravity, locomotion, or jump impulse
            Vector2 bodyVel = _center.linearVelocity;

            for (int i = 0; i < n; i++)
            {
                // Goal position in rotated rest frame
                Vector2 q    = _restOffsets[i];
                Vector2 goal = Centroid + new Vector2(
                    cosT * q.x - sinT * q.y,
                    sinT * q.x + cosT * q.y);

                Vector2 relVel = _nodes[i].linearVelocity - bodyVel;

                Vector2 force = stiffness * (goal - PerimeterPositions[i])
                              - damping  * relVel;

                // Clamp magnitude
                float mag = force.magnitude;
                if (mag > maxRecoveryForce)
                    force *= maxRecoveryForce / mag;

                // Terrain-aware clamping: strip into-surface component
                if (_contacts[i].IsGrounded)
                    force = ClampAwayFromSurface(force, _contacts[i].ContactNormal);

                _nodes[i].AddForce(force);
            }
        }

        // ── Angular separation ────────────────────────────────────────────────
        // Keeps nodes evenly distributed around the ring perimeter.

        private void EnforceAngularSeparation()
        {
            Vector2 center   = _center.position;
            float idealAngle = (2f * Mathf.PI) / nodeCount;
            float minAngle   = idealAngle * minAngularSeparation;

            for (int i = 0; i < nodeCount; i++)
            {
                int next = (i + 1) % nodeCount;

                Vector2 dirA = _nodes[i].position    - center;
                Vector2 dirB = _nodes[next].position - center;

                float diff = Mathf.DeltaAngle(
                    Mathf.Atan2(dirA.y, dirA.x) * Mathf.Rad2Deg,
                    Mathf.Atan2(dirB.y, dirB.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;

                if (Mathf.Abs(diff) >= minAngle) continue;

                float   violation = minAngle - Mathf.Abs(diff);
                float   sign      = diff >= 0f ? 1f : -1f;
                Vector2 tangA     = new Vector2(-dirA.normalized.y,  dirA.normalized.x);
                Vector2 tangB     = new Vector2(-dirB.normalized.y,  dirB.normalized.x);

                _nodes[i].AddForce(   -tangA * sign * violation * separationForce);
                _nodes[next].AddForce( tangB * sign * violation * separationForce);
            }
        }

        // ── Damping transition ────────────────────────────────────────────────

        private void UpdateDamping()
        {
            float targetP = Mathf.Lerp(airborneDamping,       groundedDamping,       GroundedRatio);
            float targetC = Mathf.Lerp(airborneCenterDamping, groundedCenterDamping, GroundedRatio);
            float dt      = dampingTransitionSpeed * Time.fixedDeltaTime;

            _perimeterDamping = Mathf.MoveTowards(_perimeterDamping, targetP, dt);
            _centerDamping    = Mathf.MoveTowards(_centerDamping,    targetC, dt);

            for (int i = 0; i < nodeCount; i++)
                _nodes[i].linearDamping = _perimeterDamping;
            _center.linearDamping = _centerDamping;
        }

        // ── Crouch spring modulation ──────────────────────────────────────────
        // Temporarily reduces neighbor spring frequency while crouching so the
        // ring perimeter can reshape to match crevice geometry instead of
        // snapping back to its rest-length polygon.

        private void UpdateCrouchSprings()
        {
            float freq = _isCrouching
                ? neighborFrequency * crouchNeighborFrequencyMultiplier
                : neighborFrequency;

            for (int i = 0; i < nodeCount; i++)
                _neighborSprings[i].frequency = freq;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PUBLIC API — called by SlimeController and SlimeNodeContact
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Applies horizontal movement force to center body.</summary>
        public void AddMovementForce(Vector2 force) =>
            _center?.AddForce(force, ForceMode2D.Force);

        /// <summary>
        /// Distributes a mass-proportional impulse across all bodies.
        /// Resets damping to airborne values so the impulse isn't absorbed.
        /// Activates the jump recovery unfurl window.
        /// </summary>
        public void AddImpulse(Vector2 impulse)
        {
            if (_center == null) return;

            // Reset damping so it doesn't eat the impulse
            _perimeterDamping = airborneDamping;
            _centerDamping    = airborneCenterDamping;
            for (int i = 0; i < nodeCount; i++)
                _nodes[i].linearDamping = _perimeterDamping;
            _center.linearDamping = _centerDamping;

            _center.AddForce(impulse * centerMass, ForceMode2D.Impulse);
            for (int i = 0; i < nodeCount; i++)
                _nodes[i].AddForce(impulse * perimeterMass, ForceMode2D.Impulse);

            _jumpActive = true;
            _jumpTimer  = 0f;
        }

        /// <summary>Triggers impact recovery burst. Called by SlimeNodeContact on landing.</summary>
        public void NotifyImpact()
        {
            _impactTimer = impactRecoveryDuration;
            _jumpActive  = false;
        }

        /// <summary>Restores the complete spring body, not just its root transform.</summary>
        public void ResetPose(Vector2 position)
        {
            if (_center == null) return;
            _center.position = position;
            _center.linearVelocity = Vector2.zero;
            _center.angularVelocity = 0f;
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 nodePosition = position + _restOffsets[i];
                _nodes[i].position = nodePosition;
                _nodes[i].linearVelocity = Vector2.zero;
                _nodes[i].angularVelocity = 0f;
                _lastValidPositions[i] = nodePosition;
                PerimeterPositions[i] = nodePosition;
                _contacts[i].ClearContact();
            }
            _isCrouching = false;
            _jumpActive = false;
            _impactTimer = 0f;
            GroundedRatio = 0f;
            Centroid = position;
            UpdateCrouchSprings();
            GetComponent<SlimeMesh>()?.ResetVisualState();
            GetComponent<SlimeController>()?.ResetInputState();
        }

        /// <summary>
        /// Sets crouch state each physics frame.
        ///
        /// While crouching:
        ///   Center body receives straight downward force.
        ///   Airborne nodes receive straight downward force (compress into gaps).
        ///   Grounded nodes receive force along their surface tangent — this is
        ///   the key difference from the original. Tangent force slides nodes
        ///   along crevice walls and slopes instead of crushing them flat against
        ///   surfaces where the solver already holds them in place.
        ///
        /// On release:
        ///   If few nodes are grounded (open air), trigger recovery burst.
        ///   If many nodes are grounded (still wedged), suppress recovery so
        ///   shape matching doesn't fight the crevice geometry.
        /// </summary>
        public void SetCrouching(bool crouching)
        {
            bool was = _isCrouching;
            _isCrouching = crouching;

            if (crouching)
            {
                _center?.AddForce(Vector2.down * crouchDownForce, ForceMode2D.Force);

                for (int i = 0; i < nodeCount; i++)
                {
                    if (!_contacts[i].IsGrounded)
                    {
                        // Airborne: straight down into whatever gap is below
                        _nodes[i].AddForce(Vector2.down * crouchDownForce);
                        continue;
                    }

                    // Grounded: flow along surface. SurfaceTangent is gravity-biased
                    // by SlimeNodeContact — it points downhill on slopes and toward
                    // body center on flat ground.
                    Vector2 tangent = _contacts[i].SurfaceTangent;
                    float tangentMag = tangent.magnitude;

                    if (tangentMag > 0.1f)
                        _nodes[i].AddForce(tangent / tangentMag * crouchDownForce);
                    // On flat ground (tangent ≈ 0): no extra force. Gravity + center
                    // mass already provide compression. Pushing down on nodes the
                    // solver is already holding against the floor just generates
                    // penetration forces that the anti-sink has to fight.
                }
            }
            else if (was)
            {
                // Release: recover unless still wedged in a tight crevice
                if (GroundedRatio < crouchReleaseGroundedThreshold)
                    NotifyImpact();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Effective idle recovery stiffness. Reduced during crouch so shape
        /// matching stops resisting compression.
        /// </summary>
        private float EffectiveStiffness =>
            _isCrouching ? idleRecoveryStiffness * crouchStiffnessMultiplier
                         : idleRecoveryStiffness;

        /// <summary>Effective pressure strength. Reduced during crouch.</summary>
        private float EffectivePressure =>
            _isCrouching ? pressureStrength * crouchPressureMultiplier
                         : pressureStrength;

        /// <summary>
        /// Removes the component of a force vector that pushes into a surface
        /// defined by the given outward normal. Returns the clamped vector.
        /// If the force is entirely into the surface, returns near-zero.
        ///
        /// Used by both RecoverShape and ApplyPressure to prevent any internal
        /// force from fighting the contact solver on grounded nodes.
        /// </summary>
        private static Vector2 ClampAwayFromSurface(Vector2 force, Vector2 surfaceNormal)
        {
            float intoSurface = Vector2.Dot(force, surfaceNormal);
            if (intoSurface < 0f)
                force -= surfaceNormal * intoSurface;
            return force;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (_nodesParent != null) Destroy(_nodesParent);
            if (_material    != null) Destroy(_material);
        }
    }
}
