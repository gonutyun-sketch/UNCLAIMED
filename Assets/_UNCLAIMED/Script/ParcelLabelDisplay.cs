using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ParcelLabelDisplay : MonoBehaviour
{
    [SerializeField] private ParcelDeliveryInfo deliveryInfo;
    [SerializeField] private TMP_Text labelText;

    public ParcelDeliveryInfo Data => deliveryInfo;
    public TMP_Text LabelText => labelText;

    private void OnEnable()
    {
        Refresh();
    }

    public void SetData(ParcelDeliveryInfo data)
    {
        deliveryInfo = data;
        Refresh();
    }

    public void Refresh()
    {
        if (labelText == null) return;
        labelText.text = deliveryInfo != null
            ? "<size=0.26>배송지</size>\n<size=0.62><b>" + deliveryInfo.DisplayAddress +
              "</b></size>\n<size=0.24>" + deliveryInfo.DeliveryInstructions + "</size>"
            : "<size=0.32>배송 정보 없음</size>";
    }
}
