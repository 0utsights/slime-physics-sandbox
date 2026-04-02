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
        private static readonly Color BraceColor  = new Color(0.2f, 0.9f, 0.2f, 0.2f);
        private static readonly Color SpokeColor  = new Color(0.2f, 0.9f, 0.2f, 0.3f);
        private static readonly Color CenterColor = new Color(1f, 0.9f, 0.1f, 1f);

        private void OnSceneGUI()
        {
            SlimeBody slime     = (SlimeBody)target;
            Vector3   center    = slime.transform.position;
            int       n         = slime.nodeCount;
            float     angleStep = 360f / n;
            Vector3[] pts       = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                pts[i] = center + new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle)) * slime.bodyRadius;
            }

            Handles.color = SpokeColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(center, pts[i]);

            Handles.color = BraceColor;
            for (int i = 0; i < n; i++)
                Handles.DrawLine(pts[i], pts[(i + 2) % n]);

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

            var header = new GUIStyle(EditorStyles.boldLabel)
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
                DrawProp("neighborFrequency", "Neighbor / Brace Frequency");
                DrawProp("springDamping",     "Spring Damping");
            });

            DrawSection("Elasticity", () => {
                DrawProp("bounciness", "Bounciness");
            });

            DrawSection("Pressure", () => {
                DrawProp("gasAmount",        "Gas Amount");
                DrawProp("pressureStrength", "Pressure Strength");
            });

            DrawSection("Shape Recovery — Idle", () => {
                DrawProp("idleRecoveryStiffness", "Stiffness");
                DrawProp("idleRecoveryDamping",   "Damping");
            });

            DrawSection("Shape Recovery — Impact", () => {
                DrawProp("impactRecoveryStiffness", "Stiffness");
                DrawProp("impactRecoveryDamping",   "Damping");
                DrawProp("impactRecoveryDuration",  "Duration");
            });

            DrawSection("Shape Recovery — Jump", () => {
                DrawProp("jumpRecoveryStiffness", "Stiffness");
                DrawProp("jumpRecoveryDamping",   "Damping");
                DrawProp("jumpRecoveryDelay",     "Delay");
                DrawProp("jumpRecoveryDuration",  "Duration");
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
                DrawProp("groundedDamping",        "Perimeter Damping");
                DrawProp("groundedCenterDamping",  "Center Damping");
                DrawProp("dampingTransitionSpeed", "Transition Speed");
            });

            DrawSection("Angular Separation", () => {
                DrawProp("minAngularSeparation", "Min Angular Separation");
                DrawProp("separationForce",      "Separation Force");
            });

            DrawSection("Safety", () => {
                DrawProp("maxNodeSpeed",     "Max Node Speed");
                DrawProp("maxRecoveryForce", "Max Recovery Force");
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
