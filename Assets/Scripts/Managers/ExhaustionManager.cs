using UnityEngine;

/// <summary>
/// Sprinting tires you out, walking or standing still recovers.
/// At max exhaustion you can't sprint (and walk a bit slower) until you've caught your breath.
/// </summary>
public class ExhaustionManager : MonoBehaviour
{
    public static ExhaustionManager Instance { get; private set; }

    public float maxExhaustion = 100f;
    [Tooltip("Exhaustion gained per second of sprinting.")]
    public float sprintCostPerSecond = 12f;
    [Tooltip("Exhaustion lost per second while walking.")]
    public float walkRecoveryPerSecond = 4f;
    [Tooltip("Exhaustion lost per second while standing still.")]
    public float restRecoveryPerSecond = 10f;
    [Tooltip("Once exhausted, you can sprint again when exhaustion drops below this.")]
    public float recoverBelow = 35f;
    [Range(0.3f, 1f)] public float exhaustedSpeedMultiplier = 0.75f;

    public float CurrentExhaustion { get; private set; }
    public bool IsExhausted { get; private set; }
    public float Normalized => maxExhaustion > 0f ? CurrentExhaustion / maxExhaustion : 0f;

    PlayerMovementAdvanced movement;
    Rigidbody body;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        movement = FindObjectOfType<PlayerMovementAdvanced>();
        if (movement) body = movement.GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (!movement) return;

        Vector3 v = body ? body.velocity : Vector3.zero;
        bool moving = movement.canMove && new Vector3(v.x, 0f, v.z).magnitude > 0.5f;

        if (moving && movement.state == PlayerMovementAdvanced.MovementState.sprinting)
            AddExhaustion(sprintCostPerSecond * Time.deltaTime);
        else
            RemoveExhaustion((moving ? walkRecoveryPerSecond : restRecoveryPerSecond) * Time.deltaTime);

        if (!IsExhausted && CurrentExhaustion >= maxExhaustion)
        {
            IsExhausted = true;
            GameHUD.Toast("You're exhausted! Catch your breath.", GameHUD.Warn);
        }
        else if (IsExhausted && CurrentExhaustion <= recoverBelow)
        {
            IsExhausted = false;
        }

        movement.canSprint = !IsExhausted;
        movement.speedMultiplier = IsExhausted ? exhaustedSpeedMultiplier : 1f;
    }

    public void AddExhaustion(float amount)
    {
        CurrentExhaustion = Mathf.Clamp(CurrentExhaustion + amount, 0f, maxExhaustion);
    }

    public void RemoveExhaustion(float amount)
    {
        CurrentExhaustion = Mathf.Clamp(CurrentExhaustion - amount, 0f, maxExhaustion);
    }
}
