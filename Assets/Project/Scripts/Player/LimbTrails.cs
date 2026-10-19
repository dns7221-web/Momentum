using UnityEngine;
using UnityEngine.Rendering;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-19)
    /// <summary>
    /// 손·발 끝에 짧은 꼬리(트레일)를 단다. 평소엔 꺼져 있다가 <see cref="Play"/>를 부르면 잠깐 켜진다 —
    /// 슬링샷 발사처럼 "확 날아가는" 순간만 궤적을 남겨 속도감을 준다.
    ///
    /// 유니티 내장 <c>TrailRenderer</c>를 휴머노이드 뼈(<c>Animator.GetBoneTransform</c>)에 붙인다.
    /// 휴머노이드라 어떤 캐릭터 모델이든 같은 코드로 손·발을 찾는다. 에셋 없이 코드로 만든다.
    /// </summary>
    public class LimbTrails : MonoBehaviour
    {
        [Tooltip("꼬리 길이(초). 길수록 궤적이 오래 남는다.")]
        [Min(0.01f)][SerializeField] private float trailTime = 0.25f;
        [Tooltip("꼬리 시작 굵기(m). 끝으로 갈수록 0으로 가늘어진다.")]
        [Min(0.001f)][SerializeField] private float trailWidth = 0.06f;
        [SerializeField] private Color trailColor = new Color(1f, 1f, 1f, 0.5f);

        private static readonly HumanBodyBones[] Limbs =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        private TrailRenderer[] _trails;
        private float _until;

        /// <summary>캐릭터 Animator에서 손·발 뼈를 찾아 꼬리를 하나씩 붙인다.</summary>
        public void Init(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;

            var material = CreateMaterial();
            var list = new System.Collections.Generic.List<TrailRenderer>();
            foreach (var bone in Limbs)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t == null) continue;
                var go = new GameObject("Trail_" + bone);
                go.transform.SetParent(t, false);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = trailTime;
                trail.startWidth = trailWidth;
                trail.endWidth = 0f;
                trail.startColor = trailColor;
                trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
                trail.sharedMaterial = material;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                list.Add(trail);
            }
            _trails = list.ToArray();
        }

        /// <summary>꼬리를 <paramref name="seconds"/>초 동안 켠다.</summary>
        public void Play(float seconds)
        {
            _until = Mathf.Max(_until, Time.time + seconds);
            SetEmitting(true);
        }

        private void Update()
        {
            if (_trails != null && Time.time > _until) SetEmitting(false);
        }

        private void SetEmitting(bool on)
        {
            if (_trails == null) return;
            foreach (var trail in _trails)
                if (trail != null && trail.emitting != on) trail.emitting = on;
        }

        /// <summary>URP 파티클 Unlit 반투명 — SpeedLines와 같은 설정.</summary>
        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }
    }
}
