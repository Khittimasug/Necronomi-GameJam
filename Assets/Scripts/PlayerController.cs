using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Rigidbody driven 3D walker on the XZ plane with a jump. Horizontal speed is
/// smoothed with an acceleration ramp and written straight to the body, so the
/// physics engine keeps resolving walls and ground while gravity stays authoritative
/// on the vertical axis.
/// Input is sampled in Update, the body is moved in FixedUpdate.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [Tooltip("Units per second squared while speeding up")]
    [SerializeField, Min(0f)] private float acceleration = 40f;
    [Tooltip("Units per second squared while slowing down. Ignored when Instant Stop is on")]
    [SerializeField, Min(0f)] private float deceleration = 60f;
    [Tooltip("Drop to zero speed the frame input is released, instead of easing out. Ground only")]
    [SerializeField] private bool instantStop = true;
    [Tooltip("Multiplier on the acceleration ramp while airborne. 0 is no air control, 1 is full")]
    [SerializeField, Range(0f, 1f)] private float airControl = 0.6f;

    [Header("Jump")]
    [SerializeField] private bool jumpEnabled = true;
    [Tooltip("Peak height in world units, reached when the button is held")]
    [SerializeField, Min(0f)] private float jumpHeight = 2f;
    [Tooltip("Grace period after walking off a ledge where a jump still counts")]
    [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
    [Tooltip("A press this long before landing still fires on touchdown")]
    [SerializeField, Min(0f)] private float jumpBuffer = 0.12f;
    [Tooltip("Releasing the button early cuts the rise short")]
    [SerializeField] private bool variableJumpHeight = true;
    [Tooltip("Extra gravity while rising with the button released")]
    [SerializeField, Min(1f)] private float lowJumpMultiplier = 2.5f;
    [Tooltip("Extra gravity while falling. 1 is plain physics")]
    [SerializeField, Min(1f)] private float fallMultiplier = 2f;
    [Tooltip("Fall speed cap, keeps the collider from tunnelling")]
    [SerializeField, Min(1f)] private float terminalVelocity = 40f;

    [Header("Ground Check")]
    [Tooltip("Layers that count as standing ground")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("How far below the collider the ground probe reaches")]
    [SerializeField, Min(0.01f)] private float groundProbeDepth = 0.15f;
    [Tooltip("Shrinks the probe box against the collider footprint, so it does not catch walls")]
    [SerializeField, Range(0.1f, 1f)] private float groundProbeInset = 0.9f;

    [Header("Direction")]
    [Tooltip("Move relative to the camera instead of world axes")]
    [SerializeField] private bool cameraRelative = true;
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private SpriteRenderer spriteToFlip;
    [SerializeField] private bool flipSpriteByDirection = true;

    /// <summary>Set false to freeze the player (used while a dialogue plays).</summary>
    public bool MovementEnabled { get; private set; } = true;

    public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
    /// <summary>Horizontal speed only, gravity excluded.</summary>
    public float PlanarSpeed => planarSpeed;
    /// <summary>Horizontal speed as a 0..1 fraction of Move Speed. Handy for a blend tree.</summary>
    public float NormalizedSpeed => moveSpeed > 0.0001f ? Mathf.Clamp01(planarSpeed / moveSpeed) : 0f;
    public float VerticalVelocity => body != null ? body.linearVelocity.y : 0f;
    public bool IsMoving => planarSpeed > 0.01f;
    public bool IsGrounded => grounded;
    /// <summary>True while rising or falling with no ground under the feet.</summary>
    public bool IsAirborne => !grounded;
    /// <summary>Last non zero facing on the XZ plane. Never zero.</summary>
    public Vector3 Facing { get; private set; } = Vector3.forward;

    /// <summary>Fires the physics step the jump impulse is applied. Hook the animation here.</summary>
    public event Action Jumped;
    /// <summary>Fires the physics step the feet find ground again. Carries the impact speed.</summary>
    public event Action<float> Landed;

    private Rigidbody body;
    private Collider bodyCollider;
    private Transform cachedTransform;
    private Transform cameraTransform;

    private Vector3 moveDirection;
    private float inputMagnitude;
    private float planarSpeed;

    private bool grounded;
    private bool jumpHeld;
    private float jumpBufferTimer;
    private float coyoteTimer;

    private void Awake()
    {
        cachedTransform = transform;
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();

        // A sprite walker must never tip over, and the visuals are interpolated
        // because the body only moves on the fixed step.
        body.constraints |= RigidbodyConstraints.FreezeRotation;
        body.useGravity = true;
        body.isKinematic = false;

        if (body.interpolation == RigidbodyInterpolation.None)
            body.interpolation = RigidbodyInterpolation.Interpolate;

        if (bodyCollider == null)
            Debug.LogError("PlayerController needs a Collider on the same object for the ground check.", this);

        CacheCamera();
    }

    private void Update()
    {
        Vector2 input = MovementEnabled ? ReadMoveInput() : Vector2.zero;

        inputMagnitude = input.magnitude;
        moveDirection = InputToWorld(input);

        bool jumpDown = jumpEnabled && MovementEnabled && ReadJumpDown();
        jumpHeld = jumpEnabled && MovementEnabled && ReadJumpHeld();

        if (jumpDown)
            jumpBufferTimer = jumpBuffer;

        if (flipSpriteByDirection && spriteToFlip != null)
        {
            float vx = body.linearVelocity.x;
            if (Mathf.Abs(vx) > 0.01f)
                spriteToFlip.flipX = vx < 0f;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        bool wasGrounded = grounded;
        float impactSpeed = body.linearVelocity.y;
        grounded = ProbeGround();

        if (grounded && !wasGrounded)
            Landed?.Invoke(Mathf.Abs(impactSpeed));

        coyoteTimer = grounded ? coyoteTime : Mathf.Max(0f, coyoteTimer - dt);
        jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);

        Vector3 velocity = body.linearVelocity;
        Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 target = moveDirection * moveSpeed;

        if (instantStop && grounded && inputMagnitude < 0.01f)
        {
            planar = Vector3.zero;
        }
        else
        {
            float rate = target.sqrMagnitude > planar.sqrMagnitude ? acceleration : deceleration;
            if (!grounded)
                rate *= airControl;

            planar = Vector3.MoveTowards(planar, target, rate * dt);
        }

        float vy = velocity.y;

        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            vy = JumpVelocity();
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            grounded = false;
            Jumped?.Invoke();
        }

        vy = ApplyJumpGravity(vy, dt);

        body.linearVelocity = new Vector3(planar.x, vy, planar.z);

        planarSpeed = planar.magnitude;

        if (planarSpeed > 0.01f)
            Facing = planar / planarSpeed;
    }

    /// <summary>Freezes or releases movement. Freezing kills the horizontal speed, gravity keeps running.</summary>
    public void SetMovementEnabled(bool enabled)
    {
        MovementEnabled = enabled;

        if (enabled)
            return;

        inputMagnitude = 0f;
        moveDirection = Vector3.zero;
        jumpBufferTimer = 0f;
        jumpHeld = false;

        if (body != null)
            body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
    }

    /// <summary>Jump from code, e.g. a pad or a cutscene. Ignores ground and buffer.</summary>
    public void ForceJump(float heightOverride = -1f)
    {
        if (body == null)
            return;

        float height = heightOverride > 0f ? heightOverride : jumpHeight;
        Vector3 velocity = body.linearVelocity;
        body.linearVelocity = new Vector3(velocity.x, VelocityForHeight(height), velocity.z);

        grounded = false;
        coyoteTimer = 0f;
        Jumped?.Invoke();
    }

    /// <summary>Impulse that reaches Jump Height under the current gravity.</summary>
    private float JumpVelocity()
    {
        return VelocityForHeight(jumpHeight);
    }

    private float VelocityForHeight(float height)
    {
        float gravity = Mathf.Abs(Physics.gravity.y);
        return gravity > 0.0001f ? Mathf.Sqrt(2f * gravity * height) : 0f;
    }

    /// <summary>
    /// Adds the extra gravity that makes the arc feel snappy: heavier on the way
    /// down, and heavier on the way up once the button is released.
    /// </summary>
    private float ApplyJumpGravity(float vy, float dt)
    {
        float gravity = Physics.gravity.y;

        if (vy < 0f)
            vy += gravity * (fallMultiplier - 1f) * dt;
        else if (vy > 0f && variableJumpHeight && !jumpHeld)
            vy += gravity * (lowJumpMultiplier - 1f) * dt;

        return Mathf.Max(vy, -terminalVelocity);
    }

    /// <summary>
    /// Overlap box sitting just under the collider footprint. It is offset clear of
    /// the body so the player never detects itself, no layer setup required.
    /// </summary>
    private bool ProbeGround()
    {
        if (bodyCollider == null)
            return false;

        GetGroundProbe(out Vector3 center, out Vector3 extents);

        return Physics.CheckBox(center, extents, Quaternion.identity, groundMask,
            QueryTriggerInteraction.Ignore);
    }

    private void GetGroundProbe(out Vector3 center, out Vector3 extents)
    {
        const float selfClearance = 0.02f;

        Bounds bounds = bodyCollider.bounds;
        float half = groundProbeDepth * 0.5f;

        center = new Vector3(bounds.center.x, bounds.min.y - selfClearance - half, bounds.center.z);
        extents = new Vector3(bounds.extents.x * groundProbeInset, half, bounds.extents.z * groundProbeInset);
    }

    private Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

        // Clamp so diagonals are not faster than the cardinals.
        return input.sqrMagnitude > 1f ? input.normalized : input;
