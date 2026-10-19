using UnityEngine;
using UnityEngine.Rendering;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-19)
    /// <summary>
    /// 속도선 — 화면 가장자리에서 카메라 쪽으로 흘러 지나가는 가는 선. 빠를수록 많아지고,
    /// <see cref="TraversalFx.Burst"/>가 부르면(슬링샷 발사 등) 잠깐 꽉 차게 터진다.
    ///
    /// 에셋 없이 유니티 내장 <c>ParticleSystem</c>을 코드로 만든다. 카메라 앞 도넛 모양 영역에서 태어나
    /// 카메라 쪽으로 날아오고, <c>Stretch</c> 렌더 모드로 속도 방향으로 길게 늘려 선처럼 보이게 한다.
    /// 시뮬레이션을 카메라 기준(Local)으로 두어 카메라가 돌아도 선이 화면에 붙어 흐른다.
    /// <see cref="SpeedFieldOfView"/>가 실행 중에 메인 카메라에 붙인다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SpeedLines : MonoBehaviour
    {
        [Tooltip("속도를 읽을 플레이어 Rigidbody. 비우면 아무것도 안 그린다(SpeedFieldOfView가 넣어 준다).")]
        [SerializeField] private Rigidbody body = null;

        [Tooltip("이 속도(m/s)부터 선이 나오기 시작한다. 평소 스윙보다 조금 빠른 값.")]
        [Min(0f)][SerializeField] private float startSpeed = 25f;
        [Tooltip("이 속도(m/s)에서 선이 가장 많다.")]
        [Min(1f)][SerializeField] private float fullSpeed = 45f;
        [Tooltip("가장 많을 때 초당 선 개수.")]
        [Min(0f)][SerializeField] private float maxRate = 140f;
        [Tooltip("선이 카메라 쪽으로 날아오는 속도(m/s). 빠를수록 휙휙 지나간다.")]
        [Min(1f)][SerializeField] private float lineSpeed = 60f;
        [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.35f);

        private ParticleSystem _particles;
        private float _burstUntil;

        /// <summary>붙이는 쪽에서 플레이어 몸을 넘겨준다.</summary>
        public void Init(Rigidbody target) => body = target;

        private void Awake() => _particles = CreateParticles();
        private void OnEnable() => TraversalFx.SpeedLineBurst += OnBurst;
        private void OnDisable() => TraversalFx.SpeedLineBurst -= OnBurst;

        private void OnBurst(float seconds) => _burstUntil = Mathf.Max(_burstUntil, Time.unscaledTime + seconds);

        /// <summary>속도(또는 터뜨리기 요청)에 맞춰 초당 개수만 바꾼다.</summary>
        private void Update()
        {
            if (_particles == null) return;

            float amount = 0f;
            if (body != null)
                amount = Mathf.InverseLerp(startSpeed, fullSpeed, body.linearVelocity.magnitude);
            if (Time.unscaledTime < _burstUntil) amount = 1f;

            var emission = _particles.emission;
            emission.rateOverTime = amount * maxRate;
        }

        /// <summary>
        /// 파티클 시스템을 코드로 만든다. 카메라 12 m 앞, 반지름 4 m 도넛 둘레에서 태어나 카메라 쪽(-Z)으로 날아온다.
        /// 화면 가운데(캐릭터가 있는 곳)는 비고 가장자리에만 선이 생긴다.
        /// </summary>
        private ParticleSystem CreateParticles()
        {
            var go = new GameObject("SpeedLines");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 12f);
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = 12f / lineSpeed * 1.2f; // 카메라 뒤로 넘어갈 때쯤 사라지게
            main.startSpeed = 0f;
            main.startSize = 0.025f;
            main.startColor = lineColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 카메라에 붙어 흐른다
            main.maxParticles = 500;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = 4f;
            shape.donutRadius = 1.5f;
            // 도넛은 기본이 XZ 평면이라 카메라 정면(XY 평면)을 향하게 눕힌다.
            shape.rotation = new Vector3(90f, 0f, 0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-lineSpeed);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f; // 빠를수록 길게
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = CreateMaterial();

            ps.Play();
            return ps;
        }

        /// <summary>URP 파티클 Unlit을 반투명으로 쓴다. 에셋 없이 코드로 설정한다.</summary>
        private Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f); // 반투명
            material.SetFloat("_Blend", 0f);   // 알파 블렌드
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
