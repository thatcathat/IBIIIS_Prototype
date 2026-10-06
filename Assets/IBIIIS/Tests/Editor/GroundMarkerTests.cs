using NUnit.Framework;
using IBIIIS.Editor;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    public sealed class GroundMarkerTests
    {
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        [Test] public void EnemyPrefabsHaveShadowAndFacingArrowThatPointsForward()
        {
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { AssetPaths.Enemies }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<EnemyDefinition>() == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var arrow = instance.transform.Find(GroundMarkerSetup.ArrowName); var shadow = instance.transform.Find(GroundMarkerSetup.ShadowName);
                    Assert.NotNull(arrow, prefab.name + " 화살표"); Assert.NotNull(shadow, prefab.name + " 그림자");
                    Assert.Null(instance.transform.Find("Direction"), prefab.name + " 이전 방향 선은 화살표로 대체");
                    foreach (var d in Directions)
                    {
                        instance.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, 0, d.y));
                        Assert.That(Vector3.Distance(arrow.up, new Vector3(d.x, 0, d.y)), Is.LessThan(1e-4f), $"{prefab.name} {d}");
                    }
                    Assert.Less(shadow.GetComponent<SpriteRenderer>().sortingOrder, 0); Assert.Greater(arrow.GetComponent<SpriteRenderer>().sortingOrder, 0);
                    checkedCount++;
                }
                finally { Object.DestroyImmediate(instance); }
            }
            Assert.Greater(checkedCount, 0);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabSetup.PrefabPath);
            Assert.NotNull(player.transform.Find(GroundMarkerSetup.ShadowName), "플레이어 그림자");
        }
    }
}
