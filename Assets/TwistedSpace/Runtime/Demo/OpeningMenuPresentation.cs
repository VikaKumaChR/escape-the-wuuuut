using TwistedSpace.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TwistedSpace.Demo
{
    /// <summary>
    /// Gives the opening its own visual stage without disabling puzzle objects or tracked devices.
    /// A menu is placed once when opened; it never chases the user's gaze.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(31000)]
    public sealed class OpeningMenuPresentation : MonoBehaviour
    {
        [SerializeField] private VerticalSliceFlow flow;
        [SerializeField] private Camera headCamera;
        [SerializeField] private VRWorldSpacePanel mainMenuPanel;
        [SerializeField] private VRWorldSpacePanel resetPanel;
        [SerializeField] private VRWorldSpacePanel resultPanel;
        [SerializeField] private SimulatorMenuInput menuInput;
        [SerializeField] private Text inputHint;
        [SerializeField, Min(0.5f)] private float menuDistance = 1.5f;
        [SerializeField, Tooltip("Room and controller renderers hidden only at the opening. Physics stays active.")]
        private Renderer[] explorationRenderers;
        [SerializeField, Tooltip("The simulator instruction plaque, not the interactive menu canvas.")]
        private Canvas[] explorationCanvases;
        [SerializeField, Tooltip("Hide simulator rays in mouse mode, but keep real VR aiming visible.")]
        private LineRenderer[] controllerRays;

        private bool[] previousRenderingOff;
        private bool[] previousCanvasEnabled;
        private bool stageHidden;
        private bool raysHidden;
        private bool[] previousRayRenderingOff;
        private bool initialized;
        private bool hasState;
        private DemoState displayedState;
        private bool? desktopHint;
        private CanvasGroup mainMenuGroup;

        private void Start()
        {
            if (!flow || !headCamera || !mainMenuPanel || !resetPanel || !resultPanel || !menuInput ||
                explorationRenderers == null || explorationCanvases == null || controllerRays == null)
            {
                Debug.LogError("OpeningMenuPresentation: assign the flow, camera, modal panels and stage arrays.", this);
                enabled = false;
                return;
            }
            previousRenderingOff = new bool[explorationRenderers.Length];
            previousCanvasEnabled = new bool[explorationCanvases.Length];
            previousRayRenderingOff = new bool[controllerRays.Length];
            mainMenuGroup = mainMenuPanel.GetComponent<CanvasGroup>();
            mainMenuPanel.FollowHead = false;
            resetPanel.FollowHead = false;
            resultPanel.FollowHead = false;
            initialized = true;
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;
            if (inputHint && desktopHint != menuInput.IsDesktopMenuActive)
            {
                desktopHint = menuInput.IsDesktopMenuActive;
                inputHint.text = desktopHint.Value ? "鼠标点击进入 · 探索时启用 XR 模拟器" :
                    "射线指向按钮 · 扳机按住 0.15 秒后松开";
            }
            if (flow.State != DemoState.MainMenu && stageHidden && mainMenuGroup.alpha <= 0.001f)
                RestoreExploration();
            SetRaysHidden(flow.State == DemoState.MainMenu && menuInput.IsDesktopMenuActive);
            if (hasState && displayedState == flow.State)
                return;

            displayedState = flow.State;
            hasState = true;
            if (displayedState == DemoState.MainMenu)
            {
                HideExploration();
                PlaceOnce(mainMenuPanel);
            }
            else
            {
                if (displayedState == DemoState.ResetConfirmation)
                    PlaceOnce(resetPanel);
                else if (displayedState == DemoState.Solved)
                    PlaceOnce(resultPanel);
            }
        }

        private void PlaceOnce(VRWorldSpacePanel panel)
        {
            Vector3 forward = Vector3.ProjectOnPlane(headCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.Cross(headCamera.transform.right, Vector3.up);
            forward.Normalize();
            panel.transform.SetPositionAndRotation(headCamera.transform.position + forward * menuDistance,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        private void HideExploration()
        {
            if (stageHidden)
                return;
            for (int i = 0; i < explorationRenderers.Length; i++)
            {
                Renderer item = explorationRenderers[i];
                if (!item)
                    continue;
                previousRenderingOff[i] = item.forceRenderingOff;
                // Line visuals can change Renderer.enabled every frame; forceRenderingOff stays authoritative.
                item.forceRenderingOff = true;
            }
            for (int i = 0; i < explorationCanvases.Length; i++)
            {
                Canvas item = explorationCanvases[i];
                if (!item)
                    continue;
                previousCanvasEnabled[i] = item.enabled;
                item.enabled = false;
            }
            stageHidden = true;
        }

        private void RestoreExploration()
        {
            if (!stageHidden)
                return;
            for (int i = 0; i < explorationRenderers.Length; i++)
                if (explorationRenderers[i])
                    explorationRenderers[i].forceRenderingOff = previousRenderingOff[i];
            for (int i = 0; i < explorationCanvases.Length; i++)
                if (explorationCanvases[i])
                    explorationCanvases[i].enabled = previousCanvasEnabled[i];
            stageHidden = false;
        }

        private void SetRaysHidden(bool hide)
        {
            if (raysHidden == hide)
                return;
            for (int i = 0; i < controllerRays.Length; i++)
            {
                if (!controllerRays[i])
                    continue;
                if (hide)
                    previousRayRenderingOff[i] = controllerRays[i].forceRenderingOff;
                controllerRays[i].forceRenderingOff = hide || previousRayRenderingOff[i];
            }
            raysHidden = hide;
        }

        private void OnDisable()
        {
            RestoreExploration();
            if (initialized)
                SetRaysHidden(false);
            hasState = false;
        }
    }
}
