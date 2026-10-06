using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>스테이지 클리어 기록. `Application.persistentDataPath/IBIIIS_progress.json`에 스테이지 ID 목록으로 저장한다.
    /// 읽을 수 없는 파일은 덮어쓰기 전에 `.corrupt` 사본으로 보존한다.</summary>
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
        /// <summary>저장 파일 경로.</summary>
        public static string FilePath => overridePath ?? Path.Combine(Application.persistentDataPath, FileName);
        /// <summary>저장 파일 경로를 바꾼다(테스트용). null이면 기본 경로로 되돌린다.</summary>
        public static void UseFile(string path) { overridePath = path; data = null; }
        public static bool IsCleared(string stageId) => !string.IsNullOrEmpty(stageId) && Load().clearedStages.Contains(stageId);
        public static IReadOnlyList<string> ClearedStages => Load().clearedStages;
        /// <summary>클리어 기록을 추가하고 바로 저장한다. 이미 있으면 아무것도 하지 않는다.</summary>
        public static void MarkCleared(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) throw new ArgumentException("스테이지 ID가 비어 있어 클리어 기록을 저장할 수 없습니다.", nameof(stageId));
            var current = Load();
            if (current.clearedStages.Contains(stageId)) return;
            current.clearedStages.Add(stageId); Save(current);
        }
        /// <summary>모든 클리어 기록을 지운다(파일 삭제).</summary>
        public static void ResetAll()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            data = new Data();
        }
        /// <summary>다음 조회 때 파일을 다시 읽게 한다.</summary>
        public static void Reload() { data = null; }
        private static Data Load()
        {
            if (data != null) return data;
            var path = FilePath;
            data = new Data();
            if (!File.Exists(path)) return data;
            try
            {
                var loaded = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if (loaded == null || loaded.clearedStages == null) throw new FormatException("클리어 기록 형식이 아닙니다.");
                if (loaded.version > Version) throw new FormatException($"더 새로운 버전({loaded.version})의 기록입니다.");
                loaded.clearedStages.RemoveAll(string.IsNullOrEmpty);
                data = loaded;
            }
            catch (Exception e)
            {
                var backup = path + ".corrupt";
                File.Copy(path, backup, true);
                Debug.LogError($"[IBIIIS] 클리어 기록 '{path}'을(를) 읽지 못했습니다({e.Message}). 원본을 '{backup}'에 보존하고 빈 기록으로 시작합니다.");
            }
            return data;
        }
        private static void Save(Data value)
        {
            var path = FilePath; var temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { data = null; overridePath = null; }
    }
}
