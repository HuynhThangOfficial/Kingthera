using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    // ======================================================
    // 💎 SINGLETON
    // ======================================================
    public static UIManager Instance { get; private set; }

    // ======================================================
    // 🖥️ UI REFERENCES
    // ======================================================

    [Header("Board")]
    public Transform boardParent; // Cần để lấy GridLayoutGroup

    [Header("UI - Players")]
    public Text player1NameText;
    public Text player2NameText;
    public Text player1EnergyText, player2EnergyText;
    public Transform player1PieceButtonsParent, player2PieceButtonsParent;

    public Image[] p1Outlines;
    public Image[] p2Outlines;

    public Text timerText;

    [Header("UI - Pause Menu")]
    public GameObject pauseMenuPanel;

    [Header("UI - Colors")]
    public Color p1Color = Color.blue;
    public Color p2Color = Color.red;

    [Header("UI - Message")]
    public Text startMessage;
    public Text skillMessageText;
    private Coroutine hideMessageCoroutine;

    [Header("UI - Win Panel")]
    public GameObject winPanel;
    public Text winText;

    [Header("UI - Win Line")]
    public GameObject winLinePrefab;
    public Transform winLineParent;

    // ======================================================
    // ⚙️ INTERNAL REFERENCES
    // ======================================================

    // Tham chiếu đến logic game để gọi khi button được click
    private GameManager gameManager;
    // Tham chiếu đến bàn cờ (sẽ được GameManager gửi qua)
    private Cell[,] cells;

    private OnlineGameManager onlineGameManager;

    void Awake()
    {
        // Thiết lập Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        if (skillMessageText != null)
        {
            skillMessageText.gameObject.SetActive(false);
        }

        // Lấy tên của scene hiện tại
        string currentSceneName = SceneManager.GetActiveScene().name;

        // Nếu tên scene là "PlayWithBot" (hoặc tên bạn đã đặt cho scene bot)
        if (currentSceneName == "PlayWithBot")
        {
            UIManager.Instance.SetPlayerNames("Player", "Bot");
        }
        else
        {
            // Mặc định cho PvP
            UIManager.Instance.SetPlayerNames("Player 1", "Player 2");
        }

    }

    // GameManager sẽ gọi hàm này lúc Start để đăng ký
    public void RegisterGameManager(GameManager gm, Cell[,] gameCells)
    {
        gameManager = gm;
        cells = gameCells;
    }

    // ======================================================
    // 🖥️ UI FUNCTIONS
    // ======================================================

    public void SetupBoardVisuals(int size, Sprite tileLight, Sprite tileDark)
    {
        GridLayoutGroup grid = boardParent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = size;
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                Image img = cells[r, c].GetComponent<Image>();
                if (img != null)
                {
                    if ((r + c) % 2 == 0)
                        img.sprite = tileLight;
                    else
                        img.sprite = tileDark;

                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                }
            }
        }
        winLineParent.SetAsLastSibling();
    }

    public void RegisterOnlineGameManager(OnlineGameManager ogm, Cell[,] gameCells)
    {
        onlineGameManager = ogm;
        cells = gameCells;
    }

    public void UpdateTimerUI(float timeRemaining)
    {
        if (timerText != null)
        {
            // Làm tròn lên
            int seconds = Mathf.CeilToInt(timeRemaining);
            // Không hiển thị số âm
            seconds = Mathf.Max(0, seconds);

            timerText.text = seconds.ToString();

            // Đổi màu đỏ khi còn ít hơn 15s để báo động
            timerText.color = (seconds <= 15) ? Color.red : Color.white;
        }
    }
    public void ClearAllWinLines()
    {
        if (winLineParent == null) return;

        for (int i = winLineParent.childCount - 1; i >= 0; i--)
        {
            Transform child = winLineParent.GetChild(i);
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }
    }

    // GameManager gọi hàm này để UIManager tự thiết lập các nút bấm
    public void SetupPieceButtons(PieceData[] p1Pieces, PieceData[] p2Pieces)
    {
        // P1
        PieceButton[] p1Btns = player1PieceButtonsParent.GetComponentsInChildren<PieceButton>(true);
        for (int i = 0; i < p1Btns.Length; i++)
        {
            if (i >= 3) break;
            Button b = p1Btns[i].GetComponent<Button>();
            b.GetComponent<Image>().sprite = p1Pieces[i].sprite;
            int index = i;
            b.onClick.RemoveAllListeners();
            // Khi click, gọi lại hàm logic trong GameManager hoặc OnlineGameManager
            b.onClick.AddListener(() => {
                if (gameManager != null) gameManager.OnPieceButtonClicked(1, index);
                else if (onlineGameManager != null) onlineGameManager.OnPieceButtonClicked(1, index);
            });
        }

        // P2
        PieceButton[] p2Btns = player2PieceButtonsParent.GetComponentsInChildren<PieceButton>(true);
        for (int i = 0; i < p2Btns.Length; i++)
        {
            if (i >= 3) break;
            Button b = p2Btns[i].GetComponent<Button>();
            b.GetComponent<Image>().sprite = p2Pieces[i].sprite;
            int index = i;
            b.onClick.RemoveAllListeners();
            // Khi click, gọi lại hàm logic trong GameManager hoặc OnlineGameManager
            b.onClick.AddListener(() => {
                if (gameManager != null) gameManager.OnPieceButtonClicked(2, index);
                else if (onlineGameManager != null) onlineGameManager.OnPieceButtonClicked(2, index);
            });
        }
    }

    public void ShowStartMessage(int currentPlayer)
    {
        if (startMessage == null) return;
        if (currentPlayer == 1)
        {
            startMessage.text = "Player 1 Go First!";
            startMessage.color = Color.blue;
        }
        else
        {
            startMessage.text = "Player 2 Go First!";
            startMessage.color = Color.red;
        }

        startMessage.gameObject.SetActive(true);
        StartCoroutine(HideStartMessageAfterDelay(2f));
    }

    IEnumerator HideStartMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        startMessage.gameObject.SetActive(false);
    }

    public void ShowSkillMessage(string message, float duration = 0f)
    {
        if (skillMessageText == null) return;

        skillMessageText.text = message;
        skillMessageText.gameObject.SetActive(true);

        if (hideMessageCoroutine != null)
        {
            StopCoroutine(hideMessageCoroutine);
        }

        if (duration > 0f)
        {
            hideMessageCoroutine = StartCoroutine(HideMessageAfterDelay(duration));
        }
    }

    public void HideSkillMessage()
    {
        if (skillMessageText == null) return;

        if (hideMessageCoroutine != null)
        {
            StopCoroutine(hideMessageCoroutine);
            hideMessageCoroutine = null;
        }
        skillMessageText.gameObject.SetActive(false);
    }

    private IEnumerator HideMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        skillMessageText.gameObject.SetActive(false);
        hideMessageCoroutine = null;
    }

    // GameManager sẽ cung cấp dữ liệu energy để hàm này hiển thị
    public void UpdateEnergyUI(int p1Energy, int p2Energy)
    {
        player1EnergyText.text = p1Energy.ToString();
        player2EnergyText.text = p2Energy.ToString();
    }

    // Dùng để chỉnh tên cho player
    public void SetPlayerNames(string p1Name, string p2Name)
    {
        if (player1NameText != null) player1NameText.text = p1Name;
        if (player2NameText != null) player2NameText.text = p2Name;
    }

    // GameManager cung cấp dữ liệu lượt chơi để hàm này hiển thị
    public void UpdateTurnUI(int currentPlayer, PieceData lastSelectedP1, PieceData lastSelectedP2, PieceData[] p1Pieces, PieceData[] p2Pieces)
    {
        if (player1PieceButtonsParent != null)
        {
            foreach (Transform t in player1PieceButtonsParent)
            {
                Button b = t.GetComponent<Button>();
                if (b != null) b.interactable = (currentPlayer == 1);
            }
        }

        if (player2PieceButtonsParent != null)
        {
            foreach (Transform t in player2PieceButtonsParent)
            {
                Button b = t.GetComponent<Button>();
                if (b != null) b.interactable = (currentPlayer == 2);
            }
        }

        // Cập nhật viền chọn
        UpdateSelectionOutline(1, lastSelectedP1, p1Pieces);
        UpdateSelectionOutline(2, lastSelectedP2, p2Pieces);
    }

    public void ShowWinUI(int player)
    {
        winPanel.SetActive(true);
        winText.text = $"Player {player} Wins!";
    }

    // Đây là hàm nội bộ của UIManager, được gọi bởi UpdateTurnUI
    private void UpdateSelectionOutline(int player, PieceData currentSelected, PieceData[] playerPieces)
    {
        Image[] outlines = (player == 1) ? p1Outlines : p2Outlines;
        Color outlineColor = (player == 1) ? p1Color : p2Color;

        if (outlines == null) return;

        for (int i = 0; i < 3; i++)
        {
            if (i < outlines.Length && outlines[i] != null)
            {
                bool isSelected = (playerPieces[i] == currentSelected && currentSelected != null);
                outlines[i].gameObject.SetActive(isSelected);
                if (isSelected)
                {
                    outlines[i].color = outlineColor;
                }
            }
        }
    }

    public void TogglePausePanel(bool isPaused)
    {
        pauseMenuPanel.SetActive(isPaused);
    }

    // Hàm này được gọi bởi Button trong Scene
    public void BackToMainMenu()
    {
        Time.timeScale = 1f; // Luôn reset timeScale khi rời scene
        SceneManager.LoadScene("MainMenu");
    }

    // Hàm này được gọi bởi Button trong Scene
    public void ResumeGame()
    {
        // Báo cho GameManager tiếp tục game
        if (gameManager != null)
        {
            gameManager.TogglePause();
        }
    }

    public void UpdateRestrictVisuals(int boardSize, bool isRestricted, int restrictCenterRow, int restrictCenterCol)
    {
        for (int r = 0; r < boardSize; r++)
        {
            for (int c = 0; c < boardSize; c++)
            {
                if (isRestricted)
                {
                    int half = 2;
                    int minR = restrictCenterRow - half;
                    int maxR = restrictCenterRow + half;
                    int minC = restrictCenterCol - half;
                    int maxC = restrictCenterCol + half;
                    bool isInsideArena = (r >= minR && r <= maxR && c >= minC && c <= maxC);
                    cells[r, c].SetRestrictOverlay(!isInsideArena);
                }
                else
                {
                    cells[r, c].SetRestrictOverlay(false);
                }
            }
        }
    }

    // Trong UIManager.cs

    public GameObject DrawWinLine(int startRow, int startCol, int endRow, int endCol, int player)
    {
        if (winLinePrefab == null || cells == null || winLineParent == null) return null;

        // Kiểm tra an toàn
        int size = cells.GetLength(0);
        if (startRow < 0 || startRow >= size || startCol < 0 || startCol >= size ||
            endRow < 0 || endRow >= size || endCol < 0 || endCol >= size) return null;

        if (cells[startRow, startCol] == null || cells[endRow, endCol] == null) return null;

        // Tạo line
        GameObject line = Instantiate(winLinePrefab, winLineParent);

        line.transform.SetAsLastSibling();

        RectTransform rt = line.GetComponent<RectTransform>();
        Image img = line.GetComponent<Image>();
        if (img != null)
        {
            if (player == 1) img.color = Color.blue; else img.color = Color.red;
        }

        // Lấy vị trí
        RectTransform startRect = cells[startRow, startCol].GetComponent<RectTransform>();
        RectTransform endRect = cells[endRow, endCol].GetComponent<RectTransform>();

        if (startRect == null || endRect == null) { Destroy(line); return null; }

        // Chuyển đổi tọa độ sang Local của winLineParent
        Vector3 startLocal = winLineParent.InverseTransformPoint(startRect.position);
        Vector3 endLocal = winLineParent.InverseTransformPoint(endRect.position);

        // Tính trung điểm
        Vector3 midPoint = (startLocal + endLocal) / 2f;

        rt.localPosition = new Vector3(midPoint.x, midPoint.y, 0f);
        rt.localScale = Vector3.one; // Đảm bảo scale chuẩn

        // Tính độ dài và góc
        float length = Vector3.Distance(startLocal, endLocal);
        
        rt.sizeDelta = new Vector2(length + 50f, 30f);

        Vector3 dir = (endLocal - startLocal).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        rt.localRotation = Quaternion.Euler(0, 0, angle);

        return line;
    }

    // GameManager gọi hàm này để hủy gạch nối
    public void DestroyWinLine(GameObject lineVisual)
    {
        if (lineVisual != null)
        {
            Destroy(lineVisual);
        }
    }
}