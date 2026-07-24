using System;
using UnityEngine;

/// <summary>
/// Finds the nearest IInteractable in range and fires it on the interact button.
/// Detection is a non allocating overlap sphere on a timer, so it costs one
/// physics query every few frames instead of one per frame.
/// Input is left to the derived class.
/// </summary>
[DisallowMultipleComponent]
public abstract class BaseInteractionController : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Sphere centre. Falls back to this transform")]
    [SerializeField] protected Transform detectionOrigin;
    [SerializeField, Min(0f)] protected float interactRange = 1.5f;
    [Tooltip("Keep this to the interactable layers only, it is what makes the query cheap")]
    [SerializeField] protected LayerMask interactableMask = ~0;
    [Tooltip("Seconds between scans. 0 scans every frame")]
    [SerializeField, Min(0f)] protected float detectionInterval = 0.1f;
    [Tooltip("Upper bound of colliders considered per scan")]
    [SerializeField, Min(1)] protected int maxDetected = 8;

    [Header("Prompt")]
    [Tooltip("Optional object shown while something is in range")]
    [SerializeField] protected GameObject promptObject;

    /// <summary>Nearest usable interactable, or null.</summary>
    public IInteractable Current { get; private set; }

    /// <summary>False stops both scanning and input (used while a dialogue plays).</summary>
    public bool DetectionEnabled { get; private set; } = true;

    public event Action<IInteractable> OnTargetChanged;

    private Collider[] buffer;
    private Transform origin;
    private float nextScanTime;

    protected virtual void Awake()
    {
        buffer = new Collider[Mathf.Max(1, maxDetected)];
        origin = detectionOrigin != null ? detectionOrigin : transform;
        SetPromptVisible(false);
    }

    protected virtual void Update()
    {
        if (!DetectionEnabled)
            return;

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + detectionInterval;
            Scan();
        }

        if (Current != null && Current.CanInteract && InteractPressed())
            Current.Interact(this);
    }

    /// <summary>True on the frame the interact button goes down.</summary>
    protected abstract bool InteractPressed();

    /// <summary>Stops scanning and clears the current target.</summary>
    public void SetDetectionEnabled(bool enabled)
    {
        DetectionEnabled = enabled;

        if (!enabled)
            SetCurrent(null);
    }

    private void Scan()
    {
        Vector3 center = origin.position;

        int count = Physics.OverlapSphereNonAlloc(center, interactRange, buffer,
            interactableMask, QueryTriggerInteraction.Collide);

        IInteractable nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider collider = buffer[i];
            if (collider == null)
                continue;

            // Interactable may sit on the collider or on its rigidbody/parent root.
            if (!collider.TryGetComponent(out IInteractable candidate))
            {
                candidate = collider.GetComponentInParent<IInteractable>();
                if (candidate == null)
                    continue;
            }

            if (!candidate.CanInteract)
                continue;

            float sqr = (candidate.Transform.position - center).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = candidate;
            }
        }

        SetCurrent(nearest);
    }

    private void SetCurrent(IInteractable next)
    {
        if (ReferenceEquals(Current, next))
            return;

        Current = next;
        SetPromptVisible(next != null);
        OnTargetChanged?.Invoke(next);
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptObject != null && promptObject.activeSelf != visible)
            promptObject.SetActive(visible);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform t = detectionOrigin != null ? detectionOrigin : transform;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(t.position, interactRange);
    }
#endif
}
