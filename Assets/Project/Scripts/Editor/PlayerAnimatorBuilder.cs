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

            if (set.grappleFire != null)
                BuildGrappleFireLayer(controller, set);

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
            AnimatorState afterAir = landState != null ? landState : locomotion;

            AnimatorState airState = null;
            if (set.airLoop != null)
            {
                airState = layer.AddState("AirLoop");
                airState.motion = set.airLoop;

                var toGround = airState.AddTransition(afterAir);
                toGround.hasExitTime = false; // 체공 시간이 매번 다르므로 시간이 아니라 상태로 끊는다
                toGround.duration = 0.1f;
                toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
            AnimatorState afterDive = airState != null ? airState : afterAir;

            AnimatorState diveState = null;
            if (set.diveStart != null)
            {
                diveState = layer.AddState("DiveStart");
                diveState.motion = set.diveStart;

                if (airState != null)
                {
                    var toAir = diveState.AddTransition(airState);
                    toAir.hasExitTime = true;
                    toAir.exitTime = 0.9f; // 다이빙 모션이 거의 끝날 때 공중 루프로 넘어간다
                    toAir.duration = 0.1f;
                }
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
                var airIdleState = layer.AddState("AirIdle");
                airIdleState.motion = set.airIdle;

                var enterAirIdle = locomotion.AddTransition(airIdleState);
                enterAirIdle.hasExitTime = false;
                enterAirIdle.duration = 0.1f;
                enterAirIdle.AddCondition(AnimatorConditionMode.Greater, AirIdleAirTimeThreshold, "AirTime");

                // 진짜 낙하로 확정되면 다이빙/공중 루프 쪽으로 넘긴다.
                var toRealFall = airIdleState.AddTransition(afterEnter);
                toRealFall.hasExitTime = false;
                toRealFall.duration = 0.15f;
                toRealFall.AddCondition(AnimatorConditionMode.If, 0f, "Falling");

                // 낙하로 안 이어지고 그새 다시 땅에 닿으면(턱을 살짝 넘은 정도) 곧장
                // 이동으로 돌아간다 — Land까지 갈 정도로 크게 뜬 게 아니었으므로.
                var quickLand = airIdleState.AddTransition(locomotion);
                quickLand.hasExitTime = false;
                quickLand.duration = 0.15f;
                quickLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

                // 스윙을 놓는 순간을 Any State에서 강제로 잡는다. 스윙 중에도 이 레이어의
                // 상태 기계는 (안 보일 뿐) Grounded/Falling을 그대로 따라 계속 돌고 있어서,
                // 놓는 시점에 Locomotion에 있다는 보장이 없다 — 오래 매달려 있었으면 이미
                // AirLoop나 Land로 넘어가 있을 수 있고, 그러면 위 "Locomotion에서만 전이"
                // 규칙이 걸릴 기회 자체가 없다. Any State는 현재 상태와 무관하게 항상
                // 검사되므로, 어디에 가 있었든 놓는 순간 확실히 AirIdle로 되돌린다.
                var forceAirIdle = layer.AddAnyStateTransition(airIdleState);
                forceAirIdle.hasExitTime = false;
                forceAirIdle.duration = 0.1f;
                forceAirIdle.canTransitionToSelf = false;
                forceAirIdle.AddCondition(AnimatorConditionMode.If, 0f, "SwingReleased");
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
                var enter = locomotion.AddTransition(afterDive);
                enter.hasExitTime = false;
                enter.duration = 0.1f;
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Falling");
            }
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

            if (set.swingLand != null)
            {
                var landState = stateMachine.AddState("SwingLand");
                landState.motion = set.swingLand;

                var toLand = stateMachine.AddAnyStateTransition(landState);
                toLand.hasExitTime = false;
                toLand.duration = 0.1f;
                toLand.canTransitionToSelf = false;
                toLand.AddCondition(AnimatorConditionMode.If, 0f, "SwingEnded");

                // 돌아갈 곳을 안 잇는다. 놓는 순간 SwingConstraint가 이 레이어 가중치를
                // 서서히 0으로 내리므로(UpdateAnimator), 이 클립이 보이는 동안 자연히
                // 기본 레이어(착지/이동)로 섞여 들어간다 — 상태 기계가 SwingLoop으로
                // 되돌아갈 필요가 없다.
            }

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
                var airState = stateMachine.AddState("NinjaAirLoop");
                airState.motion = set.airLoop;

                var toAir = swingState.AddTransition(airState);
                toAir.hasExitTime = false;
                toAir.duration = 0.2f;
                toAir.AddCondition(AnimatorConditionMode.If, 0f, "HighFlight");

                var toSwingBack = airState.AddTransition(swingState);
                toSwingBack.hasExitTime = false;
                toSwingBack.duration = 0.2f;
                toSwingBack.AddCondition(AnimatorConditionMode.IfNot, 0f, "HighFlight");
            }

            // AddLayer로 만든 레이어는 값이 배열 복사본이라, 고친 뒤 반드시 다시 넣어줘야
            // 컨트롤러 에셋에 반영된다. Unity의 layers가 struct 배열이라 생기는 규칙이다.
            var layers = controller.layers;
            layers[layers.Length - 1] = swingLayer;
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

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.activeObject is PlayerAnimationSet;
    }
}
