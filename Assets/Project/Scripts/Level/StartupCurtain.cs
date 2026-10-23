using UnityEngine;

namespace Momentum.Level
{
    // 추가 생성 (2026-10-23)
    /// <summary>
    /// 시작 가림막 — 게임이 시작된 직후, 프레임이 안정될 때까지 화면을 검게 두었다가 부드럽게 밝힌다.
    ///
    /// <b>왜</b> — 렉 미터 "Play 시작부터 녹화" 기록에서 시작할 때마다 같은 모양의 멈춤이 나왔다.
    /// 첫 화면 직후 1.3초(첫 렌더 준비 — GPU Resident Drawer가 조각 1만 8천 개를 등록하고 셰이더를 준비 — 와 에디터 몫),
    /// 그 뒤 0.5~1초 동안 30~40 ms 프레임. 물리 쪽은 따로 줄였지만(Maximum Allowed Timestep, 소품 잠재우기),
    /// 첫 렌더 준비는 유니티 내부 일이라 줄일 곳이 없다. 그래서 그 구간을 플레이어가 안 보게 가린다 —
    /// 많은 게임이 로딩 화면 뒤에서 "워밍업 프레임"을 돌리는 것과 같은 방법이다.
    ///
    /// <b>언제 걷나</b> — 연속 <see cref="StableFrames"/>프레임이 <see cref="StableMs"/> ms 이하로 그려지면(= 안정)
    /// <see cref="FadeSeconds"/>초 동안 밝아진다. 무엇 때문이든 안정이 안 오면 <see cref="MaxWaitSeconds"/>초에 그냥 걷는다 —
    /// 느린 PC에서 화면이 영원히 검게 남으면 안 되니까.
    ///
    /// <b>게임은 가림막 뒤에서도 그대로 돈다.</b> 멈추면(timeScale 0) 소품·물리 준비가 걷힌 뒤로 밀려서
    /// 가린 의미가 없어진다. 렉 미터와 같은 방식(Play 때 저절로 생기는 IMGUI)이라 씬·프리팹·캔버스가 필요 없고,
    /// 다 걷히면 스스로 사라진다. 렉 미터는 가림막 위에 그대로 보인다(GUI.depth).
    /// </summary>
    [DisallowMultipleComponent]
    public class StartupCurtain : MonoBehaviour
    {
        private const float StableMs = 25f;        // 이보다 짧은 프레임을 "안정"으로 본다(= 40 FPS)
        private const int StableFrames = 15;       // 연속 몇 프레임이 안정이어야 걷나(한두 프레임 반짝 빨라진 걸로 걷지 않게)
        private const float MinWaitSeconds = 0.3f; // 너무 빨리 걷혀 깜빡이는 것 방지
        private const float MaxWaitSeconds = 3f;   // 안정이 안 와도 이 시간이 지나면 걷는다
        private const float FadeSeconds = 0.4f;    // 밝아지는 시간
        private const float MaxFadeStep = 0.05f;   // 밝아지는 도중 렉이 나도 한 번에 확 밝아지지 않게, 한 프레임에 반영할 최대 시간(초)
        private const int GuiDepth = 10;           // IMGUI는 숫자가 클수록 뒤에 그린다 — 렉 미터(기본 0)가 가림막 위에 보이게

        private float _alpha = 1f;    // 1 = 완전히 검음, 0 = 다 걷힘
        private float _elapsed;       // 가림막이 생긴 뒤 흐른 실제 시간(초)
        private int _stableCount;     // 연속으로 안정이었던 프레임 수
        private bool _fading;         // 밝아지는 중인가
        private bool _skipFirst = true; // 첫 프레임의 deltaTime엔 로딩 시간까지 들어 있어 판정에서 뺀다

        /// <summary>
        /// 첫 씬이 뜬 직후 저절로 하나 만든다 — 씬마다 넣어 둘 필요가 없게(렉 미터와 같은 방식).
        /// AfterSceneLoad라 씬의 Awake들이 끝난 뒤, 첫 화면이 그려지기 전에 생긴다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<StartupCurtain>() != null) return;
            new GameObject("StartupCurtain").AddComponent<StartupCurtain>();
        }

        private void Awake()
        {
            useGUILayout = false; // GUILayout을 안 쓰므로 Layout 이벤트를 건너뛴다
        }

        /// <summary>
        /// 매 프레임: 안정됐는지 세다가, 안정되면(또는 최대 대기 시간이 지나면) 밝아지기 시작한다. 다 밝아지면 스스로 지운다.
        /// timeScale과 상관없이 실제 시간(unscaledDeltaTime)으로 잰다 — 렉을 재는 것이라 게임 시간이 아니라 화면 시간이 기준이다.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_skipFirst)
            {
                _skipFirst = false;
                return;
            }
            _elapsed += dt;

            if (!_fading)
            {
                _stableCount = dt * 1000f <= StableMs ? _stableCount + 1 : 0;
                bool stable = _stableCount >= StableFrames && _elapsed >= MinWaitSeconds;
                if (stable || _elapsed >= MaxWaitSeconds) _fading = true;
                return;
            }

            _alpha -= Mathf.Min(dt, MaxFadeStep) / FadeSeconds;
            if (_alpha <= 0f) Destroy(gameObject);
        }

        /// <summary>화면 전체를 검게(투명도 _alpha) 덮는다. 유니티 내장 흰 텍스처에 GUI.color로 검은색·투명도를 입힌다.</summary>
        private void OnGUI()
        {
            GUI.depth = GuiDepth;
            if (Event.current.type != EventType.Repaint) return;

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_alpha));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
