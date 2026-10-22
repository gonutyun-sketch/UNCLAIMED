using UnityEngine;

[CreateAssetMenu(fileName = "ParcelDelivery", menuName = "UNCLAIMED/Parcel Delivery Info")]
public class ParcelDeliveryInfo : ScriptableObject
{
    [SerializeField] private string buildingName = "101동";
    [SerializeField] private string unitNumber = "402호";
    [SerializeField, TextArea(1, 3)] private string deliveryInstructions = "문 앞에 놓아주세요.";

    public string BuildingName => buildingName;
    public string UnitNumber => unitNumber;
    public string DisplayAddress => buildingName + " " + unitNumber;
    public string DeliveryInstructions => deliveryInstructions;
}
