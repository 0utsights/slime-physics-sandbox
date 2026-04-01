using UnityEngine;

namespace Hideout.Slime
{
    [RequireComponent(typeof(SlimeBody))]
    public class SlimeMesh : MonoBehaviour
    {
        [Header("Mesh Quality")]
        [Range(1, 8)]
        public int smoothSteps = 4;

        [Header("Rendering")]
        public Material material;
        public string sortingLayerName = "Default";
        public int sortingOrder = 0;

        private SlimeBody _body;
        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;

        private Vector3[] _vertices;
        private int[] _triangles;
        private Vector2[] _uvs;
        private Vector2[] _sortedPositions;
        private float[] _angles;
        private int[] _sortIndices;

        private int _smoothedCount;

        private void Awake()
        {
            _body = GetComponent<SlimeBody>();
            SetupRenderer();
            PreAllocate();
            BuildMesh();
        }

        private void LateUpdate() => UpdateMesh();

        private void SetupRenderer()
        {
            _filter = GetComponent<MeshFilter>(); if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>(); if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                material.color = Color.white;
            }

            _renderer.material = material;
            _renderer.sortingLayerName = sortingLayerName;
            _renderer.sortingOrder = sortingOrder;
        }

        private void PreAllocate()
        {
            _smoothedCount   = _body.nodeCount * smoothSteps;
            int vertexCount  = _smoothedCount + 1;
            _vertices        = new Vector3[vertexCount];
            _uvs             = new Vector2[vertexCount];
            _triangles       = new int[_smoothedCount * 3];
            _sortedPositions = new Vector2[_body.nodeCount];
            _angles          = new float[_body.nodeCount];
            _sortIndices     = new int[_body.nodeCount];
        }

        private void BuildMesh()
        {
            _mesh = new Mesh();
            _mesh.name = "SlimeMesh";
            _mesh.MarkDynamic();
            _filter.mesh = _mesh;
            UpdateMesh();
        }

        private void UpdateMesh()
        {
            if (_body.PerimeterPositions == null) return;

            Vector2 center = _body.CenterPosition;
            int n = _body.nodeCount;

            // Sort nodes by angle around center to prevent triangle inversion
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = _body.PerimeterPositions[i] - center;
                _angles[i] = Mathf.Atan2(dir.y, dir.x);
                _sortIndices[i] = i;
            }
            SortByAngle(n);

            for (int i = 0; i < n; i++)
                _sortedPositions[i] = _body.PerimeterPositions[_sortIndices[i]];

            // Center vertex
            _vertices[0] = transform.InverseTransformPoint(center);
            _uvs[0] = new Vector2(0.5f, 0.5f);

            // Smoothed perimeter
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = _sortedPositions[(i - 1 + n) % n];
                Vector2 p1 = _sortedPositions[i];
                Vector2 p2 = _sortedPositions[(i + 1) % n];
                Vector2 p3 = _sortedPositions[(i + 2) % n];

                for (int s = 0; s < smoothSteps; s++)
                {
                    float t = s / (float)smoothSteps;
                    Vector2 pt = CatmullRom(p0, p1, p2, p3, t);

                    int vi = 1 + i * smoothSteps + s;
                    _vertices[vi] = transform.InverseTransformPoint(pt);

                    Vector2 dir = (pt - center).normalized;
                    _uvs[vi] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.y * 0.5f);
                }
            }

            // Triangle fan
            for (int i = 0; i < _smoothedCount; i++)
            {
                int ti = i * 3;
                _triangles[ti]     = 0;
                _triangles[ti + 1] = 1 + i;
                _triangles[ti + 2] = 1 + (i + 1) % _smoothedCount;
            }

            _mesh.vertices  = _vertices;
            _mesh.uv        = _uvs;
            _mesh.triangles = _triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        // Insertion sort — zero GC, fast for small n
        private void SortByAngle(int n)
        {
            for (int i = 1; i < n; i++)
            {
                int keyIdx   = _sortIndices[i];
                float keyVal = _angles[keyIdx];
                int j = i - 1;
                while (j >= 0 && _angles[_sortIndices[j]] > keyVal)
                {
                    _sortIndices[j + 1] = _sortIndices[j];
                    j--;
                }
                _sortIndices[j + 1] = keyIdx;
            }
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
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
            if (_mesh != null) Destroy(_mesh);
        }
    }
}

