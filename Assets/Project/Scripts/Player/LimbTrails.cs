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

        // 추가 생성 (2026-10-20) — 스윙 등으로 빨라지면 꼬리가 저절로 켜진다(몸에 붙은 바람)
        [Header("속도 꼬리 — 빨라지면 저절로")]
        [Tooltip("켜면 이 속도 이상일 때 꼬리가 계속 나온다. 화면 가장자리 속도선 대신 몸에 붙은 바람으로 속도감을 준다.")]
        [SerializeField] private bool speedTrails = false; // 수정 (2026-10-21) — 바람 연출을 새 에셋으로 바꾸기 전까지 끔
        [Tooltip("꼬리가 켜지기 시작하는 속도(m/s). 평소 스윙 최고 속도(30)보다 조금 아래.")]
        [Min(0f)][SerializeField] private float trailStartSpeed = 24f;
        [Tooltip("이 속도(m/s)에서 꼬리가 가장 굵고 길다.")]
        [Min(1f)][SerializeField] private float trailFullSpeed = 40f;
        private Rigidbody _body;
        private PlayerMovement _movement; // 추가 생성 (2026-10-20) — 땅에선 속도 꼬리를 끈다

        // 추가 생성 (2026-10-20) — 외부 트레일 프리팹(Vefects Trails VFX: Sound = 공기 물결 + 왜곡)
        [Header("트레일 모양 — 프리팹을 쓰면 그 재질·곡선 그대로")]
        [Tooltip("프리팹 트레일의 굵기 배율. 원본은 무기·몸 전체용이라 손발에는 가늘게 줄인다.")]
        [Min(0.01f)][SerializeField] private float prefabWidthScale = 0.3f; // 수정 (2026-10-20) — 0.15는 한 줄 실처럼 보였다
        [Tooltip("프리팹 트레일의 길이(시간) 배율.")]
        [Min(0.01f)][SerializeField] private float prefabTimeScale = 0.12f; // 수정 (2026-10-20) — 0.3이면 30 m/s에서 9 m짜리 선이 공중에 남았다
        [Tooltip("프리팹 트레일 색을 이 색으로 바꾼다(투명도 곡선은 원본 그대로). 원본 Sound 트레일은 노랑→주황이라 바람처럼 흰색으로.")]
        [SerializeField] private bool overridePrefabColor = true;
        [SerializeField] private Color prefabTint = Color.white;
        // 추가 생성 (2026-10-20) — 한 줄이 아니라 "뭉텅이"로
        [Tooltip("손·발마다 트레일을 몇 가닥 겹칠지. 여러 가닥을 살짝 어긋나게 겹치면 한 줄이 아니라 바람 덩어리로 보인다.")]
        [Range(1, 6)][SerializeField] private int strandsPerLimb = 1; // 수정 (2026-10-20) — 여러 가닥은 화면을 선으로 뒤덮었다(09:22 영상). 1가닥 + 짧게
        [Tooltip("가닥끼리 벌어지는 거리(m, 뼈 기준 반지름).")]
        [Min(0f)][SerializeField] private float strandSpread = 0.07f;
        // 추가 생성 (2026-10-20) — "현실적인 바람"으로: 덜 빛나고, 반투명하고, 살짝 일렁이게
        [Header("바람 느낌 — 덜 빛나게 · 반투명 · 일렁임")]
        [Tooltip("프리팹 재질의 발광 세기 배율. 원본(3.3)은 네온처럼 쨍해서 줄인다.")]
        [Range(0f, 1f)][SerializeField] private float emissionScale = 0.35f;
        [Tooltip("트레일 투명도 배율. 낮을수록 공기처럼 옅다.")]
        [Range(0f, 1f)][SerializeField] private float alphaScale = 0.45f;
        [Tooltip("꼬리가 나오는 점을 이 거리(m)만큼 흔든다. 줄이 곧은 선이 아니라 바람결처럼 물결친다. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float wobbleAmplitude = 0.05f;
        [Tooltip("흔드는 빠르기(초당 횟수).")]
        [Min(0f)][SerializeField] private float wobbleFrequency = 7f;
        // 추가 생성 (2026-10-20) — 7줄기 무늬 자체를 바람결로 (Vefects 셰이더 값만 바꾼다, 원본 재질은 그대로)
        [Header("무늬를 바람결로 — 트레일 셰이더 값")]
        [Tooltip("줄 무늬가 꼬리를 따라 흘러가는 속도(UV/초, 음수 = 뒤로). 0이면 무늬가 몸에 붙어 멈춰 있다.")]
        [SerializeField] private float patternFlowSpeed = -1.6f;
        [Tooltip("무늬를 구불구불하게 비트는 양. 클수록 곧은 줄이 바람결처럼 휜다(원본 0.05).")]
        [Range(0f, 0.6f)][SerializeField] private float patternWarp = 0.22f;
        [Tooltip("무늬를 군데군데 끊어 먹는 세기(침식). 클수록 줄이 이어지지 않고 바람 조각처럼 흩어진다(원본 1).")]
        [Range(0f, 3f)][SerializeField] private float patternBreakup = 1.8f;
        [Tooltip("가로 줄 수 배율. 1보다 작으면 줄이 굵고 적어져(7줄기 → 4~5줄기) 덜 선 같다.")]
        [Range(0.1f, 2f)][SerializeField] private float patternLineDensity = 0.35f; // 수정 (2026-10-20) — 7줄기 → 2~3줄기
        private Transform[] _emitters;
        private Vector3[] _emitterRest;
        private float[] _emitterPhase;
        private Material _windMaterial;
        private float[] _baseTime;
        private float[] _baseWidth;
        // 추가 생성 (2026-10-20) — 흰 줄 트레일은 "팡" 순간에만, 평소 빠를 땐 공기 일렁임(왜곡)만
        [Tooltip("켜면 흰 줄 트레일은 슬링샷 발사 같은 연출 순간에만 나오고, 스윙으로 빨라졌을 땐 공기 일렁임(왜곡)만 나온다. " +
                 "흰 줄이 스윙 내내 나오면 화면이 선으로 뒤덮였다(09:53 영상).")]
        [SerializeField] private bool linesOnlyOnBurst = true;
        private bool[] _isDistortion;
        // 추가 생성 (2026-10-20)
        [Tooltip("흰 줄 트레일을 아예 쓸지. 끄면(기본) 슬링샷 발사 때도 흰 줄 없이 공기 일렁임만 나온다 — 흰 줄이 화면을 가렸다.")]
        [SerializeField] private bool showLines = false;

        private static readonly HumanBodyBones[] Limbs =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        private TrailRenderer[] _trails;
        private float _until;

        /// <summary>
        /// 캐릭터 Animator에서 손·발 뼈를 찾아 꼬리를 하나씩 붙인다.
        /// <paramref name="trailPrefab"/>이 있으면(수정 2026-10-20) 그 프리팹을 뼈마다 복제해 그 안의 TrailRenderer를 전부 쓰고,
        /// 없으면 예전처럼 흰 단색 꼬리를 코드로 만든다.
        /// </summary>
        public void Init(Animator animator, GameObject trailPrefab = null)
        {
            _body = GetComponent<Rigidbody>(); // 추가 생성 (2026-10-20) — 플레이어 몸(같은 오브젝트)
            _movement = GetComponent<PlayerMovement>();
            if (animator == null || !animator.isHuman) return;

            // 추가 생성 (2026-10-20) — 외부 트레일 프리팹을 손·발 뼈에 붙인다. 재질·색·굵기 곡선은 원본 그대로 쓰고
            // 굵기·길이만 손발에 맞게 줄인다.
            if (trailPrefab != null)
            {
                var found = new System.Collections.Generic.List<TrailRenderer>();
                var emitters = new System.Collections.Generic.List<Transform>();
                foreach (var bone in Limbs)
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t == null) continue;
                    // 수정 (2026-10-20) — 가닥 여러 개를 뼈 둘레에 살짝 어긋나게 둔다. 굵기·길이도 가닥마다 조금씩 달리해
                    // 하나의 덩어리처럼 보이게 한다(0번 가닥이 가장 굵고 길다).
                    for (int strand = 0; strand < Mathf.Max(1, strandsPerLimb); strand++)
                    {
                        var go = Instantiate(trailPrefab, t, false);
                        go.name = "Trail_" + bone + "_" + strand;
                        float angle = strand * 137.5f * Mathf.Deg2Rad; // 황금각 — 가닥이 겹치지 않고 고르게 퍼진다
                        float radius = strand == 0 ? 0f : strandSpread * Mathf.Sqrt(strand / (float)strandsPerLimb);
                        go.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                        emitters.Add(go.transform); // 추가 생성 (2026-10-20) — 일렁임용
                        float variation = 1f - 0.18f * strand; // 바깥 가닥일수록 가늘고 짧게
                        foreach (var trail in go.GetComponentsInChildren<TrailRenderer>(true))
                        {
                            // 추가 생성 (2026-10-20) — 화면을 일렁이게 하는 왜곡 트레일은 가운데 가닥에만 남긴다.
                            // 가닥마다 왜곡이 겹치면 화면이 출렁여 어지럽다(플레이 영상 확인).
                            if (strand > 0 && trail.sharedMaterial != null &&
                                trail.sharedMaterial.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                trail.enabled = false;
                                continue;
                            }
                            trail.time *= variation;
                            trail.widthMultiplier *= variation;
                            trail.emitting = false;
                            trail.Clear();
                            trail.shadowCastingMode = ShadowCastingMode.Off;
                            if (overridePrefabColor) Tint(trail, prefabTint, alphaScale); // 추가 생성 (2026-10-20) — 노랑 → 흰색, 옅게
                            SoftenMaterial(trail); // 추가 생성 (2026-10-20) — 덜 빛나게
                            found.Add(trail);
                        }
                        // 프리팹에 파티클이 딸려 있으면 끈다(손발마다 터지면 지저분하다).
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        // 추가 생성 (2026-10-20) — 프리팹에 붙은 트레일 효과음(AudioSource)도 뺀다. 손발 4개에서 계속 울렸다.
                        // 소리는 사운드 작업 때 따로 넣는다.
                        foreach (var audio in go.GetComponentsInChildren<AudioSource>(true))
                        {
                            audio.Stop();
                            Destroy(audio);
                        }
                    }
                }
                _trails = found.ToArray();
                CacheBase(prefabTimeScale, prefabWidthScale);
                // 추가 생성 (2026-10-20) — 꼬리 나오는 점마다 원래 자리와 서로 다른 위상을 기억한다(같이 흔들리면 기계적이다).
                _emitters = emitters.ToArray();
                _emitterRest = new Vector3[_emitters.Length];
                _emitterPhase = new float[_emitters.Length];
                for (int i = 0; i < _emitters.Length; i++)
                {
                    _emitterRest[i] = _emitters[i].localPosition;
                    _emitterPhase[i] = i * 2.399f; // 황금각(라디안)으로 위상을 고르게 흩는다
                }
                return;
            }

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
            CacheBase(1f, 1f); // 추가 생성 (2026-10-20)
        }

        // 추가 생성 (2026-10-20)
        /// <summary>
        /// 트레일 색 그라디언트의 <b>색만</b> 바꾸고 투명도 키는 그대로 둔다 — 원본의 "끝으로 갈수록 흐려지는" 모양은 살린다.
        /// 프리팹 에셋 자체는 건드리지 않고 복제본에만 적용한다.
        /// </summary>
        private static void Tint(TrailRenderer trail, Color tint, float alphaScale = 1f)
        {
            Gradient source = trail.colorGradient;
            // 수정 (2026-10-20) — 투명도 키의 모양은 그대로, 세기만 alphaScale배로(공기처럼 옅게).
            GradientAlphaKey[] alphas = source.alphaKeys;
            for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= alphaScale;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                alphas);
            trail.colorGradient = gradient;
        }

        // 추가 생성 (2026-10-20)
        /// <summary>
        /// 프리팹 재질을 한 번 복제해 발광을 줄인다(원본은 네온처럼 쨍하다). 복제본 하나를 모든 꼬리가 같이 쓴다 —
        /// 원본 에셋은 건드리지 않고, 재질 수도 늘리지 않는다. 왜곡 재질은 발광이 없어 그대로 둔다.
        /// </summary>
        private void SoftenMaterial(TrailRenderer trail)
        {
            Material source = trail.sharedMaterial;
            if (source == null || !source.HasProperty("_EmissiveIntensity")) return;
            if (_windMaterial == null)
            {
                _windMaterial = new Material(source) { name = source.name + " (Wind)" };
                _windMaterial.SetFloat("_EmissiveIntensity", source.GetFloat("_EmissiveIntensity") * emissionScale);
                // 추가 생성 (2026-10-20) — 7줄기 무늬를 바람결로: 흐르고, 휘고, 군데군데 끊기고, 줄을 조금 굵게.
                // 이 셰이더(SH_Vefects_VFX_URP_Trail_New)에 있는 값만 바꾼다. 없는 값이면 건너뛴다.
                if (_windMaterial.HasProperty("_ColorPanSpeed"))
                    _windMaterial.SetVector("_ColorPanSpeed", new Vector4(patternFlowSpeed, 0f, 0f, 0f));
                if (_windMaterial.HasProperty("_DistortionAmount"))
                    _windMaterial.SetFloat("_DistortionAmount", patternWarp);
                if (_windMaterial.HasProperty("_Erosion_Intensity"))
                    _windMaterial.SetFloat("_Erosion_Intensity", patternBreakup);
                if (_windMaterial.HasProperty("_ErosionPanSpeed"))
                    _windMaterial.SetVector("_ErosionPanSpeed", new Vector4(patternFlowSpeed * 1.3f, -0.2f, 0f, 0f));
                if (_windMaterial.HasProperty("_ColorUVScale"))
                {
                    Vector4 uv = _windMaterial.GetVector("_ColorUVScale");
                    uv.y *= patternLineDensity;
                    _windMaterial.SetVector("_ColorUVScale", uv);
                }
            }
            trail.sharedMaterial = _windMaterial;
        }

        // 추가 생성 (2026-10-20)
        /// <summary>트레일마다 기준 길이·굵기를 기억한다(배율 적용). Update가 속도에 따라 이 값에 곱한다.</summary>
        private void CacheBase(float timeScale, float widthScale)
        {
            _baseTime = new float[_trails.Length];
            _baseWidth = new float[_trails.Length];
            _isDistortion = new bool[_trails.Length];
            for (int i = 0; i < _trails.Length; i++)
            {
                Material m = _trails[i].sharedMaterial;
                _isDistortion[i] = m != null && m.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0;
                _baseTime[i] = _trails[i].time * timeScale;
                _baseWidth[i] = _trails[i].widthMultiplier * widthScale;
            }
        }

        /// <summary>꼬리를 <paramref name="seconds"/>초 동안 켠다.</summary>
        public void Play(float seconds)
        {
            _until = Mathf.Max(_until, Time.time + seconds);
            SetEmitting(true);
        }

        private void Update()
        {
            if (_trails == null) return;

            // 수정 (2026-10-20) — 연출 요청(Play) 중이거나, 속도가 문턱을 넘으면 켠다. 빠를수록 굵고 길게.
            float fast = 0f;
            // 수정 (2026-10-20) — 땅에 있을 땐 속도 꼬리를 안 낸다. 착지 순간 속도가 남아 꼬리가 확 퍼지면 어지러웠다.
            bool grounded = _movement != null && _movement.IsGrounded;
            if (speedTrails && _body != null && !grounded)
                fast = Mathf.InverseLerp(trailStartSpeed, trailFullSpeed, _body.linearVelocity.magnitude);
            bool playing = Time.time <= _until;
            // 수정 (2026-10-20) — 흰 줄은 연출 순간(playing)에만, 왜곡(공기 일렁임)은 빠를 때도.
            for (int i = 0; i < _trails.Length; i++)
            {
                var trail = _trails[i];
                if (trail == null) continue;
                bool lines = !_isDistortion[i];
                bool on = lines
                    ? showLines && (playing || (fast > 0f && !linesOnlyOnBurst)) // 수정 (2026-10-20) — 흰 줄은 기본 꺼짐
                    : playing || fast > 0f;                                       // 공기 일렁임은 빠를 때·발사 때
                if (trail.emitting != on) trail.emitting = on;
            }

            float strength = playing ? 1f : Mathf.Lerp(0.5f, 1f, fast);

            // 추가 생성 (2026-10-20) — 꼬리 나오는 점을 살짝 흔들어 바람결처럼 물결치게(사인파, 점마다 위상 다름).
            if (_emitters != null && wobbleAmplitude > 0f)
            {
                float t = Time.time * wobbleFrequency * Mathf.PI * 2f;
                for (int i = 0; i < _emitters.Length; i++)
                {
                    if (_emitters[i] == null) continue;
                    float p = t + _emitterPhase[i];
                    _emitters[i].localPosition = _emitterRest[i] +
                        new Vector3(Mathf.Sin(p), Mathf.Cos(p * 1.3f), 0f) * wobbleAmplitude;
                }
            }
            for (int i = 0; i < _trails.Length; i++)
            {
                var trail = _trails[i];
                if (trail == null) continue;
                trail.time = _baseTime[i] * strength;              // 수정 (2026-10-20) — 트레일마다 기준값에 곱한다
                trail.widthMultiplier = _baseWidth[i] * strength;
            }
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
