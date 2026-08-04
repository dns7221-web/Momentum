using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 공중 자세 변형을 무작위로 고른다. 각 <c>AirLoop_i</c> 상태에 붙는다.
    ///
    /// <b>다음에 쓸 번호를 지금 뽑는다.</b> 들어오는 순간 뽑아 지금 자세를 바꾸는 게
    /// 아니라, 이번 낙하가 끝난 뒤 다음 낙하에 쓸 번호를 미리 정해두는 방식이다.
    /// 그래야 <b>떨어지는 도중에 자세가 안 바뀐다</b> — 뽑은 값이 지금 상태의 진입
    /// 조건을 다시 건드리지 않기 때문이다. 낙하 중에 자세가 바뀌면 그건 변화가 아니라
    /// 버그로 보인다.
    ///
    /// 파라미터를 코드(<c>PlayerMovement</c>)가 아니라 여기서 넣는 이유는 <b>개수를
    /// 아는 쪽이 여기</b>이기 때문이다. 클립을 몇 개 넣었는지는 빌더가 상태를 만들 때
    /// 확정되므로, 그 값을 그대로 여기 심어두면 재생성할 때마다 저절로 맞는다 —
    /// 이동 스크립트에 개수를 따로 적어두면 클립을 늘릴 때 한쪽만 고치는 사고가 난다.
    /// </summary>
    public class AirLoopVariantPicker : StateMachineBehaviour
    {
        /// <summary>빌더가 채운다. 만들어진 <c>AirLoop_i</c> 상태 수.</summary>
        public int variantCount = 1;

        /// <summary>빌더가 만드는 파라미터 이름과 같아야 한다.</summary>
        public string parameterName = "AirVariant";

        [Tooltip("켜면 지금 재생 중인 상태 이름과, 다음 낙하에 예약된 번호를 콘솔에 찍는다. " +
                 "확인 끝나면 꺼둘 것 — 매번 뜨는 낙하마다 로그가 쌓인다.")]
        public bool logToConsole = false;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (variantCount <= 1 || string.IsNullOrEmpty(parameterName)) return;

            int next = Random.Range(0, variantCount);

            if (logToConsole)
            {
                // stateInfo에는 이름이 없고 해시뿐이라, 실제로 지금 재생되는 클립 이름은
                // GetCurrentAnimatorClipInfo로 따로 물어야 사람이 읽을 수 있다.
                var clips = animator.GetCurrentAnimatorClipInfo(layerIndex);
                string clipName = clips.Length > 0 ? clips[0].clip.name : "(알 수 없음)";

                Debug.Log($"[AirLoopVariantPicker] 지금 재생 중: '{clipName}' " +
                          $"(레이어 {layerIndex}) — 다음 낙하용 {parameterName} = {next} " +
                          $"(0~{variantCount - 1} 중)");
            }

            animator.SetInteger(parameterName, next);
        }
    }
}
