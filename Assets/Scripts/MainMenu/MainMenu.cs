using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Unity.Netcode; // 🔥 BẮT BUỘC CÓ THƯ VIỆN NÀY

public class MainMenu : MonoBehaviour
{
    [Header("Menu Panels")]
    public GameObject mainMenuPanel;
    public GameObject selectPiecesPanel;
    public GameObject difficultyPanel;
    public GameObject multiplayerPanel; // Panel này do MultiplayerUI quản lý hiển thị

    public Text selectTitleText;

    [Header("Piece Selection")]
    public PieceData[] allPieces;
    public Transform pieceGrid;
    public Button nextButton;
    public Button backButtonPVP;

    [Header("Difficulty Panel")]
    public Button backButtonBot;

    [Header("Play With Player Panel")]
    public Button backButtonPWP;

    private List<PieceData> currentSelected = new List<PieceData>();

    // --- CÁC BIẾN LOGIC (Public để MultiplayerUI truy cập) ---
    public bool selectingFirstPhase = true;
    public bool isMultiplayerMode = false;
    public bool isMultiplayerHost = false;

    private bool isVsBotMode = false;
    private List<PieceData> p1Selected = new List<PieceData>();
    private List<PieceData> p2Selected = new List<PieceData>();

    void Start()
    {
        selectPiecesPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (multiplayerPanel != null) multiplayerPanel.SetActive(false);

        mainMenuPanel.SetActive(true);

        nextButton.onClick.AddListener(OnNextButtonClicked);
        backButtonPVP.onClick.AddListener(OnBackButtonClicked);
        backButtonBot.onClick.AddListener(OnBackButtonClicked);
        backButtonPWP.onClick.AddListener(OnBackButtonClicked);
    }

    // ==========================================
    // 1. CHẾ ĐỘ PVP & BOT (OFFLINE)
    // ==========================================
    public void PlayerVsPlayer()
    {
        isVsBotMode = false;
        isMultiplayerMode = false;
        StartPieceSelection();
    }

    public void PlayWithBot_Click()
    {
        mainMenuPanel.SetActive(false);
        difficultyPanel.SetActive(true);
    }

    public void SelectEasyBot()
    {
        PlayerPrefs.SetInt("IsHardBot", 0);
        StartBotModeWithSelection();
    }

    public void SelectHardBot()
    {
        PlayerPrefs.SetInt("IsHardBot", 1);
        StartBotModeWithSelection();
    }

    void StartBotModeWithSelection()
    {
        isVsBotMode = true;
        isMultiplayerMode = false;
        difficultyPanel.SetActive(false);
        StartPieceSelection();
    }

    // ==========================================
    // 2. CHẾ ĐỘ MULTIPLAYER (ONLINE)
    // ==========================================

    // Hàm này được gọi khi bấm nút "Multiplayer" ở Menu chính
    public void OpenMultiplayerMenu()
    {
        mainMenuPanel.SetActive(false);
        multiplayerPanel.SetActive(true); // Mở panel nhập IP
    }

    // Hàm này được MultiplayerUI gọi sau khi tạo Host thành công
    public void StartMultiplayerSelectionAsHost()
    {
        isMultiplayerMode = true;
        isMultiplayerHost = true;
        isVsBotMode = false;

        // Ẩn panel nhập IP, hiện panel chọn quân
        multiplayerPanel.SetActive(false);
        StartPieceSelection();
    }

    // ==========================================
    // LOGIC CHỌN QUÂN (CORE)
    // ==========================================
    void StartPieceSelection()
    {
        mainMenuPanel.SetActive(false);
        selectPiecesPanel.SetActive(true);
        selectingFirstPhase = true;
        SetupGrid();
        UpdateTitle();
    }

    public void SetupGrid()
    {
        currentSelected.Clear();
        foreach (Transform t in pieceGrid) Destroy(t.gameObject);

        foreach (var piece in allPieces)
        {
            GameObject btnObj = new GameObject(piece.pieceName, typeof(RectTransform), typeof(Button), typeof(Image), typeof(PieceButton));
            btnObj.transform.SetParent(pieceGrid, false);

            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.GetComponent<Image>();
            img.sprite = piece.sprite;
            img.color = Color.white;

            PieceButton pieceBtn = btnObj.GetComponent<PieceButton>();
            pieceBtn.pieceData = piece;

            btn.onClick.AddListener(() => OnPieceClicked(piece, img));
        }
        nextButton.interactable = false;
    }

    void OnPieceClicked(PieceData piece, Image img)
    {
        if (currentSelected.Contains(piece))
        {
            currentSelected.Remove(piece);
            img.color = Color.white;
        }
        else
        {
            if (currentSelected.Count >= 3) return;
            currentSelected.Add(piece);
            img.color = Color.green;
        }
        nextButton.interactable = currentSelected.Count == 3;
    }

    void OnNextButtonClicked()
    {
        if (currentSelected.Count != 3) return;

        if (selectingFirstPhase)
        {
            p1Selected = new List<PieceData>(currentSelected);
            selectingFirstPhase = false;
            SetupGrid();
            UpdateTitle();
        }
        else
        {
            p2Selected = new List<PieceData>(currentSelected);
            StartGame();
        }
    }

    void StartGame()
    {
        // Lưu dữ liệu vào class tĩnh để chuyển scene
        PieceSelectionData.player1Pieces = p1Selected.ToArray();
        PieceSelectionData.player2Pieces = p2Selected.ToArray();
        PieceSelectionData.isPlayWithBot = isVsBotMode;

        // --- XỬ LÝ CHUYỂN SCENE ---
        if (isMultiplayerMode)
        {
            if (isMultiplayerHost)
            {
                // 🔥 SỬA: Gọi hàm Sync mới (không cần ServerRpc ở đuôi nữa)
                MultiplayerRoomManager.Instance.SyncDataToClients(
                    PieceSelectionData.player1Pieces,
                    PieceSelectionData.player2Pieces
                );

                // Chuyển scene
                NetworkManager.Singleton.SceneManager.LoadScene("PlayWithPlayer", LoadSceneMode.Single);
            }
        }
        else if (isVsBotMode)
        {
            SceneManager.LoadScene("PlayWithBot");
        }
        else
        {
            SceneManager.LoadScene("PlayerVSPlayer");
        }
    }

    void OnBackButtonClicked()
    {
        if (selectPiecesPanel.activeSelf && !selectingFirstPhase)
        {
            selectingFirstPhase = true;
            SetupGrid();
            UpdateTitle();
            return;
        }

        // Reset trạng thái
        selectPiecesPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (multiplayerPanel != null) multiplayerPanel.SetActive(false);

        // Nếu đang là Host mà thoát ra thì tắt Host luôn
        if (isMultiplayerMode && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        isMultiplayerMode = false;
        mainMenuPanel.SetActive(true);
    }

    public void UpdateTitle()
    {
        if (selectingFirstPhase)
        {
            selectTitleText.text = "Player 1 - Select 3 Caro";
            nextButton.GetComponentInChildren<Text>().text = "Next";
        }
        else
        {
            if (isVsBotMode)
            {
                selectTitleText.text = "Select 3 Caro For Bot";
            }
            else if (isMultiplayerMode)
            {
                selectTitleText.text = "Select 3 Caro For P2 (Host)";
            }
            else
            {
                selectTitleText.text = "Player 2 - Select 3 Caro";
            }
            nextButton.GetComponentInChildren<Text>().text = "Start Game";
        }
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}