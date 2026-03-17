using UnityEngine;

namespace UnityMCP.Generated
{
    /// <summary>
    /// 玩家角色移动控制脚本
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMovementController : MonoBehaviour
    {
        #region 序列化字段
        [SerializeField, Tooltip("基础移动速度")]
        private float baseSpeed = 5f;

        [SerializeField, Tooltip("加速度值")]
        private float acceleration = 10f;

        [SerializeField, Tooltip("最大移动速度")]
        private float maxSpeed = 10f;

        [SerializeField, Tooltip("跳跃力度")]
        private float jumpForce = 7f;

        [SerializeField, Tooltip("地面检测距离")]
        private float groundCheckDistance = 0.2f;

        [SerializeField, Tooltip("地面检测层")]
        private LayerMask groundLayer;
        #endregion

        #region 私有变量
        private Rigidbody _rigidbody;
        private Vector3 _moveDirection;
        private bool _isGrounded;
        #endregion

        /// <summary>
        /// 是否处于地面状态
        /// </summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>
        /// 初始化组件引用
        /// </summary>
        private void Start()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                Debug.LogError("Rigidbody组件不存在，无法进行物理移动控制");
                enabled = false;
            }
        }

        /// <summary>
        /// 每帧处理输入和地面检测
        /// </summary>
        private void Update()
        {
            _isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
            HandleInput();
        }

        /// <summary>
        /// 物理固定更新中的移动逻辑
        /// </summary>
        private void FixedUpdate()
        {
            ApplyMovement();
        }

        /// <summary>
        /// 处理玩家输入并计算移动方向
        /// </summary>
        private void HandleInput()
        {
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            Vector3 cameraDirection = Camera.main.transform.forward;
            Vector3 rightDirection = Camera.main.transform.right;

            _moveDirection = (horizontal * rightDirection + vertical * cameraDirection).normalized;
        }

        /// <summary>
        /// 应用移动物理效果
        /// </summary>
        private void ApplyMovement()
        {
            if (_rigidbody == null) return;

            float targetSpeed = baseSpeed * Mathf.Clamp01(_moveDirection.magnitude);
            Vector3 moveVelocity = _moveDirection * targetSpeed;

            _rigidbody.velocity = new Vector3(
                moveVelocity.x,
                _isGrounded ? 0f : _rigidbody.velocity.y,
                moveVelocity.z
            );

            if (_isGrounded && Input.GetButtonDown("Jump"))
            {
                Jump();
            }
        }

        /// <summary>
        /// 执行跳跃动作
        /// </summary>
        private void Jump()
        {
            if (_rigidbody == null) return;

            _rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }
}