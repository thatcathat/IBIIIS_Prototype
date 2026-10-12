using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor에서 대본(TutorialScript) 에셋을 새로 만들거나 복제하는 규칙(화면과 분리, 자동 테스트 대상).
    /// 대본 ID는 클리어 기록 등에 연결되는 고정값이라 비거나 다른 대본과 겹치면 만들지 않는다.</summary>
    public static class DialogueScriptAssets
    {
        /// <summary>새 대본의 기본 저장 폴더.</summary>
        public const string DefaultFolder = TutorialSetup.Folder;
        private static readonly Regex IdPattern = new Regex("^[a-z0-9_-]+$");

        /// <summary>파일 이름으로 대본 ID를 제안한다. 끝의 Script를 떼고 단어·숫자 사이를 _로 나눠 소문자로 바꾼다(Tutorial3Script → tutorial_3, BossIntro → boss_intro).</summary>
        public static string SuggestId(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
            if (name.Length > 6 && name.EndsWith("Script", System.StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 6);
            var id = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i], previous = i > 0 ? name[i - 1] : '\0';
                bool boundary = i > 0 && ((char.IsUpper(c) && char.IsLower(previous)) || (char.IsDigit(c) && char.IsLetter(previous)) || (char.IsLetter(c) && char.IsDigit(previous)));
                if (boundary) id.Append('_');
                id.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '_');
            }
            return Regex.Replace(id.ToString(), "_+", "_").Trim('_');
        }
        /// <summary>이 ID를 쓰는 다른 대본(except 제외). 없으면 null.</summary>
        public static TutorialScript FindById(string id, TutorialScript except = null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TutorialScript"))
            {
                var found = AssetDatabase.LoadAssetAtPath<TutorialScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null && found != except && found.Id == id) return found;
            }
            return null;
        }
        /// <summary>ID를 쓸 수 없는 이유(쓸 수 있으면 null).</summary>
        public static string IdProblem(string id, TutorialScript except = null)
        {
            if (string.IsNullOrWhiteSpace(id)) return "대본 ID를 입력하세요.";
            if (!IdPattern.IsMatch(id)) return "대본 ID는 영어 소문자·숫자·_·-만 쓸 수 있습니다.";
            var other = FindById(id, except);
            return other != null ? $"이미 '{other.name}' 대본이 쓰는 ID입니다." : null;
        }
        /// <summary>빈 대본의 시작 내용: 빈 지문 한 줄.</summary>
        public static TutorialStep[] BlankSteps() => new[] { new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Narrate("") } };
        /// <summary>대본 에셋을 만든다. 단계는 복사본이며 단계 ID는 새로 받는다(원본은 바뀌지 않음). 경로에 이미 파일이 있거나 ID를 쓸 수 없으면 만들지 않고 null과 이유를 돌려준다.</summary>
        public static TutorialScript Create(string path, string id, TutorialStep[] steps, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") || !path.EndsWith(".asset")) { problem = "Assets 폴더 안의 .asset 경로가 필요합니다."; return null; }
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) { problem = $"이미 파일이 있습니다: {path}"; return null; }
            problem = IdProblem(id);
            if (problem != null) return null;
            var copy = (steps ?? BlankSteps()).Select(s => { s.id = null; return s; }).ToArray();
            if (copy.Length == 0) copy = BlankSteps();
            var script = ScriptableObject.CreateInstance<TutorialScript>();
            script.Set(id, copy);
            AssetPaths.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateAsset(script, path);
            AssetDatabase.SaveAssetIfDirty(script);
            return script;
        }
    }
}
