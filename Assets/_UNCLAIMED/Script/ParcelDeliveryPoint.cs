using UnityEngine;

[RequireComponent(typeof(InteractableObject), typeof(BoxCollider))]
[DisallowMultipleComponent]
public class ParcelDeliveryPoint : MonoBehaviour
{
    [SerializeField] private ParcelCarrier carrier;
    [SerializeField] private ParcelDeliveryInfo deliveryInfo;
    [SerializeField] private Transform placementAnchor;
    [SerializeField] private GameObject placedParcelVisual;
    [SerializeField, Min(0.1f)] private float maximumPlacementDistance = 3.25f;
    [SerializeField, Min(0.1f)] private float maximumFloorDifference = 1.25f;

    private InteractableObject interaction;
    private BoxCollider placementArea;
    private FirstPersonController playerController;
    private ParcelDeliveryInfo placedDelivery;

    public bool IsPlaced { get; private set; }
    public ParcelDeliveryInfo ParcelInfo => IsPlaced ? placedDelivery : deliveryInfo;
    public bool CanPlaceParcel => CanPlace();

    private void Awake()
    {
        CacheComponents();
        if (placedParcelVisual != null &&
            (carrier == null || !placedParcelVisual.transform.IsChildOf(carrier.transform)))
            placedParcelVisual.SetActive(false);
        RefreshAvailability();
    }

    private void OnEnable()
    {
        CacheComponents();
        interaction.AddListener(PlaceParcel);
        RefreshAvailability();
    }

    private void OnDisable()
    {
        if (interaction != null)
        {
            interaction.RemoveListener(PlaceParcel);
            interaction.enabled = false;
        }
        if (placementArea != null) placementArea.enabled = false;
    }

    private void Update()
    {
        RefreshAvailability();
    }

    private void CacheComponents()
    {
        if (interaction == null) interaction = GetComponent<InteractableObject>();
        if (placementArea == null) placementArea = GetComponent<BoxCollider>();
        if (carrier != null && playerController == null)
            playerController = carrier.GetComponent<FirstPersonController>();
    }

    private bool CanPlace()
    {
        if (!isActiveAndEnabled || IsPlaced || carrier == null || deliveryInfo == null ||
            placementAnchor == null || placedParcelVisual == null ||
            !carrier.isActiveAndEnabled || !carrier.IsCarrying || carrier.BlocksPlayerActions ||
            carrier.CurrentDelivery != deliveryInfo ||
            playerController == null || !playerController.isActiveAndEnabled ||
            placementAnchor.IsChildOf(carrier.transform) ||
            placedParcelVisual.transform.IsChildOf(carrier.transform))
            return false;

        Vector3 offset = carrier.transform.position - placementAnchor.position;
        if (Mathf.Abs(offset.y) > maximumFloorDifference) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= maximumPlacementDistance * maximumPlacementDistance;
    }

    private void RefreshAvailability()
    {
        bool available = CanPlace();
        if (interaction != null && interaction.enabled != available) interaction.enabled = available;
        if (placementArea != null && placementArea.enabled != available) placementArea.enabled = available;
    }

    private void PlaceParcel()
    {
        TryPlaceParcel();
    }

    public bool TryPlaceParcel()
    {
        CacheComponents();
        if (!CanPlace() || !carrier.TryReleaseParcel(out ParcelDeliveryInfo delivery))
            return false;

        placedDelivery = delivery;
        placedParcelVisual.transform.SetPositionAndRotation(placementAnchor.position, placementAnchor.rotation);
        foreach (ParcelLabelDisplay label in placedParcelVisual.GetComponentsInChildren<ParcelLabelDisplay>(true))
            label.SetData(placedDelivery);
        IsPlaced = true;
        placedParcelVisual.SetActive(true);
        RefreshAvailability();
        return true;
    }
}
