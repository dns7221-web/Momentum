using UnityEngine;
using UnityEngine.Rendering;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-18)
    /// <summary>
    /// 카메라가 캐릭터에 너무 가까이 붙으면 캐릭터 몸을 숨기고 그림자만 남긴다.
    ///
    /// <b>왜 필요한가.</b> 벽 가까이에서는 Cinemachine Deoccluder와 CameraWallAvoid가 카메라를 캐릭터 쪽으로
    /// 당기는데, 그 결과 화면 전체가 캐릭터 몸으로 가득 차는 장면이 생긴다(2026-10-18 영상 12초쯤).
    /// 카메라 자리를 더 고치는 대신, 액션 게임들이 흔히 하듯 "너무 가까우면 몸을 안 그린다"로 막는다.
    ///
    /// 숨기는 방법은 유니티 내장 <c>ShadowCastingMode.ShadowsOnly</c>다 — 렌더러를 끄지 않으므로
    /// 애니메이션·그림자는 그대로 돌고, 몸만 화면에 안 그려진다. 머티리얼을 바꾸지 않아 셰이더와 무관하다.
    /// </summary>
    public class CameraNearHide : MonoBehaviour
    {
        [Tooltip("카메라가 캐릭터 가슴에서 이 거리(m)보다 가까우면 몸을 숨긴다.")]
        [Min(0f)][SerializeField] private float hideDistance = 0.9f;

        [Tooltip("다시 보이게 하는 거리(m). 숨기는 거리보다 조금 크게 둬서 경계에서 깜빡이지 않게 한다.")]
        [Min(0f)][SerializeField] private float showDistance = 1.1f;

        [Tooltip("거리를 잴 기준 높이(발에서 m). 캐릭터 가슴쯤.")]
        [SerializeField] private float chestHeight = 1.2f;

        private Renderer[] _renderers;
        private ShadowCastingMode[] _originalModes;
        private bool _hidden;

        /// <summary>몸을 이루는 렌더러(스킨 메시·총 등)를 모아 원래 그림자 설정을 기억해 둔다.</summary>
        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalModes = new ShadowCastingMode[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _originalModes[i] = _renderers[i].shadowCastingMode;
        }

        private void OnDisable() => SetHidden(false);

        /// <summary>카메라가 다 움직인 뒤(LateUpdate) 거리를 재서 숨기거나 보인다.</summary>
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float distance = Vector3.Distance(cam.transform.position, transform.position + Vector3.up * chestHeight);
            if (!_hidden && distance < hideDistance) SetHidden(true);
            else if (_hidden && distance > showDistance) SetHidden(false);
        }

        private void SetHidden(bool hidden)
        {
            if (_renderers == null || _hidden == hidden) return;
            _hidden = hidden;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                // 줄(LineRenderer) 같은 건 숨기지 않는다 — 몸이 아니다.
                if (_renderers[i] is LineRenderer || _renderers[i] is TrailRenderer) continue;
                _renderers[i].shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : _originalModes[i];
            }
        }
    }
}
