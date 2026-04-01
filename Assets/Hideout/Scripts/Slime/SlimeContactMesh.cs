using System.Collections;
using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Renders a contact skirt between slime perimeter nodes and terrain surfaces,
    /// closing the visual gap caused by the collider radius.
    /// Attach to the same GameObject as SlimeBody and SlimeMesh.
    ///
    /// Two modes:
    ///   GapFill — thin quads that just close the collider gap.
    ///   Pool    — wider quads that extend onto the surface for a liquid pooling look.
    ///
    /// Fix log:
    ///   - Raycast origin offset by colliderRadius so the cast starts at the physical
    ///     collider surface edge, not the node center (prevents immediate self-hit).
    ///   - Degenerate (no-hit) tipPos now projects to full cast endpoint instead of
    ///     collapsing back to nodePos — quads have correct width even off terrain.
    ///   - Uses parent transform.InverseTransformPoint instead of child (child is
    ///     identity-relative, so the child call was redundant indirection).
    ///   - Null/empty guard on PerimeterPositions in LateUpdate, matching SlimeMesh style.
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
            if (_body == null || _body.PerimeterPositions == null ||
                _body.nodeCount == 0 || _body.PerimeterPositions.Length == 0) return;

            Vector2 center  = _body.CenterPosition;
            int n           = _body.nodeCount;
            float castDist  = mode == ContactMode.GapFill ? gapFillDistance : poolDistance;
            float halfWidth = (mode == ContactMode.GapFill ? gapFillWidth : poolWidth) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                Vector2 nodePos = _body.PerimeterPositions[i];
                Vector2 dir     = (nodePos - center).normalized;
                Vector2 tangent = new Vector2(-dir.y, dir.x);

                // BUG FIX: offset ray origin past the collider sphere so we don't
                // immediately self-hit terrain that the collider is already touching.
                Vector2 rayOrigin = nodePos + dir * _body.colliderRadius;
                RaycastHit2D hit  = Physics2D.Raycast(rayOrigin, dir, castDist, terrainLayer);

                // BUG FIX: degenerate (no-hit) case projects to the full cast endpoint
                // rather than collapsing back to nodePos (which produced zero-area quads).
                Vector2 tipPos = hit.collider != null
                    ? hit.point
                    : rayOrigin + dir * castDist;

                int vi = i * 4;
                // BUG FIX: use parent transform.InverseTransformPoint — the child is
                // identity-relative so the old child call was redundant indirection.
                _vertices[vi]     = transform.InverseTransformPoint(nodePos - tangent * halfWidth);
                _vertices[vi + 1] = transform.InverseTransformPoint(nodePos + tangent * halfWidth);
                _vertices[vi + 2] = transform.InverseTransformPoint(tipPos  + tangent * halfWidth);
                _vertices[vi + 3] = transform.InverseTransformPoint(tipPos  - tangent * halfWidth);
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
