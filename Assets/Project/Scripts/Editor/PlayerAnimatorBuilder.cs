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

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
            Debug.Log($"[PlayerAnimatorBuilder] {path} 생성 완료. " +
                      "Security Officer의 Animator 컴포넌트 Controller 슬롯에 끌어넣을 것.", controller);
        }

        /// <summary>
        /// 점프를 Start → AirLoop → Land → Locomotion 사슬로 잇는다.
        ///
        /// Start에서 곧장 Land로 가면 체공 시간과 무관하게 뛰어오르자마자 착지 모션이 나온다.
        /// 공중 구간은 길이가 매번 다르므로 <b>시간이 아니라 상태로</b> 끊어야 하고,
        /// 그래서 AirLoop을 반복 재생하다가 PlayerMovement가 보내는 Grounded가 참이 되는
        /// 순간 Land로 넘긴다. 중간 클립이 없으면 그 자리를 건너뛰도록 사슬을 좁힌다.
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

            // 공중에서 착지를 기다릴 상태. Land가 없으면 곧장 Locomotion으로 돌아간다.
            AnimatorState afterAir = landState != null ? landState : locomotion;

            if (set.airLoop != null)
            {
                var airState = layer.AddState("AirLoop");
                airState.motion = set.airLoop;

                var toAir = jumpState.AddTransition(airState);
                toAir.hasExitTime = true;
                toAir.exitTime = 0.9f; // 뛰어오르는 모션이 거의 끝날 때 공중 자세로 넘어간다
                toAir.duration = 0.1f;

                var toGround = airState.AddTransition(afterAir);
                toGround.hasExitTime = false; // 체공 시간이 매번 다르므로 시간이 아니라 상태로 끊는다
                toGround.duration = 0.1f;
                toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
            else
            {
                // 공중 클립이 없으면 뛰어오르는 모션이 끝나는 대로 다음 단계로 보낸다.
                var toNext = jumpState.AddTransition(afterAir);
                toNext.hasExitTime = false;
                toNext.duration = 0.1f;
                toNext.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.activeObject is PlayerAnimationSet;
    }
}
