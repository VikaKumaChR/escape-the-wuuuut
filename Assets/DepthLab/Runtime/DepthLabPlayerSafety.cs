using UnityEngine;
using UnityEngine.InputSystem;

namespace DepthLab
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class DepthLabPlayerSafety : MonoBehaviour
    {
        public DepthLabApp app;
        [Min(0f)] public float gravity = 9.81f;
        [Min(.1f)] public float recoveryDepth = 1.5f;
        private float fallingSpeed;
        private int chapter = int.MinValue;
        private Vector3 safeFeet;
        private bool warned;
        private readonly RaycastHit[] floorHits = new RaycastHit[32];

        private void Update()
        {
            if (app == null || app.input == null || app.input.motor == null ||
                app.headCamera == null || app.playerRoot == null)
            {
                if (!warned)
                {
                    Debug.LogError("DEPTHLAB player safety requires the app, camera and collision motor.", this);
                    warned = true;
                }
                return;
            }
            CharacterController motor = app.input.motor;
            if (!motor.enabled || app.IsPaused)
                return;
            if (chapter != app.ActiveChapter)
            {
                chapter = app.ActiveChapter;
                safeFeet = chapter >= 0 && chapter < app.chapters.Length
                    ? app.chapters[chapter].spawnPoint.position : Vector3.zero;
                fallingSpeed = 0f;
            }
            Vector3 feet = app.Head.position;
            feet.y = app.playerRoot.position.y;
            if (feet.y < safeFeet.y - recoveryDepth)
            {
                Recover();
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame &&
                (app.IsPlaying || app.IsTutorial))
            {
                Recover();
                return;
            }

            fallingSpeed = motor.isGrounded ? 1f : Mathf.Min(8f, fallingSpeed + gravity * Time.deltaTime);
            motor.Move(Vector3.down * fallingSpeed * Time.deltaTime);
            if (!motor.isGrounded)
                return;

            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .2f, Vector3.down,
                floorHits, .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider floor = floorHits[i].collider;
                if (floor == motor || floor.transform.IsChildOf(app.playerRoot) ||
                    floorHits[i].normal.y < .8f ||
                    (floor.name != "Lab floor" && floor.name != "Floor" &&
                     floor.name != "Central arrival platform" && floor.name != "HallFloor"))
                    continue;
                safeFeet = new Vector3(app.Head.position.x, floorHits[i].point.y, app.Head.position.z);
                break;
            }
        }

        public void Recover()
        {
            if (app == null || app.input == null || app.playerRoot == null)
            {
                Debug.LogError("DEPTHLAB cannot recover without an assigned player rig.", this);
                return;
            }
            app.TeleportTo(safeFeet);
            fallingSpeed = 0f;
            Debug.Log("Player returned to the nearest safe position.", this);
        }
    }
}
