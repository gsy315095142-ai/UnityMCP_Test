using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 测试对象行为脚本，用于 obj_Test 预制体
    /// </summary>
    public class ObjTest : MonoBehaviour
    {
        #region 私有字段

        [SerializeField] private Color _initialColor = Color.white;
        [SerializeField] private float _interactionRadius = 2f;
        [SerializeField] private bool _isInteractive = true;
        [SerializeField] private GameObject _visualFeedbackObject;

        private Renderer _cachedRenderer;
        private Collider _cachedCollider;
        private bool _isHovered = false;

        #endregion

        #region 生命周期

        /// <summary>
        /// 初始化缓存组件引用
        /// </summary>
        private void Awake()
        {
            _cachedRenderer = GetComponent<Renderer>();
            if (_cachedRenderer == null)
            {
                Debug.LogWarning("ObjTest: 未找到 Renderer 组件", this);
            }

            _cachedCollider = GetComponent<Collider>();
            if (_cachedCollider != null && !_isInteractive)
            {
                _cachedCollider.enabled = false;
            }

            ApplyInitialColor();
        }

        /// <summary>
        /// 每帧更新逻辑，用于处理交互状态
        /// </summary>
        private void Update()
        {
            if (!_isInteractive) return;

            CheckInteractionState();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置对象的初始颜色
        /// </summary>
        /// <param name="color">要设置的渲染器颜色</param>
        public void SetColor(Color color)
        {
            if (_cachedRenderer != null)
            {
                _cachedRenderer.material.color = color;
            }
        }

        /// <summary>
        /// 切换对象的交互状态
        /// </summary>
        public void ToggleInteraction()
        {
            _isInteractive = !_isInteractive;
            
            if (_cachedCollider != null)
            {
                _cachedCollider.enabled = _isInteractive;
            }
        }

        /// <summary>
        /// 执行交互反馈效果
        /// </summary>
        public void OnInteract()
        {
            if (!_isInteractive) return;

            // 简单的视觉反馈逻辑
            SetColor(_initialColor * 1.5f);
            
            if (_visualFeedbackObject != null)
            {
                _visualFeedbackObject.SetActive(true);
                Invoke(nameof(ResetVisualFeedback), 0.5f);
            }

            Debug.Log("ObjTest: 交互触发");
        }

        /// <summary>
        /// 重置视觉反馈效果
        /// </summary>
        public void ResetVisualFeedback()
        {
            SetColor(_initialColor);
            if (_visualFeedbackObject != null)
            {
                _visualFeedbackObject.SetActive(false);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 检查交互状态（悬停检测）
        /// </summary>
        private void CheckInteractionState()
        {
            if (Physics.CheckSphere(transform.position, _interactionRadius))
            {
                if (!_isHovered)
                {
                    _isHovered = true;
                    OnEnterHover();
                }
            }
            else
            {
                if (_isHovered)
                {
                    _isHovered = false;
                    OnExitHover();
                }
            }
        }

        /// <summary>
        /// 进入悬停状态时的回调
        /// </summary>
        private void OnEnterHover()
        {
            SetColor(_initialColor * 0.8f); // 变暗表示选中
        }

        /// <summary>
        /// 退出悬停状态时的回调
        /// </summary>
        private void OnExitHover()
        {
            SetColor(_initialColor);
        }

        /// <summary>
        /// 应用初始颜色设置
        /// </summary>
        private void ApplyInitialColor()
        {
            if (_cachedRenderer != null)
            {
                _cachedRenderer.material.color = _initialColor;
            }
        }

        #endregion
    }
}