using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>번역할 때 본 한국어 원문 기록(에디터 전용). 번역을 가져올 때 그 줄의 지금 원문을 적어 두고, 나중에 원문이 바뀌면
    /// CSV 내보내기가 "원문 바뀜"으로 표시한다. CSV 가져오기만 이 기록을 고친다.</summary>
    public sealed class TranslationRecord : ScriptableObject
    {
        [Serializable]
        public struct Entry { public string table, key, language, korean; }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public string Find(string table, string key, string language)
        {
            foreach (var e in entries) if (e.table == table && e.key == key && e.language == language) return e.korean;
            return null;
        }
        public void Set(string table, string key, string language, string korean)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].table == table && entries[i].key == key && entries[i].language == language) { var e = entries[i]; e.korean = korean; entries[i] = e; return; }
            entries.Add(new Entry { table = table, key = key, language = language, korean = korean });
        }
    }
}
