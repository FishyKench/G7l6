using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerMovementAdvanced : MonoBehaviour
{
    [Header("Movement")]
    private float moveSpeed;
    public float walkSpeed;
    public float sprintSpeed;

    public float groundDrag;

    [Header("Jumping")]
    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    bool readyToJump;

    [Header("Crouching")]
    public float crouchSpeed;
    public float crouchYScale;
    private float startYScale;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    [Tooltip("Radius of the ground check. A sphere (instead of a thin ray) keeps you grounded on steep stairs and ramps.")]
    public float groundCheckRadius = 0.35f;
    bool grounded;
    RaycastHit groundHit;

    [Header("Slope Handling")]
    public float maxSlopeAngle;
    private bool exitingSlope;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;

    public MovementState state;

    public bool canMove;

    [Tooltip("Set by ExhaustionManager: false while the player is exhausted.")]
    public bool canSprint = true;
    [Tooltip("Set by ExhaustionManager: slows every movement speed while exhausted.")]
    public float speedMultiplier = 1f;

    public enum MovementState
    {
        walking,
        sprinting,
        crouching,
        air
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        canMove = true;

        readyToJump = true;

        startYScale = transform.localScale.y;
    }

    private void Update()
    {
        if (canMove)
        {
            grounded = CheckGround();

            MyInput();
            SpeedControl();
            StateHandler();

            if (grounded)
                rb.drag = groundDrag;
            else
                rb.drag = 0;
        }
        else
        {
            horizontalInput = 0;
            verticalInput = 0;
            rb.velocity = Vector3.zero;
            return;
        }
    }

    private void FixedUpdate()
    {
        if (canMove)
        {
            MovePlayer();
        }
    }

    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }

        if (Input.GetKeyDown(crouchKey))
        {
            transform.localScale = new Vector3(transform.localScale.x, crouchYScale, transform.localScale.z);
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        }

        if (Input.GetKeyUp(crouchKey))
        {
            transform.localScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
        }
    }

    private void StateHandler()
    {
        if (Input.GetKey(crouchKey))
        {
            state = MovementState.crouching;
            moveSpeed = crouchSpeed * speedMultiplier;
        }
        else if (grounded && canSprint && Input.GetKey(sprintKey))
        {
            state = MovementState.sprinting;
            moveSpeed = sprintSpeed * speedMultiplier;
        }
        else if (grounded)
        {
            state = MovementState.walking;
            moveSpeed = walkSpeed * speedMultiplier;
        }
        else
        {
            state = MovementState.air;
        }
    }

    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection() * moveSpeed * 20f, ForceMode.Force);

            // Hug the slope instead of bouncing off it. (This used to push down whenever you moved up,
            // which fought you on stairs so you had to sprint.)
            rb.velocity = Vector3.ProjectOnPlane(rb.velocity, groundHit.normal);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
        }
        else if (!grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
        }

        rb.useGravity = !OnSlope();
    }

    private void SpeedControl()
    {
        if (OnSlope() && !exitingSlope)
        {
            if (rb.velocity.magnitude > moveSpeed)
                rb.velocity = rb.velocity.normalized * moveSpeed;
        }
        else
        {
            Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

            if (flatVel.magnitude > moveSpeed)
            {
                Vector3 limitedVel = flatVel.normalized * moveSpeed;
                rb.velocity = new Vector3(limitedVel.x, rb.velocity.y, limitedVel.z);
            }
        }
    }

    private void Jump()
    {
        exitingSlope = true;
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump = true;
        exitingSlope = false;
    }

    /// <summary>
    /// Sweeps a sphere down from the middle of the player. A thin ray only just reached the ground on
    /// the 44-degree stairs, so you kept "falling" mid-climb; a sphere touches the slope much earlier.
    /// </summary>
    private bool CheckGround()
    {
        float reach = playerHeight * 0.5f - groundCheckRadius + 0.25f;
        return Physics.SphereCast(transform.position, groundCheckRadius, Vector3.down, out groundHit, reach, whatIsGround, QueryTriggerInteraction.Ignore);
    }

    private bool OnSlope()
    {
        if (!grounded) return false;
        float angle = Vector3.Angle(Vector3.up, groundHit.normal);
        return angle > 1f && angle < maxSlopeAngle;
    }

    private Vector3 GetSlopeMoveDirection()
    {
        return Vector3.ProjectOnPlane(moveDirection, groundHit.normal).normalized;
    }
}
