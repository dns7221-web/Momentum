#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Diagnostics
{
    // 추가 생성 (2026-10-22)
    /// <summary>
    /// 렉 미터 — Profiler 창을 열지 않고, Play 중 화면 오른쪽 위에서 "렉이 났는지, 그때 뭐가 무거웠는지"를 본다.
    ///
    /// <b>쓰는 법</b>
    /// <list type="bullet">
    /// <item>Play를 누르면 저절로 붙는다(씬에 넣을 필요 없음). <see cref="toggleKey"/>(F3)로 켜고 끈다 — 끈 상태는 다음 Play에도 기억한다.</item>
    /// <item><see cref="recordKey"/>(F4)로 녹화를 시작하고, 다시 누르면 프로젝트의 <c>ProfilerCaptures/</c>에
    ///       프레임별 CSV와 한국어 요약(_summary.txt)을 저장한다. 요약 파일을 Claude에게 읽히면 숫자로 원인을 찾을 수 있다.</item>
    /// <item>(추가 2026-10-23) 메뉴 <c>Momentum/렉 미터/Play 시작부터 녹화</c>를 켜 두면 F4를 누르기 전,
    ///       Play 첫 프레임부터 녹화한다 — 시작 직후 렉을 잡기 위해. 끌 때까지 Play마다 그렇게 한다.</item>
    /// <item>(추가 2026-10-23) 다른 창을 눌러 에디터가 게임을 멈췄다가 돌아온 프레임은 렉이 아니므로 기록에서 뺀다.</item>
    /// </list>
    ///
    /// <b>어떻게 재나</b> — 유니티 내장 <see cref="ProfilerRecorder"/>로 Profiler 창이 보여주는 것과 같은 값
    /// (물리·스크립트·렌더링 구간 시간, GC 할당, 배치 수)을 코드에서 바로 읽는다. 창을 열지 않아도 된다.
    /// 우리 코드에 <see cref="ProfilerMarker"/>를 붙이면(예: <c>PerchPointFinder.Scan</c>) 그 이름을 <see cref="CreateRows"/>에
    /// 한 줄 더하는 것만으로 표에 나온다.
    ///
    /// <b>도구가 렉을 만들면 안 된다</b> — 글자는 <see cref="textRefresh"/>마다만 새로 만들고, 그래프는 텍스처 한 장에
    /// 프레임마다 한 줄씩만 칠한다. 화면은 IMGUI(OnGUI)로 그린다 — 카메라 디버그 HUD(TraversalCameraDirector)와 같은 방식이라
    /// 캔버스·프리팹이 필요 없다. 이 파일 전체가 에디터·개발 빌드에서만 컴파일되므로 최종 빌드엔 아예 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class LagMeter : MonoBehaviour
    {
        [Header("키")]
        [Tooltip("화면 표시 켜기/끄기.")]
        [SerializeField] private Key toggleKey = Key.F3;
        [Tooltip("녹화 시작/저장.")]
        [SerializeField] private Key recordKey = Key.F4;

        [Header("튄 프레임(렉) 판정")]
        [Tooltip("이 시간(ms)보다 긴 프레임만 렉 후보로 본다. 25 ms ≈ 40 FPS.")]
        [Min(1f)][SerializeField] private float spikeMinMs = 25f;
        [Tooltip("최근 평균 프레임 시간의 몇 배를 넘으면 튄 프레임으로 본다. 원래 느린 장면 전체를 렉으로 세지 않기 위해.")]
        [Min(1f)][SerializeField] private float spikeRatio = 1.8f;

        [Header("화면")]
        [Tooltip("그래프 꼭대기가 가리키는 프레임 시간(ms). 이보다 긴 프레임은 꼭대기에 닿는다.")]
        [Min(10f)][SerializeField] private float graphMaxMs = 50f;
        [Tooltip("글자를 다시 만드는 간격(초). 매 프레임 만들면 이 도구가 쓰레기(GC)를 만들어 스스로 렉을 낸다.")]
        [Min(0.05f)][SerializeField] private float textRefresh = 0.25f;

        [Header("녹화")]
        [Tooltip("한 번에 녹화할 최대 프레임 수. 넘으면 저절로 저장하고 멈춘다(60fps면 10분).")]
        [Min(600)][SerializeField] private int maxRecordFrames = 36000;

        private const int HistoryLength = 300;   // 그래프·평균에 쓰는 최근 프레임 수(60fps면 5초)
        private const int GraphHeight = 64;      // 그래프 텍스처 높이(픽셀)
        private const int WarmupFrames = 60;     // 시작 직후(로딩 끝물)는 튄 프레임으로 안 센다
        private const int SpikeRingLength = 32;  // 화면용으로 기억하는 최근 렉 수
        private const string SelfMarkerName = "LagMeter.Self";
        private const string VisiblePrefKey = "Momentum.LagMeter.Visible";
        // 추가 생성 (2026-10-23)
        private const string RecordFromStartPrefKey = "Momentum.LagMeter.RecordFromStart"; // 메뉴로 켜고 끈다(PlayerPrefs라 Play마다 유지)
        private const int AwaySkipFrames = 2;     // 창 밖에 나갈 때·돌아올 때 기록에서 빼는 프레임 수
        private const int MissingRetryLimit = 10; // 시작 때 못 찾은 내장 항목을 다시 찾아보는 횟수(1초마다)

        private const string Green = "#5fe07a", Yellow = "#ffd23f", Red = "#ff5a5a", Grey = "#9aa0a6";
        private static readonly Color32 s_goodColor = new Color32(95, 224, 122, 255);   // 60fps 이상
        private static readonly Color32 s_okColor = new Color32(255, 210, 63, 255);     // 30~60fps
        private static readonly Color32 s_slowColor = new Color32(255, 140, 60, 255);   // 30fps 미만
        private static readonly Color32 s_spikeColor = new Color32(255, 70, 70, 255);   // 튄 프레임
        private static readonly Color32 s_lineColor = new Color32(255, 255, 255, 70);   // 60·30fps 기준선
        private static readonly Color32 s_clearColor = new Color32(0, 0, 0, 0);

        /// <summary>렉 미터 자신의 비용 — 표의 "렉 미터 자체" 줄. 도구가 무거우면 바로 보이게.</summary>
        private static readonly ProfilerMarker s_selfMarker = new ProfilerMarker(SelfMarkerName);

        /// <summary>한 줄이 재는 값의 종류.</summary>
        private enum Kind
        {
            Time,   // 구간 시간(ms) — 표에 나온다
            Bytes,  // 메모리(KB) — 아래 GC 줄에 나온다
            Count,  // 개수 — 아래 그리기 줄에 나온다
        }

        /// <summary>표의 한 줄 — Profiler의 마커(구간 시간) 또는 카운터(값) 하나를 읽는다.</summary>
        private sealed class Row
        {
            public readonly string[] Names;   // 이름 후보 — 유니티 버전마다 이름이 조금씩 달라 앞에서부터 찾는다
            public readonly string Label;     // 화면에 보일 이름
            public readonly Kind Kind;
            public string ParentName;         // 들여 쓸 부모 줄의 마커 이름(우리 코드 항목용). null이면 맨 위 줄
            public bool Custom;               // 우리 코드가 만든 마커 — 처음 쓰일 때 등록되므로 못 찾으면 나중에 다시 찾는다
            public bool CanBlame = true;      // 렉 순간 "제일 컸던 것" 후보인가(렉 미터 자신은 빠진다)
            public int Parent = -1;
            public string UsedName;           // 실제로 찾은 이름(CSV 머리글)
            public ProfilerRecorder Recorder;
            public bool Missing;              // 유니티 내장 항목인데 이 버전에서 이름을 못 찾음 → "측정 안 됨"
            public readonly float[] History = new float[HistoryLength];
            public float Now, Avg, Max;

            public Row(string label, Kind kind, params string[] names)
            {
                Label = label;
                Kind = kind;
                Names = names;
            }
        }

        /// <summary>튄 프레임 하나의 기록.</summary>
        private struct Spike
        {
            public float Time;      // 몇 초에(Play 시작 또는 녹화 시작 기준)
            public float Ms;        // 그 프레임 길이
            public int Row;         // 제일 컸던 줄(-1이면 모름)
            public float RowMs;
            public int Child;       // 그 줄 안에서 절반 넘게 차지한 우리 코드 항목(-1이면 없음)
            public float ChildMs;
        }

        /// <summary>표 정렬용(평균이 큰 순). 미리 하나 만들어 두고 계속 쓴다 — 람다로 정렬하면 정렬할 때마다 쓰레기가 생긴다.</summary>
        private sealed class AvgComparer : IComparer<int>
        {
            private readonly List<Row> _rows;
            public AvgComparer(List<Row> rows) { _rows = rows; }
            public int Compare(int a, int b) => _rows[b].Avg.CompareTo(_rows[a].Avg);
        }

        private List<Row> _rows;
        private AvgComparer _avgComparer;
        private readonly List<int> _tableOrder = new List<int>();
        private int _gcRow = -1, _gcAllocRow = -1, _batchRow = -1, _setPassRow = -1;
        private ProfilerRecorder _mainThread;
        private float _mainThreadMs;
        // 추가 생성 (2026-10-23) — 마커 목록 훑기용(다시 찾을 때 리스트를 새로 만들지 않게)과 다시 찾은 횟수
        private readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>();
        private int _missingRetries;

        // 추가 생성 (2026-10-23) — 창 밖 프레임 빼기
        private bool _lostFocus;   // 창 밖으로 나간 적이 있는가(Play 시작 때의 "포커스 얻음"은 무시하려고)
        private int _awaySkip;     // 앞으로 기록에서 뺄 프레임 수
        private int _awayFrames;   // Play 시작 후 뺀 프레임 수

        // 최근 프레임(링 버퍼)
        private readonly float[] _frameMs = new float[HistoryLength];
        private readonly bool[] _frameSpike = new bool[HistoryLength];
        private int _head;      // 다음에 쓸 칸 = 가장 오래된 칸
        private int _filled;    // 채워진 칸 수(≤ HistoryLength)
        private int _frames;    // 시작 후 프레임 수(워밍업용)
        private float _frameSum;
        private float _startTime;
        private readonly Spike[] _spikeRing = new Spike[SpikeRingLength];
        private int _spikeCount;

        // 화면
        private bool _visible;
        private Texture2D _graph, _panel;
        private readonly Color32[] _column = new Color32[GraphHeight];
        private bool _graphDirty;
        private GUIStyle _left, _right;
        private float _styleScale;
        private float _nextText, _nextRetry;
        private readonly StringBuilder _sb = new StringBuilder(512);
        // 수정 (2026-10-23): 표를 열마다 한 덩어리 글자로 그리던 것을 칸마다 따로 그리게 바꿨다 — 한글 열과 숫자 열의
        // 줄 높이가 달라 아래로 갈수록 이름과 숫자가 어긋났기 때문. 칸 글자를 줄 순서대로 담아 둔다.
        private readonly List<string> _cellLabels = new List<string>(), _cellNows = new List<string>(),
                                      _cellAvgs = new List<string>(), _cellMaxs = new List<string>();
        private string _headLeft = "", _headRight = "", _spikeText = "", _footer = "", _hint = "";
        private int _tableLines = 1;
        private float _rowHeight; // 추가 생성 (2026-10-23) — 한글이 들어간 한 줄의 실제 높이. 모든 줄을 이 높이로 그린다
        private string _savedNotice;
        private float _savedNoticeUntil;

        // 녹화 — 프레임마다 글자를 만들지 않고 숫자만 배열에 쌓았다가, 멈출 때 한 번에 파일로 쓴다.
        private bool _recording;
        private float[] _rec;
        private int _recCols, _recFrames;
        private float _recStart;
        private DateTime _recStamp;
        private readonly List<Spike> _recSpikes = new List<Spike>();
        // 추가 생성 (2026-10-23)
        private bool _recFromStart;                                          // 이번 녹화가 Play 첫 프레임부터인가
        private readonly List<Spike> _recStartupSpikes = new List<Spike>(); // 워밍업 구간(시작 직후)의 긴 프레임
        private int _recAwayFrames;                                          // 이번 녹화에서 뺀 창 밖 프레임 수

        /// <summary>
        /// 표에 나올 줄들. 이름은 Profiler 창에 보이는 마커·카운터 이름 그대로다.
        /// 줄끼리 겹칠 수 있다(예: GC 수거는 스크립트 도중에 일어나 그 스크립트 시간에도 들어간다).
        /// 우리 코드에 ProfilerMarker를 새로 붙이면 여기 한 줄을 더한다.
        /// </summary>
        private static List<Row> CreateRows() => new List<Row>
        {
            new Row("물리", Kind.Time, "FixedUpdate.PhysicsFixedUpdate", "Physics.Simulate"),
            new Row("스크립트 Update", Kind.Time, "Update.ScriptRunBehaviourUpdate"),
            new Row("스크립트 FixedUpdate", Kind.Time, "FixedUpdate.ScriptRunBehaviourFixedUpdate"),
            new Row("스크립트 LateUpdate", Kind.Time, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            new Row("포인트 UI", Kind.Time, "PerchPointFinder.Scan")
                { ParentName = "PreLateUpdate.ScriptRunBehaviourLateUpdate", Custom = true },
            new Row("애니메이션", Kind.Time, "PreLateUpdate.DirectorUpdateAnimationBegin", "Animators.Update"),
            // 수정 (2026-10-23): "PostLateUpdate.FinishFrameRendering"만 찾았더니 이 버전에선 늘 "측정 안 됨"이었다.
            // URP가 카메라를 그리는 메인 스레드 시간 전체를 재는 "Inl_UniversalRenderTotal"을 먼저 찾는다
            // (URP의 ProfilingScope가 만드는 즉시 측정용 마커). 에디터에선 씬 뷰를 그리는 시간도 여기에 같이 들어간다.
            new Row("렌더링(CPU)", Kind.Time, "Inl_UniversalRenderTotal", "PostLateUpdate.FinishFrameRendering",
                    "RenderPipelineManager.DoRenderLoop_Internal()"),
            new Row("GPU 기다림", Kind.Time, "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForPresent"),
            new Row("GC 수거", Kind.Time, "GC.Collect"),
            new Row("에디터(빌드엔 없음)", Kind.Time, "EditorLoop"),
            new Row("렉 미터 자체", Kind.Time, SelfMarkerName) { Custom = true, CanBlame = false },
            new Row("GC 할당", Kind.Bytes, "GC Allocated In Frame"),
            new Row("배치", Kind.Count, "Batches Count"),
            new Row("SetPass", Kind.Count, "SetPass Calls Count"),
        };

        /// <summary>
        /// Play를 누르면 씬이 뜬 직후 저절로 하나 만든다 — 씬마다 넣어 둘 필요가 없게.
        /// DontDestroyOnLoad로 씬이 바뀌어도 계속 잰다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<LagMeter>() != null) return;
            var go = new GameObject("LagMeter");
            DontDestroyOnLoad(go);
            go.AddComponent<LagMeter>();
        }

        private void Awake()
        {
            useGUILayout = false; // GUILayout을 안 쓰므로 Layout 이벤트를 건너뛰어 OnGUI 비용을 줄인다
            _visible = PlayerPrefs.GetInt(VisiblePrefKey, 1) == 1;
            _startTime = Time.realtimeSinceStartup;

            _rows = CreateRows();
            _avgComparer = new AvgComparer(_rows);
            LinkRows();
            using (s_selfMarker.Auto()) { } // 우리 마커를 지금 써 둬서 아래 목록 찾기에 확실히 잡히게 한다
            ResolveRecorders();

            _graph = new Texture2D(HistoryLength, GraphHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat, // 링 버퍼를 UV 오프셋으로 밀어 그리기 위해(가장 최근 프레임이 오른쪽 끝)
                hideFlags = HideFlags.HideAndDontSave,
            };
            RedrawGraph();
            _graph.Apply(false);

            _panel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _panel.Apply(false);

            // 추가 생성 (2026-10-23) — 메뉴에서 "Play 시작부터 녹화"를 켜 뒀으면 F4 없이 첫 프레임부터 녹화한다.
            // 시작 직후 렉은 F4를 누를 틈도 없이 지나가서, 지금까지의 기록엔 한 번도 안 잡혔다.
            if (PlayerPrefs.GetInt(RecordFromStartPrefKey, 0) == 1) StartRecording(true);
        }

        private void OnDestroy()
        {
            // Play를 끝낼 때 녹화 중이었으면 버리지 않고 저장한다.
            if (_recording) StopRecording();
            _mainThread.Dispose();
            if (_rows != null)
                foreach (Row row in _rows) row.Recorder.Dispose();
            if (_graph != null) Destroy(_graph);
            if (_panel != null) Destroy(_panel);
        }

        /// <summary>
        /// 매 프레임: 키 → 지난 프레임 값 읽기 → 그래프 한 줄. 글자는 가끔만.
        /// 레코더의 LastValue와 unscaledDeltaTime은 둘 다 "방금 끝난 프레임"의 값이라 서로 맞는다.
        /// </summary>
        private void Update()
        {
            using (s_selfMarker.Auto())
            {
                HandleKeys();
                Sample();
                if (_graphDirty)
                {
                    _graph.Apply(false);
                    _graphDirty = false;
                }

                float now = Time.unscaledTime;
                if (now >= _nextRetry)
                {
                    _nextRetry = now + 1f;
                    RetryCustomRows();
                    RetryMissingRows(); // 추가 생성 (2026-10-23)
                }
                if (_visible && now >= _nextText)
                {
                    _nextText = now + textRefresh;
                    BuildTexts();
                }
            }
        }

        // ───────────────────────── 측정 ─────────────────────────

        /// <summary>ParentName을 줄 번호로 바꾸고, 아래 GC·그리기 줄 번호를 챙겨 둔다.</summary>
        private void LinkRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.ParentName != null)
                    row.Parent = _rows.FindIndex(r => Array.IndexOf(r.Names, row.ParentName) >= 0);
                if (row.Names[0] == "GC.Collect") _gcRow = i;
                else if (row.Names[0] == "GC Allocated In Frame") _gcAllocRow = i;
                else if (row.Names[0] == "Batches Count") _batchRow = i;
                else if (row.Names[0] == "SetPass Calls Count") _setPassRow = i;
            }
        }

        /// <summary>
        /// 지금 등록된 마커·카운터를 한 번 훑어 이름에 맞는 분류(카테고리)를 얻고 레코더를 연다(Play 시작 때 한 번).
        /// 분류까지 맞춰야 하므로 이름만으로 바로 열지 않고 목록에서 찾는다.
        /// </summary>
        private void ResolveRecorders()
        {
            var wanted = new HashSet<string> { "Main Thread" };
            foreach (Row row in _rows)
                foreach (string name in row.Names) wanted.Add(name);

            // 수정 (2026-10-23): 목록 훑기와 레코더 열기를 FindCategories·TryOpenRow로 뺐다 — RetryMissingRows도 같이 쓴다.
            Dictionary<string, ProfilerCategory> found = FindCategories(wanted);

            // "Main Thread" = 이번 프레임에 메인 스레드가 일한 시간(CPU). 머리줄에만 쓴다.
            if (found.TryGetValue("Main Thread", out ProfilerCategory mainCategory))
                _mainThread = ProfilerRecorder.StartNew(mainCategory, "Main Thread");

            foreach (Row row in _rows)
            {
                TryOpenRow(row, found);
                // 유니티 내장 항목은 Play 시작 때 이미 다 등록돼 있다 — 여기서 못 찾으면 이 버전엔 없는 이름이다.
                // 수정 (2026-10-23): URP 렌더링 마커처럼 처음 그릴 때 등록되는 내장 항목도 있어서,
                // 못 찾은 줄은 RetryMissingRows가 몇 번 더 찾아본다. 그래도 없으면 "측정 안 됨"으로 남는다.
                row.Missing = !row.Recorder.Valid && !row.Custom;
            }
        }

        // 추가 생성 (2026-10-23)
        /// <summary>
        /// 지금 등록된 마커·카운터 목록을 훑어, 원하는 이름마다 분류(카테고리)를 찾는다.
        /// 목록 리스트는 필드 하나를 계속 써서, 다시 찾을 때마다 쓰레기가 생기지 않게 한다.
        /// </summary>
        private Dictionary<string, ProfilerCategory> FindCategories(HashSet<string> wanted)
        {
            _handles.Clear();
            ProfilerRecorderHandle.GetAvailable(_handles);
            var found = new Dictionary<string, ProfilerCategory>();
            foreach (ProfilerRecorderHandle handle in _handles)
            {
                ProfilerRecorderDescription desc = ProfilerRecorderHandle.GetDescription(handle);
                string name = desc.Name;
                if (wanted.Contains(name) && !found.ContainsKey(name)) found.Add(name, desc.Category);
            }
            return found;
        }

        // 추가 생성 (2026-10-23)
        /// <summary>줄의 이름 후보를 앞에서부터 보며, 처음 열리는 레코더를 그 줄에 붙인다. 붙였으면 true.</summary>
        private static bool TryOpenRow(Row row, Dictionary<string, ProfilerCategory> found)
        {
            foreach (string name in row.Names)
            {
                if (!found.TryGetValue(name, out ProfilerCategory category)) continue;
                var recorder = ProfilerRecorder.StartNew(category, name);
                if (!recorder.Valid)
                {
                    recorder.Dispose();
                    continue;
                }
                row.Recorder = recorder;
                row.UsedName = name;
                return true;
            }
            return false;
        }

        // 추가 생성 (2026-10-23)
        /// <summary>
        /// Play 시작 때 못 찾은 유니티 내장 항목을 1초마다 다시 찾는다(최대 <see cref="MissingRetryLimit"/>번).
        /// URP 렌더링 마커는 렌더 파이프라인이 처음 그릴 때 등록돼서, 씬이 막 뜬 순간엔 목록에 없을 수 있다.
        /// 횟수를 정해 둔 건, 이 버전에 정말 없는 이름을 매초 계속 찾느라 목록을 훑지 않게 하려고.
        /// </summary>
        private void RetryMissingRows()
        {
            if (_missingRetries >= MissingRetryLimit) return;
            _missingRetries++;

            HashSet<string> wanted = null;
            foreach (Row row in _rows)
            {
                if (!row.Missing) continue;
                wanted ??= new HashSet<string>();
                foreach (string name in row.Names) wanted.Add(name);
            }
            if (wanted == null)
            {
                _missingRetries = MissingRetryLimit; // 다 찾았으면 더 볼 것이 없다
                return;
            }

            Dictionary<string, ProfilerCategory> found = FindCategories(wanted);
            foreach (Row row in _rows)
                if (row.Missing && TryOpenRow(row, found)) row.Missing = false;
        }

        /// <summary>
        /// 우리 코드 마커는 그 코드가 처음 돌 때 등록된다(예: 포인트 UI는 첫 훑기 때). 아직 못 찾았으면 1초마다 다시 열어 본다.
        /// </summary>
        private void RetryCustomRows()
        {
            foreach (Row row in _rows)
            {
                if (!row.Custom || row.Recorder.Valid) continue;
                try
                {
                    var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, row.Names[0]);
                    if (recorder.Valid)
                    {
                        row.Recorder = recorder;
                        row.UsedName = row.Names[0];
                    }
                    else recorder.Dispose();
                }
                catch (Exception)
                {
                    // 아직 등록 전이면 열기가 실패할 수 있다 — 1초 뒤 다시 본다. 측정 도구가 게임을 멈추게 하면 안 된다.
                }
            }
        }

        /// <summary>지난 프레임의 값을 모든 줄에서 읽어 링 버퍼에 넣고, 튄 프레임인지 판정한다.</summary>
        private void Sample()
        {
            // 추가 생성 (2026-10-23) — 창 밖에 나갔다 온 프레임은 통째로 뺀다(OnApplicationFocus 참고).
            // 평균·그래프·녹화 어디에도 안 넣는다 — 수 초짜리 "렉"이 최악 값과 평균을 망가뜨리지 않게.
            if (_awaySkip > 0)
            {
                _awaySkip--;
                _awayFrames++;
                if (_recording) _recAwayFrames++;
                return;
            }

            float dtMs = Time.unscaledDeltaTime * 1000f;
            _mainThreadMs = _mainThread.Valid ? _mainThread.LastValue * 1e-6f : 0f;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                float value = 0f;
                if (row.Recorder.Valid)
                {
                    long raw = row.Recorder.LastValue;
                    value = row.Kind == Kind.Time ? raw * 1e-6f      // 나노초 → ms
                          : row.Kind == Kind.Bytes ? raw / 1024f    // 바이트 → KB
                          : raw;
                }
                row.Now = value;
                row.History[_head] = value;
            }

            // 최근 평균(이번 프레임 빼고)과 비교한다. 원래 느린 장면이면 평균도 높아 렉으로 안 센다.
            float average = _filled > 0 ? _frameSum / _filled : dtMs;
            _frames++;
            bool spike = _frames > WarmupFrames && dtMs >= Mathf.Max(spikeMinMs, average * spikeRatio);

            // 추가 생성 (2026-10-23) — 시작 직후(워밍업)의 긴 프레임은 렉으로 세지 않지만, 녹화 중이면 따로 적어 둔다.
            // "Play 시작부터 녹화"로 시작 렉을 볼 때 요약 파일에 원인과 함께 나오게.
            if (_recording && _frames <= WarmupFrames && dtMs >= spikeMinMs)
                _recStartupSpikes.Add(MakeSpike(Time.realtimeSinceStartup - _recStart, dtMs));

            if (_filled == HistoryLength) _frameSum -= _frameMs[_head];
            else _filled++;
            _frameSum += dtMs;
            _frameMs[_head] = dtMs;
            _frameSpike[_head] = spike;

            if (spike) OnSpike(dtMs);
            if (_recording) RecordFrame(dtMs, spike);
            if (_visible) DrawColumn(_head, dtMs, spike);

            _head = (_head + 1) % HistoryLength;
        }

        // 추가 생성 (2026-10-23)
        /// <summary>
        /// 다른 창(예: 녹화 프로그램)을 누르면 Run In Background가 꺼진 에디터는 게임을 멈추고, 게임 뷰를 다시 눌러야 돈다.
        /// 돌아온 첫 프레임의 길이는 멈춰 있던 시간 전체(수 초)라 렉처럼 보이지만 게임 탓이 아니다.
        /// 나갈 때와 돌아올 때 몇 프레임을 기록에서 뺀다. Play 시작 때 오는 "포커스 얻음"은 나간 적이 없으니 무시한다.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _lostFocus = true;
                _awaySkip = AwaySkipFrames;
            }
            else if (_lostFocus)
            {
                _lostFocus = false;
                _awaySkip = AwaySkipFrames;
            }
        }

        private void OnSpike(float ms)
        {
            Spike spike = MakeSpike(Time.realtimeSinceStartup - _startTime, ms);
            _spikeRing[_spikeCount % SpikeRingLength] = spike;
            _spikeCount++;
            if (_recording)
            {
                spike.Time = Time.realtimeSinceStartup - _recStart;
                _recSpikes.Add(spike);
            }
        }

        /// <summary>
        /// 튄 프레임에서 제일 컸던 맨 위 줄을 고른다. GC 수거는 다른 구간 안에서 일어나서(스크립트 도중 등)
        /// 그 구간도 같이 커지므로, GC가 크면 GC를 먼저 탓한다. 그 줄 밑의 우리 코드가 절반 넘게 차지하면 같이 적는다.
        /// </summary>
        private Spike MakeSpike(float time, float ms)
        {
            var spike = new Spike { Time = time, Ms = ms, Row = -1, Child = -1 };
            float best = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.Kind != Kind.Time || row.Parent >= 0 || !row.CanBlame) continue;
                if (row.Now > best)
                {
                    best = row.Now;
                    spike.Row = i;
                }
            }
            if (_gcRow >= 0 && _rows[_gcRow].Now > 1f && _rows[_gcRow].Now >= 0.5f * best) spike.Row = _gcRow;
            if (spike.Row < 0) return spike;

            spike.RowMs = _rows[spike.Row].Now;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Parent != spike.Row || _rows[i].Now < 0.5f * spike.RowMs) continue;
                spike.Child = i;
                spike.ChildMs = _rows[i].Now;
            }
            return spike;
        }

        // ───────────────────────── 키·녹화 ─────────────────────────

        private void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
            {
                _visible = !_visible;
                PlayerPrefs.SetInt(VisiblePrefKey, _visible ? 1 : 0);
                if (_visible)
                {
                    RedrawGraph(); // 숨긴 동안 안 칠한 그래프를 기록에서 다시 칠한다
                    BuildTexts();
                }
            }

            if (recordKey != Key.None && keyboard[recordKey].wasPressedThisFrame)
            {
                if (_recording) StopRecording();
                else StartRecording();
                if (_visible) BuildTexts();
            }
        }

        /// <param name="fromStart">(추가 2026-10-23) Play 첫 프레임부터 녹화하는가 — 요약에 시작 구간을 따로 적는다.</param>
        private void StartRecording(bool fromStart = false)
        {
            _recCols = 3 + _rows.Count; // 시간, 프레임 ms, 튐 여부 + 줄마다 하나
            int length = maxRecordFrames * _recCols;
            if (_rec == null || _rec.Length != length) _rec = new float[length];
            _recFrames = 0;
            _recSpikes.Clear();
            // 추가 생성 (2026-10-23)
            _recStartupSpikes.Clear();
            _recAwayFrames = 0;
            _recFromStart = fromStart;
            _recStart = Time.realtimeSinceStartup;
            _recStamp = DateTime.Now;
            _recording = true;
            Debug.Log(fromStart
                ? $"[LagMeter] Play 시작부터 녹화 중 — {recordKey}를 누르면 저장한다(메뉴 Momentum/렉 미터에서 끈다)."
                : $"[LagMeter] 녹화 시작 — {recordKey}를 다시 누르면 저장한다.", this);
        }

        private void RecordFrame(float dtMs, bool spike)
        {
            if (_recFrames >= maxRecordFrames)
            {
                StopRecording(); // 꽉 차면 저절로 저장하고 멈춘다
                return;
            }
            int offset = _recFrames * _recCols;
            _rec[offset] = Time.realtimeSinceStartup - _recStart;
            _rec[offset + 1] = dtMs;
            _rec[offset + 2] = spike ? 1f : 0f;
            for (int i = 0; i < _rows.Count; i++) _rec[offset + 3 + i] = _rows[i].Now;
            _recFrames++;
        }

        /// <summary>녹화를 멈추고 CSV(프레임별 전부)와 요약(한국어)을 ProfilerCaptures/에 쓴다.</summary>
        private void StopRecording()
        {
            _recording = false;
            if (_recFrames == 0) return;
            try
            {
                string folder = CaptureFolder();
                Directory.CreateDirectory(folder);
                string stem = Path.Combine(folder, $"lag_{_recStamp:yyyyMMdd_HHmmss}");
                var utf8WithBom = new UTF8Encoding(true); // BOM이 있어야 엑셀이 한글을 안 깨뜨린다
                File.WriteAllText(stem + ".csv", BuildCsv(), utf8WithBom);
                File.WriteAllText(stem + "_summary.txt", BuildSummary(), utf8WithBom);
                _savedNotice = $"저장됨: ProfilerCaptures/{Path.GetFileName(stem)}_summary.txt";
                _savedNoticeUntil = Time.unscaledTime + 6f;
                Debug.Log($"[LagMeter] 기록 저장 ({_recFrames}프레임): {stem}_summary.txt", this);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LagMeter] 기록 저장 실패: {e.Message}", this);
            }
        }

        /// <summary>에디터면 프로젝트의 ProfilerCaptures(.gitignore에 이미 있어 커밋에 안 섞인다), 빌드면 쓸 수 있는 개인 폴더.</summary>
        private static string CaptureFolder()
        {
            string root = Application.isEditor
                ? Directory.GetParent(Application.dataPath).FullName
                : Application.persistentDataPath;
            return Path.Combine(root, "ProfilerCaptures");
        }

        private string BuildCsv()
        {
            var sb = new StringBuilder(_recFrames * _recCols * 6 + 256);
            sb.Append("time_s,frame_ms,spike");
            foreach (Row row in _rows)
            {
                string unit = row.Kind == Kind.Time ? "_ms" : row.Kind == Kind.Bytes ? "_KB" : "";
                sb.Append(',').Append(row.UsedName ?? row.Names[0]).Append(unit);
            }
            sb.Append('\n');

            CultureInfo invariant = CultureInfo.InvariantCulture;
            for (int frame = 0; frame < _recFrames; frame++)
            {
                int offset = frame * _recCols;
                for (int c = 0; c < _recCols; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(_rec[offset + c].ToString("0.###", invariant));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 사람이(그리고 Claude가) 먼저 읽을 요약. 줄마다 평균·최고와 함께 "렉 때 평균"을 적는다 —
        /// 평소엔 가볍다가 튄 프레임에서만 커지는 줄이 렉의 범인이다.
        /// </summary>
        private string BuildSummary()
        {
            var sb = new StringBuilder(4096);
            float length = _rec[(_recFrames - 1) * _recCols];
            float sumMs = 0f, worst = 0f;
            for (int frame = 0; frame < _recFrames; frame++)
            {
                float ms = _rec[frame * _recCols + 1];
                sumMs += ms;
                worst = Mathf.Max(worst, ms);
            }
            float avgMs = sumMs / _recFrames;

            sb.AppendLine($"렉 미터 기록 — {_recStamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"길이 {length:0.0}초 · {_recFrames}프레임 · 평균 {1000f / avgMs:0.0} FPS ({avgMs:0.0} ms) · 최악 {worst:0.0} ms");
            sb.AppendLine($"튄 프레임 {_recSpikes.Count}번 (기준: {spikeMinMs:0} ms 이상이면서 최근 평균의 {spikeRatio:0.0}배 이상)");
            if (Application.isEditor) sb.AppendLine("※ 에디터에서 잰 값 — 빌드보다 느리게 나온다(\"에디터\" 줄 참고).");
            // 추가 생성 (2026-10-23)
            if (_recFromStart) sb.AppendLine("※ Play 시작부터 녹화함 — 아래 \"시작 직후 긴 프레임\" 참고.");
            if (_recAwayFrames > 0)
                sb.AppendLine($"※ 창 밖에 있던 동안의 프레임 {_recAwayFrames}개는 뺐다 (다른 창을 누르면 에디터가 게임을 멈춘다 — 렉 아님).");
            sb.AppendLine();

            sb.AppendLine("항목별 — 평균 / 최고 / 렉 때 평균 (시간은 ms)");
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                string name = (row.Parent >= 0 ? "    └ " : "  ") + row.Label;
                if (!row.Recorder.Valid)
                {
                    sb.AppendLine($"{name}: 측정 안 됨");
                    continue;
                }
                float sum = 0f, max = 0f, spikeSum = 0f;
                int spikeFrames = 0;
                for (int frame = 0; frame < _recFrames; frame++)
                {
                    int offset = frame * _recCols;
                    float value = _rec[offset + 3 + i];
                    sum += value;
                    max = Mathf.Max(max, value);
                    if (_rec[offset + 2] > 0f)
                    {
                        spikeSum += value;
                        spikeFrames++;
                    }
                }
                string unit = row.Kind == Kind.Time ? "" : row.Kind == Kind.Bytes ? " KB" : "개";
                string spikeAvg = spikeFrames > 0 ? (spikeSum / spikeFrames).ToString("0.00") + unit : "-";
                sb.AppendLine($"{name}: {sum / _recFrames:0.00}{unit} / {max:0.00}{unit} / {spikeAvg}");
            }
            sb.AppendLine();

            const int MaxListed = 100;

            // 추가 생성 (2026-10-23) — 시작 구간은 워밍업이라 튄 프레임으로 안 세므로 따로 적는다.
            if (_recFromStart || _recStartupSpikes.Count > 0)
            {
                sb.AppendLine($"시작 직후 긴 프레임 (처음 {WarmupFrames}프레임 중 {spikeMinMs:0} ms 이상)");
                if (_recStartupSpikes.Count == 0) sb.AppendLine("  없음");
                for (int i = 0; i < _recStartupSpikes.Count && i < MaxListed; i++)
                {
                    Spike spike = _recStartupSpikes[i];
                    sb.AppendLine($"  {spike.Time,7:0.00}초  {spike.Ms,6:0.0} ms  제일 컸던 것: {BlameText(spike)}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"튄 프레임 목록 (시간순, 최대 {MaxListed}개)");
            if (_recSpikes.Count == 0) sb.AppendLine("  없음");
            for (int i = 0; i < _recSpikes.Count && i < MaxListed; i++)
            {
                Spike spike = _recSpikes[i];
                sb.AppendLine($"  {spike.Time,7:0.00}초  {spike.Ms,6:0.0} ms  제일 컸던 것: {BlameText(spike)}");
            }
            if (_recSpikes.Count > MaxListed) sb.AppendLine($"  … 외 {_recSpikes.Count - MaxListed}개");
            return sb.ToString();
        }

        private string BlameText(in Spike spike)
        {
            if (spike.Row < 0) return "모름";
            string text = $"{_rows[spike.Row].Label} {spike.RowMs:0.0} ms";
            if (spike.Child >= 0) text += $" (그중 {_rows[spike.Child].Label} {spike.ChildMs:0.0} ms)";
            return text;
        }

        // ───────────────────────── 화면 ─────────────────────────

        /// <summary>
        /// 그래프 한 줄(프레임 하나)을 텍스처에 칠한다. 텍스처 전체를 다시 그리지 않고 이 줄만 바꾼다.
        /// 색: 초록 60fps 이상, 노랑 30~60, 주황 30 미만, 빨강 튄 프레임. 가는 선 두 개가 60·30fps 기준선이다.
        /// </summary>
        private void DrawColumn(int x, float ms, bool spike)
        {
            int height = ms <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(ms / graphMaxMs * GraphHeight), 1, GraphHeight);
            int y60 = Mathf.RoundToInt(1000f / 60f / graphMaxMs * GraphHeight);
            int y30 = Mathf.RoundToInt(1000f / 30f / graphMaxMs * GraphHeight);
            Color32 bar = spike ? s_spikeColor
                        : ms <= 1000f / 60f + 0.5f ? s_goodColor
                        : ms <= 1000f / 30f + 0.5f ? s_okColor
                        : s_slowColor;
            for (int y = 0; y < GraphHeight; y++)
                _column[y] = y < height ? bar : (y == y60 || y == y30) ? s_lineColor : s_clearColor;
            _graph.SetPixels32(x, 0, 1, GraphHeight, _column);
            _graphDirty = true;
        }

        private void RedrawGraph()
        {
            for (int i = 0; i < HistoryLength; i++) DrawColumn(i, _frameMs[i], _frameSpike[i]);
        }

        /// <summary>화면 글자를 새로 만든다(textRefresh마다). OnGUI는 여기서 만든 글자를 그리기만 한다.</summary>
        private void BuildTexts()
        {
            int n = _filled;
            if (n == 0) return;

            // 줄마다 최근 n프레임 평균·최고
            foreach (Row row in _rows)
            {
                float sum = 0f, max = 0f;
                for (int i = 0; i < n; i++)
                {
                    float value = row.History[i];
                    sum += value;
                    if (value > max) max = value;
                }
                row.Avg = sum / n;
                row.Max = max;
            }

            // 머리줄 — FPS는 최근 30프레임 평균(한 프레임 값은 너무 출렁인다)
            int recent = Mathf.Min(30, n);
            float recentSum = 0f, worst = 0f;
            for (int k = 1; k <= recent; k++) recentSum += _frameMs[(_head - k + HistoryLength) % HistoryLength];
            for (int i = 0; i < n; i++) worst = Mathf.Max(worst, _frameMs[i]);
            float recentMs = recentSum / recent;
            float fps = recentMs > 0f ? 1000f / recentMs : 0f;

            _headLeft = "<b>렉 미터</b>";
            _sb.Clear();
            _sb.Append("<color=").Append(fps >= 55f ? Green : fps >= 30f ? Yellow : Red).Append("><b>")
               .Append(fps.ToString("0")).Append("</b> FPS</color>   ").Append(recentMs.ToString("0.0")).Append(" ms");
            if (_mainThread.Valid) _sb.Append("   CPU ").Append(_mainThreadMs.ToString("0.0")).Append(" ms");
            _headRight = _sb.ToString();

            // 렉 줄
            float now = Time.realtimeSinceStartup - _startTime;
            int stored = Mathf.Min(_spikeCount, SpikeRingLength), recentSpikes = 0;
            for (int k = 0; k < stored; k++)
                if (now - _spikeRing[k].Time <= 10f) recentSpikes++;
            _sb.Clear();
            _sb.Append("최근 10초 튄 프레임 <color=").Append(recentSpikes > 0 ? Red : Green).Append("><b>")
               .Append(recentSpikes >= SpikeRingLength ? SpikeRingLength + "+" : recentSpikes.ToString())
               .Append("</b>번</color>   최근 ").Append(n).Append("프레임 최악 ").Append(worst.ToString("0.0")).Append(" ms\n");
            if (_spikeCount == 0)
            {
                _sb.Append("<color=").Append(Grey).Append(">아직 튄 프레임 없음</color>");
            }
            else
            {
                Spike last = _spikeRing[(_spikeCount - 1) % SpikeRingLength];
                _sb.Append("<color=#ff8a8a>마지막 렉</color> ").Append(last.Time.ToString("0.0")).Append("초 — ")
                   .Append(last.Ms.ToString("0.0")).Append(" ms   제일 컸던 것: ").Append(BlameText(last));
            }
            _spikeText = _sb.ToString();

            BuildTable();

            // 아래 줄 — GC와 그리기
            _sb.Clear();
            int gcCount = 0;
            if (_gcRow >= 0)
                for (int i = 0; i < n; i++)
                    if (_rows[_gcRow].History[i] > 0f) gcCount++;
            Row alloc = _gcAllocRow >= 0 ? _rows[_gcAllocRow] : null;
            if (alloc != null && alloc.Recorder.Valid)
                _sb.Append("GC 할당 평균 ").Append(alloc.Avg.ToString("0.0")).Append(" KB/프레임 · 최고 ")
                   .Append(alloc.Max.ToString("0.0")).Append(" KB");
            else
                _sb.Append("GC 할당 측정 안 됨");
            _sb.Append("   GC 수거 <color=").Append(gcCount > 0 ? Yellow : Green).Append('>').Append(gcCount).Append("번</color>\n");
            _sb.Append("그리기: 배치 ").Append(CountText(_batchRow)).Append(" · SetPass ").Append(CountText(_setPassRow));
            _footer = _sb.ToString();

            // 안내 줄
            if (_recording)
            {
                float t = Time.realtimeSinceStartup - _recStart;
                // 수정 (2026-10-23): Play 시작부터 녹화 중이면 그렇다고 적는다.
                string from = _recFromStart ? " (Play 시작부터)" : "";
                _hint = $"<color={Red}>● 녹화 중{from} {(int)(t / 60f):00}:{(int)(t % 60f):00}</color>   {recordKey} 누르면 저장";
            }
            else if (_savedNotice != null && Time.unscaledTime < _savedNoticeUntil)
                _hint = $"<color={Green}>{_savedNotice}</color>";
            else
                _hint = $"<color={Grey}>{toggleKey} 숨기기 · {recordKey} 녹화 시작(파일로 저장)</color>";
        }

        private string CountText(int row) =>
            row >= 0 && _rows[row].Recorder.Valid ? _rows[row].Avg.ToString("0") : "-";

        /// <summary>
        /// 시간 표 — 맨 위 줄을 평균이 큰 순으로, 우리 코드 항목은 부모 줄 바로 밑에 들여 쓴다.
        /// 수정 (2026-10-23): 예전엔 열마다 한 덩어리 글자로 만들어 Label 네 번에 그렸는데, 한글이 든 이름 열은
        /// 대체 글꼴 때문에 줄이 숫자 열보다 높아서 아래로 갈수록 이름과 숫자가 어긋나고 아래 줄과 겹쳤다.
        /// 이제 칸마다 글자를 따로 만들어 두고, OnGUI가 줄마다 같은 높이로 그린다.
        /// </summary>
        private void BuildTable()
        {
            _tableOrder.Clear();
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Kind == Kind.Time && _rows[i].Parent < 0 && !_rows[i].Missing) _tableOrder.Add(i);
            _tableOrder.Sort(_avgComparer);

            _cellLabels.Clear();
            _cellNows.Clear();
            _cellAvgs.Clear();
            _cellMaxs.Clear();
            AddTableLine("<b>항목 (ms)</b>", "<b>이번</b>", "<b>평균</b>", "<b>최고</b>");

            foreach (int index in _tableOrder)
            {
                AppendTableRow(_rows[index], false);
                for (int child = 0; child < _rows.Count; child++)
                    if (_rows[child].Parent == index) AppendTableRow(_rows[child], true);
            }
            // 이 유니티 버전에 없는 항목은 맨 아래에 흐리게
            foreach (Row row in _rows)
            {
                if (!row.Missing || row.Kind != Kind.Time) continue;
                AddTableLine($"<color={Grey}>{row.Label} — 측정 안 됨</color>", "", "", "");
            }

            _tableLines = _cellLabels.Count;
        }

        // 추가 생성 (2026-10-23)
        /// <summary>표에 한 줄(네 칸)을 더한다.</summary>
        private void AddTableLine(string label, string now, string avg, string max)
        {
            _cellLabels.Add(label);
            _cellNows.Add(now);
            _cellAvgs.Add(avg);
            _cellMaxs.Add(max);
        }

        /// <summary>표 한 줄. 평균이 3 ms 넘으면 노랑, 8 ms 넘으면 빨강 — 60fps 한 프레임(16.7 ms)에서 차지하는 몫이 크다는 뜻.</summary>
        private void AppendTableRow(Row row, bool child)
        {
            // 수정 (2026-10-23): 열 덩어리 대신 칸마다 글자를 만든다(BuildTable 설명 참고).
            string indent = child ? "   └ " : "";
            if (!row.Recorder.Valid)
            {
                // 우리 코드 마커인데 아직 한 번도 안 불림(예: 그 기능이 꺼져 있음)
                AddTableLine($"{indent}<color={Grey}>{row.Label} (아직 없음)</color>", "-", "-", "-");
                return;
            }

            string color = !row.CanBlame ? Grey : row.Avg >= 8f ? Red : row.Avg >= 3f ? Yellow : null;
            _sb.Clear().Append(indent);
            if (color != null) _sb.Append("<color=").Append(color).Append('>');
            _sb.Append(row.Label);
            if (color != null) _sb.Append("</color>");
            AddTableLine(_sb.ToString(), row.Now.ToString("0.0"), row.Avg.ToString("0.0"), row.Max.ToString("0.0"));
        }

        /// <summary>화면 높이에 맞춰 글자 크기를 정한다(1080p 기준 14px). 화면 크기가 바뀔 때만 다시 만든다.</summary>
        private void EnsureStyles()
        {
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2f);
            if (_left != null && Mathf.Approximately(scale, _styleScale)) return;
            _styleScale = scale;
            _left = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(14f * scale),
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _left.normal.textColor = new Color(0.92f, 0.93f, 0.95f);
            _right = new GUIStyle(_left) { alignment = TextAnchor.UpperRight };

            // 추가 생성 (2026-10-23) — 한글은 기본 글꼴에 없어 대체 글꼴로 그려지고, 그 줄은 lineHeight보다 높다.
            // 한글이 든 한 줄을 실제로 재서 모든 줄 높이로 쓴다 — 표·렉 줄·아래 줄이 서로 겹치지 않게.
            _rowHeight = Mathf.Ceil(Mathf.Max(_left.lineHeight, _left.CalcSize(new GUIContent("렉 미터 항목 Ag")).y));
        }

        /// <summary>오른쪽 위 판넬. 그리기만 한다 — 글자는 BuildTexts가 미리 만들어 둔다.</summary>
        private void OnGUI()
        {
            if (!_visible || Event.current.type != EventType.Repaint) return;
            using (s_selfMarker.Auto())
            {
                EnsureStyles();
                float scale = _styleScale;
                float line = _rowHeight; // 수정 (2026-10-23): _left.lineHeight → 한글 줄을 잰 높이(EnsureStyles 참고)
                float pad = 8f * scale;
                float gap = 4f * scale;
                float width = 470f * scale;
                float graphHeight = GraphHeight * scale;
                const int SpikeLines = 2, FooterLines = 2;
                float height = pad * 2f + line * (1 + SpikeLines + _tableLines + FooterLines + 1) + graphHeight + gap * 4f;

                var panel = new Rect(Screen.width - width - 10f, 10f, width, height);
                GUI.DrawTexture(panel, _panel);

                float x = panel.x + pad, w = panel.width - pad * 2f, y = panel.y + pad;
                GUI.Label(new Rect(x, y, w, line), _headLeft, _left);
                GUI.Label(new Rect(x, y, w, line), _headRight, _right);
                y += line + gap;

                // 링 버퍼의 가장 오래된 칸(_head)부터 그려서, 가장 최근 프레임이 오른쪽 끝에 오게 한다.
                GUI.DrawTextureWithTexCoords(new Rect(x, y, w, graphHeight), _graph,
                                             new Rect((float)_head / HistoryLength, 0f, 1f, 1f));
                y += graphHeight + gap;

                GUI.Label(new Rect(x, y, w, line * SpikeLines), _spikeText, _left);
                y += line * SpikeLines + gap;

                float labelWidth = w * 0.52f, columnWidth = (w - labelWidth) / 3f, tableHeight = line * _tableLines;
                // 수정 (2026-10-23): 줄마다 같은 높이로 네 칸을 따로 그린다 — 열 덩어리로 그리면 한글 열만 줄이 높아 어긋났다.
                for (int i = 0; i < _cellLabels.Count; i++)
                {
                    float rowY = y + line * i;
                    GUI.Label(new Rect(x, rowY, labelWidth, line), _cellLabels[i], _left);
                    GUI.Label(new Rect(x + labelWidth, rowY, columnWidth, line), _cellNows[i], _right);
                    GUI.Label(new Rect(x + labelWidth + columnWidth, rowY, columnWidth, line), _cellAvgs[i], _right);
                    GUI.Label(new Rect(x + labelWidth + columnWidth * 2f, rowY, columnWidth, line), _cellMaxs[i], _right);
                }
                y += tableHeight + gap;

                GUI.Label(new Rect(x, y, w, line * FooterLines), _footer, _left);
                y += line * FooterLines;
                GUI.Label(new Rect(x, y, w, line), _hint, _left);
            }
        }

#if UNITY_EDITOR
        // ───────────────────────── 에디터 메뉴 ─────────────────────────

        // 추가 생성 (2026-10-23)
        private const string RecordFromStartMenu = "Momentum/렉 미터/Play 시작부터 녹화";

        /// <summary>
        /// 메뉴로 "Play 시작부터 녹화"를 켜고 끈다. 값은 PlayerPrefs에 둬서 Play 때 Awake가 바로 읽는다.
        /// 렉 미터는 Play 때 저절로 생기는 오브젝트라 인스펙터에서 미리 체크해 둘 수가 없어 메뉴로 뺐다.
        /// 녹화는 F4로 저장하고, 안 누르고 Play를 끝내도 OnDestroy가 저장한다.
        /// </summary>
        [UnityEditor.MenuItem(RecordFromStartMenu)]
        private static void ToggleRecordFromStart()
        {
            bool on = PlayerPrefs.GetInt(RecordFromStartPrefKey, 0) == 0;
            PlayerPrefs.SetInt(RecordFromStartPrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log(on
                ? "[LagMeter] 다음 Play부터 첫 프레임부터 녹화한다 — F4나 Play 끝내기로 저장."
                : "[LagMeter] Play 시작부터 녹화를 껐다 — 다시 F4로 직접 녹화한다.");
        }

        /// <summary>메뉴 옆 체크 표시를 지금 값에 맞춘다.</summary>
        [UnityEditor.MenuItem(RecordFromStartMenu, true)]
        private static bool ToggleRecordFromStartValidate()
        {
            UnityEditor.Menu.SetChecked(RecordFromStartMenu, PlayerPrefs.GetInt(RecordFromStartPrefKey, 0) == 1);
            return true;
        }
#endif
    }
}
#endif
