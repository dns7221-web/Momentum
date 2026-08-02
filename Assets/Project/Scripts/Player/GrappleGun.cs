using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 갈고리 발사와 앵커 판정 — 스윙 물리의 앞단.
    ///
    /// 여기까지는 <b>물리가 없다.</b> 쏘고, 맞은 지점을 기억하고, 선을 긋는 것이 전부다.
    /// 구속을 같이 넣지 않는 이유는 나중을 위해서다 — 줄이 엉뚱한 데 붙는 것과 구속이 틀린
    /// 것은 화면에서 똑같이 "이상하다"로 보인다. 앞단이 먼저 맞아야 그 구분이 생긴다.
    ///
    /// 갈고리는 날아가지 않고 <b>즉시 붙는다.</b> 발사체를 날리면 20m를 0.33초에 가는데
    /// 그 사이 플레이어는 8m를 이동하므로 조준을 앞질러야 하는 게임이 된다. 그게 스킬이
    /// 될지 짜증이 될지는 손맛을 판정한 뒤의 문제라, 지금 단계에 지연을 넣지 않는다.
    /// </summary>
    public class GrappleGun : MonoBehaviour
    {
        [Header("사거리")]
        [Tooltip("로프 길이. 이보다 먼 곳에는 붙지 않는다. 프리셋의 ropeLength와 같은 값을 쓴다.")]
        [Min(1f)][SerializeField] private float maxRange = 22f;
        [Tooltip("갈고리가 걸리는 레이어. Ground는 반드시 빠져 있어야 한다 — " +
                 "아스팔트에 붙으면 스윙이 아니라 지면 견인이 된다.")]
        [SerializeField] private LayerMask anchorMask;

        [Header("조준 보조")]
        [Tooltip("정중앙이 빗나갔을 때 크로스헤어에서 이 각도 안까지 후보를 찾아본다. " +
                 "0이면 보조 없음 — 정확히 겨눈 것만 붙는다. 24m 앞에서 12°면 약 5m 반경이다.")]
        [Range(0f, 30f)][SerializeField] private float aimAssistAngle = 12f;

        [Tooltip("보조에 쓰는 레이 수. 많을수록 촘촘하지만 한 번 클릭에 이만큼 쏜다. " +
                 "12개면 5m 반경을 충분히 덮는다.")]
        [Range(0, 32)][SerializeField] private int aimAssistRays = 12;

        [Tooltip("이보다 가까운 앵커에는 붙지 않는다. <b>발밑 바닥을 쏘는 것을 막는 값이 " +
                 "이쪽이다.</b> 서 있는 옥상 바닥은 2~5m 거리에 잡히고, 쓸 만한 스윙 앵커는 " +
                 "15m 이상이라 거리로 깨끗하게 갈린다.")]
        [Min(0f)][SerializeField] private float minAnchorDistance = 6f;

        [Tooltip("나보다 이 각도만큼 아래인 앵커는 버린다. <b>느슨하게 두는 것이 맞다.</b> " +
                 "이 게임에서 가장 자주 쓰는 샷은 옥상 높이에서 앞 건물 꼭대기를 쏘는 것이라 " +
                 "앙각이 0° 근처이고, 크게 튀어 오른 뒤에는 주변이 전부 나보다 아래다. " +
                 "값을 올리면 그 상황에서 웹이 통째로 안 나간다. -90이면 제한 없음.")]
        [Range(-90f, 60f)][SerializeField] private float minAnchorElevation = -25f;

        [Header("참조")]
        [Tooltip("로프가 나가는 지점. 권총 프리팹 안의 Muzzle. " +
                 "물리에는 쓰지 않고 선을 그릴 때만 쓴다.")]
        [SerializeField] private Transform muzzle = null;
        [Tooltip("로프를 그릴 LineRenderer. 붙어 있는 동안에만 켜진다.")]
        [SerializeField] private LineRenderer rope = null;
        [Tooltip("조준 기준 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera aimCamera = null;

        [Header("입력 (InputSystem_Actions.inputactions의 Player 맵에서 드래그)")]
        [SerializeField] private InputActionReference grappleAction = null;

        /// <summary>갈고리가 붙어 있는가. 구속이 붙을 때 이 값이 그대로 스위치가 된다.</summary>
        public bool IsAttached { get; private set; }

        /// <summary>
        /// 총구. 로프의 시작점이자 <see cref="GrappleArmAim"/>이 조준 회전을 계산하는 기준이다.
        ///
        /// 참조를 두 곳에 따로 꽂게 하지 않으려고 열어둔다 — 총을 바꾸면 여기 한 곳만 바꾼다.
        /// </summary>
        public Transform Muzzle => muzzle;

        /// <summary>
        /// 앵커의 현재 월드 좌표.
        ///
        /// 좌표를 저장해두지 않고 매번 다시 계산하는 이유는 앵커가 움직일 수 있기 때문이다.
        /// 지금은 고정된 건물뿐이라 차이가 없지만, 전투 단계에서 드럼통에 붙는 순간
        /// 월드 좌표로 굳혀둔 앵커는 물체를 두고 허공에 남는다. 그때 고치면 이미 맞춰둔
        /// 손맛 값들이 전부 흔들리므로 처음부터 이 모양으로 들고 있는다.
        /// </summary>
        public Vector3 AnchorPoint =>
            _anchorTransform != null
                ? _anchorTransform.TransformPoint(_anchorLocalPoint)
                : Vector3.zero;

        /// <summary>
        /// 앵커가 얹힌 강체. 고정된 건물이면 <c>null</c>이다.
        ///
        /// 구속이 붙을 때 이 자리가 "두 강체 중 반대쪽"이 된다. null을 역질량 0으로 읽으면
        /// 건물은 스윙, 가벼운 물체는 끌어오기, 무거운 물체는 내가 끌려가기가 분기 없이 나온다.
        /// </summary>
        public Rigidbody AnchorBody { get; private set; }

        /// <summary>
        /// 로프를 잠시 다른 목표로 그린다. <c>null</c>이면 평소대로 앵커까지 그린다.
        ///
        /// <see cref="GrappleZip"/>이 견인하는 동안 쓴다. 줄을 그리는 책임은 여기 하나로
        /// 두는 편이 낫다 — LineRenderer를 두 곳에서 만지면 어느 쪽이 껐는지 못 가리는
        /// 순간이 반드시 온다.
        /// </summary>
        public Vector3? OverrideRopeTarget { get; set; }

        /// <summary>맞은 콜라이더의 트랜스폼. 부착점을 이것의 로컬 좌표로 들고 있는다.</summary>
        private Transform _anchorTransform;
        private Vector3 _anchorLocalPoint;

        /// <summary>컴포넌트를 처음 붙일 때 레이어 마스크를 채워둔다. 빈 마스크는 아무것도 안 맞는다.</summary>
        private void Reset()
        {
            anchorMask = LayerMask.GetMask(Level.CityGenerator.AnchorLayerName);
        }

        private void Awake()
        {
            if (aimCamera == null) aimCamera = Camera.main;

            if (muzzle == null || rope == null)
            {
                Debug.LogWarning("[GrappleGun] Muzzle 또는 LineRenderer가 비어 있다. " +
                                 "판정은 되지만 로프가 그려지지 않는다.", this);
            }

            if (anchorMask == 0)
            {
                Debug.LogWarning("[GrappleGun] 앵커 레이어 마스크가 비어 있어 아무 데도 붙지 않는다. " +
                                 $"'{Level.CityGenerator.AnchorLayerName}'을 선택할 것.", this);
            }

            SetRopeVisible(false);
        }

        // ─── 입력 ───
        //
        // 액션 하나로 발사와 릴리즈를 모두 처리한다. Button 액션에 Interaction을 걸지 않으면
        // 누를 때 performed, 뗄 때 canceled가 오기 때문이다. 릴리즈 타이밍이 이 게임의 핵심
        // 조작이므로 토글이 아니라 "누르고 있는 동안 붙어 있다"가 맞다.

        private void OnEnable()
        {
            if (grappleAction == null) return;
            grappleAction.action.Enable();
            grappleAction.action.performed += OnGrapplePerformed;
            grappleAction.action.canceled += OnGrappleCanceled;
        }

        private void OnDisable()
        {
            if (grappleAction != null)
            {
                // 구독 해제를 빼먹으면 에디터에서 플레이를 반복할 때마다 콜백이 쌓여
                // 한 번 클릭에 갈고리가 여러 번 발사된다.
                grappleAction.action.performed -= OnGrapplePerformed;
                grappleAction.action.canceled -= OnGrappleCanceled;
                grappleAction.action.Disable();
            }

            Detach();
        }

        private void OnGrapplePerformed(InputAction.CallbackContext ctx) => TryAttach();

        private void OnGrappleCanceled(InputAction.CallbackContext ctx) => Detach();

        // ─── 판정 ───

        /// <summary>
        /// 화면 중앙에서 레이를 쏘아 앵커를 찾는다.
        ///
        /// 총구가 아니라 <b>카메라</b>에서 쏜다. 조준은 크로스헤어로 하는 것이고 총은 손에
        /// 들려 흔들리는 물건이라, 총구를 기준으로 삼으면 크로스헤어가 가리키는 곳과 실제로
        /// 맞는 곳이 어긋난다. 총구는 선을 그릴 때만 쓴다.
        /// </summary>
        /// <summary>
        /// 크로스헤어가 가리키는 앵커를 찾는다. 붙이지는 않는다.
        ///
        /// <c>public</c>인 이유는 <see cref="GrappleZip"/>이 <b>같은</b> 판정을 써야 하기
        /// 때문이다. 레이캐스트를 두 벌 두면 사거리·레이어 마스크·카메라 보정이 언젠가
        /// 반드시 어긋나고, 그때 "왜 스윙은 붙는데 견인은 안 붙지"를 못 가린다.
        /// </summary>
        public bool TryFindAnchor(out RaycastHit hit)
        {
            hit = default;
            if (aimCamera == null) return false;

            // 사거리는 플레이어 기준인데 레이는 뒤에 있는 카메라에서 나간다. 그 차이만큼
            // 더 쏘고, 맞은 뒤에 플레이어와의 실제 거리로 다시 거른다. 카메라 거리를 빼먹으면
            // 카메라를 당길수록 사거리가 줄어드는 이상한 규칙이 생긴다.
            Vector3 origin = aimCamera.transform.position;
            float distance = maxRange + Vector3.Distance(origin, transform.position);
            Vector3 center = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)).direction;

            // 정중앙을 먼저 본다. 겨눈 그대로 붙는 경험이 기본이어야 조준이 조작으로 남는다.
            if (CastAnchor(origin, center, distance, out hit)) return true;

            if (aimAssistAngle <= 0f || aimAssistRays <= 0) return false;

            // 빗나갔을 때만 부채꼴로 흩어 쏜다. 황금각 나선이라 겹치지 않고 고르게 퍼지고,
            // 반지름이 i에 따라 <b>단조 증가</b>하므로 <b>처음 맞는 것이 곧 크로스헤어에
            // 가장 가까운 것</b>이다 — 점수를 매겨 고를 필요가 없다.
            Vector3 up = aimCamera.transform.up;
            for (int i = 0; i < aimAssistRays; i++)
            {
                float t = (i + 0.5f) / aimAssistRays;
                float radius = Mathf.Sqrt(t) * aimAssistAngle;   // √ 를 씌워야 면적이 고르다
                float spin = i * 137.50776f;                     // 황금각(도)

                Vector3 direction = Quaternion.AngleAxis(spin, center)
                                  * (Quaternion.AngleAxis(radius, up) * center);

                if (CastAnchor(origin, direction, distance, out hit)) return true;
            }

            return false;
        }

        /// <summary>레이 하나를 쏘고 사거리·고도까지 전부 통과했는지 본다.</summary>
        private bool CastAnchor(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            if (!Physics.Raycast(origin, direction, out hit, distance, anchorMask,
                                 QueryTriggerInteraction.Ignore))
                return false;

            return IsUsableAnchor(hit.point);
        }

        /// <summary>
        /// 붙을 만한 앵커인가 — 거리와 고도 둘로 거른다.
        ///
        /// <b>거리가 주력이다.</b> 막으려는 것은 "서 있는 옥상 바닥을 쏘는 것"인데, 그건
        /// 2~5m 앞에 잡히고 쓸 만한 스윙 앵커는 15m 이상이라 거리로 깨끗하게 갈린다.
        ///
        /// <b>고도는 보조이고, 느슨해야 한다.</b> 처음에 이걸 10°로 잡았다가 웹이 통째로
        /// 안 나가는 상태를 만들었다. 이 게임에서 가장 자주 쓰는 샷은 옥상 높이에서 앞
        /// 건물 꼭대기를 쏘는 것이라 앙각이 0° 근처이고, 릴리즈로 크게 솟은 뒤에는 주변이
        /// <b>전부 나보다 아래</b>다. 각도로는 "발밑 바닥"과 "앞 건물 꼭대기"를 가를 수 없다.
        /// </summary>
        private bool IsUsableAnchor(Vector3 point)
        {
            Vector3 toAnchor = point - transform.position;

            if (toAnchor.magnitude > maxRange) return false;
            if (toAnchor.magnitude < minAnchorDistance) return false;

            float horizontal = new Vector2(toAnchor.x, toAnchor.z).magnitude;

            // 바로 머리 위. 수평 거리가 0이면 각도가 정의되지 않으므로 부호로 가른다.
            if (horizontal < 0.01f) return toAnchor.y > 0f;

            return Mathf.Atan2(toAnchor.y, horizontal) * Mathf.Rad2Deg >= minAnchorElevation;
        }

        private void TryAttach()
        {
            if (!TryFindAnchor(out RaycastHit hit)) return;

            _anchorTransform = hit.collider.transform;
            _anchorLocalPoint = _anchorTransform.InverseTransformPoint(hit.point);
            AnchorBody = hit.collider.attachedRigidbody;
            IsAttached = true;

            SetRopeVisible(true);
        }

        /// <summary>붙은 것을 놓는다. 속도에는 손대지 않는다 — 관성이 곧 보상이다.</summary>
        private void Detach()
        {
            IsAttached = false;
            _anchorTransform = null;
            AnchorBody = null;

            SetRopeVisible(false);
        }

        // ─── 그리기 ───

        /// <summary>
        /// 로프를 그린다.
        ///
        /// <c>Update</c>가 아니라 <c>LateUpdate</c>인 이유는 애니메이터가 손 본을 옮기는
        /// 시점이 Update 이후이기 때문이다. Update에서 그리면 총구가 한 프레임 전 위치라
        /// 선이 손에서 떨어져 따라다닌다. 로프가 떨려 보이는 원인의 대부분이 이것이다.
        /// </summary>
        private void LateUpdate()
        {
            // 견인 중에는 줄이 그쪽으로 간다. 붙어 있지 않아도 그려야 하므로 먼저 본다.
            if (OverrideRopeTarget.HasValue)
            {
                SetRopeVisible(true);
                DrawRope(OverrideRopeTarget.Value);
                return;
            }

            if (!IsAttached)
            {
                // 견인이 끝난 프레임에 줄이 남아 있지 않게 한다.
                SetRopeVisible(false);
                return;
            }

            // 앵커가 사라지는 경우 — 프롭이 파괴되거나 도시를 재생성했을 때.
            if (_anchorTransform == null)
            {
                Detach();
                return;
            }

            DrawRope(AnchorPoint);
        }

        private void DrawRope(Vector3 target)
        {
            if (rope == null || muzzle == null) return;

            rope.positionCount = 2;
            rope.SetPosition(0, muzzle.position);
            rope.SetPosition(1, target);
        }

        private void SetRopeVisible(bool visible)
        {
            if (rope != null) rope.gameObject.SetActive(visible);
        }
    }
}
