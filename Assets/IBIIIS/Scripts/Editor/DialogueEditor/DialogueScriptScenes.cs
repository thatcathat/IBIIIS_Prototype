using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor의 '씬에 연결': 프로젝트 씬의 대본 진행기(ScenarioDirector)에 대본을 꽂는 규칙(화면과 분리, 자동 테스트 대상).
    /// 열린 씬은 그 자리에서 연결하고(Ctrl+Z 가능, 저장은 사용자), 닫힌 씬은 파일을 잠깐 더해 열어 연결·저장한 뒤 닫는다(되돌리기 없음).</summary>
    public static class DialogueScriptScenes
    {
        /// <summary>진행기 하나. 열린 씬이면 Director가 있고, 닫힌 씬이면 파일에서 읽은 정보(오브젝트 이름·연결된 대본 GUID·등장 오브젝트 ID·자동 시작)만 있다.</summary>
        public sealed class Entry
        {
            public string ScenePath, SceneName, ObjectName, ScriptGuid;
            public ulong FileId;
            public ScenarioDirector Director;
            public bool Open => Director != null;
            public ScenarioScript Script => Open ? ScriptOf(Director) : string.IsNullOrEmpty(ScriptGuid) ? null : AssetDatabase.LoadAssetAtPath<ScenarioScript>(AssetDatabase.GUIDToAssetPath(ScriptGuid));
            public List<string> FileActorIds = new List<string>();
            public bool FilePlayOnStart = true;
            /// <summary>등장 오브젝트(Actors) ID.</summary>
            public List<string> ActorIds => Open ? new List<string>(Director.ActorIds) : new List<string>(FileActorIds);
            public bool PlayOnStart => Open ? Director.PlayOnStart : FilePlayOnStart;
        }

        /// <summary>대본이 ID로 부르는 등장 오브젝트(감정 표시·켜고 끄기·감정 표시 끝의 Target). 예약된 "player"와 빈 칸은 뺀다. 처음 나온 단계 번호와 함께.</summary>
        public static List<(string id, int index)> TargetIds(ScenarioStep[] steps)
        {
            var list = new List<(string, int)>(); var seen = new HashSet<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                bool usesTarget = s.type == ScenarioStepType.Emote || s.type == ScenarioStepType.SetActive || (s.type == ScenarioStepType.EndDisplay && s.display == ScenarioDisplay.Emote);
                if (usesTarget && !string.IsNullOrEmpty(s.target) && s.target != "player" && seen.Add(s.target)) list.Add((s.target, i));
            }
            return list;
        }
        /// <summary>연결된 진행기마다 Actors에 없는 대상 ID를 검사 항목(주의)으로 돌려준다. 진행기는 이런 단계를 경고만 남기고 건너뛴다.</summary>
        public static List<DialogueScriptEdits.Issue> ActorIssues(ScenarioStep[] steps, IEnumerable<Entry> linked)
        {
            var issues = new List<DialogueScriptEdits.Issue>(); var targets = TargetIds(steps);
            foreach (var entry in linked)
            {
                var actors = new HashSet<string>(entry.ActorIds);
                foreach (var (id, index) in targets)
                    if (!actors.Contains(id)) issues.Add(new DialogueScriptEdits.Issue(index, $"대상 '{id}'가 {entry.SceneName} 씬 진행기({entry.ObjectName})의 Actors에 없음 — 이 단계는 건너뜀", false));
            }
            return issues;
        }

        /// <summary>씬 하나에 있는 진행기(꺼진 오브젝트 포함).</summary>
        public static List<ScenarioDirector> DirectorsIn(Scene scene)
        {
            var list = new List<ScenarioDirector>();
            if (!scene.IsValid() || !scene.isLoaded) return list;
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<ScenarioDirector>(true));
            return list;
        }
        /// <summary>지금 열린(Hierarchy에 있는) 모든 씬의 진행기.</summary>
        public static List<ScenarioDirector> DirectorsInOpenScenes()
        {
            var list = new List<ScenarioDirector>();
            for (int i = 0; i < SceneManager.sceneCount; i++) list.AddRange(DirectorsIn(SceneManager.GetSceneAt(i)));
            return list;
        }
        /// <summary>프로젝트(Assets)의 모든 씬에 있는 진행기. 열린 씬은 Hierarchy에서, 닫힌 씬은 씬 파일을 읽어서 찾는다(씬을 열지 않음).</summary>
        public static List<Entry> DirectorsInProject()
        {
            var entries = new List<Entry>(); var openPaths = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded) continue;
                openPaths.Add(scene.path);
                foreach (var director in DirectorsIn(scene))
                    entries.Add(new Entry { ScenePath = scene.path, SceneName = string.IsNullOrEmpty(scene.name) ? "(저장 안 된 씬)" : scene.name, ObjectName = PathOf(director.transform), Director = director, FileId = GlobalObjectId.GetGlobalObjectIdSlow(director).targetObjectId });
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!openPaths.Contains(path)) entries.AddRange(ReadSceneFile(path));
            }
            return entries;
        }
        private static string directorGuid;
        private static string DirectorGuid
        {
            get
            {
                if (directorGuid != null) return directorGuid;
                foreach (var guid in AssetDatabase.FindAssets("ScenarioDirector t:MonoScript"))
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                    if (script != null && script.GetClass() == typeof(ScenarioDirector)) return directorGuid = guid;
                }
                return directorGuid = "";
            }
        }
        private static readonly Dictionary<string, (System.DateTime written, List<Entry> entries)> fileCache = new Dictionary<string, (System.DateTime, List<Entry>)>();
        private static readonly Regex BlockHeader = new Regex(@"^--- !u!(\d+) &(\d+)", RegexOptions.Multiline);
        /// <summary>씬 파일(텍스트 직렬화)을 열지 않고 읽어 진행기를 찾는다: 오브젝트 이름과 연결된 대본 GUID.</summary>
        public static List<Entry> ReadSceneFile(string path)
        {
            if (string.IsNullOrEmpty(DirectorGuid) || !File.Exists(path)) return new List<Entry>();
            // 파일이 바뀌지 않았으면 전에 읽은 결과를 쓴다(Hierarchy가 바뀔 때마다 다시 읽지 않게).
            var written = File.GetLastWriteTimeUtc(path);
            if (fileCache.TryGetValue(path, out var cached) && cached.written == written) return new List<Entry>(cached.entries);
            var entries = new List<Entry>();
            string text = File.ReadAllText(path);
            fileCache[path] = (written, entries);
            if (!text.Contains(DirectorGuid)) return new List<Entry>(entries);
            // 블록: "--- !u!<종류> &<파일 ID>" 다음부터 다음 머리까지.
            var headers = BlockHeader.Matches(text); var blocks = new Dictionary<ulong, (int type, string body)>();
            for (int i = 0; i < headers.Count; i++)
            {
                int start = headers[i].Index, end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                blocks[ulong.Parse(headers[i].Groups[2].Value)] = (int.Parse(headers[i].Groups[1].Value), text.Substring(start, end - start));
            }
            foreach (var pair in blocks)
            {
                if (pair.Value.type != 114 || !pair.Value.body.Contains("guid: " + DirectorGuid)) continue;
                var body = pair.Value.body;
                string name = "";
                var go = Regex.Match(body, @"m_GameObject: \{fileID: (\d+)\}");
                if (go.Success && blocks.TryGetValue(ulong.Parse(go.Groups[1].Value), out var goBlock)) name = Regex.Match(goBlock.body, @"m_Name: (.*)").Groups[1].Value.Trim();
                var script = Regex.Match(body, @"^  script: \{fileID: \d+(?:, guid: ([0-9a-f]+))?", RegexOptions.Multiline);
                var entry = new Entry { ScenePath = path, SceneName = Path.GetFileNameWithoutExtension(path), ObjectName = name, FileId = pair.Key, ScriptGuid = script.Success ? script.Groups[1].Value : "" };
                // 등장 오브젝트: "  actors:" 아래 "  - id: <ID>" 줄들(배열 항목은 두 칸 들여쓰기 "- ", 항목 안 칸은 네 칸).
                var actors = Regex.Match(body, @"^  actors:[^\n]*\n((?:  [- ] .*\n?)*)", RegexOptions.Multiline);
                if (actors.Success) foreach (Match id in Regex.Matches(actors.Groups[1].Value, @"^  - id: ?(.*)$", RegexOptions.Multiline)) entry.FileActorIds.Add(id.Groups[1].Value.Trim().Trim('"', '\''));
                var play = Regex.Match(body, @"^  playOnStart: (\d)", RegexOptions.Multiline);
                if (play.Success) entry.FilePlayOnStart = play.Groups[1].Value != "0";
                entries.Add(entry);
            }
            return new List<Entry>(entries);
        }
        /// <summary>진행기에 연결된 대본.</summary>
        public static ScenarioScript ScriptOf(ScenarioDirector director)
            => director != null ? new SerializedObject(director).FindProperty("script").objectReferenceValue as ScenarioScript : null;
        /// <summary>진행기에 대본을 연결한다(되돌리기 가능, 씬 변경 표시). 이미 같은 대본이면 아무것도 하지 않고 false.</summary>
        public static bool Connect(ScenarioDirector director, ScenarioScript script)
        {
            if (director == null || script == null) return false;
            var so = new SerializedObject(director); var property = so.FindProperty("script");
            if (property.objectReferenceValue == script) return false;
            property.objectReferenceValue = script;
            so.ApplyModifiedProperties(); // Undo 기록 포함
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            return true;
        }
        /// <summary>목록의 진행기에 대본을 연결한다. 열린 씬이면 Connect와 같다. 닫힌 씬이면 씬을 더해 열고 연결·저장한 뒤 닫는다(되돌리기 없음).
        /// 실패하면 false와 이유. 저장되지 않은 대본(에셋이 아닌 대본)은 닫힌 씬에 연결하지 않는다.</summary>
        public static bool Connect(Entry entry, ScenarioScript script, out string problem)
        {
            problem = null;
            if (entry == null || script == null) { problem = "연결할 진행기·대본이 없습니다."; return false; }
            if (entry.Open) { if (!Connect(entry.Director, script)) problem = "이미 이 대본이 연결되어 있습니다."; return problem == null; }
            if (!EditorUtility.IsPersistent(script)) { problem = "파일로 저장된 대본만 닫힌 씬에 연결할 수 있습니다."; return false; }
            if (Application.isPlaying) { problem = "Play 중에는 닫힌 씬에 연결할 수 없습니다."; return false; }
            Scene scene;
            try { scene = EditorSceneManager.OpenScene(entry.ScenePath, OpenSceneMode.Additive); }
            catch (System.Exception e) { problem = $"씬을 열 수 없습니다: {e.Message}"; return false; }
            try
            {
                ScenarioDirector target = null;
                foreach (var director in DirectorsIn(scene)) if (GlobalObjectId.GetGlobalObjectIdSlow(director).targetObjectId == entry.FileId) target = director;
                if (target == null) { problem = $"{entry.SceneName} 씬에서 진행기를 찾지 못했습니다(씬이 바뀌었을 수 있음)."; return false; }
                if (!Connect(target, script)) { problem = "이미 이 대본이 연결되어 있습니다."; return false; }
                Undo.ClearUndo(target); // 곧 닫는 씬의 되돌리기 기록은 남기지 않는다
                if (!EditorSceneManager.SaveScene(scene)) { problem = $"{entry.ScenePath}를 저장하지 못했습니다."; return false; }
                return true;
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        /// <summary>목록·안내에 쓰는 이름: 씬 이름 / 오브젝트 경로.</summary>
        public static string Describe(ScenarioDirector director) => $"{director.gameObject.scene.name} / {PathOf(director.transform)}";
        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
