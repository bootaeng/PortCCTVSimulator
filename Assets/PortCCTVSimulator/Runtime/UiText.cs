using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Korean UI labels. Display only: scenario files, metadata, labels and logs keep the English ids / display names, so
    /// captures are unchanged by the UI language. Unknown ids fall back to the English display name.
    /// </summary>
    public static class UiText
    {
        static readonly Dictionary<string, string> Harbors = new Dictionary<string, string>
        {
            { "container_terminal_01", "컨테이너 터미널" },
            { "fishing_port_01", "어항" },
            { "marina_01", "마리나" },
            { "generic_harbor_02", "혼합 항만 (v0.2)" },
        };

        static readonly Dictionary<string, string> Cameras = new Dictionary<string, string>
        {
            { "ct_A_quay_east_mast", "A - 안벽 마스트 20 m (항내 → 입구)" },
            { "ct_B_breakwater_head", "B - 방파제 두부 10 m (입구 접근로)" },
            { "ct_C_admin_roof", "C - 동측 안벽 마스트 30 m (입구)" },
            { "ct_D_hill_tower", "D - 동측 곶 58 m (산업항 전경)" },
            { "ct_E_basin_breakwater", "E - 남방파제 마스트 13 m (선석)" },
            { "fp_A_market_roof", "A - 위판장 지붕 11 m (항내 → 입구)" },
            { "fp_B_hillside", "B - 서측 곶 40 m (입구 감시)" },
            { "fp_C_breakwater", "C - 동방파제 18 m (선착장·항구 마을)" },
            { "mr_A_promenade", "A - 산책로 가로등 7 m (계류장)" },
            { "mr_B_tower", "B - 동측 곶 60 m (해변·해안 도시)" },
            { "mr_C_breakwater", "C - 방파제 두부 12 m (입구 접근로)" },
            { "cam_A_quay_mast", "A - 북측 안벽 마스트 (12 m)" },
            { "cam_B_breakwater_head", "B - 동방파제 두부 (8 m)" },
            { "cam_C_warehouse_roof", "C - 창고 지붕, 광각 (22 m)" },
            { "cam_D_tower_overview", "D - 아파트 옥상 조망 (78 m)" },
        };

        static readonly Dictionary<string, string> Environments = new Dictionary<string, string>
        {
            { "day_clear", "주간, 맑음" },
            { "day_backlit", "주간, 역광 (태양이 카메라 정면)" },
            { "dusk_low_light", "황혼, 저조도" },
            { "light_haze", "주간, 옅은 연무" },
            { "night_clear", "야간, 맑음 (달빛·항만 조명)" },
            { "night_overcast", "야간, 흐림 (도시 불빛)" },
        };

        static readonly Dictionary<string, string> Waters = new Dictionary<string, string>
        {
            { "calm", "잔잔한 항내" },
            { "light_chop", "약한 파랑" },
        };

        static readonly Dictionary<string, string> Vessels = new Dictionary<string, string>
        {
            { "kayak", "카약" }, { "canoe", "카누" }, { "sailing_dinghy", "딩기 요트" }, { "sailing_yacht", "세일링 요트" },
            { "rib", "고무보트(RIB)" }, { "motorboat", "모터보트" }, { "cabin_cruiser", "캐빈 크루저" },
            { "fishing_boat", "소형 어선" }, { "korean_fishing_boat", "연안 어선" }, { "ferry", "여객선" },
            { "tugboat", "예인선(구형)" }, { "barge", "바지선" }, { "container_ship", "컨테이너선(구형)" }, { "tanker", "유조선" },
            { "tug_harbor", "항만 예인선" }, { "general_cargo", "일반 화물선" }, { "feeder_ship", "컨테이너 피더선" }, { "bulk_carrier", "벌크선" },
            { "lng_carrier", "LNG 운반선" }, { "trawler", "트롤 어선" }, { "fishing_vessel", "어선 12 m" }, { "inshore_fishing_boat", "소형 연안 어선" },
            { "sar_boat", "구조정" }, { "pilot_boat", "도선선" }, { "patrol_boat", "경비정" }, { "police_boat", "경찰정" },
            { "speedboat", "스피드보트" }, { "motor_yacht", "모터 요트" },
        };

        public static readonly string[] MotionModes = { "none", "anchor", "roam", "auto" };

        static readonly Dictionary<string, string> Motions = new Dictionary<string, string>
        {
            { "none", "고정" }, { "anchor", "정박" }, { "roam", "자유 항해" }, { "auto", "순찰" }, { "route", "지정 항로" },
        };

        static readonly Dictionary<string, string> States = new Dictionary<string, string>
        {
            { "fixed", "고정" }, { "at_anchor", "정박 중" }, { "underway", "항해 중" }, { "give_way", "피항 중" },
            { "slowing", "감속" }, { "holding_obstacle", "장애물 앞 대기" }, { "holding_vessel", "다른 선박 앞 대기" }, { "arrived", "도착" }, { "no_route", "경로 없음" },
        };

        public static string Harbor(string id) => Harbors.TryGetValue(id, out var s) ? s : (id == HarborLibrary.LegacyHarborId ? "구형 단순 항만" : HarborLibrary.DisplayName(id));
        public static string Camera(CameraPreset p) => Cameras.TryGetValue(p.id, out var s) ? s : p.display_name;
        public static readonly string[] SensorPresetNames = { "끔", "HD IP", "일반 CCTV", "저화질", "야간 IR 흑백" };
        public static string SensorPresetNote(string id) => id switch
        {
            "hd_ip" => "최신 HD IP 카메라: 약한 왜곡·잡음, JPEG 85",
            "typical" => "일반 항만 CCTV: 술통형 왜곡, 약한 흐림, 잡음, JPEG 55",
            "low_quality" => "오래된 저화질: 강한 왜곡·흐림·흔들림·잡음, JPEG 25",
            "night_ir" => "야간 적외선 모드: 흑백, 잡음 많음, 흐림",
            "custom" => "사용자 지정",
            _ => "열화 없음: 렌더 화면 그대로 저장",
        };

        public static string Environment(EnvironmentPreset e) => Environments.TryGetValue(e.id, out var s) ? s : e.display_name;
        public static string Water(WaterPreset w) => Waters.TryGetValue(w.id, out var s) ? s : w.display_name;
        public static string Vessel(string assetId) => Vessels.TryGetValue(assetId, out var s) ? s : assetId;
        public static string Precipitation(string p) => p == "rain" ? "비" : p == "snow" ? "눈" : "없음";
        public static string Motion(string mode) => mode != null && Motions.TryGetValue(mode, out var s) ? s : mode;
        public static string State(string state) => state != null && States.TryGetValue(state, out var s) ? s : state;

        // ---------------------------------------------------------------- font
        static Font _font;
        static bool _fontTried;
        public static string FontName { get; private set; }

        /// <summary>The IMGUI default font has no Hangul glyphs in the player; use an installed Korean OS font (Windows ships
        /// Malgun Gothic). Returns null when none is installed (Hangul then renders as boxes and the log says so).</summary>
        public static Font KoreanFont()
        {
            if (_fontTried) return _font;
            _fontTried = true;
            string[] installed;
            try { installed = Font.GetOSInstalledFontNames(); } catch { installed = new string[0]; }
            var set = new HashSet<string>(installed);
            foreach (var n in new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "나눔고딕", "Noto Sans KR", "Noto Sans CJK KR", "Gulim", "굴림", "Dotum", "돋움" })
                if (set.Contains(n)) { FontName = n; break; }
            if (FontName != null) _font = Font.CreateDynamicFontFromOSFont(FontName, 14);
            return _font;
        }
    }
}
