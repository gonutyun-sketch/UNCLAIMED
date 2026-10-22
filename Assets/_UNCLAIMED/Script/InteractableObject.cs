using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class InteractableObject : MonoBehaviour
{
    [SerializeField] private string prompt = "Interact";
    [SerializeField] private UnityEvent onInteract = new UnityEvent();

    public string Prompt => prompt;

    public void AddListener(UnityAction listener)
    {
        onInteract.AddListener(listener);
    }

    public void RemoveListener(UnityAction listener)
    {
        onInteract.RemoveListener(listener);
    }

    public void Interact()
    {
        if (isActiveAndEnabled)
        {
            onInteract.Invoke();
        }
    }
}
