using UnityEngine;

public enum PiecePassive
{
    None,
    RestrictArea,       // Giam cầm 5x5
    GainEnergyEvery2Plays,   // +1 energy khi đặt 2 con caro này
    UnblockableOnFour,  // Tạo chuỗi 4 => Không được chặn 2 đầu
    ExplodeOnDeath,     // Khi hy sinh => Tự hủy xung quanh
    EnergySpendGrant,   // Mỗi 3 energy tiêu thụ => +1 next grant
    PyroRage,            // Mỗi 4 lượt không chơi => trạng thái hỏa cuồng
    CrossStrike,      // Tiêu diệt quân địch xung quanh theo hình dấu cộng
    DeathMark,        // Khi bị hạ gục sẽ gắn dấu ấn tử vong cho các caro đối thủ xung quanh (Dấu ấn tử vong: Khi chịu dấu ấn tử vong thêm 1 lần nữa sẽ bị tiêu diệt) 
    IsolationLock,     // Khi được đặt tại ô không có caro xung quanh, khoá các ô xung quanh trong 1 lượt, ngoại trừ caro đồng minh caro đối thủ không được đặt
    LavaSpawnOnDeath,  // Khi hy sinh sẽ để lại ô dung nham ở vị trí hy sinh, không thể đặt caro trên ô dung nham
    FourInRowWin,       // Giảm 1 caro cần thiết để caro này giành được lợi thế tuy nhiên đổi lại sẽ không tính hàng chéo
    WinLineExplode,      // Giành được lợi thế tiêu diệt caro xung quanh
    WildCardCaro,         // Quân Joker, tính là quân đồng minh
    AllyReplace,          // Đánh đè lên đồng minh
    Active_TeleportAlly,   // Chủ động: dịch chuyển đồng minh đến 1 vị trí bất kỳ
    Active_SwapAllies     // Chủ động: đổi chỗ 2 đồng minh bất kỳ
}

[CreateAssetMenu(fileName = "PieceData", menuName = "Caro/PieceData")]
public class PieceData : ScriptableObject
{
    public string pieceName;
    public Sprite sprite;
    public PiecePassive passive;
    public int energyCost = 0;
    public int id = 0;
}
