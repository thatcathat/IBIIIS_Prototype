using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Map Camera Settings")]
    public sealed class MapCameraSettings : ScriptableObject
    {
        [SerializeField, Tooltip("켜면 Orthographic, 끄면 Perspective")] private bool orthographic;
        [SerializeField, Tooltip("카메라 회전(도). Y=0은 맵 정면, X는 내려다보는 기울기입니다.")] private Vector3 rotation = new Vector3(55, 0, 0);
        [SerializeField, Range(10, 100), Tooltip("Perspective 수직 화각(도)")] private float fieldOfView = 60;
        public void Apply(Camera camera)
        {
            camera.orthographic = orthographic;
            camera.transform.rotation = Quaternion.Euler(rotation);
            camera.fieldOfView = fieldOfView;
        }
    }
}
