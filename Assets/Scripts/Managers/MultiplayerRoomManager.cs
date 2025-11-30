using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Collections.Generic;
using System.Linq;

// 🔥 QUAN TRỌNG: Phải kế thừa từ NetworkBehaviour
public class MultiplayerRoomManager : NetworkBehaviour
{
    public static MultiplayerRoomManager Instance;

    // 🔥 Cần danh sách gốc để tra cứu (Kéo tất cả PieceData vào đây trong Inspector)
    public PieceData[] allPiecesLibrary;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject); // Giữ lại khi chuyển scene
    }

    // 👉 Host tạo room LAN
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log("Host started!");
    }

    // 👉 Client join theo IP LAN
    public void StartClient(string ipAddress)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = ipAddress;
        }
        NetworkManager.Singleton.StartClient();
        Debug.Log("Client connecting to " + ipAddress);
    }

    // =======================================================================
    // 🔥 LOGIC ĐỒNG BỘ DATA (SỬA LẠI ĐỂ GỬI ID THAY VÌ OBJECT)
    // =======================================================================

    // Hàm này được gọi từ MainMenu (Host)
    public void SyncDataToClients(PieceData[] p1Pieces, PieceData[] p2Pieces)
    {
        // 1. Chuyển đổi PieceData thành mảng ID (int)
        int[] p1IDs = ConvertPiecesToIDs(p1Pieces);
        int[] p2IDs = ConvertPiecesToIDs(p2Pieces);

        // 2. Gửi ID qua mạng
        SyncPieceSelectionDataClientRpc(p1IDs, p2IDs);
    }

    // [ServerRpc] không cần thiết ở đây vì Host gọi trực tiếp, 
    // và ClientRpc sẽ được gửi từ Host tới tất cả Client.

    [ClientRpc]
    private void SyncPieceSelectionDataClientRpc(int[] p1IDs, int[] p2IDs)
    {
        // 3. Client nhận ID và chuyển ngược lại thành PieceData
        PieceSelectionData.player1Pieces = ConvertIDsToPieces(p1IDs);
        PieceSelectionData.player2Pieces = ConvertIDsToPieces(p2IDs);

        // Đánh dấu là đang chơi Online
        PieceSelectionData.isPlayWithBot = false;

        Debug.Log($"Client received data: P1({p1IDs.Length}), P2({p2IDs.Length})");
    }

    // --- CÁC HÀM HỖ TRỢ CHUYỂN ĐỔI ---

    private int[] ConvertPiecesToIDs(PieceData[] pieces)
    {
        int[] ids = new int[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            ids[i] = pieces[i].id; // Lấy ID từ PieceData
        }
        return ids;
    }

    private PieceData[] ConvertIDsToPieces(int[] ids)
    {
        PieceData[] pieces = new PieceData[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            // Tìm trong thư viện xem con nào có ID trùng khớp
            pieces[i] = allPiecesLibrary.FirstOrDefault(p => p.id == ids[i]);
        }
        return pieces;
    }
}