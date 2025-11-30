using UnityEngine;
using UnityEngine.UI;

public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance;

    public Text nameText;
    public Text descText;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        Instance = this;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        HideTooltip();
    }

    void Update()
    {
        if (!gameObject.activeSelf) return;

        Vector2 localMousePos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            transform.parent as RectTransform,
            Input.mousePosition,
            null,
            out localMousePos
        );

        // Kiểm tra vị trí chuột so với giữa màn hình
        float screenWidth = Screen.width;
        bool isRightSide = Input.mousePosition.x > screenWidth * 0.5f;

        // Nếu ở bên phải màn hình → tooltip lệch sang trái
        Vector2 offset = isRightSide ? new Vector2(-420, -15) : new Vector2(35, -15);

        rectTransform.anchoredPosition = localMousePos + offset;
    }

    public void ShowTooltip(PieceData piece)
    {
        if (piece == null) return;

        nameText.text = piece.pieceName;
        descText.text = GetPassiveDescription(piece.passive);

        gameObject.SetActive(true);

        canvasGroup.blocksRaycasts = false;

        // Ép Unity tính lại layout
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }


    public void HideTooltip()
    {
        gameObject.SetActive(false);
    }

    private string GetPassiveDescription(PiecePassive passive)
    {
        switch (passive)
        {
            case PiecePassive.RestrictArea:
                return "Passive: Khi đặt caro này ra sẽ giam cầm đối thủ trong 5x5 trong 1 lượt.";
            case PiecePassive.GainEnergyEvery2Plays:
                return "Passive: Mỗi ghi đánh được 2 caro này sẽ cộng 1 năng lượng cho lượt tiếp theo.";
            case PiecePassive.UnblockableOnFour:
                return "Passive: Khi caro này tạo thành chuỗi 4, không thể chặn đầu chuỗi này trong 1 lượt. ";
            case PiecePassive.ExplodeOnDeath:
                return "Passive: Khi chết, phát nổ tiêu diệt toàn bộ ô trong vùng 3x3.";
            case PiecePassive.EnergySpendGrant:
                return "Passive: Khi tiêu hao 3 năng lượng sẽ được nhận 1 năng lượng.";
            case PiecePassive.PyroRage:
                return "Passive: Sau khi rời khỏi chiến đấu 4 lượt, caro này nhận trạng thái “Hoả Cuồng”: Lần kế chơi caro này, Thiêu Cháy 2 caro đối thủ bên trái và bên phải.";
            case PiecePassive.CrossStrike:
                return "Passive: Lần đầu caro này được đặt xuống, tiêu diệt tất cả caro ở 4 hướng (cả kẻ địch lẫn đồng minh).";
            case PiecePassive.DeathMark:
                return "Passive: Khi bị đối thủ hạ gục sẽ gắn dấu ấn tử vong cho các caro xung quanh (Dấu ấn tử vong: Khi chịu dấu ấn tử vong thêm 1 lần nữa sẽ bị tiêu diệt). ";
            case PiecePassive.IsolationLock:
                return "Passive: Khi được đặt tại ô không có caro xung quanh, khoá các ô xung quanh trong 1 lượt.";
            case PiecePassive.LavaSpawnOnDeath:
                return "Passive: Khi hy sinh sẽ để lại ô dung nham ở vị trí hy sinh, không thể đặt caro trên ô dung nham.";
            case PiecePassive.FourInRowWin:
                return "Passive: Chỉ cần 4 caro liên tiếp để giành lợi thế. Đổi lại, không thể giành lợi thế bằng hàng chéo. ";
            case PiecePassive.WinLineExplode:
                return "Passive: Khi giành lợi thế, tiêu diệt tất cả quân cờ xung quanh.";
            case PiecePassive.WildCardCaro:
                return "Passive: Caro này được xem như bản sao của tất cả caro đồng minh.";
            case PiecePassive.AllyReplace:
                return "Passive: Có thể đặt đè lên quân đồng minh. Quân bị đè sẽ bị tiêu diệt.";
            case PiecePassive.Active_TeleportAlly:
                return "Active: Khi kích hoạt có thể dịch chuyển 1 đồng minh bất kỳ tới một vị trí được chọn (Chuột phải để hủy dùng kỹ năng).";
            case PiecePassive.Active_SwapAllies:
                return "Active: Khi kích hoạt sẽ phải chọn 2 quân đồng minh bất kỳ trên bàn để hoán đổi vị trí (Chuột phải để hủy dùng kỹ năng).";
            default:
                return "Không có kỹ năng đặc biệt.";
        }
    }
}
