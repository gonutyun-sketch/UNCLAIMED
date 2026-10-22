using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class VanOpeningController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FirstPersonController playerController;
    [SerializeField] private Transform viewCamera;
    [SerializeField] private Transform driverView;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Seated Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float horizontalLimit = 75f;
    [SerializeField] private float verticalLimit = 45f;

    [Header("Opening Subtitles")]
    [SerializeField, Min(0f)] private float startDelay = 1f;
    [SerializeField, Min(1f)] private float lineDuration = 4f;
    [SerializeField, Min(0f)] private float lineGap = 0.5f;
    [SerializeField, TextArea(2, 4)] private string[] openingLines =
    {
        "하… 이제 여기만 돌면 끝이네.",
        "이 시간엔 다들 자고 있겠지.",
        "조용히 놓고, 사진만 찍고 나오자.",
        "차 갖다 놓으면… 집엔 몇 시에 가려나."
    };

    private CharacterController characterController;
    private PlayerInteractor playerInteractor;
    private Transform cameraOriginalParent;
    private Vector3 cameraOriginalPosition;
    private Quaternion cameraOriginalRotation;
    private float yaw;
    private float pitch;
    private float subtitleClock;
    private bool inVan;
    private bool introFinished;
    private bool ownsPlayerControl;
    private bool shuttingDown;
    private int resumeOnFrame = -1;

    public bool IsInVan => inVan;
    public bool IntroFinished => introFinished;

    private void Start()
    {
        if (playerController == null || viewCamera == null ||
            driverView == null || exitPoint == null || subtitleText == null)
        {
            Debug.LogError("VanOpeningController: Assign all five references.", this);
            enabled = false;
            return;
        }

        characterController = playerController.GetComponent<CharacterController>();
        playerInteractor = playerController.GetComponent<PlayerInteractor>();
        if (characterController == null || playerInteractor == null ||
            !viewCamera.IsChildOf(playerController.transform))
        {
            Debug.LogError("VanOpeningController: Check Player components and keep Main Camera under Player before playing.", this);
            enabled = false;
            return;
        }

        cameraOriginalParent = viewCamera.parent;
        cameraOriginalPosition = viewCamera.localPosition;
        cameraOriginalRotation = viewCamera.localRotation;
        ownsPlayerControl = true;
        playerController.enabled = false;
        playerInteractor.enabled = false;
        characterController.enabled = false;

        viewCamera.SetParent(driverView, false);
        viewCamera.localPosition = Vector3.zero;
        viewCamera.localRotation = Quaternion.identity;
        yaw = 0f;
        pitch = 0f;
        subtitleClock = -Mathf.Max(0f, startDelay);
        introFinished = false;
        inVan = true;
        shuttingDown = false;
        SetSubtitle("", 0f);
        SetCursorLocked(true);
    }

    private void Update()
    {
        // Resume one frame later so E cannot also interact outside.
        if (resumeOnFrame >= 0)
        {
            if (Time.frameCount >= resumeOnFrame) ResumeWalking();
            return;
        }
        if (!inVan || !Application.isFocused) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
            return;
        }

        if (mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            yaw = Mathf.Clamp(yaw + look.x, -horizontalLimit, horizontalLimit);
            pitch = Mathf.Clamp(pitch - look.y, -verticalLimit, verticalLimit);
            viewCamera.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        UpdateSubtitles();
        if (introFinished && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            ExitVehicle();
    }

    private void UpdateSubtitles()
    {
        if (introFinished) return;
        subtitleClock += Time.deltaTime;
        if (subtitleClock < 0f) return;

        float duration = Mathf.Max(1f, lineDuration);
        float cycle = duration + Mathf.Max(0f, lineGap);
        int index = Mathf.FloorToInt(subtitleClock / cycle);
        if (openingLines == null || index >= openingLines.Length)
        {
            introFinished = true;
            SetSubtitle("[E] 차에서 내리기", 1f);
            return;
        }

        float elapsed = subtitleClock - index * cycle;
        if (elapsed >= duration)
        {
            SetSubtitle("", 0f);
            return;
        }

        const float fadeDuration = 0.25f;
        float fadeIn = Mathf.Clamp01(elapsed / fadeDuration);
        float fadeOut = Mathf.Clamp01((duration - elapsed) / fadeDuration);
        SetSubtitle(openingLines[index], Mathf.Min(fadeIn, fadeOut));
    }

    private void SetSubtitle(string text, float alpha)
    {
        if (subtitleText == null) return;
        subtitleText.text = text;
        subtitleText.alpha = alpha;
    }

    public void ExitVehicle()
    {
        if (!inVan || !introFinished) return;
        inVan = false;
        SetSubtitle("", 0f);
        PlacePlayerOutside();
        RestoreCamera();
        resumeOnFrame = Time.frameCount + 1;
    }

    private void PlacePlayerOutside()
    {
        if (playerController == null || exitPoint == null) return;
        Quaternion facing = Quaternion.Euler(0f, exitPoint.eulerAngles.y, 0f);
        playerController.transform.SetPositionAndRotation(exitPoint.position, facing);
    }

    private void RestoreCamera()
    {
        if (viewCamera == null || cameraOriginalParent == null) return;
        viewCamera.SetParent(cameraOriginalParent, false);
        viewCamera.localPosition = cameraOriginalPosition;
        viewCamera.localRotation = cameraOriginalRotation;
    }

    private void ResumeWalking()
    {
        if (characterController != null) characterController.enabled = true;
        if (playerController != null) playerController.enabled = true;
        if (playerInteractor != null) playerInteractor.enabled = true;
        resumeOnFrame = -1;
        ownsPlayerControl = false;
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && ownsPlayerControl) SetCursorLocked(false);
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }

    private void OnDisable()
    {
        SetSubtitle("", 0f);
        if (!ownsPlayerControl) return;
        if (!shuttingDown && Application.isPlaying)
        {
            inVan = false;
            PlacePlayerOutside();
            RestoreCamera();
            ResumeWalking();
        }
        SetCursorLocked(false);
    }
}
