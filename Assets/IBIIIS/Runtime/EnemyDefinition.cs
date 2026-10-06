using System;
using UnityEngine;

namespace IBIIIS
{
    [DisallowMultipleComponent]
    public sealed class EnemyDefinition : MonoBehaviour
    {
        [SerializeField] private string enemyId = "Enemy001";
        [SerializeField, Tooltip("플레이어 행동 한 번마다 위에서부터 순서대로 수행하는 행동. 비어 있으면 이전 설정(인식 후 조준 → Move Cells만큼 전진)으로 동작합니다.")]
        private EnemyActionStep[] actions;
        // 이전 데이터 호환용. actions가 비어 있을 때만 읽는다.
        [SerializeField, HideInInspector] private int moveCells = 1;
        [SerializeField, Tooltip("로컬 오프셋: X=오른쪽, Y=앞. 기본 001/002는 전방 반원 맨해튼 2칸.")]
        private Vector2Int[] recognition = { new Vector2Int(-2,0), new Vector2Int(-1,0), Vector2Int.zero, new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(0,2) };
        [SerializeField, Tooltip("도착 칸 기준. X=오른쪽, Y=앞.")] private Vector2Int[] attack = { Vector2Int.zero };
        [SerializeField] private Vector2Int[] recognizedAttack = { Vector2Int.zero, Vector2Int.up };
        [SerializeField] private Color editorColor = new Color(1f, .5f, .3f);
        [SerializeField, Tooltip("선택. 카메라를 향하게 할 스프라이트/평면 외형의 자식 Transform.")] private Transform visual;
        public string Id => enemyId;
        public bool UsesLegacyActions => actions == null || actions.Length == 0;
        public int LegacyMoveCells => moveCells;
        /// <summary>실행에 쓰는 행동 목록의 복사본. actions가 비어 있으면 이전 설정에서 만든 [조준, 전진]을 반환한다.</summary>
        public EnemyActionStep[] Actions => UsesLegacyActions ? new[] { EnemyActionStep.Aim(), EnemyActionStep.Move(moveCells) } : (EnemyActionStep[])actions.Clone();
        public int MoveCells { get { int total = 0; foreach (var step in Actions) if (step.Type == EnemyActionType.MoveForward) total += step.Cells; return total; } }
        private bool ActionsValid { get { foreach (var step in Actions) if (!step.IsValid) return false; return true; } }
        public Vector2Int[] Recognition => recognition == null ? null : (Vector2Int[])recognition.Clone();
        public Vector2Int[] Attack => attack == null ? null : (Vector2Int[])attack.Clone();
        public Vector2Int[] RecognizedAttack => recognizedAttack == null ? null : (Vector2Int[])recognizedAttack.Clone();
        public Color EditorColor => editorColor;
        public bool IsValid => !string.IsNullOrEmpty(enemyId) && ActionsValid && recognition != null && attack != null && recognizedAttack != null;
        /// <summary>카메라를 향하는 외형 자식. 없으면 null.</summary>
        public Transform Visual => visual != null && visual != transform ? visual : null;
        public void FaceCamera(Camera camera) { if (visual != null && visual != transform && camera != null) CameraFacingSprite.Face(visual, camera); }
    }
    [Serializable]
    public sealed class EnemyPlacement
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector2Int position;
        [SerializeField] private Vector2Int direction;
        public GameObject Prefab => prefab;
        public Vector2Int Position => position;
        public Vector2Int Direction => direction;
        public EnemyPlacement(GameObject value, Vector2Int cell, Vector2Int facing) { prefab = value; position = cell; direction = facing; }
    }
}