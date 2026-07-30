using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 마우스 커서를 숨기고 화면 중앙에 잠근다.
    ///
    /// 에디터에서도 그대로 작동한다 — 빌드 전용 기능이 아니다. 다만 유니티 에디터는
    /// Esc를 누르면 개발자가 Game 뷰에 갇히지 않도록 <see cref="Cursor.lockState"/>를
    /// 스크립트와 무관하게 강제로 풀어버린다. 그래서 Update에서 매 프레임 잠금을 다시
    /// 걸면 Esc가 영원히 듣지 않는 상태가 되고, 반대로 한 번만 걸어두면 에디터가 풀어버린
    /// 뒤 스크립트는 아직 잠겨 있다고 착각한다.
    ///
    /// 그래서 이 스크립트는 잠금을 명령하지 않고 <b>추적</b>한다. 원하는 상태를
    /// <see cref="_wantLock"/>에 두고, 실제 커서 상태가 그것과 어긋났을 때만 개입한다.
    /// 에디터가 몰래 풀어버린 경우는 어긋남이 아니라 사용자 의도로 받아들인다.
    /// </summary>
    public class CursorLock : MonoBehaviour
    {
        [Header("동작")]
        [Tooltip("시작할 때 곧바로 잠근다. 끄면 Game 뷰를 클릭할 때까지 커서가 보인다.")]
        [SerializeField] private bool lockOnStart = true;

        [Tooltip("잠금이 풀린 상태에서 Game 뷰를 클릭하면 다시 잠근다. " +
                 "빌드에서는 이것이 유일한 복귀 경로이므로 켜 두는 편이 낫다.")]
        [SerializeField] private bool relockOnClick = true;

        [Tooltip("Confined는 커서를 창 안에만 묶고 위치는 자유롭게 둔다. " +
                 "Locked는 중앙에 못박아 delta만 남긴다 — 3인칭 카메라는 이쪽이다.")]
        [SerializeField] private CursorLockMode lockMode = CursorLockMode.Locked;

        /// <summary>지금 잠겨 있어야 하는가. 실제 커서 상태가 아니라 <b>의도</b>다.</summary>
        private bool _wantLock;

        private void OnEnable()
        {
            SetLock(lockOnStart);
        }

        private void OnDisable()
        {
            // 컴포넌트가 죽어도 커서는 남는다. 여기서 되돌리지 않으면 이 오브젝트를
            // 런타임에 끄는 순간 커서를 되찾을 방법이 사라진다.
            SetLock(false);
        }

        private void Update()
        {
            // 포커스가 없는 동안엔 OS·에디터가 커서를 제 마음대로 돌려놓는다. 그 상태를
            // 읽으면 "사용자가 Esc를 눌렀다"로 오해하므로, 포커스가 돌아올 때까지 판단을 미룬다.
            if (!Application.isFocused) return;

            // ─── 에디터가 Esc로 몰래 풀어버린 경우 ───
            // 명령을 다시 밀어넣지 않고 의도를 그쪽에 맞춘다. 이 두 줄이 없으면
            // 에디터에서 Esc를 눌러도 다음 프레임에 다시 잠긴다.
            if (_wantLock && Cursor.lockState != lockMode)
            {
                _wantLock = false;
                Cursor.visible = true;
                return;
            }

            var keyboard = Keyboard.current;
            if (_wantLock && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetLock(false);
                return;
            }

            if (!_wantLock && relockOnClick)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    SetLock(true);
            }
        }

        /// <summary>
        /// 창 포커스를 잃으면 OS가 커서를 돌려준다. 되찾았을 때 원래 상태로 복원한다.
        /// 이게 없으면 알트탭 후 돌아왔을 때 커서가 화면에 남는다.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _wantLock)
                Apply(true);
        }

        /// <summary>바깥에서 UI를 열 때 호출한다 — 인벤토리·일시정지 같은 것.</summary>
        public void SetLock(bool locked)
        {
            _wantLock = locked;
            Apply(locked);
        }

        private void Apply(bool locked)
        {
            Cursor.lockState = locked ? lockMode : CursorLockMode.None;
            // Locked 모드는 대개 커서를 자동으로 숨기지만 플랫폼마다 다르다. 명시한다.
            Cursor.visible = !locked;
        }
    }
}
