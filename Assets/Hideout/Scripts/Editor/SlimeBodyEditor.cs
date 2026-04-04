#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Hideout.Slime.Editor
{
    [CustomEditor(typeof(SlimeBody))]
    public class SlimeBodyEditor : UnityEditor.Editor
    {
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

            DrawSection("Crouch", () => {
                DrawProp("crouchDownForce",                    "Down Force");
                DrawProp("crouchStiffnessMultiplier",          "Stiffness Multiplier");
                DrawProp("crouchPressureMultiplier",           "Pressure Multiplier");
                DrawProp("crouchNeighborFrequencyMultiplier",  "Neighbor Freq Multiplier");
                DrawProp("crouchReleaseGroundedThreshold",     "Release Threshold");
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
                DrawProp("gravityScale",          "Center Gravity Scale");
                DrawProp("perimeterGravityScale", "Perimeter Gravity Scale");
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
                DrawProp("maxNodeSpeed",       "Max Node Speed");
                DrawProp("maxRecoveryForce",   "Max Recovery Force");
                DrawProp("antiSinkForceScale", "Anti-Sink Force Scale");
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
