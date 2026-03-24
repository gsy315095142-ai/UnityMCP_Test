using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 使物体围绕指定目标沿Z轴旋转
    /// 适用于位于世界坐标(0,0,8)的Cube预制体，围绕ActionComponentPrefab旋转
    /// </summary>
    public class OrbitAroundTargetZAxis : MonoBehaviour
    {
        #region Fields
        [Tooltip("旋转中心目标（ActionComponentPrefab），请在Inspector中赋值")]
        [SerializeField] private Transform target;
        
        [Tooltip("旋转速度（度/秒），默认值较慢")]
        [SerializeField] private float rotationSpeed = 20f;
        #endregion

        #region Unity Methods
        private void Update()
        {
            if (target == null)
            {
                Debug.LogWarning("[OrbitAroundTargetZAxis] 未指定旋转目标，请设置ActionComponentPrefab");
                return;
            }
            
            // 围绕目标沿世界Z轴旋转（基于Z轴）
            transform.RotateAround(target.position, Vector3.forward, rotationSpeed * Time.deltaTime);
        }
        #endregion
    }
}