using UnityEngine;

public class CameraZoom : MonoBehaviour
{
    public float zoomSpeed = 5f;   // Tốc độ zoom
    public float minZoom = 5f;     // Zoom nhỏ nhất
    public float maxZoom = 50f;    // Zoom lớn nhất
    public float dragSpeed = 0.5f;
    public float minX = -10f;
    public float maxX = 10f;
    public float minY = -5f;
    public float maxY = 5f;
    public Transform boardParent;

    private Camera cam;
    private Vector3 dragOrigin;
    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        //HandleZoom();
        HandleDrag();
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel"); // cuộn chuột
        if (scroll != 0.0f)
        {
            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
    }

    void HandleDrag()
    {
        if (Input.GetMouseButtonDown(0))
        {
            dragOrigin = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))
        {
            Vector3 difference = Input.mousePosition - dragOrigin;
            dragOrigin = Input.mousePosition;

            // 👉 Nếu có boardParent thì di chuyển bàn cờ
            if (boardParent != null)
            {
                // Nếu bàn cờ là UI (trong Canvas)
                RectTransform rt = boardParent.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition += new Vector2(difference.x, difference.y) * dragSpeed * Time.deltaTime;
                }
            }
            else
            {
                // Nếu chưa gán bàn cờ → fallback: di chuyển camera
                transform.Translate(-difference.x * dragSpeed * Time.deltaTime,
                                    -difference.y * dragSpeed * Time.deltaTime,
                                    0);
            }
            Vector3 pos = boardParent.position;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            boardParent.position = pos;
        }
    }
}
