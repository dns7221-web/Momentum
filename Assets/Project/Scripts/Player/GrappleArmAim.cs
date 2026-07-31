using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Momentum.Player
{
    /// <summary>
    /// 총을 든 팔이 향할 지점을 매 프레임 옮긴다.
    ///
    /// 무료 웹스윙 애니메이션이 존재하지 않는다는 문제를 README 설계 결정 4번이 이렇게 쪼갰다
    /// — 몸통은 기존 클립이 그대로 재생되고, <b>총을 든 한 팔만</b> 런타임에 앵커를 향한다.
    /// 이 스크립트가 그 "한 팔"의 목표 지점만 담당하고, 실제로 관절을 굽히는 일은
    /// Animation Rigging의 Two Bone IK Constraint가 한다.
    ///
    /// 즉 여기서 하는 일은 트랜스폼 하나를 옮기는 것뿐이다. 뼈를 직접 만지지 않는다.
    ///
    /// <c>LateUpdate</c>가 아니라 <c>Update</c>인 이유가 있다. 리그는 애니메이터 평가 단계에서
    /// 계산되는데 그 시점이 모든 <c>Update</c> 이후·<c>LateUpdate</c> 이전이다. 여기서 목표를
    /// 옮겨야 <b>같은 프레임</b>에 반영되고, <c>LateUpdate</c>에서 옮기면 한 프레임 늦게 따라온다.
    /// (반대로 <see cref="GrappleGun"/>이 로프 선을 <c>LateUpdate</c>에서 긋는 것은 그때라야
    /// 손 본의 최종 위치를 읽을 수 있기 때문이다. 쓰는 쪽과 읽는 쪽의 시점이 다르다.)
    /// </summary>
    public class GrappleArmAim : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커를 물어다 주는 쪽. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;
        [Tooltip("Two Bone IK Constraint의 Target으로 꽂아둔 그 오브젝트.")]
        [SerializeField] private Transform aimTarget = null;
        [Tooltip("팔 IK 자체. 가중치를 0으로 두면 원래 애니메이션이 그대로 보인다.")]
        [SerializeField] private TwoBoneIKConstraint armIK = null;

        [Header("조준")]
        [Tooltip("IK 가중치가 오르내리는 속도. 0이면 즉시 바뀐다 — 팔이 튀어서 대개 부자연스럽다.")]
        [Min(0f)][SerializeField] private float weightLerpSpeed = 10f;

        [Tooltip("스윙 중에도 오른팔을 앵커 쪽으로 IK로 꺾을지. 기본은 끔이다 — 지금 쓰는 " +
                 "스윙 클립이 이미 양손으로 뭔가를 쥔 자세라, IK로 한쪽 팔만 다른 방향으로 " +
                 "꺾으면 그 자연스러운 그립을 깨뜨린다. 나중에 스윙과 무관한 별도 조준 상황이 " +
                 "생기면 그때 켠다.")]
        [SerializeField] private bool aimWhileSwinging = false;

        [Header("관절 보호")]
        [Tooltip("팔 길이의 몇 %까지 뻗을지. 1이면 완전히 편 상태라 팔꿈치가 잠기고 미세한 " +
                 "떨림에도 관절이 뒤집힌다. 살짝 굽은 채로 두는 편이 안정적이다.")]
        [Range(0.5f, 1f)][SerializeField] private float reachFraction = 0.95f;
        [Tooltip("몸 정면에서 이 각도를 넘어간 목표는 포기하고 팔을 내린다. " +
                 "뒤쪽 앵커를 억지로 겨누면 팔이 몸을 뚫고 돌아간다.")]
        [Range(30f, 180f)][SerializeField] private float maxAimAngle = 110f;

        /// <summary>어깨(IK의 Root 뼈). 조준 방향의 기준점이자 팔 길이의 시작점이다.</summary>
        private Transform _shoulder;

        /// <summary>실측한 팔 길이. 위팔 + 아래팔.</summary>
        private float _armLength = 0.6f;

        /// <summary>
        /// 손목 기준으로 본 총구의 회전. 대기 자세에서 한 번 재두고 계속 쓴다.
        ///
        /// 이게 필요한 이유는 <b>손과 총이 같은 방향을 보고 있지 않기 때문</b>이다. 총은 손에
        /// 비스듬히 쥐여 있고, 그 어긋난 각도는 씬에서 눈으로 맞춘 값이라 코드가 알 수 없다.
        /// 한 번 재두면 "총구가 저기를 향하려면 손목이 어느 각도여야 하는가"를 역산할 수 있다.
        /// </summary>
        private Quaternion _muzzleOffset = Quaternion.identity;

        private void Awake()
        {
            if (gun == null) gun = GetComponent<GrappleGun>();

            if (aimTarget == null)
            {
                Debug.LogWarning("[GrappleArmAim] Aim Target이 비어 있어 팔이 조준하지 않는다.", this);
            }

            MeasureArm();

            // 시작부터 팔이 뻗어 있으면 대기 자세가 이상해진다. 붙을 때 올라간다.
            if (armIK != null) armIK.weight = 0f;
        }

        /// <summary>
        /// 팔 길이를 <b>리그에서 직접 잰다.</b>
        ///
        /// 인스펙터에 숫자로 적어두지 않는 이유는 도시 생성기와 같다 — 적어둔 값과 실제 뼈대가
        /// 어긋나는 순간 팔이 조용히 과신전되거나 덜 뻗는다. IK가 이미 Root·Mid·Tip을 알고
        /// 있으므로 그 셋의 거리를 더하면 그게 이 캐릭터의 실제 도달 거리다.
        /// </summary>
        private void MeasureArm()
        {
            if (armIK == null) return;

            var data = armIK.data;
            if (data.root == null || data.mid == null || data.tip == null) return;

            _shoulder = data.root;
            _armLength = Vector3.Distance(data.root.position, data.mid.position)
                       + Vector3.Distance(data.mid.position, data.tip.position);

            // 손목(Tip)에서 본 총구의 상대 회전. IK가 아직 개입하지 않은 대기 자세에서 재야
            // 그것이 "총을 쥔 각도"의 기준이 된다.
            if (gun != null && gun.Muzzle != null)
                _muzzleOffset = Quaternion.Inverse(data.tip.rotation) * gun.Muzzle.rotation;
        }

        private void Update()
        {
            if (armIK == null) return;

            bool attached = gun != null && gun.IsAttached;
            bool wantAim = attached && aimWhileSwinging;

            if (wantAim && aimTarget != null)
            {
                // 붙어 있으면 앵커를 겨눈다. 목표를 계속 갱신해두는 이유는, 가중치가 0에서
                // 1로 오르는 동안 목표가 엉뚱한 곳에 남아 있으면 팔이 그쪽으로 한 번
                // 휘둘렀다 돌아오기 때문이다.
                bool reachable = PlaceTarget(gun.AnchorPoint);
                wantAim = reachable; // 닿을 수 없는 방향이면 조준을 포기하고 팔을 내린다.
            }

            // 붙는 순간 팔이 딱 꺾이면 기계처럼 보인다. 가중치를 잠깐에 걸쳐 올린다.
            float target = wantAim ? 1f : 0f;
            armIK.weight = weightLerpSpeed <= 0f
                ? target
                : Mathf.MoveTowards(armIK.weight, target, weightLerpSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 목표를 <b>팔이 닿는 자리</b>에 놓는다.
        ///
        /// 앵커가 20m 밖에 있어도 목표를 거기 그대로 두면 안 된다. Two Bone IK는 어떻게든
        /// 닿으려고 팔을 완전히 펴고 어깨를 비틀어, 관절이 뒤집히거나 몸을 뚫는 자세가 나온다.
        /// 실제로 필요한 것은 <b>거리가 아니라 방향</b>이므로, 어깨에서 그 방향으로 팔 길이만큼
        /// 떨어진 점에 목표를 놓는다. 그림은 똑같고 관절은 무사하다.
        /// </summary>
        /// <returns>겨눌 수 있는 방향인지. 몸 뒤쪽이면 <c>false</c>.</returns>
        private bool PlaceTarget(Vector3 lookAt)
        {
            if (_shoulder == null)
            {
                aimTarget.position = lookAt;
                return true;
            }

            Vector3 toTarget = lookAt - _shoulder.position;
            if (toTarget.sqrMagnitude < 0.0001f) return false;

            Vector3 direction = toTarget.normalized;

            // 몸을 뚫고 뒤로 돌아가는 자세를 막는다. 스윙 중에는 앵커가 등 뒤로 넘어가는
            // 순간이 반드시 오는데, 그때 팔을 억지로 돌리면 그 한 순간이 전부를 망친다.
            if (Vector3.Angle(transform.forward, direction) > maxAimAngle) return false;

            aimTarget.position = _shoulder.position + direction * (_armLength * reachFraction);

            // 총구가 그 방향을 보려면 손목이 어느 각도여야 하는지 역산한다.
            //
            //   총구회전 = 손목회전 × 오프셋   →   손목회전 = 목표방향 × 오프셋⁻¹
            //
            // 이 한 줄이 없으면 Target Rotation Weight를 조금이라도 올리는 순간 아무 의미 없는
            // 회전이 손목에 섞여 들어가 꺾인다. 값이 문제가 아니라 회전에 의미가 없던 것이다.
            aimTarget.rotation = Quaternion.LookRotation(direction, Vector3.up)
                               * Quaternion.Inverse(_muzzleOffset);
            return true;
        }
    }
}
