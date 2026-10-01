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

            Section("Body Shape", () => {
                Prop("nodeCount",      "Node Count");
                Prop("bodyRadius",     "Body Radius");
                Prop("colliderRadius", "Collider Radius");
            });

            Section("Springs", () => {
                Prop("neighborFrequency", "Neighbor Frequency");
                Prop("springDamping",     "Spring Damping");
            });

            Section("Elasticity", () => {
                Prop("bounciness", "Bounciness");
            });

            Section("Pressure", () => {
                Prop("pressureStrength", "Pressure Strength");
            });

            Section("Shape Recovery — Idle", () => {
                Prop("idleRecoveryStiffness", "Stiffness");
                Prop("idleRecoveryDamping",   "Damping");
            });

            Section("Shape Recovery — Impact", () => {
                Prop("impactRecoveryStiffness", "Stiffness");
                Prop("impactRecoveryDamping",   "Damping");
                Prop("impactRecoveryDuration",  "Duration");
            });

            Section("Crouch", () => {
                Prop("crouchDownForce",                    "Down Force");
                Prop("crouchStiffnessMultiplier",          "Stiffness Multiplier");
                Prop("crouchPressureMultiplier",           "Pressure Multiplier");
                Prop("crouchNeighborFrequencyMultiplier",  "Neighbor Freq Multiplier");
                Prop("crouchReleaseGroundedThreshold",     "Release Grounded Threshold");
            });

            Section("Shape Recovery — Jump", () => {
                Prop("jumpRecoveryStiffness", "Stiffness");
                Prop("jumpRecoveryDamping",   "Damping");
                Prop("jumpRecoveryDelay",     "Delay");
                Prop("jumpRecoveryDuration",  "Duration");
            });

            Section("Mass", () => {
                Prop("centerMass",    "Center Mass");
                Prop("perimeterMass", "Perimeter Mass");
            });

            Section("Gravity", () => {
                Prop("gravityScale",          "Center Gravity Scale");
                Prop("perimeterGravityScale", "Perimeter Gravity Scale");
            });

            Section("Damping — Airborne", () => {
                Prop("airborneDamping",       "Perimeter Damping");
                Prop("airborneCenterDamping", "Center Damping");
            });

            Section("Damping — Grounded", () => {
                Prop("groundedDamping",        "Perimeter Damping");
                Prop("groundedCenterDamping",  "Center Damping");
                Prop("dampingTransitionSpeed", "Transition Speed");
            });

            Section("Angular Separation", () => {
                Prop("minAngularSeparation", "Min Angular Separation");
                Prop("separationForce",      "Separation Force");
            });

            Section("Safety", () => {
                Prop("maxNodeSpeed",       "Max Node Speed");
                Prop("maxRecoveryForce",   "Max Recovery Force");
                Prop("antiSinkForceScale", "Anti-Sink Force Scale");
            });

            serializedObject.ApplyModifiedProperties();
            if (GUI.changed) SceneView.RepaintAll();
        }

        private void Section(string title, System.Action content)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            content();
            EditorGUI.indentLevel--;
        }

        private void Prop(string name, string label)
        {
            var p = serializedObject.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label));
        }
    }
}
#endif
