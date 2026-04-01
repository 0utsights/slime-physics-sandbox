#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Hideout.Slime.Editor
{
    [CustomEditor(typeof(SlimeBody))]
    public class SlimeBodyEditor : UnityEditor.Editor
    {
        private static readonly Color NodeColor   = new Color(0.2f, 0.9f, 0.2f, 1f);
        private static readonly Color RingColor   = new Color(0.2f, 0.9f, 0.2f, 0.6f);
        private static readonly Color SpokeColor  = new Color(0.2f, 0.9f, 0.2f, 0.3f);
        private static readonly Color CenterColor = new Color(1f, 0.9f, 0.1f, 1f);

        private void OnSceneGUI()
        {
            SlimeBody slime = (SlimeBody)target;
            Vector3   center     = slime.transform.position;
            int       n          = slime.nodeCount;
            float     angleStep  = 360f / n;
            Vector3[] pts        = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                pts[i] = center + new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle)) * slime.bodyRadius;
            }

            Handles.color = SpokeColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(center, pts[i]);

            Handles.color = RingColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(pts[i], pts[(i + 1) % n]);

            Handles.color = NodeColor;
            for (int i = 0; i < n; i++)
            {
                Handles.DrawWireDisc(pts[i], Vector3.forward, slime.colliderRadius);
                Handles.Label(
                    pts[i] + Vector3.up * (slime.colliderRadius + 0.05f),
                    i.ToString(),
                    new GUIStyle { normal = { textColor = NodeColor }, fontSize = 9 });
            }

            Handles.color = CenterColor;
            Handles.DrawSolidDisc(center, Vector3.forward, 0.04f);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUIStyle header = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal   = { textColor = new Color(0.4f, 0.9f, 0.4f) }
            };
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Slime Body", header);
            EditorGUILayout.Space(2);

            DrawSection("Body Shape", () => {
                DrawProp("nodeCount",      "Node Count");
                DrawProp("bodyRadius",     "Body Radius");
                DrawProp("colliderRadius", "Collider Radius");
            });

            DrawSection("Springs", () => {
                DrawProp("radialFrequency",   "Radial Frequency");
                DrawProp("neighborFrequency", "Neighbor Frequency");
                DrawProp("springDamping",     "Spring Damping");
            });

            DrawSection("Elasticity", () => {
                DrawProp("bounciness", "Bounciness");
            });

            DrawSection("Shape Matching", () => {
                DrawProp("shapeMatchStrength", "Shape Match Strength");
            });

            DrawSection("Spread", () => {
                DrawProp("spreadRate",          "Spread Rate");
                DrawProp("recoveryRate",        "Recovery Rate");
                DrawProp("maxSpreadMultiplier", "Max Spread Multiplier");
            });

            DrawSection("Mass", () => {
                DrawProp("centerMass",    "Center Mass");
                DrawProp("perimeterMass", "Perimeter Mass");
            });

            DrawSection("Gravity", () => {
                DrawProp("gravityScale", "Gravity Scale");
            });

            DrawSection("Damping — Airborne", () => {
                DrawProp("airborneDamping",       "Perimeter Damping");
                DrawProp("airborneCenterDamping", "Center Damping");
            });

            DrawSection("Damping — Grounded", () => {
                DrawProp("groundedDamping",       "Perimeter Damping");
                DrawProp("groundedCenterDamping", "Center Damping");
                DrawProp("dampingTransitionSpeed", "Transition Speed");
            });

            DrawSection("Angular Separation", () => {
                DrawProp("minAngularSeparation", "Min Angular Separation");
                DrawProp("separationForce",      "Separation Force");
            });

            DrawSection("Debug", () => {
                DrawProp("showGizmos", "Show Gizmos");
            });

            serializedObject.ApplyModifiedProperties();
            if (GUI.changed) SceneView.RepaintAll();
        }

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
            var prop = serializedObject.FindProperty(propName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }
    }
}
#endif
