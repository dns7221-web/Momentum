using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momentum.Player.EditorTools
{
    /// <summary>
    /// PlayerAnimationSet 하나로 Animator Controller를 통째로 만든다.
    ///
    /// 블렌드 트리와 전이를 마우스로 그리게 하면 문턱값을 잘못 입력하거나 전이 조건을
    /// 빠뜨리기 쉽다. PlayerMovement가 보내는 파라미터 이름(Speed·Grounded·Jump)과
    /// 여기서 만드는 이름이 반드시 일치해야 하므로, 손으로 반복할 일이 아니라 코드로
    /// 고정해 두는 편이 안전하다.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        /// <summary>
        /// AirIdle이 걸리려면 이만큼 연속으로 공중이어야 한다.
        ///
        /// 처음엔 0.08초로 뒀는데 실측에서 여전히 턱에서 튀었다 — Falling 문턱(0.15초)조차
        /// "완만한 언덕을 속도 붙여 넘을 때도 넘길 수 있다"고 README에 이미 적혀 있는데,
        /// 그보다 더 짧은 값을 썼으니 당연한 결과였다. Falling은 그 구멍을 <b>시간과 속도
        /// 둘 다</b>로 막지만, AirIdle은 상승 구간(놓은 직후·점프 정점)도 잡아야 해서 속도로는
        /// 못 거른다 — 그래서 시간 하나로 버티려면 확실히 크게 잡아야 한다. Falling 문턱보다
        /// 넉넉히 위로 둔다.
        /// </summary>
        private const float AirIdleAirTimeThreshold = 0.25f;

        /// <summary>
        /// 히어로 착지(<c>SwingLandOnGround</c>)가 나오려면 이만큼 연속으로 공중이었어야 한다.
        ///
        /// <c>LandingSoon</c>(착지 예고)만으로 이 클립을 걸었을 때, 실측 로그에서 예고
        /// 시간이 0.03~0.32초로 널뛰었다 — SphereCast가 물리 스텝 양자화·지형 요철에
        /// 물리는 값이라 완벽하게 못 맞춘다. 화려한 자세를 <b>평소 착지의 기본값</b>으로
        /// 쓰면 그 오차가 매번 티가 난다. 대신 체공 시간이 충분히 길었던(=진짜 크게
        /// 떨어지거나 오래 스윙한) "특정 상황"에만 걸어서, 타이밍이 살짝 어긋나도 화려한
        /// 연출 자체가 그 어색함을 가려주는 자리에서만 쓰기로 한다. 평범한 착지는
        /// <c>Grounded</c> 하나로 예고 없이 확실하게 튼다(아래 <see cref="AddLandingTransitions"/>).
        /// </summary>
        private const float HeroLandingMinAirTime = 0.6f;

        /// <summary>공중 자세 변형 번호를 담는 파라미터. 빌더와 Picker가 같은 이름을 써야 한다.</summary>
        private const string AirVariantParameter = "AirVariant";

        private const string OutputFolder = "Assets/Project/Player";
        private const string MenuPath = "Momentum/선택한 애니메이션 세트로 Animator Controller 만들기";

        [MenuItem(MenuPath, false, 130)]
        private static void Build()
        {
            var set = Selection.activeObject as PlayerAnimationSet;
            if (set == null)
            {
                Debug.LogWarning("[PlayerAnimatorBuilder] 프로젝트 창에서 PlayerAnimationSet 에셋을 선택하고 실행할 것.");
                return;
            }

            if (set.idle == null || set.walk == null || set.run == null)
            {
                Debug.LogError("[PlayerAnimatorBuilder] Idle · Walk · Run 클립이 비어 있다. 셋 다 채울 것.", set);
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Project", "Player");

            string path = $"{OutputFolder}/PlayerAnimator.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Falling", AnimatorControllerParameterType.Bool);
            controller.AddParameter("AirTime", AnimatorControllerParameterType.Float);
            // 공중 자세 변형 번호. AirLoopVariantPicker가 낙하마다 미리 뽑아 넣는다.
            controller.AddParameter(AirVariantParameter, AnimatorControllerParameterType.Int);
            // 직전에 스윙을 했는가. 착지 순간 일반 착지와 스윙 착지를 가르는 데 쓴다.
            controller.AddParameter("SwingLanding", AnimatorControllerParameterType.Bool);
            // 발밑 레이캐스트로 미리 켜는 신호 — Grounded보다 먼저 참이 된다.
            controller.AddParameter("LandingSoon", AnimatorControllerParameterType.Bool);
            // 기본 레이어 전용 릴리즈 신호. 스윙 레이어의 SwingEnded와 <b>일부러 이름을
            // 다르게</b> 둔다 — 유니티 트리거는 그것을 쓰는 전이가 하나 발동하면 즉시
            // 소비(reset)되므로, 두 레이어가 같은 트리거를 보면 한쪽만 먹고 다른 쪽은
            // 영영 못 받는다. 레이어마다 자기 트리거를 갖게 해야 둘 다 확실히 걸린다.
            controller.AddParameter("SwingReleased", AnimatorControllerParameterType.Trigger);

            var rootLayer = controller.layers[0].stateMachine;

            // 로코모션 블렌드 트리 — PlayerMovement가 정규화하지 않은 실제 m/s 값을 그대로
            // 보내므로, 문턱값도 실제 이동 속도와 같은 숫자를 써야 한다.
            var blendTree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(blendTree, controller);
            blendTree.AddChild(set.idle, 0f);
            blendTree.AddChild(set.walk, set.walkSpeedThreshold);
            blendTree.AddChild(set.run, set.runSpeedThreshold);

            var locomotionState = rootLayer.AddState("Locomotion");
            locomotionState.motion = blendTree;
            rootLayer.defaultState = locomotionState;

            // 점프는 클립이 있을 때만 만든다. 없어도 이동 자체는 정상 동작해야 하므로
            // 필수로 두지 않는다 — 지금 당장 이동만 먼저 검증하고 싶을 수도 있으니까.
            if (set.jump != null)
                BuildJumpChain(rootLayer, locomotionState, set);
            else
                Debug.LogWarning("[PlayerAnimatorBuilder] Jump 클립이 비어 있어 점프 상태 없이 만들었다.");

            // 낙하는 점프와 완전히 별개 사슬이다. 버튼 없이 바닥이 사라졌을 때만 걸려야 하므로
            // Any State가 아니라 Locomotion에서만 전이를 건다 — 이유는 함수 주석 참고.
            if (set.diveStart != null || set.airLoop != null)
                BuildFallChain(rootLayer, locomotionState, set);

            // 스윙은 기본 레이어를 뜯어고치지 않고 별도 레이어로 얹는다. Grounded 하나로
            // 갈리던 기존 축과 "매달려 돎"이 맞지 않기 때문이다 — 스윙 중에도 Grounded는
            // 계속 거짓이지만 원하는 그림은 걷기·점프 사슬과 완전히 다르다. 레이어 가중치가
            // 0/1로 그 레이어 전체를 켜고 끄므로, 기본 레이어의 상태·전이는 손대지 않는다.
            if (set.swingLoop != null)
                BuildSwingLayer(controller, set);
            else
                Debug.LogWarning("[PlayerAnimatorBuilder] swingLoop 클립이 비어 있어 스윙 레이어 없이 만들었다.");

            if (set.zipLoop != null)
                BuildZipLayer(controller, set);

            if (set.grappleFire != null)
                BuildGrappleFireLayer(controller, set);

            if (set.throwProp != null)
                BuildThrowLayer(controller, set);

            if (set.grabAndSlam != null)
                BuildGrabAndSlamLayer(controller, set);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
            Debug.Log($"[PlayerAnimatorBuilder] {path} 생성 완료. " +
                      "Security Officer의 Animator 컴포넌트 Controller 슬롯에 끌어넣을 것.", controller);
        }

        /// <summary>
        /// 점프를 Any State → JumpStart → Locomotion 사슬로 잇는다.
        ///
        /// 공중 루프를 일부러 안 태운다. 이건 <b>버튼으로 시작하는 짧은 점프</b>고, 체공이
        /// 짧으니 뛰어오르는 클립만 재생하다 Grounded가 참이 되는 대로 이동으로 돌아가면
        /// 충분하다. 길게 뜨는 낙하(옥상에서 발을 헛디디는 것 등)는 여기가 아니라
        /// <see cref="BuildFallChain"/>이 완전히 별개 사슬로 담당한다 — 같은 "공중"이라도
        /// 트리거 방식이 달라 하나로 뭉치면 서로 간섭한다.
        /// </summary>
        private static void BuildJumpChain(AnimatorStateMachine layer,
                                           AnimatorState locomotion,
                                           PlayerAnimationSet set)
        {
            var jumpState = layer.AddState("JumpStart");
            jumpState.motion = set.jump;

            // 어느 상태에 있든 Jump 트리거 하나로 진입한다.
            var enter = layer.AddAnyStateTransition(jumpState);
            enter.hasExitTime = false;
            enter.duration = 0.05f;
            // 점프 도중 트리거가 또 들어와도 처음부터 다시 재생되지 않게 막는다.
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            var toLocomotion = jumpState.AddTransition(locomotion);
            toLocomotion.hasExitTime = false;
            toLocomotion.duration = 0.1f;
            toLocomotion.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        }

        /// <summary>
        /// 낙하를 Locomotion → DiveStart → AirLoop → Land → Locomotion 사슬로 잇는다.
        ///
        /// 진입 전이를 <b>Any State가 아니라 Locomotion에서만</b> 건다. 그래야 진짜 점프와
        /// 안 겹친다 — 유니티는 Any State 전이를 현재 상태 전이보다 먼저 검사하므로, Jump
        /// 버튼을 눌러 트리거가 뜨면 Grounded가 거짓이 되는 그 프레임에도 Any State가 먼저
        /// JumpStart로 데려가버려 캐릭터는 이미 Locomotion을 떠나 있다. 그 상태에서
        /// Locomotion 전용 전이는 검사될 기회조차 없다. 버튼 없이 바닥이 사라질 때만
        /// Locomotion에 머무른 채 이 전이가 걸린다 — 판단 코드를 짜지 않고 <b>어느 상태에서
        /// 전이를 거는지</b>만으로 유니티 상태 기계가 둘을 갈라준다.
        ///
        /// 조건은 <c>Grounded</c>가 아니라 <c>Falling</c>이다. 접지 판정은 물리 스텝 단위로
        /// 갱신되는데, 도로 타일 이음매나 인도 턱을 지나갈 때 스텝 한 번만큼 순간적으로
        /// 끊길 수 있다. 그걸 그대로 조건에 쓰면 <b>일단 DiveStart에 들어간 뒤에는 되돌릴
        /// 방법이 없다</b> — DiveStart는 재생의 90%가 지나야 다음으로 넘어가므로, 접지가
        /// 그 즉시 회복돼도 이미 시작된 다이빙·착지 모션 전체를 강제로 끝까지 재생하게 된다.
        /// <c>Falling</c>은 PlayerMovement가 일정 시간(<c>fallDebounceSeconds</c>) 이상
        /// 연속으로 공중일 때만 참이 되므로, 순간적인 끊김은 애초에 이 문턱을 못 넘는다.
        /// </summary>
        private static void BuildFallChain(AnimatorStateMachine layer,
                                           AnimatorState locomotion,
                                           PlayerAnimationSet set)
        {
            AnimatorState landState = null;
            if (set.land != null)
            {
                landState = layer.AddState("Land");
                landState.motion = set.land;

                var landToLocomotion = landState.AddTransition(locomotion);
                landToLocomotion.hasExitTime = true;
                landToLocomotion.exitTime = 0.7f; // 착지 모션 끝자락에서 이동으로 녹여 넣는다
                landToLocomotion.duration = 0.15f;
            }

            // 스윙 후 착지는 <b>기본 레이어</b>에 둔다. 원래 SwingLand는 스윙 레이어에서
            // SwingEnded(=줄을 놓는 순간)로 걸려 있었는데, 그 순간은 보통 공중 한복판이고
            // 놓자마자 스윙 레이어 가중치가 0으로 내려가므로 <b>착지할 때쯤엔 이미 끝나
            // 있었다.</b> 순서가 "착지 모션 → 공중"으로 거꾸로 돌던 원인이다.
            //
            // 신호를 Grounded로 바꾸고 자리를 기본 레이어로 옮기면, 공중 자세를 다 돌고
            // <b>발이 닿는 순간</b> 이 상태로 들어간다. 일반 Land와 나눠 두는 이유는
            // PlayerMovement가 "직전에 스윙했는가"를 SwingLanding으로 알려주기 때문이다 —
            // 그냥 떨어진 착지와 스윙에서 내려온 착지를 다르게 보여줄 수 있다.
            AnimatorState swingLandState = null;
            if (set.swingLand != null)
            {
                swingLandState = layer.AddState("SwingLandOnGround");
                swingLandState.motion = set.swingLand;

                var toLocomotion = swingLandState.AddTransition(locomotion);
                toLocomotion.hasExitTime = true;
                toLocomotion.exitTime = 0.7f;
                toLocomotion.duration = 0.15f;
            }

            AnimatorState afterAir = landState != null ? landState : locomotion;

            // 공중 자세는 여러 개일 수 있다. 하나면 예전과 똑같이 AirLoop 상태 하나만 만들고,
            // 여러 개면 AirLoop_0..n을 만들어 뜰 때마다 무작위로 하나를 고른다.
            var airClips = set.AirLoopClips();
            var airStates = BuildVariantStates(layer, airClips, "AirLoop");

            foreach (var state in airStates)
                AddLandingTransitions(state, afterAir, swingLandState);

            AnimatorState afterDive = airStates.Count > 0 ? airStates[0] : afterAir;

            AnimatorState diveState = null;
            if (set.diveStart != null)
            {
                diveState = layer.AddState("DiveStart");
                diveState.motion = set.diveStart;

                // 다이빙 모션이 거의 끝날 때 공중 루프로 넘어간다. 변형이 여럿이면
                // AirVariant가 가리키는 것 하나로만 간다.
                LinkToAirLoops(diveState, airStates, exitTime: 0.9f);

                // DiveStart에도 착지 탈출구를 놓는다. 기본 레이어는 스윙 중에도(안 보일
                // 뿐) 계속 돌고 있어서, 스윙하며 실제로 하강하는 구간마다 Falling 조건을
                // 만족해 DiveStart로 들어갈 수 있다. 이 상태를 만들 때는 "클립의 90%를
                // 재생해야 다음으로 넘어간다"는 exitTime 전이 하나뿐이었는데, 그러면 하필
                // 착지하는 순간 기본 레이어가 여기 가 있었을 때 — 스윙 레이어가 0으로
                // 내려가 기본 레이어가 드러나도, 발은 이미 땅인데 DiveStart 클립이 끝날
                // 때까지(그리고 AirLoop로 넘어갈 때까지) 계속 낙하 자세를 억지로 재생한다.
                // 다른 공중 상태들과 똑같이 Grounded/히어로 조건으로 즉시 빠져나가게 한다 —
                // hasExitTime이 없는 조건부 전이라 재생 진행률과 무관하게 그 프레임에 바로 먹힌다.
                AddLandingTransitions(diveState, afterAir, swingLandState);
            }
            // "진짜 낙하로 확정됐을 때" 들어갈 곳. DiveStart가 있으면 거기, 없으면
            // 곧장 공중 루프/착지 단계로 — 위 afterDive와 같은 폴백 규칙이다.
            AnimatorState afterEnter = diveState != null ? diveState : afterDive;

            if (set.airIdle != null)
            {
                // Locomotion과 "진짜 낙하 확정" 사이의 빈 구간을 메운다. 스윙을 놓은 직후나
                // 점프 정점처럼 "공중이지만 아직 낙하는 아닌" 순간에 기본 Idle/Walk/Run
                // 대신 이 자세가 보인다.
                //
                // 조건은 raw !Grounded가 아니라 <c>AirTime > 문턱</c>이다. 처음엔 !Grounded로
                // 만들었는데, 그러면 인도 턱 같은 단차를 지나갈 때 물리 스텝 한 번만큼
                // 접지가 순간적으로 끊기는 것까지 그대로 잡아버려 걷다가 AirIdle이 튀었다 —
                // 정확히 Falling이 시간+속도 문턱으로 이미 한 번 피해간 그 문제다. AirTime은
                // PlayerMovement가 연속 공중 시간을 누적해 매 프레임 보내는 값이라, 한 스텝짜리
                // blip은 이 문턱을 못 넘는다.
                // AirLoop 변형(airLoopVariants)을 다시 섞는다. 한 번 뺐다가(2026-08-05,
                // 아래 사유) 다시 넣는 것이다.
                //
                // 뺐던 이유: 당시엔 SwingReleased가 이미 접지 중이어도 무조건 나갔고,
                // DiveStart엔 착지 탈출구가 없었고, 스윙 레이어도 접지 중에 서서히만
                // 내려갔다 — 그래서 "옥상에 가만히 서 있는데도", "이미 땅인데도" 화려한
                // AirLoop 자세가 새어 나왔다. 그 세 가지를 전부 각자 자리에서 고쳤으므로
                // (SwingConstraint.EndSwing의 접지 가드, DiveStart의 AddLandingTransitions,
                // UpdateAnimator의 접지 스냅), 이 풀에 변형이 섞여 있어도 "엉뚱한 순간에
                // 튀어나오는" 사고는 이제 각각의 그 자리에서 막힌다.
                //
                // 다시 넣는 이유: 도시가 건물로 빽빽해서, 릴리즈 뒤 <c>Falling</c>(시간
                // 0.15초+하강 6m/s)을 확정 짓기 전에 착지하거나 다음 앵커를 다시 잡는
                // 경우가 대부분이다 — 그러면 AirLoop_i(변형 4개)까지 갈 일이 거의 없어
                // 실질적으로 늘 AirIdle(클립 1개) 하나만 보인다. 스윙을 자주 할수록 오히려
                // 이 짧은 구간만 반복해서 보게 되므로, 변형을 보고 싶으면 여기 있어야 한다.
                var airIdleClips = new List<AnimationClip> { set.airIdle };
                foreach (var clip in set.AirLoopClips())
                    if (clip != null && !airIdleClips.Contains(clip)) airIdleClips.Add(clip);

                var airIdleStates = BuildVariantStates(layer, airIdleClips, "AirIdle");

                foreach (var state in airIdleStates)
                {
                    // 진짜 낙하로 확정되면 다이빙/공중 루프 쪽으로 넘긴다.
                    if (diveState != null)
                    {
                        var toRealFall = state.AddTransition(diveState);
                        toRealFall.hasExitTime = false;
                        toRealFall.duration = 0.15f;
                        toRealFall.AddCondition(AnimatorConditionMode.If, 0f, "Falling");
                    }
                    else
                    {
                        LinkToAirLoops(state, airStates, condition: "Falling");
                    }

                    // 낙하로 안 이어지고 그새 다시 땅에 닿으면(턱을 살짝 넘은 정도) 곧장
                    // 이동으로 돌아간다 — Land까지 갈 정도로 크게 뜬 게 아니었으므로.
                    // 다만 스윙에서 내려온 것이라면 그쪽 착지를 보여준다.
                    AddLandingTransitions(state, locomotion, swingLandState);
                }

                LinkVariants(locomotion, airIdleStates, airIdleClips.Count,
                             condition: "AirTime", mode: AnimatorConditionMode.Greater,
                             threshold: AirIdleAirTimeThreshold);

                // 스윙을 놓는 순간을 Any State에서 강제로 잡는다. 스윙 중에도 이 레이어의
                // 상태 기계는 (안 보일 뿐) Grounded/Falling을 그대로 따라 계속 돌고 있어서,
                // 놓는 시점에 Locomotion에 있다는 보장이 없다 — 오래 매달려 있었으면 이미
                // AirLoop나 Land로 넘어가 있을 수 있고, 그러면 위 "Locomotion에서만 전이"
                // 규칙이 걸릴 기회 자체가 없다. Any State는 현재 상태와 무관하게 항상
                // 검사되므로, 어디에 가 있었든 놓는 순간 확실히 AirIdle로 되돌린다.
                for (int i = 0; i < airIdleStates.Count; i++)
                {
                    var forceAirIdle = layer.AddAnyStateTransition(airIdleStates[i]);
                    forceAirIdle.hasExitTime = false;
                    forceAirIdle.duration = 0.1f;
                    forceAirIdle.canTransitionToSelf = false;
                    forceAirIdle.AddCondition(AnimatorConditionMode.If, 0f, "SwingReleased");
                    if (airIdleStates.Count > 1)
                        forceAirIdle.AddCondition(AnimatorConditionMode.Equals, i, AirVariantParameter);
                }
            }
            else if (diveState != null)
            {
                var enter = locomotion.AddTransition(diveState);
                enter.hasExitTime = false;
                enter.duration = 0.1f;
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Falling");
            }
            else
            {
                // 다이빙 시작 클립도 없으면 곧장 공중/착지 단계로 들어간다.
                LinkToAirLoops(locomotion, airStates, condition: "Falling");
            }
        }

        /// <summary>
        /// 어떤 상태에서 공중 자세들로 잇는다.
        ///
        /// 변형이 하나면 조건 없이 그냥 잇고, 여럿이면 각각에 <c>AirVariant == i</c>를 걸어
        /// <b>그 순간 뽑혀 있는 번호 하나로만</b> 들어가게 한다. 전이를 여러 개 만들어두고
        /// 조건으로 가르는 이유는, 유니티 상태 기계에 "여럿 중 하나를 무작위로"가 없기
        /// 때문이다 — 번호는 <see cref="Momentum.Player.AirLoopVariantPicker"/>가 미리 뽑아둔다.
        /// </summary>
        private static void LinkToAirLoops(AnimatorState from, List<AnimatorState> airStates,
                                           float exitTime = -1f, string condition = null)
        {
            for (int i = 0; i < airStates.Count; i++)
            {
                var transition = from.AddTransition(airStates[i]);
                if (exitTime >= 0f)
                {
                    transition.hasExitTime = true;
                    transition.exitTime = exitTime;
                }
                else
                {
                    transition.hasExitTime = false;
                }
                transition.duration = 0.1f;

                if (!string.IsNullOrEmpty(condition))
                    transition.AddCondition(AnimatorConditionMode.If, 0f, condition);
                if (airStates.Count > 1)
                    transition.AddCondition(AnimatorConditionMode.Equals, i, AirVariantParameter);
            }
        }

        /// <summary>
        /// 공중 상태에서 착지로 나가는 전이를 단다.
        ///
        /// 착지 종류가 둘이라 전이도 둘이다 — 스윙에서 내려온 것이면
        /// <paramref name="swingLand"/>(히어로 착지)로, 그냥 떨어진 것이면
        /// <paramref name="normalLand"/>(평범한 착지)로. <b>스윙 쪽을 먼저 단다.</b> 유니티는
        /// 전이를 등록 순서대로 검사하므로, 조건이 겹치는 두 전이 중 먼저 등록된 것이 이긴다
        /// — 히어로 착지를 뒤에 달면 조건이 더 느슨한 일반 착지에 항상 먹혀 영영 안 나온다.
        ///
        /// <b>둘의 타이밍 전략이 다르다.</b>
        /// <list type="bullet">
        /// <item><b>히어로 착지</b>는 <c>LandingSoon</c>(착지 예고) + <c>SwingLanding</c> +
        ///       체공 시간이 <see cref="HeroLandingMinAirTime"/>을 넘었을 때만 나온다.
        ///       예고가 착지 전에 미리 켜져야 자세가 다 형성된 채로 발이 닿는데, 실측해보니
        ///       <c>LandingSoon</c>의 실제 예고 시간이 SphereCast·물리 스텝 오차로
        ///       0.03~0.32초까지 널뛴다. 이 불안정을 완전히 없앨 수 없으니, 대신 "진짜 크게
        ///       뜬 특정 상황"에만 걸어 다소 어긋나도 화려한 연출 자체가 그 어색함을
        ///       가리게 한다</item>
        /// <item><b>평범한 착지</b>는 예고 없이 <c>Grounded</c> 하나로 튼다. 발이 실제로
        ///       닿은 <b>뒤에</b> 시작해도 어색하지 않은 자세라, 정확한 타이밍이 필요한
        ///       예고 시스템에 기댈 이유가 없다 — 이쪽은 항상 확실하게 걸린다</item>
        /// </list>
        /// </summary>
        private static void AddLandingTransitions(AnimatorState from, AnimatorState normalLand,
                                                  AnimatorState swingLand)
        {
            if (swingLand != null)
            {
                var toSwingLand = from.AddTransition(swingLand);
                toSwingLand.hasExitTime = false;
                toSwingLand.duration = 0.1f;
                toSwingLand.AddCondition(AnimatorConditionMode.If, 0f, "LandingSoon");
                toSwingLand.AddCondition(AnimatorConditionMode.If, 0f, "SwingLanding");
                toSwingLand.AddCondition(AnimatorConditionMode.Greater, HeroLandingMinAirTime, "AirTime");
            }

            var toGround = from.AddTransition(normalLand);
            toGround.hasExitTime = false;
            toGround.duration = 0.1f;
            toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        }

        /// <summary>
        /// 한 상태에서 변형 상태들로, 같은 조건에 <c>AirVariant</c>만 달리 걸어 잇는다.
        /// <see cref="LinkToAirLoops"/>와 하는 일은 같고 조건 종류(Bool이 아니라 Float 비교 등)를
        /// 호출부가 정할 수 있다는 점만 다르다.
        /// </summary>
        private static void LinkVariants(AnimatorState from, List<AnimatorState> targets, int count,
                                         string condition, AnimatorConditionMode mode, float threshold)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var transition = from.AddTransition(targets[i]);
                transition.hasExitTime = false;
                transition.duration = 0.1f;
                transition.AddCondition(mode, threshold, condition);
                if (count > 1)
                    transition.AddCondition(AnimatorConditionMode.Equals, i, AirVariantParameter);
            }
        }

        /// <summary>
        /// 클립 목록으로 상태를 여러 개 만든다. 하나면 <paramref name="namePrefix"/> 그대로,
        /// 여럿이면 <c>{namePrefix}_0</c>·<c>_1</c>...로 나누고 각각에 변형 뽑기
        /// (<see cref="Momentum.Player.AirLoopVariantPicker"/>)를 붙인다.
        ///
        /// 진입·탈출 전이는 여기서 안 잇는다 — 낙하 사슬(Grounded로 나간다)과 스윙 레이어
        /// (HighFlight로 들고난다)가 붙이는 조건이 서로 다르기 때문에, 상태를 만드는
        /// 부분만 공용으로 빼고 배선은 호출부가 각자 한다.
        /// </summary>
        private static List<AnimatorState> BuildVariantStates(
            AnimatorStateMachine stateMachine, List<AnimationClip> clips, string namePrefix)
        {
            var states = new List<AnimatorState>();
            for (int i = 0; i < clips.Count; i++)
            {
                var state = stateMachine.AddState(clips.Count == 1 ? namePrefix : $"{namePrefix}_{i}");
                state.motion = clips[i];

                if (clips.Count > 1)
                {
                    var picker = state.AddStateMachineBehaviour<Momentum.Player.AirLoopVariantPicker>();
                    picker.variantCount = clips.Count;
                    picker.parameterName = AirVariantParameter;
                }

                states.Add(state);
            }
            return states;
        }

        /// <summary>
        /// 스윙 전용 레이어를 새로 만든다.
        ///
        /// 이 레이어의 가중치는 코드(<c>SwingConstraint</c>)가 붙고 뗄 때마다 0과 1 사이로
        /// 직접 옮긴다. 1이면 이 레이어가 기본 레이어의 전신 포즈를 완전히 덮어써서
        /// 몸 전체가 스윙 자세가 되고, 0이면 다시 기본 레이어(걷기·점프)가 그대로 보인다.
        /// Override 블렌드 모드가 그 "완전히 덮어쓰기"를 담당하는 유니티 기능이다.
        ///
        /// 레이어 안에는 SwingLoop ↔ ReelLoop ↔ NinjaAirLoop, 셋이다.
        /// <c>Reeling</c>과 <c>HighFlight</c> 두 파라미터로 갈린다.
        ///
        /// NinjaAirLoop은 <b>새 클립을 안 쓴다.</b> 낙하 사슬(<see cref="BuildFallChain"/>)이
        /// 쓰는 <c>set.airLoop</c>(NinjaJump 루프)를 그대로 재사용한다 — 스윙 중 크게 솟아
        /// 오른 상태와 자유낙하 중인 상태는 둘 다 "아치를 벗어난 큰 공중 동작"이라는 같은
        /// 그림이라, 클립을 하나 더 구할 필요가 없었다.
        /// </summary>
        private static void BuildSwingLayer(AnimatorController controller, PlayerAnimationSet set)
        {
            controller.AddParameter("Reeling", AnimatorControllerParameterType.Bool);
            controller.AddParameter("HighFlight", AnimatorControllerParameterType.Bool);
            controller.AddParameter("SwingStarted", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SwingEnded", AnimatorControllerParameterType.Trigger);

            controller.AddLayer("Swing");
            var swingLayer = controller.layers[controller.layers.Length - 1];
            swingLayer.defaultWeight = 0f;   // 평소엔 안 보인다. SwingConstraint가 켠다.
            swingLayer.blendingMode = AnimatorLayerBlendingMode.Override;

            var stateMachine = swingLayer.stateMachine;

            var swingState = stateMachine.AddState("SwingLoop");
            swingState.motion = set.swingLoop;
            stateMachine.defaultState = swingState;

            // 붙는 순간·놓는 순간 한 번씩 재생되는 전환 클립. Any State에서 트리거로 들어오는
            // 이유는 이 레이어의 가중치가 코드로 0↔1을 오가는 것과 별개로, 상태 기계 자체는
            // 가중치 0일 때도 계속 돌고 있어서다 — 매번 SwingLoop부터 다시 시작한다는 보장이
            // 없으므로, "지금 막 붙었다/놓았다"는 사실을 트리거로 명시적으로 알려야 한다.
            if (set.startSwing != null)
            {
                var startState = stateMachine.AddState("StartSwing");
                startState.motion = set.startSwing;

                var toStart = stateMachine.AddAnyStateTransition(startState);
                toStart.hasExitTime = false;
                toStart.duration = 0.05f;
                toStart.canTransitionToSelf = false;
                toStart.AddCondition(AnimatorConditionMode.If, 0f, "SwingStarted");

                // 재생이 끝나면(exitTime) 자동으로 루프로 넘어간다 — Reeling·HighFlight처럼
                // 조건으로 가르지 않는 이유는 붙는 순간은 항상 SwingLoop에서 시작해야 하기 때문이다.
                var startToLoop = startState.AddTransition(swingState);
                startToLoop.hasExitTime = true;
                startToLoop.exitTime = 0.9f;
                startToLoop.duration = 0.1f;
            }

            // swingLand는 여기 두지 않는다. 예전에는 이 레이어에서 SwingEnded(=줄을 놓는
            // 순간)로 걸었는데, 그 순간은 보통 공중 한복판이라 <b>착지 모션이 착지 전에
            // 재생되고 끝나 있었다.</b> 지금은 BuildFallChain이 기본 레이어에 두고 Grounded로
            // 건다 — 공중 자세를 다 돌고 발이 닿을 때 나온다.

            if (set.reelLoop != null)
            {
                var reelState = stateMachine.AddState("ReelLoop");
                reelState.motion = set.reelLoop;

                var toReel = swingState.AddTransition(reelState);
                toReel.hasExitTime = false;
                toReel.duration = 0.15f;
                toReel.AddCondition(AnimatorConditionMode.If, 0f, "Reeling");

                var toSwing = reelState.AddTransition(swingState);
                toSwing.hasExitTime = false;
                toSwing.duration = 0.15f;
                toSwing.AddCondition(AnimatorConditionMode.IfNot, 0f, "Reeling");
            }

            // 감고 있는 동안(ReelLoop)은 이 전환을 안 건다 — 줄을 당기는 능동적 동작이
            // 우선이고, "높이 떴다"는 부가 연출이 그걸 덮어쓰면 안 된다. 그래서 SwingLoop
            // 쪽에만 오간다.
            if (set.airLoop != null)
            {
                // 예전엔 낙하 사슬(BuildFallChain)의 AirLoop_i와 같은 클립 목록(airLoop +
                // 변형 3개)을 통째로 재사용했다 — "공중이지만 아직 확정 안 된 자세"와
                // "스윙에서 크게 솟은 자세"가 같은 종류라는 판단이었다. 그런데 그 변형
                // 3개(Floating·Head Spinning·Breakdance 1990)는 자유낙하용 화려한 곡예
                // 동작이라, 아직 <b>줄에 매달린 채</b> 스윙 중인 상태에 섞이면 로프를 잡고
                // 헤드스핀·브레이크댄스를 추는 꼴이 된다. NinjaAirLoop은 airLoop 하나만
                // 쓴다 — 변형은 진짜로 로프를 놓은 뒤(낙하 사슬)에만 나온다.
                var ninjaClips = new List<AnimationClip> { set.airLoop };
                var ninjaStates = BuildVariantStates(stateMachine, ninjaClips, "NinjaAirLoop");

                for (int i = 0; i < ninjaStates.Count; i++)
                {
                    var toAir = swingState.AddTransition(ninjaStates[i]);
                    toAir.hasExitTime = false;
                    toAir.duration = 0.2f;
                    toAir.AddCondition(AnimatorConditionMode.If, 0f, "HighFlight");
                    if (ninjaStates.Count > 1)
                        toAir.AddCondition(AnimatorConditionMode.Equals, i, AirVariantParameter);

                    var toSwingBack = ninjaStates[i].AddTransition(swingState);
                    toSwingBack.hasExitTime = false;
                    toSwingBack.duration = 0.2f;
                    toSwingBack.AddCondition(AnimatorConditionMode.IfNot, 0f, "HighFlight");
                }
            }

            // AddLayer로 만든 레이어는 값이 배열 복사본이라, 고친 뒤 반드시 다시 넣어줘야
            // 컨트롤러 에셋에 반영된다. Unity의 layers가 struct 배열이라 생기는 규칙이다.
            var layers = controller.layers;
            layers[layers.Length - 1] = swingLayer;
            controller.layers = layers;
        }

        /// <summary>
        /// 견인(우클릭) 전용 레이어 — 끌려가는 동안의 전신 자세.
        ///
        /// 스윙 레이어와 <b>따로</b> 두는 이유는 가중치의 주인이 다르기 때문이다. 스윙
        /// 레이어는 <c>SwingConstraint</c>가, 이쪽은 <c>GrappleZip</c>이 켜고 끈다. 한
        /// 레이어를 둘이 나눠 쓰면 서로의 값을 매 프레임 덮어써서 어느 쪽이 이겼는지
        /// 화면으로는 못 가린다.
        ///
        /// 상태가 하나뿐이라 전이도 파라미터도 없다 — 보일지 말지는 전적으로 레이어
        /// 가중치가 정하고, 그건 <c>GrappleZip</c>이 직접 넣는다.
        /// </summary>
        private static void BuildZipLayer(AnimatorController controller, PlayerAnimationSet set)
        {
            controller.AddLayer("Zip");
            var zipLayer = controller.layers[controller.layers.Length - 1];
            zipLayer.defaultWeight = 0f;
            zipLayer.blendingMode = AnimatorLayerBlendingMode.Override;

            var zipState = zipLayer.stateMachine.AddState("ZipLoop");
            zipState.motion = set.zipLoop;
            zipLayer.stateMachine.defaultState = zipState;

            var layers = controller.layers;
            layers[layers.Length - 1] = zipLayer;
            controller.layers = layers;
        }

        /// <summary>
        /// 갈고리를 쏘는 순간 팔(+상체)만 겹쳐 보여주는 전용 레이어.
        ///
        /// 스윙 레이어와 달리 <b>가중치를 코드가 아니라 여기서 항상 0으로 깔아둔다.</b>
        /// 쏘는 동작은 짧고 일회성이라 SwingConstraint처럼 매 프레임 상태를 들고 있을
        /// 이유가 없다 — <c>GrappleGun</c>이 트리거를 쏘는 순간에만 잠깐 1로 올렸다가
        /// 스스로 0으로 되돌린다(간단한 타이머). 마스크로 팔만 남기는 이유는, 걷거나
        /// 뛰는 중에 쏴도 다리는 그대로 이동을 계속해야 하기 때문이다 — 전신을 덮으면
        /// 쏘는 순간 발이 멈추거나 이동 애니메이션과 어긋난다.
        /// </summary>
        private static void BuildGrappleFireLayer(AnimatorController controller, PlayerAnimationSet set)
        {
            controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);

            controller.AddLayer("GrappleFire");
            var fireLayer = controller.layers[controller.layers.Length - 1];
            fireLayer.defaultWeight = 0f;
            fireLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            if (set.grappleFireMask != null) fireLayer.avatarMask = set.grappleFireMask;

            var stateMachine = fireLayer.stateMachine;
            var fireState = stateMachine.AddState("Fire");
            fireState.motion = set.grappleFire;
            stateMachine.defaultState = fireState;

            // canTransitionToSelf를 켜야 연사(누르고 있는 동안 재시도)할 때마다 클립이
            // 처음부터 다시 재생된다 — 꺼두면 이미 그 상태인 동안 들어온 트리거는 무시된다.
            var restart = stateMachine.AddAnyStateTransition(fireState);
            restart.hasExitTime = false;
            restart.duration = 0f;
            restart.canTransitionToSelf = true;
            restart.AddCondition(AnimatorConditionMode.If, 0f, "Fire");

            var layers = controller.layers;
            layers[layers.Length - 1] = fireLayer;
            controller.layers = layers;
        }

        /// <summary>
        /// 소품을 던지는 순간 전신을 잠깐 덮는 레이어.
        ///
        /// <c>GrappleFire</c>와 같은 뼈대다(마스크 없이 단일 클립, 트리거로 재시작 가능한
        /// Any State 전이 하나) — 다른 점은 <b>마스크가 없다는 것뿐</b>이다. 쏘는 동작은
        /// 팔만 움직이면 그림이 나오지만, 던지는 동작은 몸통까지 크게 쓰는 동작이라
        /// 마스크로 다리를 남겨두면 이동 애니메이션과 뒤섞여 이상해진다 — 던지는 그 순간은
        /// 이동 애니메이션과 안 섞여도 된다는 판단.
        ///
        /// 가중치도 <c>GrappleFire</c>와 같은 방식으로 여기서는 항상 0으로 깔아두고,
        /// <see cref="WebGrabThrow"/>가 던지는 순간에만 잠깐 1로 올렸다가 스스로 내린다
        /// (같은 종류의 간단한 타이머).
        /// </summary>
        private static void BuildThrowLayer(AnimatorController controller, PlayerAnimationSet set)
        {
            controller.AddParameter("ThrowTrigger", AnimatorControllerParameterType.Trigger);

            controller.AddLayer("Throw");
            var throwLayer = controller.layers[controller.layers.Length - 1];
            throwLayer.defaultWeight = 0f;
            throwLayer.blendingMode = AnimatorLayerBlendingMode.Override;

            // 마스크를 넣으면 스윙 중에 던져도 다리가 매달린 자세를 유지한다. 비워두면
            // 예전처럼 전신을 덮는다 — GrappleFire와 같은 구조이므로 여기도 조건부다.
            if (set.throwMask != null) throwLayer.avatarMask = set.throwMask;

            var stateMachine = throwLayer.stateMachine;
            var throwState = stateMachine.AddState("ThrowProp");
            throwState.motion = set.throwProp;
            stateMachine.defaultState = throwState;

            var restart = stateMachine.AddAnyStateTransition(throwState);
            restart.hasExitTime = false;
            restart.duration = 0f;
            restart.canTransitionToSelf = true;
            restart.AddCondition(AnimatorConditionMode.If, 0f, "ThrowTrigger");

            var layers = controller.layers;
            layers[layers.Length - 1] = throwLayer;
            controller.layers = layers;
        }

        /// <summary>
        /// 잡은 소품을 회전하며 던지는 순간(V) 전신을 잠깐 덮는 레이어.
        ///
        /// <see cref="BuildThrowLayer"/>와 완전히 같은 뼈대다(마스크 없는 단일 클립,
        /// 트리거로 재시작 가능한 Any State 전이 하나) — 그랩 앤 슬램도 몸통까지 크게
        /// 쓰는 전신 동작이라 마스크가 없다. 이 클립은 제자리 회전이 루트 모션으로
        /// 들어있어야 <see cref="Momentum.Player.PlayerRootMotionRelay"/>가 그 회전을
        /// 뽑아 캐릭터에 옮길 수 있다 — Override 블렌드라 가중치 1일 때 기본 레이어를
        /// 완전히 덮으므로 루트 모션 추출에는 지장이 없다.
        /// </summary>
        private static void BuildGrabAndSlamLayer(AnimatorController controller, PlayerAnimationSet set)
        {
            controller.AddParameter("GrabAndSlam", AnimatorControllerParameterType.Trigger);

            controller.AddLayer("GrabAndSlam");
            var slamLayer = controller.layers[controller.layers.Length - 1];
            slamLayer.defaultWeight = 0f;
            slamLayer.blendingMode = AnimatorLayerBlendingMode.Override;

            var stateMachine = slamLayer.stateMachine;
            var slamState = stateMachine.AddState("GrabAndSlam");
            slamState.motion = set.grabAndSlam;
            stateMachine.defaultState = slamState;

            var restart = stateMachine.AddAnyStateTransition(slamState);
            restart.hasExitTime = false;
            restart.duration = 0f;
            restart.canTransitionToSelf = true;
            restart.AddCondition(AnimatorConditionMode.If, 0f, "GrabAndSlam");

            var layers = controller.layers;
            layers[layers.Length - 1] = slamLayer;
            controller.layers = layers;
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.activeObject is PlayerAnimationSet;
    }
}
