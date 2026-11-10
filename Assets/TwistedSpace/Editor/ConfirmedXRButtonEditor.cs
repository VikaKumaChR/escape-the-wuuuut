using TwistedSpace.UI;
using UnityEditor;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Unity's ButtonEditor only draws the built-in Button fields. Draw the serialized
    /// hierarchy instead so confirmation, hover and halo settings remain drag-and-drop
    /// editable in the normal Inspector, including the inherited On Click event.
    /// </summary>
    [CustomEditor(typeof(ConfirmedXRButton))]
    [CanEditMultipleObjects]
    public sealed class ConfirmedXRButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "XR: hold UI Press for at least 0.15 seconds, then release. " +
                "SimulatorMenuInput can enable runtime-only desktop mouse clicks with a fixed " +
                "0.15-second post-click delay. Submit and touch remain unsupported.",
                MessageType.Info);
        }
    }
}
