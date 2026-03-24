using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 物体绕世界坐标点圆周运动的控制器
    /// </summary>
    public class WorldCoordinateOrbiter : MonoBehaviour
    {
        #region Fields
        [SerializeField] private Vector3 orbitCenter = new Vector3(0f, 0f, 1f);
        [SerializeField] private float orbitRadius = 5f;
        [SerializeField] private float rotationSpeed = 60f;
        [SerializeField] private bool autoStartOnEnable = true;

        private float _currentAngle = 0f;
        #endregion

        #region Unity Methods
        /// <summary>
        /// Awake 初始化
        /// </summary>
        private void Awake()
        {
            _currentAngle = 0f;
        }

        /// <summary>
        /// OnEnable 启用时检查是否自动启动
        /// </summary>
        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                // 预计算初始位置
                UpdateOrbitPosition();
            }
        }

        /// <summary>
        /// Start 场景启动后初始化
        /// </summary>
        private void Start()
        {
            transform.position = GetOrbitPosition(_currentAngle);
        }

        /// <summary>
        /// Update 每帧更新圆周运动位置
        /// </summary>
        private void Update()
        {
            if (autoStartOnEnable)
            {
                _currentAngle += rotationSpeed * Time.deltaTime;
                UpdateOrbitPosition();
            }
        }

        /// <summary>
        /// OnDisable 禁用时保存状态（可选）
        /// </summary>
        private void OnDisable()
        {
            // 可在此处添加清理逻辑
        }

        /// <summary>
        /// OnDestroy 销毁前清理资源
        /// </summary>
        private void OnDestroy()
        {
            // 无需要清理的资源
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 获取当前轨道位置的世界坐标
        /// </summary>
        public Vector3 GetOrbitPosition(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            float x = orbitCenter.x + Mathf.Cos(radians) * orbitRadius;
            float y = orbitCenter.y + Mathf.Sin(radians) * orbitRadius;
            return new Vector3(x, y, orbitCenter.z);
        }

        /// <summary>
        /// 设置轨道中心点
        /// </summary>
        public void SetOrbitCenter(Vector3 center)
        {
            orbitCenter = center;
        }

        /// <summary>
        /// 设置轨道半径
        /// </summary>
        public void SetOrbitRadius(float radius)
        {
            orbitRadius = Mathf.Max(0.1f, radius);
        }

        /// <summary>
        /// 设置旋转速度
        /// </summary>
        public void SetRotationSpeed(float speed)
        {
            rotationSpeed = speed;
        }

        /// <summary>
        /// 暂停/继续圆周运动
        /// </summary>
        public void ToggleOrbit(bool enable)
        {
            autoStartOnEnable = enable;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// 更新物体的轨道位置
        /// </summary>
        private void UpdateOrbitPosition()
        {
            transform.position = GetOrbitPosition(_currentAngle);
        }
        #endregion
    }
}