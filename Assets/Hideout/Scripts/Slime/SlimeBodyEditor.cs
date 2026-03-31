#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Hideout.Slime.Editor
{
    /// <summary>
    /// Custom editor for SlimeBody.
    /// Draws a Jelly Sprites-style Scene view preview:
    /// - Green circles at each perimeter node position
    /// - Lines from center to each node
    /// - Perimeter ring connecting all nodes
    /// - Yellow center dot
    /// - Inspector shows all tuning parameters cleanly grouped
    /// </summary>
    [CustomEditor(typeof(SlimeBody))]
    public class SlimeBodyEditor : UnityEditor.Editor
    {
        // Colors
        private static readonly Color NodeColor      = new Color(0.2f, 0.9f, 0.2f, 1f);
        private static readonly Color RingColor      = new Color(0.2f, 0.9f, 0.2f, 0.6f);
        private static readonly Color SpokeColor     = new Color(0.2f, 0.9f, 0.2f, 0.3f);
        private static readonly Color CenterColor    = new Color(1f, 0.9f, 0.1f, 1f);
        private static readonly Color NeighborColor  = new Color(0.2f, 0.6f, 1f, 0.4f);

        private void OnSceneGUI()
        {
            SlimeBody slime = (SlimeBody)target;
            Transform t = slime.transform;
            Vector3 center = t.position;

            int n = slime.nodeCount;
            float angleStep = 360f / n;
            Vector3[] nodePositions = new Vector3[n];

            // Compute preview positions
            for (int i = 0; i < n; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                nodePositions[i] = center + new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f
                ) * slime.bodyRadius;
            }

            // Spokes: center → each node
            Handles.color = SpokeColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(center, nodePositions[i]);

            // Neighbor spring lines
            Handles.color = NeighborColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(nodePositions[i], nodePositions[(i + 1) % n]);

            // Perimeter ring (thicker)
            Handles.color = RingColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(nodePositions[i], nodePositions[(i + 1) % n]);

            // Node circles
            Handles.color = NodeColor;
            for (int i = 0; i < n; i++)
            {
                Handles.DrawWireDisc(nodePositions[i], Vector3.forward, slime.colliderRadius);

                // Node label
                Handles.Label(
                    nodePositions[i] + Vector3.up * (slime.colliderRadius + 0.05f),
                    i.ToString(),
                    new GUIStyle { normal = { textColor = NodeColor }, fontSize = 9 }
                );
            }

            // Center dot
            Handles.color = CenterColor;
            Handles.DrawSolidDisc(center, Vector3.forward, 0.04f);
            Handles.DrawWireDisc(center, Vector3.forward, 0.08f);
        }

        public override void OnInspectorGUI()
        {
            SlimeBody slime = (SlimeBody)target;
            serializedObject.Update();

            // Header
            EditorGUILayout.Space(4);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.4f, 0.9f, 0.4f) }
            };
            EditorGUILayout.LabelField("Slime Body", headerStyle);
            EditorGUILayout.Space(2);

            // Body Shape
            DrawSection("Body Shape", () =>
            {
                DrawProp("nodeCount",      "Node Count");
                DrawProp("bodyRadius",     "Body Radius");
                DrawProp("colliderRadius", "Collider Radius");
            });

            // Spring Settings
            DrawSection("Spring Settings", () =>
            {
                DrawProp("springFrequency", "Frequency");
                DrawProp("springDamping",   "Damping Ratio");

                EditorGUILayout.Space(2);
                float freq = slime.springFrequency;
                float damp = slime.springDamping;
                string feel = freq < 6 ? "Very soft" :
                              freq < 10 ? "Soft jelly" :
                              freq < 14 ? "Firm jelly ✓" :
                                          "Very stiff";
                EditorGUILayout.HelpBox($"Feel: {feel}  |  Bounces: ~{Mathf.RoundToInt(1f / (damp + 0.01f))}",
                    MessageType.None);
            });

            // Mass
            DrawSection("Mass", () =>
            {
                DrawProp("centerMass",     "Center Mass");
                DrawProp("perimeterMass",  "Perimeter Mass");
                DrawProp("gravityScale",   "Gravity Scale");
            });

            // Volume Preservation
            DrawSection("Volume Preservation", () =>
            {
                DrawProp("pressureForce", "Pressure Force");
            });

            // Debug
            DrawSection("Debug", () =>
            {
                DrawProp("showGizmos", "Show Gizmos");
            });

            serializedObject.ApplyModifiedProperties();

            // Force scene repaint so gizmos update live
            if (GUI.changed)
                SceneView.RepaintAll();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            content();
            EditorGUI.indentLevel--;
        }

        private void DrawProp(string propName, string label)
        {
            SerializedProperty prop = serializedObject.FindProperty(propName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }
    }
}
#endif
