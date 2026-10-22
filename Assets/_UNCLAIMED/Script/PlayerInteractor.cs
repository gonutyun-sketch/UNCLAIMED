using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Transform viewCamera;
    [SerializeField, Min(0.1f)] private float interactionDistance = 2.5f;

    private InteractableObject currentTarget;
    private GUIStyle promptStyle;

    private void Awake()
    {
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

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Ray ray = new Ray(
            viewCamera.position,
            viewCamera.forward);

        if (Physics.SphereCast(
            ray,
            0.2f,
            out RaycastHit hit,
            interactionDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore))
        {
            InteractableObject candidate =
                hit.collider.GetComponentInParent<InteractableObject>();

            if (candidate != null && candidate.isActiveAndEnabled)
            {
                currentTarget = candidate;
            }
        }

        Keyboard keyboard = Keyboard.current;

        if (currentTarget != null &&
            keyboard != null &&
            keyboard.eKey.wasPressedThisFrame)
        {
            currentTarget.Interact();
            currentTarget = null;
        }
    }

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        // Temporary aiming dot.
        GUI.DrawTexture(
            new Rect(centerX - 2f, centerY - 2f, 4f, 4f),
            Texture2D.whiteTexture);

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