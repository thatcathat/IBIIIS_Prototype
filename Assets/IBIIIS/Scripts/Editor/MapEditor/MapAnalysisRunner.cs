using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>선택한 맵을 MapSolver로 분석하고 결과를 Console과 대화상자에 보여 준다. 맵 에셋은 수정하지 않는다.</summary>
    public static class MapAnalysisRunner
    {
        [MenuItem("IBIIIS/Analyze Selected Map")]
        public static void AnalyzeSelection()
        {
            var map = Selection.activeObject as GridMap;
            if (map == null) { EditorUtility.DisplayDialog("맵 분석", "Project 창에서 GridMap 에셋을 선택하세요.", "확인"); return; }
            Run(map);
        }
        /// <summary>진행 막대와 함께 분석만 수행한다. 결과 표시는 호출한 쪽이 맡는다.</summary>
        public static MapAnalysis Compute(GridMap map, int maxStates = 200000)
        {
            var options = new MapAnalysisOptions
            {
                MaxStates = Mathf.Max(1000, maxStates),
                Progress = (expanded, found) => !EditorUtility.DisplayCancelableProgressBar("맵 분석", $"상태 {found}개 발견, {expanded}개 탐색", Mathf.Clamp01(found / (float)Mathf.Max(1000, maxStates)))
            };
            MapAnalysis result;
            try { result = MapSolver.Analyze(map, options); }
            finally { EditorUtility.ClearProgressBar(); }
            return result;
        }
        public static MapAnalysis Run(GridMap map)
        {
            var result = Compute(map);
            string report = result.ToReport();
            Debug.Log($"[IBIIIS] 맵 분석: {map.name}\n{report}", map);
            EditorUtility.DisplayDialog($"맵 분석 — {map.name}", report, "확인");
            return result;
        }
    }
}
