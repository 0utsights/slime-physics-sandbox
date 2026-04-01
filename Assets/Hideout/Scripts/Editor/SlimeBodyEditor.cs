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
        private static readonly Color NodeColor     = new Color(0.2f, 0.9f, 0.2f, 1f);
        private static readonly Color RingColor     = new Color(0.2f, 0.9f, 0.2f, 0.6f);
        private static readonly Color SpokeColor    = new Color(0.2f, 0.9f, 0.2f, 0.3f);
        private static readonly Color CenterColor   = new Color(1f, 0.9f, 0.1f, 1f);
        private static readonly Color NeighborColor = new Color(0.2f, 0.6f, 1f, 0.4f);

        private void OnSceneGUI()
        {
            SlimeBody slime = (SlimeBody)target;
            Transform t = slime.transform;
            Vector3 center = t.position;

            int n = slime.nodeCount;
            float angleStep = 360f / n;
            Vector3[] nodePositions = new Vector3[n];

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

            // Node circles + labels
            Handles.color = NodeColor;
            for (int i = 0; i < n; i++)
            {
                Handles.DrawWireDisc(nodePositions[i], Vector3.forward, slime.colliderRadius);
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
            serializedObject.Update();

            EditorGUILayout.Space(4);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.4f, 0.9f, 0.4f) }
            };
            EditorGUILayout.LabelField("Slime Body", headerStyle);
            EditorGUILayout.Space(2);

            DrawSection("Body Shape", () =>
            {
                DrawProp("nodeCount",      "Node Count");
                DrawProp("bodyRadius",     "Body Radius");
                DrawProp("colliderRadius", "Collider Radius");
            });

            DrawSection("Springs", () =>
            {
                DrawProp("radialFrequency",   "Radial Frequency");
                DrawProp("neighborFrequency", "Neighbor Frequency");
                DrawProp("springDamping",     "Damping Ratio");
            });

            DrawSection("Shape Matching", () =>
            {
                DrawProp("shapeMatchStrength", "Shape Match Strength");
            });

            DrawSection("Dynamic Rest Lengths", () =>
            {
                DrawProp("spreadRate",          "Spread Rate");
                DrawProp("recoveryRate",        "Recovery Rate");
                DrawProp("maxSpreadMultiplier", "Max Spread Multiplier");
            });

            DrawSection("Mass & Damping", () =>
            {
                DrawProp("centerMass",    "Center Mass");
                DrawProp("perimeterMass", "Perimeter Mass");
                DrawProp("gravityScale",  "Gravity Scale");
                DrawProp("linearDamping", "Linear Damping");
                DrawProp("centerDamping", "Center Damping");
            });

            DrawSection("Corner Escape", () =>
            {
                DrawProp("cornerEscapeForce", "Corner Escape Force");
            });

            DrawSection("Debug", () =>
            {
                DrawProp("showGizmos", "Show Gizmos");
            });

            serializedObject.ApplyModifiedProperties();

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
