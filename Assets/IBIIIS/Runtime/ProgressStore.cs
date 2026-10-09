using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>스테이지 클리어 기록. `Application.persistentDataPath/IBIIIS_progress.json`에 스테이지 ID 목록으로 저장한다.
    /// 형식이 잘못된 파일은 `.corrupt` 사본으로 보존한 뒤 빈 기록으로 시작한다. 파일을 읽지 못했거나(IO 오류) 사본을 만들지 못했으면
    /// 원본을 지키려고 이번 실행 동안 저장하지 않는다(Reload 또는 ResetAll 전까지). 저장에 성공한 뒤에만 메모리 기록을 바꾼다.</summary>
    public static class ProgressStore
    {
        public const string FileName = "IBIIIS_progress.json";
        public const int Version = 1;
        [Serializable]
        private sealed class Data
        {
            public int version = Version;
            public List<string> clearedStages = new List<string>();
        }
        private static Data data;
        private static string overridePath;
        // 기록 파일을 안전하게 읽지 못해 저장을 막은 이유. null이면 저장 가능.
        private static string blocked;
        /// <summary>저장을 막은 이유(파일을 읽지 못함 등). 저장할 수 있으면 null.</summary>
        public static string SaveBlockedReason { get { Load(); return blocked; } }
        /// <summary>저장 파일 경로.</summary>
        public static string FilePath => overridePath ?? Path.Combine(Application.persistentDataPath, FileName);
        /// <summary>저장 파일 경로를 바꾼다(테스트용). null이면 기본 경로로 되돌린다.</summary>
        public static void UseFile(string path) { overridePath = path; data = null; blocked = null; }
        public static bool IsCleared(string stageId) => !string.IsNullOrEmpty(stageId) && Load().clearedStages.Contains(stageId);
        public static IReadOnlyList<string> ClearedStages => Load().clearedStages;
        /// <summary>클리어 기록을 추가하고 바로 저장한다. 이미 있으면 아무것도 하지 않는다.
        /// 저장에 실패하면 예외를 던지고 메모리 기록도 바꾸지 않는다(같은 스테이지를 다시 이기면 다시 저장을 시도한다).</summary>
        public static void MarkCleared(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) throw new ArgumentException("스테이지 ID가 비어 있어 클리어 기록을 저장할 수 없습니다.", nameof(stageId));
            var current = Load();
            if (current.clearedStages.Contains(stageId)) return;
            if (blocked != null) throw new InvalidOperationException(blocked);
            var next = new Data { version = current.version, clearedStages = new List<string>(current.clearedStages) { stageId } };
            Save(next); data = next;
        }
        /// <summary>모든 클리어 기록을 지운다(파일 삭제).</summary>
        public static void ResetAll()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            data = new Data(); blocked = null;
        }
        /// <summary>다음 조회 때 파일을 다시 읽게 한다.</summary>
        public static void Reload() { data = null; blocked = null; }
        // 결과는 실패해도 캐시한다(OnGUI에서 매 프레임 조회하므로 매번 다시 읽지 않는다).
        private static Data Load()
        {
            if (data != null) return data;
            var path = FilePath;
            if (!File.Exists(path)) return data = new Data();
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                blocked = $"클리어 기록 파일을 읽지 못해 저장하지 않습니다({e.Message}). 파일: {path}";
                Debug.LogError("[IBIIIS] " + blocked);
                return data = new Data();
            }
            try
            {
                var loaded = JsonUtility.FromJson<Data>(text);
                if (loaded == null || loaded.clearedStages == null) throw new FormatException("클리어 기록 형식이 아닙니다.");
                if (loaded.version > Version) throw new FormatException($"더 새로운 버전({loaded.version})의 기록입니다.");
                loaded.clearedStages.RemoveAll(string.IsNullOrEmpty);
                return data = loaded;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                var backup = path + ".corrupt";
                try
                {
                    File.Copy(path, backup, true);
                    Debug.LogError($"[IBIIIS] 클리어 기록 '{path}'을(를) 읽지 못했습니다({e.Message}). 원본을 '{backup}'에 보존하고 빈 기록으로 시작합니다.");
                }
                catch (Exception copyError) when (copyError is IOException || copyError is UnauthorizedAccessException)
                {
                    blocked = $"클리어 기록 '{path}'의 형식이 잘못되었는데({e.Message}) 사본도 만들지 못해({copyError.Message}) 원본을 지키려고 저장하지 않습니다.";
                    Debug.LogError("[IBIIIS] " + blocked);
                }
                return data = new Data();
            }
        }
        private static void Save(Data value)
        {
            var path = FilePath; var temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { data = null; overridePath = null; blocked = null; }
    }
}
