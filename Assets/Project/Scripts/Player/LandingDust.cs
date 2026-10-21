using UnityEngine;
using UnityEngine.Rendering;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 착지 먼지 — 빠르게 떨어져 땅에 닿는 순간 발밑에서 먼지가 둥글게 퍼진다. 착지 애니메이션 대신 이것만 쓴다.
    ///
    /// <b>언제.</b> 공중 → 땅으로 바뀌는 프레임에, 직전 공중에서의 낙하 속도가 <see cref="minFallSpeed"/> 이상일 때.
    /// 떨어진 속도가 빠를수록 먼지 개수·크기·퍼지는 속도가 커진다(<see cref="fullFallSpeed"/>에서 최대).
    /// 계단 내려가기 같은 작은 착지엔 안 나온다.
    ///
    /// <b>모양.</b> 유니티 내장 ParticleSystem. 땅과 나란한 원판 둘레에서 바깥쪽으로 퍼지고, 공기 저항으로 금방 느려지며,
    /// 커지면서 흐려진다. 텍스처는 Kenney Particle Pack(CC0)의 smoke(흰색 + 투명 배경)를 회색 먼지 색으로 알파 블렌드한다.
    /// <see cref="GrappleGun"/>이 실행 중에 붙이고 텍스처를 넘겨준다.
    /// </summary>
    public class LandingDust : MonoBehaviour
    {
        [Tooltip("이 낙하 속도(m/s) 이상으로 땅에 닿아야 먼지가 나온다.")]
        [Min(0f)][SerializeField] private float minFallSpeed = 9f;
        [Tooltip("이 낙하 속도(m/s)에서 먼지가 가장 크고 많다.")]
        [Min(1f)][SerializeField] private float fullFallSpeed = 30f;
        [Tooltip("먼지 개수 (가장 약할 때, 가장 셀 때).")]
        [SerializeField] private Vector2Int count = new Vector2Int(10, 28);
        [Tooltip("바깥으로 퍼지는 속도 (가장 약할 때, 가장 셀 때, m/s).")]
        [SerializeField] private Vector2 spreadSpeed = new Vector2(3f, 9f);
        [Tooltip("먼지 한 덩이 크기 (가장 약할 때, 가장 셀 때, m).")]
        [SerializeField] private Vector2 puffSize = new Vector2(0.8f, 1.8f);
        [Tooltip("먼지 색과 최대 진하기(알파).")]
        [SerializeField] private Color color = new Color(0.72f, 0.69f, 0.64f, 0.55f);
        [Tooltip("먼지가 남아 있는 시간(초).")]
        [Min(0.1f)][SerializeField] private float lifetime = 0.9f;

        private Rigidbody _body;
        private PlayerMovement _movement;
        private ParticleSystem _particles;
        private bool _wasGrounded = true;
        private float _fallSpeed;

        /// <summary>붙이는 쪽에서 텍스처를 넘긴다. 텍스처가 없으면 아무것도 만들지 않는다.</summary>
        public void Init(Texture texture)
        {
            _body = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            if (texture == null || _particles != null) return;
            _particles = CreateParticles(texture);
        }

        /// <summary>
        /// 공중에선 낙하 속도를 기억해 두고, 땅에 닿는 프레임에 그 속도로 먼지 세기를 정해 터뜨린다.
        /// 닿는 순간의 속도는 이미 0으로 잘려 있으므로 직전 공중 프레임 값을 쓴다.
        /// </summary>
        private void FixedUpdate()
        {
            if (_particles == null || _body == null || _movement == null) return;

            bool grounded = _movement.IsGrounded;
            if (!grounded)
                _fallSpeed = Mathf.Max(0f, -_body.linearVelocity.y);
            else if (!_wasGrounded && _fallSpeed >= minFallSpeed)
                Burst(Mathf.InverseLerp(minFallSpeed, fullFallSpeed, _fallSpeed));
            _wasGrounded = grounded;
        }

        /// <summary>세기(0~1)에 맞춰 발밑에서 먼지를 한 번 터뜨린다.</summary>
        private void Burst(float strength)
        {
            // 몸 기준점이 발인지 허리인지와 상관없이 실제 땅 위에서 터지게 아래로 재서 맞은 점을 쓴다.
            Vector3 ground = _body.position;
            if (Physics.Raycast(_body.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                ground = hit.point;
            _particles.transform.position = ground + Vector3.up * 0.1f;
            _particles.transform.rotation = Quaternion.identity;

            var main = _particles.main;
            float speed = Mathf.Lerp(spreadSpeed.x, spreadSpeed.y, strength);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            float size = Mathf.Lerp(puffSize.x, puffSize.y, strength);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);

            _particles.Emit(Mathf.RoundToInt(Mathf.Lerp(count.x, count.y, strength)));
        }

        /// <summary>
        /// 파티클 시스템을 코드로 만든다. 땅과 나란한 원 둘레(Circle)에서 바깥쪽으로 퍼지는 먼지,
        /// 공기 저항(Limit Velocity의 drag)으로 금방 멈추고, 커지면서 흐려진다. 월드 공간이라 플레이어가 움직여도 그 자리에 남는다.
        /// </summary>
        private ParticleSystem CreateParticles(Texture texture)
        {
            var go = new GameObject("LandingDust");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.05f; // 아주 살짝 떠오른다
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            // 원(Circle)은 기본이 XY 평면이라 눕혀서 땅과 나란하게.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.4f;
            shape.radiusThickness = 0f; // 둘레에서만 → 바깥으로 퍼진다
            shape.rotation = new Vector3(90f, 0f, 0f);

            // 공기 저항 — 확 퍼졌다가 금방 멈춘다.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 4f;
            limit.multiplyDragByParticleSize = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.1f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = CreateMaterial(texture);
            return ps;
        }

        /// <summary>URP 파티클 Unlit, 알파 블렌드 — 흰 연기 텍스처를 회색 먼지로 칠한다(빛나지 않게).</summary>
        private static Material CreateMaterial(Texture texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var material = new Material(shader) { name = "LandingDust (Alpha)" };
            material.SetFloat("_Surface", 1f); // 반투명
            material.SetFloat("_Blend", 0f);   // 알파 블렌드
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
