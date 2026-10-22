using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Transform viewCamera;
    [SerializeField, Min(0.1f)] private float interactionDistance = 2.5f;
    [SerializeField] private TMP_Text interactionPrompt;

    private readonly RaycastHit[] interactionHits = new RaycastHit[32];
    private InteractableObject currentTarget;
    private ParcelCarrier parcelCarrier;
    private GUIStyle promptStyle;

    private void Awake()
    {
        parcelCarrier = GetComponent<ParcelCarrier>();
        if (viewCamera == null)
        {
            Debug.LogError(
                "PlayerInteractor: Assign Main Camera to View Camera.",
                this);

            enabled = false;
        }
    }

    private void LateUpdate()
    {
        currentTarget = null;

        if (Cursor.lockState != CursorLockMode.Locked ||
            (parcelCarrier != null && parcelCarrier.BlocksPlayerActions))
        {
            UpdatePrompt();
            return;
        }

        Ray ray = new Ray(
            viewCamera.position,
            viewCamera.forward);

        int count = Physics.SphereCastNonAlloc(ray, 0.2f, interactionHits,
            interactionDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        RaycastHit[] hits = interactionHits;
        // Preserve occlusion even if a crowded scene fills the reusable buffer.
        if (count == interactionHits.Length)
        {
            hits = Physics.SphereCastAll(ray, 0.2f, interactionDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            count = hits.Length;
        }

        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform)) continue;
            InteractableObject candidate = hitCollider.GetComponentInParent<InteractableObject>();
            bool available = candidate != null && candidate.isActiveAndEnabled;
            // Unrelated gameplay triggers do not block the view; solid walls still do.
            if (hitCollider.isTrigger && !available) continue;
            if (hits[i].distance >= closestDistance) continue;
            closestDistance = hits[i].distance;
            currentTarget = available ? candidate : null;
        }

        Keyboard keyboard = Keyboard.current;

        if (currentTarget != null &&
            keyboard != null &&
            keyboard.eKey.wasPressedThisFrame)
        {
            currentTarget.Interact();
            currentTarget = null;
        }

        UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        if (interactionPrompt == null) return;
        interactionPrompt.text = currentTarget != null && currentTarget.isActiveAndEnabled
            ? "[E] " + currentTarget.Prompt
            : "";
    }

    private void OnDisable()
    {
        currentTarget = null;
        if (interactionPrompt != null) interactionPrompt.text = "";
    }

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked ||
            (parcelCarrier != null && parcelCarrier.BlocksPlayerActions))
        {
            return;
        }

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        // Temporary aiming dot.
        GUI.DrawTexture(
            new Rect(centerX - 2f, centerY - 2f, 4f, 4f),
            Texture2D.whiteTexture);

        if (interactionPrompt != null) return;

        if (currentTarget == null || !currentTarget.isActiveAndEnabled)
        {
            return;
        }

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.box);
            promptStyle.fontSize = 20;
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.normal.textColor = Color.white;
        }

        GUI.Box(
            new Rect(centerX - 130f, centerY + 28f, 260f, 40f),
            "[E] " + currentTarget.Prompt,
            promptStyle);
    }
}
