using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Cell : MonoBehaviour
{
    [Header("UI References")]
    public Image outlineImage;
    public Image pieceImage;      // Hiển thị sprite quân cờ
    public Image deathMarkImage;  // Hiển thị dấu ấn tử vong
    public Image lockIconImage;   // Hiển thị ổ khóa khi bị cấm
    public Image restrictOverlayImage; // Hiển thị khi bị restrict area
    public Image lavaImage; // Hiển thị nền dung nham
    public Button button;

    [HideInInspector] public int row, col;
    [HideInInspector] public GameManager gm;
    [HideInInspector] public OnlineGameManager onlineGm;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();

        // Đảm bảo các hiệu ứng đặc biệt được tắt khi bắt đầu
        if (deathMarkImage != null) deathMarkImage.enabled = false;
        if (lockIconImage != null) lockIconImage.enabled = false;
    }

    public void Init(GameManager gameManager, int r, int c)
    {
        gm = gameManager;
        row = r;
        col = c;

        // Reset tất cả trạng thái hình ảnh về mặc định
        pieceImage.sprite = null;
        pieceImage.enabled = false;

        if (outlineImage != null) outlineImage.enabled = false;
        if (deathMarkImage != null) deathMarkImage.enabled = false;
        if (lockIconImage != null) lockIconImage.enabled = false;
        if (restrictOverlayImage != null) restrictOverlayImage.enabled = false;
        if (lavaImage != null) lavaImage.enabled = false;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);
    }

    public void InitOnline(OnlineGameManager ogm, int r, int c)
    {
        onlineGm = ogm;
        gm = null; // Đảm bảo không nhầm lẫn
        row = r;
        col = c;
        ResetVisuals();

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        if (gm != null)
        {
            gm.OnCellClicked(this); // Gọi Offline Logic
        }
        else if (onlineGm != null)
        {
            onlineGm.OnCellClicked(this); // Gọi Online Logic
        }
    }

    void ResetVisuals()
    {
        pieceImage.sprite = null;
        pieceImage.enabled = false;
        if (outlineImage != null) outlineImage.enabled = false;
        if (deathMarkImage != null) deathMarkImage.enabled = false;
        if (lockIconImage != null) lockIconImage.enabled = false;
        if (restrictOverlayImage != null) restrictOverlayImage.enabled = false;
        if (lavaImage != null) lavaImage.enabled = false;
    }

    public bool IsEmpty()
    {
        return !pieceImage.enabled;
    }

    public void PlacePiece(Sprite sprite, int ownerPlayer, PieceData data)
    {
        pieceImage.sprite = sprite;
        pieceImage.enabled = true;

        // --- Hiển thị viền màu tuỳ theo người chơi ---
        if (outlineImage != null)
        {
            outlineImage.enabled = true;
            outlineImage.color = (ownerPlayer == 1) ? Color.blue : Color.red;
        }

        // Lưu owner & pieceData vào GameManager
        if (gm != null)
        {
            CellState cs = new CellState { owner = ownerPlayer, pieceData = data, burnTurns = 0 };
            gm.SetCellState(row, col, cs);
        }
    }

    public void ClearCell()
    {
        pieceImage.sprite = null;
        pieceImage.enabled = false;

        if (outlineImage != null) outlineImage.enabled = false;
        if (deathMarkImage != null) deathMarkImage.enabled = false;
        if (lockIconImage != null) lockIconImage.enabled = false;
        if (restrictOverlayImage != null) restrictOverlayImage.enabled = false;

        if (gm != null)
        {
            gm.ClearCellState(row, col);
        }
    }

    public void SetLockIcon(bool isLocked)
    {
        if (lockIconImage != null)
        {
            lockIconImage.enabled = isLocked;
        }
    }

    // --- VISUAL EFFECTS ---

    public void ShowBurnVisual()
    {
        StartCoroutine(BlinkAndDestroy());
    }

    public void ShowDeathMarkVisual()
    {
        if (deathMarkImage != null)
        {
            deathMarkImage.enabled = true;
        }
    }

    public void SetRestrictOverlay(bool active)
    {
        if (restrictOverlayImage != null)
        {
            restrictOverlayImage.enabled = active;
        }
    }

    public void SetLavaVisual(bool active)
    {
        if (lavaImage != null)
        {
            lavaImage.enabled = active;
        }
    }

    private IEnumerator BlinkAndDestroy()
    {
        for (int i = 0; i < 3; i++)
        {
            pieceImage.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            pieceImage.color = Color.white;
            yield return new WaitForSeconds(0.1f);
        }
    }
}

// Container lưu trạng thái của ô (đã có sẵn từ trước)
public struct CellState
{
    public int owner; // 0: empty, 1: player1, 2: player2
    public PieceData pieceData;
    public int burnTurns;
    public int deathMarkCount;
    public bool isLava;
}