# Twisted Space — VR 패널과 강제 원근 퍼즐: 설정 가이드

🌐 [English](../en/twisted-space-setup.md) · **한국어** · [中文原文](../../Assets/TwistedSpace/SETUP.txt)

> [!NOTE]
> 이 문서는 `Assets/TwistedSpace/SETUP.txt`의 번역입니다. 초기 **Twisted Space** 프로토타입(*Parallax Chamber*, 시차 관측실)을 설명합니다.
> 현재 메인 씬에서는 **DepthLab**이 이 방을 대체했습니다. Parallax Chamber 오브젝트는 비활성화되어 있고 XR Device Simulator도 꺼져 있습니다.
> DepthLab이 여전히 사용하는 퍼즐·월드 스페이스 UI 컴포넌트의 참고 자료로 보관합니다.

## 버전과 입력

- XR Interaction Toolkit은 **3.3.2**로 고정되어 있으며, **새 Input System**을 사용합니다.
- 실제 에디터 버전은 6000.3 LTS가 아니라 **Unity 6000.6.8f1**입니다. 에디터 버전은 바꾸지 않았고 URP도 다운그레이드하지 않았습니다. 스크립트는 Unity 6 / XRI 3.x API를 사용합니다.
- XR Device Simulator는 공식 패키지에 포함된 기본 시뮬레이터이며, 레거시 Input Manager를 사용하지 않습니다.
- Apple Silicon Mac에서 키보드·마우스 시뮬레이션을 할 때는 SteamVR이나 실제 헤드셋 런타임이 필요 없습니다.
- 실제 헤드셋용 빌드는 대상 기기에 맞는 XR Provider를 별도로 설정해야 합니다.

## XR 씬 준비

1. Unity가 패키지와 스크립트 임포트를 마칠 때까지 기다립니다. 공식 XR Device Simulator 샘플은 이미 `Assets/Samples`에 임포트되어 있습니다.
   다시 임포트하려면 **Twisted Space › 1. Import XR Device Simulator** 메뉴를 사용하세요.
2. Camera, EventSystem, XR Origin이 없는 씬에서 **Twisted Space › 2. Create Simulator XR Rig**를 실행한 뒤 씬을 저장합니다.
   이 도구는 기존 카메라를 삭제하지 않으며 기존 씬을 덮어쓰지 않습니다.
3. 생성된 좌우 컨트롤러에는 모두 `XRDirectInteractor`가 있으며, **그립**이 직접 잡기를 담당합니다.
   `XRRayInteractor`는 **UI 전용**입니다. UI Press는 트리거에 바인딩되어 있어 잡기와 선택이 충돌하지 않습니다.
   UI 입력 모듈은 `XRUIInputModule`을 사용하며, `StandaloneInputModule`이나 `InputSystemUIInputModule`은 사용하지 않습니다.
4. 생성된 인스턴스에서는 공식 시뮬레이터의 도움말 오버레이가 비활성화되어 있습니다. 샘플의 도움말 UI 에셋을 게임 씬에 넣을 필요는 없습니다. **게임 UI는 모두 World Space를 사용합니다.**
5. 시뮬레이터는 Game 창에서 사용합니다. 키 설정은 샘플 안의 `.inputactions` 에셋 세 개를 기준으로 하며, Inspector에서 확인하거나 수정할 수 있습니다. **시뮬레이터 두 개를 동시에 켜지 마세요.**

## 패널과 버튼

- Canvas에는 `VRWorldSpacePanel`을 붙이고 렌더 모드를 **World Space**로 설정합니다. 패널을 헤드셋의 자식으로 만들지 마세요.
- XR Origin 안의 **Main Camera**를 패널의 헤드셋 카메라 필드에 드래그합니다.
- Canvas에는 `CanvasGroup`과 `TrackedDeviceGraphicRaycaster`가 필요하며, 스크립트가 이 컴포넌트를 요구합니다.
- 권장값: Canvas 크기 **700 × 400**, 월드 스케일 **0.0015**, 버튼 높이 최소 **70px**.
- 패널 자체의 레이를 가리는 Collider를 패널에 추가하지 마세요.

**버튼**

- 일반 `Button` 대신 `ConfirmedXRButton`을 사용합니다(한 오브젝트에 두 버튼을 함께 붙이지 마세요).
- 버튼 Image를 **Target Graphic**으로 지정합니다. 기본 색이 순백색이면 하이라이트할 여유가 없으므로 피하세요.
- 버튼마다 연한 파란색 **Halo Image**를 지정할 수 있으며, 이 이미지의 *Raycast Target*은 꺼 둡니다.
- 일반 UGUI 머티리얼은 조명을 받지 않습니다. 하이라이트와 헤일로는 Bloom이나 무거운 포스트 프로세싱 없이 발광하는 느낌을 흉내 냅니다.
- 버튼마다 **On Click**에 각자의 `UnityEvent` 콜백을 연결할 수 있습니다.
- 확인 버튼은 레이를 *놓을 때* 발생하는 클릭 이벤트를 사용합니다. Poke의 "Click UI On Down" 모드는 지원하지 않습니다.
- 트리거를 **최소 0.15초** 누른 상태로 유지하다가 **같은 버튼 위에서** 놓아야 콜백이 한 번 실행됩니다. 일찍 놓거나, 버튼 밖으로 벗어나거나, 패널이 숨겨지거나, 상호작용이 비활성화되면 취소됩니다.
- `EventTrigger` / `PointerDown`에서 게임 로직 이벤트를 따로 호출하지 마세요. 확인 보호 장치를 우회하게 됩니다.

**패널 API**

