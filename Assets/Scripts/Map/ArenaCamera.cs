using UnityEngine;

[DefaultExecutionOrder(100)]
public class ArenaCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private bool lockTargetToCenter = true;
    [SerializeField] private float paddingMultiplier = 1.05f;
    [SerializeField] private float smoothSpeed = 8f;
    [SerializeField] private float fixedHeight = 20f;
    [SerializeField, Range(0f, 0.4f)] private float uiTopReserve = 0.15f; // fraction of screen height reserved for UI at top

    private Camera cam;
    private float fixedZ;
    private float minX, maxX;
    private Renderer targetVisual;

    public static ArenaCamera Instance { get; private set; }

    private float shakeTimer = 0f;
    private float shakeMagnitude = 0f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        cam = GetComponent<Camera>();
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        SetupBounds();
        CacheTargetVisual();
        CenterOnTarget();
    }

    void CacheTargetVisual()
    {
        var facing = target != null ? target.GetComponentInChildren<FaceCamera>() : null;
        targetVisual = facing != null ? facing.GetComponentInChildren<Renderer>() : null;
    }

    void CenterOnTarget()
    {
        if (!lockTargetToCenter || target == null) return;
        Vector3 focus = targetVisual != null ? targetVisual.bounds.center : target.position;
        transform.position = new Vector3(focus.x, fixedHeight, focus.z);
    }

    void SetupBounds()
    {
        int width = GridManager.Instance.Width;
        int height = GridManager.Instance.Height;
        float tileSize = GridManager.Instance.TileSize;

        float arenaWidth = width * tileSize;
        float arenaHeight = height * tileSize;

        // Ortho size needs to fit arena height WITHIN the space left after reserving UI room
        // If UI takes uiTopReserve fraction of screen, arena needs to fit in the remaining (1 - uiTopReserve)
        float usableHeightFraction = 1f - uiTopReserve;
        float orthoSize = (arenaHeight / 2f / usableHeightFraction) * paddingMultiplier;
        cam.orthographicSize = orthoSize;

        // Arena's natural vertical center in world space
        float arenaCenterZ = (height - 1) * tileSize / 2f;

        // Shift camera's Z so the arena sits in the LOWER part of the view,
        // leaving empty world space at the "top" of screen for UI to overlay
        float worldShift = orthoSize * uiTopReserve;
        fixedZ = arenaCenterZ + worldShift; // push camera center upward off the arena, so arena appears lower on screen

        float visibleHalfWidth = orthoSize * cam.aspect;
        float arenaLeft = 0f;
        float arenaRight = (width - 1) * tileSize;

        minX = arenaLeft + visibleHalfWidth;
        maxX = arenaRight - visibleHalfWidth;

        if (minX > maxX)
        {
            float center = (arenaLeft + arenaRight) / 2f;
            minX = maxX = center;
        }

        float startX = Mathf.Clamp(target != null ? target.position.x : (arenaLeft + arenaRight) / 2f, minX, maxX);
        transform.position = new Vector3(startX, fixedHeight, fixedZ);
    }

    void LateUpdate()
    {
        if (lockTargetToCenter && target != null)
        {
            CenterOnTarget();
            // A positional shake would move the character away from screen centre.
            shakeTimer = 0f;
            shakeMagnitude = 0f;
            return;
        }

        float currentX = (target != null) ? Mathf.Clamp(target.position.x, minX, maxX) : transform.position.x;
        float smoothedX = (target != null) ? Mathf.Lerp(transform.position.x, currentX, smoothSpeed * Time.deltaTime) : currentX;

        Vector3 shakeOffset = Vector3.zero;
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            shakeOffset = new Vector3(
                (Random.value * 2f - 1f) * shakeMagnitude,
                0f,
                (Random.value * 2f - 1f) * shakeMagnitude
            );
            if (shakeTimer <= 0f) shakeMagnitude = 0f;
        }

        transform.position = new Vector3(smoothedX, fixedHeight, fixedZ) + shakeOffset;
    }

    public void Shake(float magnitude = 0.22f, float duration = 0.2f)
    {
        shakeMagnitude = Mathf.Max(shakeMagnitude, magnitude);
        shakeTimer = Mathf.Max(shakeTimer, duration);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        CacheTargetVisual();
        CenterOnTarget();
    }
}
