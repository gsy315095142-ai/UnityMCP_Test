using UnityEngine;
using UnityEngine.UI;

namespace UnityMCP.Generated
{
    /// <summary>
    /// UI背景颜色设置器，用于在启动时设置Image组件的背景颜色
    /// </summary>
    public class UIBackgroundSetter : MonoBehaviour
    {
        #region Fields
        [SerializeField] private Image targetImage;
        [SerializeField] private Color backgroundColor = Color.white;
        #endregion

        #region Unity Methods
        private void Start()
        {
            ApplyBackgroundColor();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// 应用背景颜色到目标Image组件
        /// </summary>
        private void ApplyBackgroundColor()
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }

            if (targetImage != null)
            {
                targetImage.color = backgroundColor;
            }
            else
            {
                Debug.LogWarning($"[{nameof(UIBackgroundSetter)}] 未找到Image组件，请确保物体上有Image组件或手动指定Target Image。", this);
            }
        }
        #endregion
    }
}