using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform viewCamera;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float gravity = -20f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float lookLimit = 80f;

    private CharacterController controller;
    private ParcelCarrier parcelCarrier;
    private float verticalVelocity;
    private float cameraPitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        parcelCarrier = GetComponent<ParcelCarrier>();

        if (viewCamera == null)
        {
            Debug.LogError(
                "FirstPersonController: View Camera에 Main Camera를 연결하세요.",
                this);

            enabled = false;
            return;
        }

        cameraPitch = Mathf.DeltaAngle(
            0f, viewCamera.localEulerAngles.x);
    }

    private void Start()
    {
        SetCursorLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        // Esc를 누르면 마우스 커서를 풀어준다.
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        // 커서가 풀려 있는 동안에는 조작을 멈춘다.
        // 중력은 계속 적용하고, 클릭하면 조작을 재개한다.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            MovePlayer(Vector2.zero);
            return;
        }

        if (mouse != null)
        {
            UpdateLook(mouse.delta.ReadValue());
        }

        Vector2 moveInput = Vector2.zero;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) moveInput.y += 1f;
            if (keyboard.sKey.isPressed) moveInput.y -= 1f;
            if (keyboard.dKey.isPressed) moveInput.x += 1f;
            if (keyboard.aKey.isPressed) moveInput.x -= 1f;
        }

        MovePlayer(moveInput);
    }

    private void UpdateLook(Vector2 mouseDelta)
    {
        Vector2 look = mouseDelta * mouseSensitivity;

        // 좌우 회전은 플레이어 몸체에 적용한다.
        transform.Rotate(0f, look.x, 0f);

        // 상하 회전은 카메라에만 적용한다.
        cameraPitch -= look.y;
        cameraPitch = Mathf.Clamp(
            cameraPitch, -lookLimit, lookLimit);

        viewCamera.localRotation =
            Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void MovePlayer(Vector2 input)
    {
        if (parcelCarrier != null && parcelCarrier.BlocksPlayerActions)
            input = Vector2.zero;

        // 대각선으로 움직일 때도 속도를 동일하게 유지한다.
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 horizontal =
            transform.right * input.x +
            transform.forward * input.y;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            horizontal * moveSpeed +
            Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SetCursorLocked(false);
        }
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }
}