- 패널은 `FadeIn()` / `FadeOut()`을 제공하며, 페이드는 코루틴으로 실행되고 시간을 조절할 수 있습니다.
- "퍼즐 초기화"와 "뒤로" 버튼을 패널의 해당 필드에 드래그한 뒤 `OnResetPuzzle` / `OnBack`에 연결합니다.
- 같은 게임 함수는 **한 번만** 연결하세요. 버튼의 On Click과 패널 이벤트에 중복으로 연결하지 마세요.
- *뒤로*를 눌렀을 때 패널을 숨길지, 방으로 돌아갈지, 레벨을 바꿀지는 게임이 결정합니다. 스크립트가 임의로 앱을 종료하지 않습니다.

## 퍼즐 설정

- 영역에는 `PuzzleObservationVolume`, 목표에는 `PerspectivePuzzleTarget`, 메인 컨트롤러에는 `ForcedPerspectivePuzzle`을 사용합니다. 모든 필드에 주석과 Inspector 툴팁이 있습니다.
- 영역은 **Trigger**여야 합니다. 관찰자로는 헤드셋 전용 머리 콜라이더를 지정하고, 컨트롤러나 소품 콜라이더는 사용하지 마세요.
- 트리거 쌍 중 최소 한쪽에는 Rigidbody가 있어야 합니다. 머리 감지체는 작은 `SphereCollider`에 Rigidbody(*Is Kinematic* 켬, *Use Gravity* 끔)를 붙여 헤드셋을 따라가게 하는 것을 권장합니다. 씬 오브젝트를 밀지 않도록 머리 감지체를 Trigger로 둘 수도 있습니다.

**목표**

- 각 목표의 루트 오브젝트에는 `Collider`, `Rigidbody`, `XRGrabInteractable`, `PerspectivePuzzleTarget`이 필요합니다.
- 볼록하지 않은 동적 `MeshCollider`는 사용하지 마세요. VR에는 단순한 `BoxCollider`나 볼록 콜라이더가 더 적합합니다.
- 목표 머티리얼은 **URP/Lit**처럼 `_EmissionColor`가 있는 셰이더를 사용합니다.
- 빌드 전에 해당 머티리얼 에셋에서 **Emission**을 켜고 0이 아닌 아주 낮은 기본 발광 값을 설정해, 빌드에서 `_EMISSION` 셰이더 배리언트가 유지되게 하세요. 런타임에 키워드만 켜는 방식에 의존하면 배리언트가 제거될 수 있습니다.
- 잡은 뒤 공중에 떠 있어야 하는 도형은 Rigidbody의 *Use Gravity*를 끄는 것을 권장합니다.
- 실루엣 판정에 참여하는 렌더러만 목표 목록에 넣으세요. 보조 표시나 그림자 평면은 넣지 않습니다.

**메인 컨트롤러**

- 컨트롤러에 영역, Main Camera, 목표 배열, 올바른 관찰 방향을 나타내는 Transform을 연결합니다.
- 복합 관측 영역은 `BoxCollider` + `PuzzleObservationVolume`을 가진 자식 오브젝트 여러 개로 만들고, 모두 컨트롤러의 **Observation Volumes** 배열에 넣습니다.
- 올바른 방향은 해당 Transform의 파란색 **+Z** 축입니다. Scene 뷰에서 퍼즐 오브젝트를 향하게 하세요.
- `OnPuzzleSolved`에는 문 열기, 효과음 등을 연결하고, 초기화 버튼에는 `ResetPuzzle()`을 연결합니다.
- 진행도 이벤트로 World Space UGUI의 `Slider.value`를 갱신할 수 있습니다. Slider 범위는 0..1로 설정하고, 표시만 할 경우 *Interactable*을 끕니다. 진행도는 특정 목표 하나가 조건을 만족했다는 뜻이 아닙니다. **모든 목표가 동시에 정렬되어야 합니다.**

**판정 방식**

- 판정은 **투영 바운딩 박스**로 근사합니다. 픽셀 실루엣 인식이나 GPU 가림 테스트가 아닙니다. 따라서 지정한 관찰 지점을 기준으로 도형의 크기와 거리를 설계해 투영된 박스가 서로 겹칠 수 있게 해야 합니다. "서로 보완해 하나의 완전한 모양을 만드는" 퍼즐은 이 겹침 알고리즘에 맞지 않습니다.
- 좌우 눈이 번갈아 렌더링되며 진행도가 흔들리지 않도록 **중심 눈(mono) 투영**을 사용합니다.
- 관찰자 뒤에 있거나, 근거리 클리핑 면을 넘었거나, 시야 밖에 있는 목표는 통과할 수 없습니다.
- 성공 상태는 `ResetPuzzle`을 명시적으로 호출할 때까지 **유지**됩니다. 성공 이벤트를 `ResetPuzzle`에 다시 연결하지 마세요.
- 초기화할 때 목표를 아직 잡고 있으면 손을 놓으라는 안내가 나옵니다. 마지막 손이 놓이고 XRI 분리가 끝나면 목표가 처음 자세로 돌아가며, 컨트롤러를 갑자기 끌어당기지 않습니다. 초기화를 기다리는 목표는 판정에 참여하지 않습니다.

## 조작감 기본값

- 느린 머리 추종, 부드러운 호버 전환, 관대한 정렬 임계값 **0.65**를 사용합니다.
- 런타임에서 값을 조정했다면 **먼저 Play Mode를 종료한 뒤** 씬이나 프리팹에 값을 저장하세요.
- 이후 조작감을 계속 다듬을 때는 Inspector의 시간, 임계값, 커브만 바꾸면 됩니다. 이벤트 구조는 수정할 필요가 없습니다.
