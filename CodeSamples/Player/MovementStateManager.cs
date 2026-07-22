using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerInput))]
public class MovementStateManager : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 3f;
    public float runSpeed = 7f;

    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float rotationSpeed = 25f;

    [Header("Turn In Place Settings")]
    [SerializeField] private float turnThreshold = 0.1f;
    [SerializeField] private float turnAnimSmoothing = 30f;
    [SerializeField] private float minAnimSpeed = 1f;

    [Header("Jump Settings")]
    public float jumpForce = 6f;

    [SerializeField] private float fallMultiplier = 2.5f;
    [SerializeField] private float coyoteTime = 0.15f;

    [Header("Fall Animation Settings")]
    [Tooltip("Мінімальна висота падіння для запуску анімації JumpLoop.")]
    [SerializeField] private float minimumFallHeight = 0.35f;

    [Tooltip("Швидкість руху вниз, після якої кіт вважається таким, що падає.")]
    [SerializeField] private float fallVelocityThreshold = -1.5f;

    [Header("Ground Check & Slope")]
    [SerializeField] private Vector3 boxSize =
        new Vector3(0.35f, 0.08f, 0.65f);

    [SerializeField] private float groundCheckDistance = 0.35f;
    [SerializeField] private float boxOffsetY = 0.28f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float maxSlopeAngle = 55f;
    [SerializeField] private float groundStickForce = 25f;

    [Header("Visual Slope Alignment")]
    [Tooltip(
        "Об'єкт із візуальною моделлю кота. " +
        "Rigidbody та Collider не повинні бути на цьому об'єкті."
    )]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Швидкість плавного нахилу моделі відповідно до схилу.")]
    [SerializeField] private float slopeTiltSpeed = 10f;

    [Tooltip("Максимальний кут, під який може нахилятися візуальна модель.")]
    [SerializeField] private float maxVisualSlopeAngle = 55f;

    public bool IsGrounded => _isGrounded;
    public bool IsJumpLocked => _jumpLockTimer > 0f;

    private Rigidbody _rb;
    private Animator _anim;
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private Transform _cameraTransform;
    private InventoryManager _inventory;

    private bool _isGrounded;
    private float _groundedBufferTimer;
    private float _coyoteTimeCounter;

    private Vector3 _moveDirection;
    private Vector3 _surfaceNormal = Vector3.up;

    private bool _isShiftPressed;
    private float _jumpLockTimer;

    private float _currentAnimSpeed;
    private float _currentTurnValue;
    private float _lastCameraYRotation;

    private float _highestAirPositionY;
    private bool _fallAnimationTriggered;

    private Quaternion _visualInitialLocalRotation;

    private readonly int _speedHash =
        Animator.StringToHash("Speed");

    private readonly int _turnHash =
        Animator.StringToHash("Turn");

    private readonly int _isGroundedHash =
        Animator.StringToHash("isGrounded");

    private readonly int _jumpTriggerHash =
        Animator.StringToHash("jump");

    private readonly int _fallTriggerHash =
        Animator.StringToHash("fall");

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _anim = GetComponentInChildren<Animator>();
        _playerInput = GetComponent<PlayerInput>();
        _inventory = GetComponent<InventoryManager>();

        /*
         * Якщо Visual Root не встановлений вручну,
         * використовуємо дочірній об'єкт з Animator.
         */
        if (visualRoot == null &&
            _anim != null &&
            _anim.transform != transform)
        {
            visualRoot = _anim.transform;
        }

        /*
         * Не можна нахиляти той самий об'єкт,
         * на якому знаходиться Rigidbody.
         */
        if (visualRoot == transform)
        {
            Debug.LogWarning(
                "[MovementStateManager] Visual Root не може бути " +
                "Player-об'єктом із Rigidbody. Створи дочірній " +
                "VisualRoot і перенеси модель кота всередину."
            );

            visualRoot = null;
        }

        if (visualRoot != null)
        {
            _visualInitialLocalRotation =
                visualRoot.localRotation;
        }

        _rb.interpolation =
            RigidbodyInterpolation.Interpolate;

        _rb.collisionDetectionMode =
            CollisionDetectionMode.Continuous;

        /*
         * Фізичне тіло залишається вертикальним.
         * Нахиляється лише візуальна модель.
         */
        _rb.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    private void Start()
    {
        _highestAirPositionY =
            transform.position.y;

        if (Camera.main != null)
        {
            _cameraTransform =
                Camera.main.transform;

            _lastCameraYRotation =
                _cameraTransform.eulerAngles.y;
        }

        RebindActions();
    }

    private void Update()
    {
        if (_moveAction == null ||
            _jumpAction == null)
        {
            return;
        }

        HandleCursor();

        Vector2 input =
            _moveAction.ReadValue<Vector2>();

        _moveDirection =
            CalculateMoveDirection(input);

        _isShiftPressed =
            Keyboard.current != null &&
            Keyboard.current.leftShiftKey.isPressed;

        CheckGrounded();
        HandleFallAnimation();
        HandleJumpInput();
        UpdateAnimParameters(input);
    }

    private void FixedUpdate()
    {
        ApplyMovement();
        HandleRotation();
        ApplyBetterFall();

        if (_moveDirection.sqrMagnitude <= 0.01f)
        {
            _rb.angularVelocity =
                Vector3.zero;
        }

        if (_jumpLockTimer > 0f)
        {
            _jumpLockTimer -=
                Time.fixedDeltaTime;
        }
    }

    private void LateUpdate()
    {
        UpdateVisualSlopeAlignment();
    }

    private void HandleCursor()
    {
        if (Mouse.current == null)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null ||
                !UnityEngine.EventSystems.EventSystem.current
                    .IsPointerOverGameObject())
            {
                Cursor.lockState =
                    CursorLockMode.Locked;

                Cursor.visible = false;
            }
        }
    }

    private void UpdateAnimParameters(Vector2 input)
    {
        if (_anim == null)
            return;

        float targetAnimSpeed =
            input.sqrMagnitude > 0.01f
                ? (_isShiftPressed ? 2f : 1f)
                : 0f;

        _currentAnimSpeed = Mathf.Lerp(
            _currentAnimSpeed,
            targetAnimSpeed,
            Time.deltaTime * 15f
        );

        _anim.SetFloat(
            _speedHash,
            _currentAnimSpeed
        );

        bool looksGrounded =
            _isGrounded &&
            _jumpLockTimer <= 0f &&
            _rb.linearVelocity.y <= 0.25f;

        _anim.SetBool(
            _isGroundedHash,
            looksGrounded
        );

        HandleTurnAnimation(input);
    }

    private void HandleTurnAnimation(Vector2 input)
    {
        float targetTurnValue = 0f;

        bool isCombatActive =
            IsAnyCombatActive();

        bool canTurnInPlace =
            isCombatActive &&
            _isGrounded &&
            input.sqrMagnitude < 0.01f &&
            _cameraTransform != null;

        if (canTurnInPlace)
        {
            float currentCameraY =
                _cameraTransform.eulerAngles.y;

            float deltaAngle =
                Mathf.DeltaAngle(
                    _lastCameraYRotation,
                    currentCameraY
                );

            float cameraRotationSpeed =
                Mathf.Abs(deltaAngle) /
                Mathf.Max(Time.deltaTime, 0.0001f);

            _lastCameraYRotation =
                currentCameraY;

            Vector3 cameraForward =
                Vector3.ProjectOnPlane(
                    _cameraTransform.forward,
                    Vector3.up
                ).normalized;

            float angleDiff =
                Vector3.SignedAngle(
                    transform.forward,
                    cameraForward,
                    Vector3.up
                );

            if (Mathf.Abs(angleDiff) > turnThreshold)
            {
                targetTurnValue =
                    angleDiff > 0f ? 1f : -1f;

                _anim.speed = Mathf.Clamp(
                    minAnimSpeed +
                    cameraRotationSpeed / 50f,
                    1f,
                    5f
                );
            }
            else
            {
                _anim.speed = 1f;
            }
        }
        else
        {
            _anim.speed = 1f;

            if (_cameraTransform != null)
            {
                _lastCameraYRotation =
                    _cameraTransform.eulerAngles.y;
            }
        }

        _currentTurnValue =
            Mathf.MoveTowards(
                _currentTurnValue,
                targetTurnValue,
                Time.deltaTime * turnAnimSmoothing
            );

        _anim.SetFloat(
            _turnHash,
            _currentTurnValue
        );
    }

    private void HandleRotation()
    {
        /*
         * Здесь оставлена исходная логика:
         * поворот точно по направлению камеры нужен
         * только для предмета в первом слоте.
         */
        bool shouldLookAtTarget =
            _inventory != null &&
            _inventory.IsCombatAimActive() &&
            _inventory.GetCurrentSlot() == 1;

        Vector3 lookDirection =
            _moveDirection;

        if (shouldLookAtTarget &&
            _cameraTransform != null)
        {
            lookDirection =
                Vector3.ProjectOnPlane(
                    _cameraTransform.forward,
                    Vector3.up
                ).normalized;
        }

        if (lookDirection.sqrMagnitude <= 0.001f)
            return;

        Vector3 flatLookDirection =
            Vector3.ProjectOnPlane(
                lookDirection,
                Vector3.up
            );

        if (flatLookDirection.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                flatLookDirection,
                Vector3.up
            );

        Quaternion smoothedRotation =
            Quaternion.Slerp(
                _rb.rotation,
                targetRotation,
                rotationSpeed *
                Time.fixedDeltaTime
            );

        _rb.MoveRotation(smoothedRotation);
    }

    private void ApplyMovement()
    {
        float targetSpeed =
            _isShiftPressed
                ? runSpeed
                : walkSpeed;

        bool hasInput =
            _moveDirection.sqrMagnitude > 0.01f;

        Vector3 velocity =
            _rb.linearVelocity;

        Vector3 horizontalVelocity =
            new Vector3(
                velocity.x,
                0f,
                velocity.z
            );

        if (!hasInput)
        {
            horizontalVelocity =
                Vector3.Lerp(
                    horizontalVelocity,
                    Vector3.zero,
                    12f * Time.fixedDeltaTime
                );

            _rb.linearVelocity =
                new Vector3(
                    horizontalVelocity.x,
                    velocity.y,
                    horizontalVelocity.z
                );

            if (_isGrounded)
            {
                _rb.AddForce(
                    Vector3.down *
                    groundStickForce,
                    ForceMode.Acceleration
                );
            }

            return;
        }

        Vector3 moveAlongSurface =
            _moveDirection;

        if (_isGrounded)
        {
            moveAlongSurface =
                Vector3.ProjectOnPlane(
                    _moveDirection,
                    _surfaceNormal
                ).normalized;
        }

        Vector3 targetVelocity =
            moveAlongSurface * targetSpeed;

        float control =
            _isGrounded ? 1f : 0.35f;

        Vector3 currentVelocityForLerp =
            _isGrounded
                ? Vector3.ProjectOnPlane(
                    velocity,
                    _surfaceNormal
                )
                : horizontalVelocity;

        Vector3 finalVelocity =
            Vector3.Lerp(
                currentVelocityForLerp,
                targetVelocity,
                acceleration *
                control *
                Time.fixedDeltaTime
            );

        if (_isGrounded)
        {
            _rb.linearVelocity =
                finalVelocity;

            _rb.AddForce(
                Vector3.down *
                groundStickForce,
                ForceMode.Acceleration
            );
        }
        else
        {
            _rb.linearVelocity =
                new Vector3(
                    finalVelocity.x,
                    velocity.y,
                    finalVelocity.z
                );
        }
    }

    private void CheckGrounded()
    {
        if (_jumpLockTimer > 0f)
        {
            _isGrounded = false;
            _surfaceNormal = Vector3.up;
            _groundedBufferTimer = 0f;
            return;
        }

        Vector3 origin =
            transform.position +
            Vector3.up * boxOffsetY;

        bool hitDetected =
            Physics.BoxCast(
                origin,
                boxSize / 2f,
                Vector3.down,
                out RaycastHit hit,
                transform.rotation,
                groundCheckDistance,
                groundMask
            );

        bool validGround = false;

        if (hitDetected)
        {
            float slopeAngle =
                Vector3.Angle(
                    Vector3.up,
                    hit.normal
                );

            if (slopeAngle <= maxSlopeAngle)
            {
                validGround = true;

                _surfaceNormal =
                    hit.normal.normalized;

                _groundedBufferTimer =
                    0.18f;
            }
        }

        _groundedBufferTimer -=
            Time.deltaTime;

        _isGrounded =
            validGround ||
            _groundedBufferTimer > 0f;

        if (!_isGrounded)
        {
            _surfaceNormal =
                Vector3.up;
        }
    }

    private bool IsAnyCombatActive()
    {
        /*
         * Проверяет общий боевой режим InventoryManager.
         * Номер активного слота здесь специально не проверяется.
         *
         * Поэтому наклон отключается для любого оружия
         * или предмета, который включает Combat Aim.
         */
        return
            _inventory != null &&
            _inventory.IsCombatAimActive();
    }

    private void UpdateVisualSlopeAlignment()
    {
        if (visualRoot == null)
            return;

        bool isCombatActive =
            IsAnyCombatActive();

        /*
         * Исходный поворот модели означает
         * обычное вертикальное положение кота.
         */
        Quaternion targetLocalRotation =
            _visualInitialLocalRotation;

        /*
         * Наклон по поверхности разрешён только когда:
         *
         * 1. кот находится на земле;
         * 2. общий Combat Aim выключен.
         *
         * Активный слот инвентаря значения не имеет.
         */
        bool canUseSlopeAlignment =
            _isGrounded &&
            !isCombatActive;

        if (canUseSlopeAlignment)
        {
            float slopeAngle =
                Vector3.Angle(
                    Vector3.up,
                    _surfaceNormal
                );

            float allowedVisualAngle =
                Mathf.Min(
                    maxVisualSlopeAngle,
                    maxSlopeAngle
                );

            if (slopeAngle <= allowedVisualAngle)
            {
                /*
                 * localRotation задаётся относительно parent,
                 * поэтому переводим нормаль поверхности
                 * в локальные координаты родителя VisualRoot.
                 */
                Transform visualParent =
                    visualRoot.parent;

                Vector3 localSurfaceNormal =
                    visualParent != null
                        ? visualParent.InverseTransformDirection(
                            _surfaceNormal
                        )
                        : _surfaceNormal;

                localSurfaceNormal.Normalize();

                /*
                 * Учитываем исходный импортированный
                 * поворот визуальной модели.
                 */
                Vector3 initialVisualUp =
                    _visualInitialLocalRotation *
                    Vector3.up;

                Quaternion slopeRotation =
                    Quaternion.FromToRotation(
                        initialVisualUp,
                        localSurfaceNormal
                    );

                targetLocalRotation =
                    slopeRotation *
                    _visualInitialLocalRotation;
            }
        }

        /*
         * В Combat, в прыжке или в падении
         * модель плавно возвращается
         * в исходное вертикальное положение.
         */
        float smoothing =
            1f - Mathf.Exp(
                -slopeTiltSpeed *
                Time.deltaTime
            );

        visualRoot.localRotation =
            Quaternion.Slerp(
                visualRoot.localRotation,
                targetLocalRotation,
                smoothing
            );
    }

    private void HandleFallAnimation()
    {
        if (_anim == null)
            return;

        if (_isGrounded)
        {
            _highestAirPositionY =
                transform.position.y;

            _fallAnimationTriggered =
                false;

            _anim.ResetTrigger(
                _fallTriggerHash
            );

            return;
        }

        _highestAirPositionY =
            Mathf.Max(
                _highestAirPositionY,
                transform.position.y
            );

        float fallenDistance =
            _highestAirPositionY -
            transform.position.y;

        bool isMovingDown =
            _rb.linearVelocity.y <=
            fallVelocityThreshold;

        bool hasFallenFarEnough =
            fallenDistance >=
            minimumFallHeight;

        if (!_fallAnimationTriggered &&
            _jumpLockTimer <= 0f &&
            isMovingDown &&
            hasFallenFarEnough)
        {
            _anim.ResetTrigger(
                _jumpTriggerHash
            );

            _anim.SetBool(
                _isGroundedHash,
                false
            );

            _anim.SetTrigger(
                _fallTriggerHash
            );

            _fallAnimationTriggered =
                true;
        }
    }

    private void HandleJumpInput()
    {
        if (_isGrounded)
        {
            _coyoteTimeCounter =
                coyoteTime;
        }
        else
        {
            _coyoteTimeCounter -=
                Time.deltaTime;
        }

        if (_jumpAction.triggered &&
            _coyoteTimeCounter > 0f)
        {
            bool validGround =
                Vector3.Angle(
                    Vector3.up,
                    _surfaceNormal
                ) <= maxSlopeAngle;

            if (_isGrounded && validGround)
            {
                HandleJump();
            }
        }
    }

    private void HandleJump()
    {
        Vector3 velocity =
            _rb.linearVelocity;

        velocity.y = 0f;

        _rb.linearVelocity =
            velocity;

        _rb.AddForce(
            Vector3.up * jumpForce,
            ForceMode.VelocityChange
        );

        _jumpLockTimer = 0.35f;
        _isGrounded = false;
        _groundedBufferTimer = 0f;

        _highestAirPositionY =
            transform.position.y;

        /*
         * При звичайному стрибку JumpStart
         * сам переходить у JumpLoop.
         */
        _fallAnimationTriggered = true;

        if (_anim != null)
        {
            _anim.ResetTrigger(
                _fallTriggerHash
            );

            _anim.SetBool(
                _isGroundedHash,
                false
            );

            _anim.SetTrigger(
                _jumpTriggerHash
            );
        }
    }

    private void ApplyBetterFall()
    {
        if (_rb.linearVelocity.y < 0f &&
            !_isGrounded)
        {
            _rb.AddForce(
                Vector3.down *
                (fallMultiplier * 9.81f),
                ForceMode.Acceleration
            );
        }
    }

    private Vector3 CalculateMoveDirection(
        Vector2 input
    )
    {
        if (_cameraTransform == null)
            return Vector3.zero;

        Vector3 forward =
            Vector3.ProjectOnPlane(
                _cameraTransform.forward,
                Vector3.up
            ).normalized;

        Vector3 right =
            Vector3.ProjectOnPlane(
                _cameraTransform.right,
                Vector3.up
            ).normalized;

        return (
            forward * input.y +
            right * input.x
        ).normalized;
    }

    public void RebindActions()
    {
        if (_playerInput == null)
            return;

        _moveAction =
            _playerInput.actions
                .FindAction("Move");

        _jumpAction =
            _playerInput.actions
                .FindAction("Jump");

        _moveAction?.Enable();
        _jumpAction?.Enable();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color =
            _isGrounded
                ? Color.green
                : Color.red;

        Vector3 origin =
            transform.position +
            Vector3.up * boxOffsetY;

        Gizmos.DrawWireCube(
            origin +
            Vector3.down *
            groundCheckDistance,
            boxSize
        );

        Gizmos.color = Color.cyan;

        Gizmos.DrawRay(
            transform.position,
            _surfaceNormal
        );
    }
}