#else
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        return input.sqrMagnitude > 1f ? input.normalized : input;
#endif
    }

    private bool ReadJumpDown()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool ReadJumpHeld()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.isPressed;
#else
        return Input.GetKey(KeyCode.Space);
#endif
    }

    private Vector3 InputToWorld(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        if (!cameraRelative)
            return new Vector3(input.x, 0f, input.y);

        if (cameraTransform == null)
            CacheCamera();

        if (cameraTransform == null)
            return new Vector3(input.x, 0f, input.y);

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;

        // Looking straight down: fall back to the camera up vector.
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = cameraTransform.up;
            forward.y = 0f;
        }

        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        return right * input.x + forward * input.y;
    }

    private void CacheCamera()
    {
        if (referenceCamera == null)
            referenceCamera = Camera.main;

        cameraTransform = referenceCamera != null ? referenceCamera.transform : null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Collider preview = bodyCollider != null ? bodyCollider : GetComponent<Collider>();
        if (preview == null)
            return;

        bodyCollider = preview;
        GetGroundProbe(out Vector3 center, out Vector3 extents);

        Gizmos.color = Application.isPlaying && grounded
            ? new Color(0.3f, 1f, 0.4f, 0.6f)
            : new Color(1f, 0.5f, 0.2f, 0.5f);
        Gizmos.DrawWireCube(center, extents * 2f);
    }
#endif
}
