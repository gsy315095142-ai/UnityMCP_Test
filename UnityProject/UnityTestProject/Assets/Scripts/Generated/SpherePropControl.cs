using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 球体属性控制器，用于控制球状预制体的各种属性
    /// </summary>
    public class SpherePropControl : MonoBehaviour
    {
        #region Fields

        [SerializeField] private float rotationSpeed = 30f;
        [SerializeField] private bool isSpinning = true;
        [SerializeField] private Color sphereColor = new Color(1f, 0.5f, 0f);
        [SerializeField] private Renderer sphereRenderer;

        #endregion

        #region Unity Methods

        /// <summary>
        /// 在组件激活时初始化渲染器
        /// </summary>
        private void Awake()
        {
            if (sphereRenderer == null)
            {
                sphereRenderer = GetComponent<Renderer>();
            }
            ApplyColor();
        }

        /// <summary>
        /// 每帧更新球体旋转效果
        /// </summary>
        private void Update()
        {
            if (isSpinning && rotationSpeed > 0f)
            {
                transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 在编辑器模式下设置球体颜色
        /// </summary>
        private void ApplyColor()
        {
            if (sphereRenderer != null)
            {
                sphereRenderer.material.color = sphereColor;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 切换球体旋转状态
        /// </summary>
        public void ToggleRotation()
        {
            isSpinning = !isSpinning;
        }

        /// <summary>
        /// 设置球体颜色
        /// </summary>
        /// <param name="color">要应用的颜色</param>
        public void SetSphereColor(Color color)
        {
            sphereColor = color;
            ApplyColor();
        }

        #endregion
    }
}