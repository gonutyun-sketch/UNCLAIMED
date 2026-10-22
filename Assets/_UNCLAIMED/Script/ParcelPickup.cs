using UnityEngine;

[RequireComponent(typeof(InteractableObject), typeof(BoxCollider))]
[DisallowMultipleComponent]
public class ParcelPickup : MonoBehaviour
{
    [SerializeField] private ParcelCarrier carrier;
    [SerializeField] private GameObject parcelInVan;
    [SerializeField] private ParcelDeliveryInfo deliveryInfo;

    private InteractableObject interaction;
    private BoxCollider pickupArea;
    private bool collected;

    private void Awake()
    {
        interaction = GetComponent<InteractableObject>();
        pickupArea = GetComponent<BoxCollider>();
    }

    private void OnEnable()
    {
        if (interaction == null) interaction = GetComponent<InteractableObject>();
        interaction.AddListener(PickUp);
    }

    private void OnDisable()
    {
        if (interaction != null) interaction.RemoveListener(PickUp);
    }

    public void PickUp()
    {
        if (collected || carrier == null || parcelInVan == null || !carrier.TryTakeParcel(deliveryInfo))
            return;

        collected = true;
        parcelInVan.SetActive(false);
        pickupArea.enabled = false;
        interaction.enabled = false;
    }
}
