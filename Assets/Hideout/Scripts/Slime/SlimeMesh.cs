using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Generates and updates a 2D mesh from SlimeBody node positions each frame.
    /// Uses Catmull-Rom spline interpolation between perimeter nodes for a smooth outline.
    /// Attach to the same GameObject as SlimeBody.
    /// Requires a MeshFilter and MeshRenderer (auto-created if missing).
    /// </summary>
    [RequireComponent(typeof(SlimeBody))]
    public class SlimeMesh : MonoBehaviour
    {
        [Header("Mesh Quality")]
        [Tooltip("How many vertices to interpolate between each pair of nodes. " +
                 "Higher = smoother. 4 is a good balance.")]
        [Range(1, 8)]
        public int smoothSteps = 4;

        [Header("Rendering")]
        [Tooltip("Material to use for the slime mesh. Leave null to use a default white material.")]
        public Material material;

        [Tooltip("Sorting layer name for the mesh renderer.")]
        public string sortingLayerName = "Default";
        public int sortingOrder = 0;

        // ── Internal ─────────────────────────────────────────────────────────

        private SlimeBody _body;
        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;

        // Pre-allocated mesh data arrays (sized on Awake, reused every frame)
        private Vector3[] _vertices;
        private int[] _triangles;
        private Vector2[] _uvs;

        private int _smoothedCount;  // total perimeter vertices after interpolation

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _body = GetComponent<SlimeBody>();

            SetupRenderer();
            PreAllocateMeshArrays();
            BuildMesh();
        }

        private void LateUpdate()
        {
            // Update mesh every frame after physics (LateUpdate runs after FixedUpdate)
            UpdateMesh();
        }

        // ── Setup ─────────────────────────────────────────────────────────────

        private void SetupRenderer()
        {
            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            if (material == null)
            {
                // Default: unlit white so the 1-bit camera shader can remap it
                material = new Material(Shader.Find("Sprites/Default"));
                material.color = Color.white;
            }

            _renderer.material = material;
            _renderer.sortingLayerName = sortingLayerName;
            _renderer.sortingOrder = sortingOrder;
        }

        private void PreAllocateMeshArrays()
        {
            _smoothedCount = _body.nodeCount * smoothSteps;

            // +1 center vertex at index 0
            int vertexCount = _smoothedCount + 1;
            _vertices  = new Vector3[vertexCount];
            _uvs       = new Vector2[vertexCount];
            _triangles = new int[_smoothedCount * 3];
        }

        // ── Mesh construction ─────────────────────────────────────────────────

        private void BuildMesh()
        {
            _mesh = new Mesh();
            _mesh.name = "SlimeMesh";
            _mesh.MarkDynamic(); // Hint to GPU: this mesh changes every frame
            _filter.mesh = _mesh;

            UpdateMesh();
        }

        private void UpdateMesh()
        {
            if (_body.PerimeterPositions == null) return;

            Vector2 center = _body.CenterPosition;
            Vector2[] raw = _body.PerimeterPositions;
            int n = _body.nodeCount;

            // Center vertex (local space)
            _vertices[0] = transform.InverseTransformPoint(center);
            _uvs[0] = new Vector2(0.5f, 0.5f);

            // Catmull-Rom smoothed perimeter vertices
            for (int i = 0; i < n; i++)
            {
                // Catmull-Rom control points (wrap around)
                Vector2 p0 = raw[(i - 1 + n) % n];
                Vector2 p1 = raw[i];
                Vector2 p2 = raw[(i + 1) % n];
                Vector2 p3 = raw[(i + 2) % n];

                for (int s = 0; s < smoothSteps; s++)
                {
                    float t = s / (float)smoothSteps;
                    Vector2 pt = CatmullRom(p0, p1, p2, p3, t);

                    Vector2 centerWorld = _body.CenterPosition;
                    float maxDist = _body.bodyRadius * 2f;
                    if ((pt - centerWorld).sqrMagnitude > maxDist * maxDist)
                        pt = centerWorld + (pt - centerWorld).normalized * maxDist;

                    int vi = 1 + i * smoothSteps + s;
                    _vertices[vi] = transform.InverseTransformPoint(pt);

                    // UV: radial mapping from center
                    Vector2 dir = (pt - center).normalized;
                    _uvs[vi] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.y * 0.5f);
                }
            }

            // Triangle fan: center (0) → each pair of adjacent perimeter vertices
            for (int i = 0; i < _smoothedCount; i++)
            {
                int ti = i * 3;
                _triangles[ti]     = 0;
                _triangles[ti + 1] = 1 + i;
                _triangles[ti + 2] = 1 + (i + 1) % _smoothedCount;
            }

            // Push to GPU
            _mesh.vertices  = _vertices;
            _mesh.uv        = _uvs;
            _mesh.triangles = _triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        // ── Math ──────────────────────────────────────────────────────────────

        /// <summary>Catmull-Rom spline interpolation between p1 and p2.</summary>
        private static Vector2 CatmullRom(
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
