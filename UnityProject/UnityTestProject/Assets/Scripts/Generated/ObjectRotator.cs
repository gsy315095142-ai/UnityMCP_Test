using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 物体旋转控制器脚本
    /// 用于控制游戏对象在VR环境中的平滑旋转动画
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class ObjectRotator : MonoBehaviour
    {
        #region Serialized Fields
        [SerializeField, Tooltip("旋转速度（度/秒）")]
        private float rotationSpeed = 1.0f;

        [SerializeField, Tooltip("旋转轴方向")]
        private Vector3 rotationAxis = Vector3.up;

        [SerializeField, Tooltip("是否连续旋转")]
        private bool isContinuousRotation = true;

        [SerializeField, Tooltip("初始旋转角度")]
        private Vector3 initialRotationAngle = new Vector3(0f, 0f, 0f);

        [SerializeField, Range(0.0f, 1.0f), Tooltip("旋转平滑度")]
        private float rotationSmoothness = 0.5f;

        [SerializeField, Tooltip("启用时是否立即开始旋转")]
        private bool startOnEnable = true;
        #endregion

        #region Private Fields
        private Quaternion targetRotation;
        private bool isRotating;
        #endregion

        /// <summary>
        /// 获取当前旋转状态
        /// </summary>
        public bool IsRotating => isRotating;

        /// <summary>
        /// 启动或停止旋转控制
        /// </summary>
        public void ToggleRotation()
        {
            isRotating = !isRotating;
            targetRotation = Quaternion.Euler(transform.localEulerAngles);
        }

        /// <summary>
        /// 设置旋转速度
        /// </summary>
        /// <param name="speed">新的旋转速度（度/秒）</param>
        public void SetRotationSpeed(float speed)
        {
            rotationSpeed = Mathf.Max(0f, speed);
        }

        /// <summary>
        /// 设置旋转轴方向
        /// </summary>
        /// <param name="axis">新的旋转轴向量</param>
        public void SetRotationAxis(Vector3 axis)
        {
            rotationAxis = Vector3.Normalize(axis);
        }

        private void Awake()
        {
            targetRotation = Quaternion.Euler(initialRotationAngle);
            transform.localEulerAngles = initialRotationAngle;
        }

        private void OnEnable()
        {
            if (startOnEnable)
            {
                isRotating = true;
            }
        }

        private void Update()
        {
            if (!isRotating || !Application.isPlaying) return;

            float deltaRotation = rotationSpeed * Time.deltaTime * rotationSmoothness;
            Quaternion targetDelta = Quaternion.AngleAxis(deltaRotation, rotationAxis);
            transform.localRotation = Quaternion.Lerp(
                transform.localRotation, 
                targetRotation * targetDelta, 
                rotationSmoothness
            );

            if (!isContinuousRotation && Mathf.Approximately(transform.localEulerAngles.x, initialRotationAngle.x) 
                && Mathf.Approximately(transform.localEulerAngles.y, initialRotationAngle.y) 
                && Mathf.Approximately(transform.localEulerAngles.z, initialRotationAngle.z))
            {
                isRotating = false;
            }
        }

        private void OnDisable()
        {
            if (isRotating)
            {
                targetRotation = transform.localRotation;
            }
        }

        private void OnDestroy()
        {
            targetRotation = default;
        }
    }
}