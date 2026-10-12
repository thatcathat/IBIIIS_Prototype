using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>인식 순간 주고받는 한 쌍의 대사: 플레이어가 먼저 말하고, 인식한 적이 대답한다.</summary>
    [Serializable]
    public struct DialogueExchange
    {
        [Tooltip("플레이어 머리 위에 먼저 뜨는 말. 비우면 플레이어 말 없이 적만 바로 말합니다.")] public string player;
        [Tooltip("인식한 적 머리 위에 이어서 뜨는 대답. 비우면 그 적은 말하지 않습니다.")] public string enemy;
    }
    /// <summary>전투 말풍선 대사 목록(표시 전용). 공용 목록은 Enemy Alert 설정에, 적별 목록은 적 프리팹의 EnemyDefinition에 연결한다.
    /// 실행 중 이 에셋을 바꾸지 않는다(직전 대사 기억은 실행 중인 전투가 따로 가진다).</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Battle Dialogue")]
    public sealed class BattleDialogue : ScriptableObject
    {
        [SerializeField, Tooltip("적이 플레이어를 새로 인식했을 때 고를 대사 쌍(플레이어 말 → 적 대답). 하나를 무작위로 고릅니다.")] private DialogueExchange[] spotted = Array.Empty<DialogueExchange>();
        [SerializeField, Tooltip("적의 인식에서 벗어났을 때 그 적이 혼자 하는 말. 하나를 무작위로 고르며, 비우면 아무 말도 하지 않습니다.")] private string[] lost = Array.Empty<string>();
        [SerializeField, Tooltip("체크하면 바로 직전에 나온 대사(쌍)는 다음에 다시 고르지 않습니다(목록이 2개 이상일 때).")] private bool noImmediateRepeat = true;
        public DialogueExchange[] Spotted => spotted ?? Array.Empty<DialogueExchange>();
        public string[] Lost => lost ?? Array.Empty<string>();
        public bool NoImmediateRepeat => noImmediateRepeat;
        /// <summary>코드·테스트에서 내용을 채운다.</summary>
        public void Set(DialogueExchange[] spottedLines, string[] lostLines, bool avoidRepeat)
        { spotted = spottedLines ?? Array.Empty<DialogueExchange>(); lost = lostLines ?? Array.Empty<string>(); noImmediateRepeat = avoidRepeat; }
    }
}
