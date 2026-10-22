using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(FirstPersonController))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
public class ParcelCarrier : MonoBehaviour
{
    [SerializeField] private Transform holdPoint;
    [SerializeField] private GameObject heldParcelVisual;
    [SerializeField] private ParcelDeliveryInfo defaultDelivery;
    [SerializeField] private TMP_Text carryHint;

    [Header("Label Inspection")]
    [SerializeField] private Vector3 inspectionLocalPosition = new Vector3(0f, -0.02f, 0.65f);
    [SerializeField] private Vector3 inspectionEulerAngles = new Vector3(-90f, 0f, 0f);
    [SerializeField, Min(0.01f)] private float inspectionDuration = 0.3f;

    private FirstPersonController playerController;
    private Vector3 carryPosition;
    private Quaternion carryRotation;
    private float inspectionBlend;
    private bool inspectionRequested;
    private bool poseCached;

    public bool IsCarrying { get; private set; }
    public ParcelDeliveryInfo CurrentDelivery { get; private set; }
    public bool IsInspecting => IsCarrying && inspectionRequested;
    public bool BlocksPlayerActions => isActiveAndEnabled && IsCarrying &&
        (inspectionRequested || inspectionBlend > 0.001f);

    private void Awake()
    {
        playerController = GetComponent<FirstPersonController>();
        IsCarrying = false;
        CurrentDelivery = null;

        if (holdPoint == null || heldParcelVisual == null)
        {
            Debug.LogError("ParcelCarrier: Assign Hold Point and Held Parcel Visual.", this);
            enabled = false;
            return;
        }

        carryPosition = holdPoint.localPosition;
        carryRotation = heldParcelVisual.transform.localRotation;
        poseCached = true;
        inspectionBlend = 0f;
        inspectionRequested = false;

        // The held model is visual only; it must not hit the player or the interaction cast.
        foreach (Collider parcelCollider in heldParcelVisual.GetComponentsInChildren<Collider>(true))
            parcelCollider.enabled = false;

        foreach (Rigidbody body in heldParcelVisual.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        heldParcelVisual.SetActive(false);
        UpdateHint();
    }

    private void Update()
    {
        if (IsCarrying && playerController != null && playerController.isActiveAndEnabled &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            inspectionRequested = !inspectionRequested;

        UpdateHint();
    }

    private void LateUpdate()
    {
        if (!IsCarrying || !poseCached) return;
        inspectionBlend = Mathf.MoveTowards(inspectionBlend, inspectionRequested ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, inspectionDuration));
        float eased = Mathf.SmoothStep(0f, 1f, inspectionBlend);
        holdPoint.localPosition = Vector3.Lerp(carryPosition, inspectionLocalPosition, eased);
        heldParcelVisual.transform.localRotation = Quaternion.Slerp(carryRotation,
            Quaternion.Euler(inspectionEulerAngles), eased);
    }

    private void UpdateHint()
    {
        if (carryHint == null) return;
        bool visible = IsCarrying && playerController != null && playerController.isActiveAndEnabled &&
            Cursor.lockState == CursorLockMode.Locked;
        carryHint.text = !visible ? "" : inspectionRequested ? "[F] 송장 닫기" : "[F] 송장 확인";
    }

    public bool TryTakeParcel(ParcelDeliveryInfo delivery = null)
    {
        if (!isActiveAndEnabled || IsCarrying || playerController == null ||
            !playerController.isActiveAndEnabled || holdPoint == null || heldParcelVisual == null)
            return false;

        CurrentDelivery = delivery != null ? delivery : defaultDelivery;
        foreach (ParcelLabelDisplay label in heldParcelVisual.GetComponentsInChildren<ParcelLabelDisplay>(true))
            label.SetData(CurrentDelivery);

        IsCarrying = true;
        heldParcelVisual.SetActive(true);
        UpdateHint();
        return true;
    }

    public bool TryReleaseParcel(out ParcelDeliveryInfo delivery)
    {
        delivery = null;
        if (!isActiveAndEnabled || !IsCarrying || BlocksPlayerActions ||
            playerController == null || !playerController.isActiveAndEnabled ||
            holdPoint == null || heldParcelVisual == null)
            return false;

        delivery = CurrentDelivery;
        heldParcelVisual.SetActive(false);
        IsCarrying = false;
        CurrentDelivery = null;
        inspectionRequested = false;
        inspectionBlend = 0f;
        if (poseCached)
        {
            holdPoint.localPosition = carryPosition;
            heldParcelVisual.transform.localRotation = carryRotation;
        }
        UpdateHint();
        return true;
    }

    private void OnDisable()
    {
        inspectionRequested = false;
        inspectionBlend = 0f;
        if (poseCached && holdPoint != null) holdPoint.localPosition = carryPosition;
        if (poseCached && heldParcelVisual != null) heldParcelVisual.transform.localRotation = carryRotation;
        if (carryHint != null) carryHint.text = "";
    }
}
