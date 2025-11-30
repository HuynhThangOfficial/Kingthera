using UnityEngine;
using UnityEngine.UI;

public class MultiplayerUI : MonoBehaviour
{
    [Header("UI References")]
    public InputField ipInput;
    public Button createRoomButton;
    public Button joinRoomButton;
    public Button backButton;

    [Header("Main Menu Reference")]
    public MainMenu mainMenu; // Kéo script MainMenu vào đây

    void Start()
    {
        if (mainMenu == null) mainMenu = FindObjectOfType<MainMenu>();

        // Gán sự kiện cho các nút
        createRoomButton.onClick.AddListener(OnClickCreateRoom);
        joinRoomButton.onClick.AddListener(OnClickJoinRoom);

        // Nút Back trong panel này sẽ gọi hàm Back chung của MainMenu
        // (Bạn cần gán hàm MainMenu.OnBackButtonClicked vào nút này trong Inspector, 
        // hoặc gọi qua code nếu sửa MainMenu.OnBackButtonClicked thành public)
    }

    public void OnClickCreateRoom()
    {
        // 1. Khởi động Host
        MultiplayerRoomManager.Instance.StartHost();

        // 2. Báo cho MainMenu bắt đầu quy trình chọn quân
        if (mainMenu != null)
        {
            mainMenu.StartMultiplayerSelectionAsHost();
        }
    }

    public void OnClickJoinRoom()
    {
        string ip = ipInput.text;
        if (string.IsNullOrEmpty(ip))
        {
            ip = "127.0.0.1"; // Mặc định localhost nếu để trống
        }

        // Client chỉ cần kết nối và chờ đợi
        MultiplayerRoomManager.Instance.StartClient(ip);

        // (Tùy chọn) Disable các nút để tránh bấm nhiều lần
        joinRoomButton.interactable = false;
        createRoomButton.interactable = false;

        Debug.Log("Client đang kết nối tới: " + ip + "... Chờ Host bắt đầu game.");
    }
}