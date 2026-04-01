using System.Collections;
using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Renders a contact skirt between slime perimeter nodes and terrain surfaces,
    /// closing the visual gap caused by the collider radius.
    /// Attach to the same GameObject as SlimeBody and SlimeMesh.
    /// Two modes:
    ///   GapFill — thin quads that just close the collider gap.
    ///   Pool    — wider quads that extend onto the surface for a liquid pooling look.
    /// </summary>
    [RequireComponent(typeof(SlimeBody))]
    public class SlimeContactMesh : MonoBehaviour
    {
        public enum ContactMode { GapFill, Pool }

        [Header("Mode")]
        public ContactMode mode = ContactMode.GapFill;

        [Header("Gap Fill")]
        public float gapFillDistance = 0.08f;
        public float gapFillWidth    = 0.02f;

        [Header("Pool")]
        public float poolDistance = 0.25f;
        public float poolWidth    = 0.15f;

        [Header("Rendering")]
        public Material material;
        public string sortingLayerName = "Default";
        public int sortingOrder = -1;

        [Header("Collision")]
        public LayerMask terrainLayer = ~0;

        private SlimeBody _body;
        private Mesh _mesh;
        private Transform _childTransform;

        private Vector3[] _vertices;
        private int[]     _triangles;
        private Vector2[] _uvs;

        private bool _ready;

        private IEnumerator Start()
        {
            _body = GetComponent<SlimeBody>();
            yield return null; // wait one frame for SlimeBody.Awake to finish

            var child = new GameObject("SlimeContactMesh");
            child.transform.SetParent(transform);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale    = Vector3.one;
            _childTransform = child.transform;

            var filter   = child.AddComponent<MeshFilter>();
            var renderer = child.AddComponent<MeshRenderer>();

            if (material == null)
            {
                material       = new Material(Shader.Find("Sprites/Default"));
                material.color = Color.white;
            }

            renderer.material         = material;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder     = sortingOrder;

            int n      = _body.nodeCount;
            _vertices  = new Vector3[n * 4];
            _triangles = new int[n * 6];
            _uvs       = new Vector2[n * 4];

            // Triangle indices never change — build once
            for (int i = 0; i < n; i++)
            {
                int vi = i * 4;
                int ti = i * 6;
                _triangles[ti]     = vi;
                _triangles[ti + 1] = vi + 1;
                _triangles[ti + 2] = vi + 2;
                _triangles[ti + 3] = vi;
                _triangles[ti + 4] = vi + 2;
                _triangles[ti + 5] = vi + 3;
            }

            // UV layout: node edge = v=0, surface edge = v=1
            for (int i = 0; i < n; i++)
            {
                int vi = i * 4;
                _uvs[vi]     = new Vector2(0f, 0f);
                _uvs[vi + 1] = new Vector2(1f, 0f);
                _uvs[vi + 2] = new Vector2(1f, 1f);
                _uvs[vi + 3] = new Vector2(0f, 1f);
            }

            _mesh = new Mesh { name = "SlimeContactMesh" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0);
            filter.mesh = _mesh;

            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready) return;

            Vector2 center    = _body.CenterPosition;
            int n             = _body.nodeCount;
            float castDist    = mode == ContactMode.GapFill ? gapFillDistance : poolDistance;
            float halfWidth   = (mode == ContactMode.GapFill ? gapFillWidth : poolWidth) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                Vector2 nodePos = _body.PerimeterPositions[i];
                Vector2 dir     = (nodePos - center).normalized;
                Vector2 tangent = new Vector2(-dir.y, dir.x);

                RaycastHit2D hit = Physics2D.Raycast(nodePos, dir, castDist, terrainLayer);

                // Degenerate (zero-area) quad when no surface is hit
                Vector2 tipPos = hit.collider != null ? hit.point : nodePos;

                int vi = i * 4;
                _vertices[vi]     = _childTransform.InverseTransformPoint(nodePos - tangent * halfWidth);
                _vertices[vi + 1] = _childTransform.InverseTransformPoint(nodePos + tangent * halfWidth);
                _vertices[vi + 2] = _childTransform.InverseTransformPoint(tipPos  + tangent * halfWidth);
                _vertices[vi + 3] = _childTransform.InverseTransformPoint(tipPos  - tangent * halfWidth);
            }

            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
