using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("AI Settings")]
    public bool isVsBot = false;       // Check cái này ở Inspector nếu muốn chơi với Bot
    public bool isHardBot = true;      // Chế độ khó
    public AIController aiController;  // Kéo file AIController vào đây hoặc AddComponent

    // ======================================================
    // 🎮 INSPECTOR VARIABLES
    // ======================================================

    [Header("Board Settings")]
    public GameObject cellPrefab;
    public int boardSize = 30;
    public Sprite tileLight, tileDark;

    [Header("VFX Prefabs")]
    public GameObject explosionPrefab;
    public GameObject winLineExplosionPrefab;

    [Header("Piece Data")]
    public int burnTurns = 2;

    // ======================================================
    // 📊 INTERNAL DATA
    // ======================================================

    private Cell[,] cells;
    private CellState[,] state;
    private int currentPlayer = 1;
    private bool isPaused = false;

    // Energy System
    private int player1PermanentEnergy, player2PermanentEnergy;
    private int player1TemporaryEnergy, player2TemporaryEnergy;
    private int player1EnergySpent, player2EnergySpent;
    private (int r, int c)? player1EnergySpendAssigned, player2EnergySpendAssigned;

    // Piece & Turn Tracking
    private PieceData[] p1Pieces = new PieceData[3];
    private PieceData[] p2Pieces = new PieceData[3];
    private PieceData lastSelectedP1, lastSelectedP2, selectedPiece;
    private int[] p1PlayCounts = new int[3], p2PlayCounts = new int[3];
    private int[] p1TurnsSincePlayed = new int[3], p2TurnsSincePlayed = new int[3];
    private bool[] p1TypeCompleted = new bool[3], p2TypeCompleted = new bool[3];
    private bool[] p1PyroActive = new bool[3], p2PyroActive = new bool[3];
    public bool hasActivatedCrossStrike = false;
    private List<ActiveWinLine> activeWinLines = new List<ActiveWinLine>();

    // Restrictions
    private bool restrictOpponentNextTurn = false;
    private int restrictCenterRow = -1, restrictCenterCol = -1, restrictNextPlayer = 0;
    private HashSet<(int, int)> forbiddenThisTurn = new(), forbiddenNextTurnPositions = new();

    private HashSet<(int, int)> forbiddenForOpponentNextTurn = new();
    private HashSet<(int, int)> forbiddenForOpponentThisTurn = new();

    // Active Skills
    private bool isTeleportMode = false;
    private Cell teleportSourceCell = null;

    private bool isSwapMode = false;
    private Cell swapCell1 = null;

    // ======================================================
    // 🧩 INITIALIZATION & GAME SETUP
    // ======================================================

    void Start()
    {
        this.isVsBot = PieceSelectionData.isPlayWithBot;
        
        // 1. Lấy dữ liệu quân cờ
        if (PieceSelectionData.player1Pieces[0] != null && PieceSelectionData.player2Pieces[0] != null)
        {
            p1Pieces = PieceSelectionData.player1Pieces;
            p2Pieces = PieceSelectionData.player2Pieces;
        }

        // 2. Tạo bàn cờ
        CreateBoard(boardSize);

        // 3. Đăng ký GameManager với UIManager và gửi cho UI biết các ô cờ để nó vẽ
        UIManager.Instance.RegisterGameManager(this, cells);

        // 4. Nhờ UIManager thiết lập hình ảnh ô cờ
        UIManager.Instance.SetupBoardVisuals(boardSize, tileLight, tileDark);

        // 5. Nhờ UIManager thiết lập các nút bấm
        UIManager.Instance.SetupPieceButtons(p1Pieces, p2Pieces);

        // 6. Bắt đầu game
        // --- SỬA ĐOẠN CHỌN NGƯỜI ĐI TRƯỚC ---
        if (isVsBot)
        {
            currentPlayer = 1; // Nếu chơi với Bot, Player 1 luôn đi trước
            Debug.Log("Mode: Vs Bot. Player 1 (Human) đi trước.");
        }
        else
        {
            currentPlayer = Random.Range(1, 3); // Nếu PvP thì hên xui
            Debug.Log("Mode: PvP. Random người đi trước: Player " + currentPlayer);
        }
        // -------------------------------------
        StartNewGame();

        // 7. Nhờ UIManager hiển thị thông báo bắt đầu
        UIManager.Instance.ShowStartMessage(currentPlayer);

        // --- ADDED FOR AI ---
        // Tự động thêm AIController nếu chưa có
        if (aiController == null) aiController = gameObject.AddComponent<AIController>();
        aiController.Init(this, boardSize);
        
        // Nếu mode Vs Bot, đảm bảo Bot là Player 2
        // Và tắt nút bấm của Player 2 đi (đã xử lý trong UpdateTurnUI nhưng cẩn thận vẫn hơn)
        if (isVsBot)
        {
            // Đọc độ khó từ PlayerPrefs
            int difficulty = PlayerPrefs.GetInt("IsHardBot", 1);
            isHardBot = (difficulty == 1);
            Debug.Log("Bot Difficulty: " + (isHardBot ? "Hard" : "Easy"));
        }
        // --------------------
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePause();
        }

        if (Input.GetMouseButtonDown(1)) // 1 là phím chuột phải
        {
            // Kiểm tra xem có đang ở chế độ skill nào không
            if (isTeleportMode || isSwapMode)
            {
                CancelActiveSkillMode();
            }
        }

        // --- ADDED FOR AI ---
        // Nếu đang lượt Bot và chưa Pause, thì KHÔNG cho người chơi click chuột trái
        if (isVsBot && currentPlayer == 2 && !isPaused)
        {
            return; 
        }
    }

    void StartNewGame()
    {
        player1PermanentEnergy = 0;
        player2PermanentEnergy = 0;
        player1TemporaryEnergy = 1;
        player2TemporaryEnergy = 1;

        player1EnergySpent = player2EnergySpent = 0;
        player1EnergySpendAssigned = null;
        player2EnergySpendAssigned = null;

        selectedPiece = null;

        for (int i = 0; i < 3; i++)
        {
            p1TypeCompleted[i] = false;
            p2TypeCompleted[i] = false;
            p1TurnsSincePlayed[i] = 0;
            p2TurnsSincePlayed[i] = 0;
            p1PyroActive[i] = false;
            p2PyroActive[i] = false;
        }

        lastSelectedP1 = null;
        lastSelectedP2 = null;

        // Reset Restrictions
        forbiddenThisTurn.Clear();
        forbiddenNextTurnPositions.Clear();
        forbiddenForOpponentNextTurn.Clear();
        forbiddenForOpponentThisTurn.Clear();

        for (int r = 0; r < boardSize; r++)
        {
            for (int c = 0; c < boardSize; c++)
            {
                if (cells[r, c] != null)
                {
                    cells[r, c].SetLockIcon(false);
                    cells[r, c].SetLavaVisual(false);
                }
                state[r, c].isLava = false;
            }
        }

        restrictOpponentNextTurn = false;
        restrictNextPlayer = 0;

        // Dọn dẹp các line thắng cũ
        foreach (var line in activeWinLines)
        {
            UIManager.Instance.DestroyWinLine(line.lineVisual);
        }
        activeWinLines.Clear();

        // Cập nhật UI sau khi reset logic
        UpdateAllUI();

        Debug.Log("Cả hai người chơi đều có 1 năng lượng tạm thời để chọn quân đầu tiên!");
    }


    // ======================================================
    // 🖥️ UI FUNCTIONS
    // ======================================================

    // Hàm tiện ích để gọi tất cả update UI
    void UpdateAllUI()
    {
        int total1 = player1TemporaryEnergy + player1PermanentEnergy;
        int total2 = player2TemporaryEnergy + player2PermanentEnergy;
        UIManager.Instance.UpdateEnergyUI(total1, total2);

        UIManager.Instance.UpdateTurnUI(currentPlayer, lastSelectedP1, lastSelectedP2, p1Pieces, p2Pieces);
    }

    public void TogglePause()
    {
        isPaused = !isPaused;

        if (isPaused)
        {
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = 1f;
        }
        UIManager.Instance.TogglePausePanel(isPaused);
    }


    // ======================================================
    // ⚡ ENERGY SYSTEM
    // ======================================================

    int GetTotalEnergyForPlayer(int player)
    {
        return (player == 1)
            ? (player1TemporaryEnergy + player1PermanentEnergy)
            : (player2TemporaryEnergy + player2PermanentEnergy);
    }

    void SpendEnergy(int player, int cost)
    {
        int originalCost = cost;
        if (player == 1)
        {
            int used = Mathf.Min(cost, player1TemporaryEnergy);
            player1TemporaryEnergy -= used;
            cost -= used;
            if (cost > 0) player1PermanentEnergy -= cost;

            if (player1EnergySpendAssigned != null)
            {
                player1EnergySpent += originalCost;
                while (player1EnergySpent >= 3)
                {
                    player1PermanentEnergy += 1;
                    player1EnergySpent -= 3;
                    Debug.Log("Player1 nhận 1 energy vĩnh viễn từ EnergySpendGrant.");
                }
            }
        }
        else
        {
            int used = Mathf.Min(cost, player2TemporaryEnergy);
            player2TemporaryEnergy -= used;
            cost -= used;
            if (cost > 0) player2PermanentEnergy -= cost;

            if (player2EnergySpendAssigned != null)
            {
                player2EnergySpent += originalCost;
                while (player2EnergySpent >= 3)
                {
                    player2PermanentEnergy += 1;
                    player2EnergySpent -= 3;
                    Debug.Log("Player2 nhận 1 energy vĩnh viễn từ EnergySpendGrant.");
                }
            }
        }
    }


    // ======================================================
    // 🎯 GAMEPLAY CORE
    // ======================================================

    // Hàm này được gọi bởi UIManager
    public void OnPieceButtonClicked(int player, int index)
    {
        if (player != currentPlayer) return;

        PieceData newPiece = (player == 1) ? p1Pieces[index] : p2Pieces[index];
        PieceData currentSelectedPiece = (player == 1) ? lastSelectedP1 : lastSelectedP2;

        // Kỹ năng teleport
        if (newPiece.passive == PiecePassive.Active_TeleportAlly)
        {
            if (newPiece == currentSelectedPiece)
            {
                if (GetTotalEnergyForPlayer(currentPlayer) >= 1)
                {

                    isTeleportMode = true;
                    teleportSourceCell = null;
                    isSwapMode = false;
                    swapCell1 = null;

                    UIManager.Instance.ShowSkillMessage("Kỹ năng Dịch chuyển: Chọn 1 quân đồng minh trên bàn", 0);

                    return; // Thoát ra, người chơi đang ở chế độ Dịch chuyển
                }
                else
                {
                    UIManager.Instance.ShowSkillMessage("Không đủ năng lượng để dùng kỹ năng", 3f);
                    return;
                }
            }
        }
        // Kỹ năng hoán đổi
        else if (newPiece.passive == PiecePassive.Active_SwapAllies)
        {
            if (newPiece == currentSelectedPiece && currentSelectedPiece != null)
            {
                if (GetTotalEnergyForPlayer(currentPlayer) >= 1)
                {
                    isSwapMode = true; // Bật Swap
                    swapCell1 = null;
                    isTeleportMode = false; // Tắt Teleport
                    teleportSourceCell = null;

                    UIManager.Instance.ShowSkillMessage("Kỹ năng Hoán đổi: Chọn quân đồng minh thứ 1", 0);

                    return;
                }
                else
                {
                    UIManager.Instance.ShowSkillMessage("Không đủ năng lượng để dùng kỹ năng", 3f);
                    return;
                }
            }
        }

        if (player == 1)
        {
            if (lastSelectedP1 == null)
            {
                if (player1TemporaryEnergy >= 1)
                {
                    player1TemporaryEnergy -= 1;
                    lastSelectedP1 = newPiece;
                }
                else return;
            }
            else if (lastSelectedP1 != newPiece)
            {
                if (GetTotalEnergyForPlayer(1) >= 1)
                {
                    SpendEnergy(1, 1);
                    lastSelectedP1 = newPiece;
                }
                else return;
            }
        }
        else // player 2
        {
            if (lastSelectedP2 == null)
            {
                if (player2TemporaryEnergy >= 1)
                {
                    player2TemporaryEnergy -= 1;
                    lastSelectedP2 = newPiece;
                }
                else return;
            }
            else if (lastSelectedP2 != newPiece)
            {
                if (GetTotalEnergyForPlayer(2) >= 1)
                {
                    SpendEnergy(2, 1);
                    lastSelectedP2 = newPiece;
                }
                else return;
            }
        }

        selectedPiece = newPiece;

        // Cập nhật UI
        UpdateAllUI();
    }


    public void OnCellClicked(Cell cell)
    {
        // Xử lý khi người chơi đang ở chế độ Hoán Đổi.
        if (isSwapMode)
        {
            // Chọn ô có caro đồng minh thứ nhất
            if (swapCell1 == null)
            {
                if (cell.IsEmpty() || state[cell.row, cell.col].owner != currentPlayer)
                {
                    UIManager.Instance.ShowSkillMessage("Hoán đổi: Phải chọn 1 quân đồng minh", 3f);
                    return;
                }

                swapCell1 = cell;
                UIManager.Instance.ShowSkillMessage("Chọn quân đồng minh thứ 2", 3f);
                return;
            }
            // Chọn ô có caro đồng minh thứ hai
            else
            {
                if (cell.IsEmpty() || state[cell.row, cell.col].owner != currentPlayer)
                {
                    UIManager.Instance.ShowSkillMessage("Phải chọn quân đồng minh thứ 2", 3f);
                    return;
                }
                if (cell == swapCell1)
                {
                    UIManager.Instance.ShowSkillMessage("Không thể chọn cùng 1 quân", 3f);
                    return;
                }

                // Thực hiện hoán đổi
                Cell cell2 = cell;
                Debug.Log("Thực hiện hoán đổi!");

                // Lấy state của cả 2 ô
                CellState state1 = state[swapCell1.row, swapCell1.col];
                CellState state2 = state[cell2.row, cell2.col];

                // Lấy data (phải lấy trước khi ClearCell)
                PieceData piece1 = state1.pieceData;
                PieceData piece2 = state2.pieceData;

                // Xóa cả 2 ô
                swapCell1.ClearCell();
                cell2.ClearCell();

                // Đặt quân chéo (Gán lại state để giữ burn/deathmark)
                swapCell1.PlacePiece(piece2.sprite, state2.owner, piece2);
                state[swapCell1.row, swapCell1.col] = state2;

                cell2.PlacePiece(piece1.sprite, state1.owner, piece1);
                state[cell2.row, cell2.col] = state1;

                // Áp dụng lại visual cho cả 2
                if (state2.deathMarkCount > 0) swapCell1.ShowDeathMarkVisual();
                if (state2.burnTurns > 0) swapCell1.ShowBurnVisual();
                if (state1.deathMarkCount > 0) cell2.ShowDeathMarkVisual();
                if (state1.burnTurns > 0) cell2.ShowBurnVisual();

                SpendEnergy(currentPlayer, 1);
                UpdateAllUI();

                // Thoát chế độ
                isSwapMode = false;
                swapCell1 = null;
                UIManager.Instance.ShowSkillMessage("Hoán đổi thành công", 2f);
                return;
            }
        }

        // Xử lý skill dịch chuyển
        if (isTeleportMode)
        {
            // Chọn ô caro có đồng minh
            if (teleportSourceCell == null)
            {
                // Kiểm tra xem ô đó có quân và là đồng minh không
                if (cell.IsEmpty() || state[cell.row, cell.col].owner != currentPlayer)
                {
                    UIManager.Instance.ShowSkillMessage("Phải chọn 1 quân đồng minh", 3f);
                    return;
                }

                teleportSourceCell = cell;

                UIManager.Instance.ShowSkillMessage("Hãy chọn ô trống để dịch chuyển đến", 3f);
                return; // Chờ click tiếp theo
            }
            // Chọn ô bất kỳ
            else
            {
                // Kiểm tra xem có click vào ô trống không
                if (!cell.IsEmpty())
                {
                    UIManager.Instance.ShowSkillMessage("Phải chọn 1 ô trống", 3f);
                    return;
                }

                // --- THỰC HIỆN DỊCH CHUYỂN ---
                Debug.Log("Thực hiện dịch chuyển!");

                // Lấy dữ liệu (state) của ô nguồn (để giữ burn, deathmark)
                CellState sourceState = state[teleportSourceCell.row, teleportSourceCell.col];

                // Xóa ô nguồn (ClearCell sẽ tự gọi ClearCellState, không kích hoạt on-death)
                teleportSourceCell.ClearCell();

                // Đặt quân vào ô bất kỳ (PlacePiece sẽ tạo 1 state mới)
                cell.PlacePiece(sourceState.pieceData.sprite, sourceState.owner, sourceState.pieceData);

                // Ghi đè state mới bằng state cũ (để giữ lại burn, deathmark)
                state[cell.row, cell.col] = sourceState;

                if (sourceState.deathMarkCount > 0)
                {
                    cell.ShowDeathMarkVisual();
                }

                if (sourceState.burnTurns > 0)
                {
                    cell.ShowBurnVisual();
                }

                SpendEnergy(currentPlayer, 1);
                UpdateAllUI();

                // Thoát chế độ
                isTeleportMode = false;
                teleportSourceCell = null;

                UIManager.Instance.ShowSkillMessage("Dịch chuyển thành công", 2f);

                return;
            }
        }

        // KIỂM TRA ĐẦU VÀO CƠ BẢN
        if (selectedPiece == null)
        {
            Debug.Log("Chưa chọn con caro để đặt.");
            return;
        }
        if (cell == null) return;

        if (state[cell.row, cell.col].isLava)
        {
            Debug.Log("Ô này là DUNG NHAM! Không thể đặt quân.");
            return;
        }
        // XỬ LÝ LOGIC ĐẶT QUÂN (2 TRƯỜNG HỢP)

        // TRƯỜNG HỢP 1: Ô KHÔNG TRỐNG (CỐ ĐÈ LÊN)
        if (!cell.IsEmpty())
        {
            if (selectedPiece.passive == PiecePassive.AllyReplace &&
                state[cell.row, cell.col].owner == currentPlayer)
            {
                int costReplace = selectedPiece.energyCost;
                if (GetTotalEnergyForPlayer(currentPlayer) < costReplace)
                {
                    Debug.Log("Không đủ năng lượng để đặt con này.");
                    return;
                }

                Debug.Log($"{selectedPiece.pieceName} đè lên quân đồng minh tại ({cell.row},{cell.col})");
                SpendEnergy(currentPlayer, costReplace);

                bool triggeredExplosion = (state[cell.row, cell.col].pieceData != null &&
                                              state[cell.row, cell.col].pieceData.passive == PiecePassive.ExplodeOnDeath);

                DestroyCellAt(cell.row, cell.col, false, true, true);
                cell.PlacePiece(selectedPiece.sprite, currentPlayer, selectedPiece);

                if (triggeredExplosion)
                {
                    Debug.Log($"💥 {selectedPiece.pieceName} (quân vừa đặt) đã bị nổ ngay lập tức!");
                    StartCoroutine(DelayedDestroy(cell.row, cell.col, 0.1f, false, false, false));
                }
            }
            else
            {
                return;
            }
        }
        // TRƯỜNG HỢP 2: Ô TRỐNG
        else
        {
            if (forbiddenThisTurn.Contains((cell.row, cell.col))) { Debug.Log("Ô này bị cấm."); return; }
            if (forbiddenForOpponentThisTurn.Contains((cell.row, cell.col))) { Debug.Log("Ô này bị IsolationLock."); return; }

            if (restrictOpponentNextTurn && restrictNextPlayer == currentPlayer)
            {
                int half = 2;
                int minR = Mathf.Clamp(restrictCenterRow - half, 0, boardSize - 1);
                int maxR = Mathf.Clamp(restrictCenterRow + half, 0, boardSize - 1);
                int minC = Mathf.Clamp(restrictCenterCol - half, 0, boardSize - 1);
                int maxC = Mathf.Clamp(restrictCenterCol + half, 0, boardSize - 1);
                if (!(cell.row >= minR && cell.row <= maxR && cell.col >= minC && cell.col <= maxC))
                {
                    Debug.Log("Bạn chỉ được đặt trong phạm vi 5x5.");
                    return;
                }
            }

            int cost = selectedPiece.energyCost;
            if (GetTotalEnergyForPlayer(currentPlayer) < cost)
            {
                Debug.Log("Không đủ năng lượng để đặt con này.");
                return;
            }

            SpendEnergy(currentPlayer, cost);
            cell.PlacePiece(selectedPiece.sprite, currentPlayer, selectedPiece);
        }

        // Sau khi đặt quân xong

        // (Pyro counters)
        if (currentPlayer == 1)
        {
            for (int i = 0; i < 3; i++) { if (p1Pieces[i] == selectedPiece) p1TurnsSincePlayed[i] = 0; else p1TurnsSincePlayed[i]++; }
        }
        else
        {
            for (int i = 0; i < 3; i++) { if (p2Pieces[i] == selectedPiece) p2TurnsSincePlayed[i] = 0; else p2TurnsSincePlayed[i]++; }
        }

        HandlePassiveOnPlace(selectedPiece, cell.row, cell.col, currentPlayer);

        // (Kiểm tra WinLine)
        List<(int, int)> cellsInLine = null;
        (int, int) start = (0, 0);
        (int, int) end = (0, 0);
        PieceData pieceThatWon = null;

        if (selectedPiece.passive == PiecePassive.WildCardCaro)
        {
            Debug.Log("Wildcard đã đặt. Kiểm tra như là các quân đồng minh...");
            PieceData[] alliedPieces = (currentPlayer == 1) ? p1Pieces : p2Pieces;
            foreach (PieceData alliedPiece in alliedPieces)
            {
                if (alliedPiece.passive == PiecePassive.WildCardCaro) continue;
                var (tempLine, tempStart, tempEnd) = CheckFiveInRow(cell.row, cell.col, currentPlayer, alliedPiece);
                if (tempLine != null)
                {
                    cellsInLine = tempLine; start = tempStart; end = tempEnd;
                    pieceThatWon = alliedPiece;
                    Debug.Log($"Wildcard đã hoàn thành 1 line của {alliedPiece.pieceName}!");
                    break;
                }
            }
        }
        else
        {
            (cellsInLine, start, end) = CheckFiveInRow(cell.row, cell.col, currentPlayer, selectedPiece);
            if (cellsInLine != null) { pieceThatWon = selectedPiece; }
        }
        if (cellsInLine != null)
        {
            int typeIndex = GetTypeIndexForPlayer(currentPlayer, pieceThatWon);
            bool alreadyCompleted = (currentPlayer == 1) ? p1TypeCompleted[typeIndex] : p2TypeCompleted[typeIndex];
            if (!alreadyCompleted)
            {
                MarkTypeCompleted(currentPlayer, pieceThatWon);
                GameObject lineVisual = UIManager.Instance.DrawWinLine(start.Item1, start.Item2, end.Item1, end.Item2, currentPlayer);
                ActiveWinLine newLine = new ActiveWinLine { player = currentPlayer, pieceTypeIndex = typeIndex, lineVisual = lineVisual, cells = cellsInLine };
                activeWinLines.Add(newLine);
                if (pieceThatWon.passive == PiecePassive.WinLineExplode)
                {
                    Debug.Log($"💥 {pieceThatWon.pieceName} giành lợi thế và kích hoạt nổ!");
                    HashSet<(int, int)> cellsToDestroy = new HashSet<(int, int)>();
                    Vector2Int[] crossDirs = new Vector2Int[] { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };
                    foreach (var cellPos in cellsInLine)
                    {
                        int r = cellPos.Item1; int c = cellPos.Item2;
                        foreach (var dir in crossDirs)
                        {
                            int nr = r + dir.x; int nc = c + dir.y;
                            if (IsInBounds(nr, nc) && state[nr, nc].owner != 0 && !cellsInLine.Contains((nr, nc)))
                            {
                                cellsToDestroy.Add((nr, nc));
                            }
                        }
                    }
                    foreach (var cellToDestroy in cellsToDestroy)
                    {
                        PlayWinLineExplosion(cellToDestroy.Item1, cellToDestroy.Item2);
                        StartCoroutine(DelayedDestroy(cellToDestroy.Item1, cellToDestroy.Item2, 0.1f, true, true, true));
                    }
                }
            }
        }

        // (check victory)
        if (CheckVictory(currentPlayer))
        {
            UIManager.Instance.ShowWinUI(currentPlayer);
            return;
        }

        EndTurnAndSwitch();
    }

    void EndTurnAndSwitch()
    {
        isTeleportMode = false;
        teleportSourceCell = null;

        isSwapMode = false;
        swapCell1 = null;

        UIManager.Instance.HideSkillMessage();

        if (currentPlayer == 1) player1TemporaryEnergy = 0; else player2TemporaryEnergy = 0;
        currentPlayer = (currentPlayer == 1) ? 2 : 1;

        // Cập nhật lại quân đang được "cầm" sang cho người chơi mới
        if (currentPlayer == 1)
            selectedPiece = lastSelectedP1; // Nạp lại quân P1 đã chọn
        else
            selectedPiece = lastSelectedP2; // Nạp lại quân P2 đã chọn

        // ----- PYRORAGE -----
        if (currentPlayer == 1)
        {
            for (int i = 0; i < 3; i++)
            {
                if (p1Pieces[i].passive == PiecePassive.PyroRage)
                {
                    if (p1TurnsSincePlayed[i] >= 4 && !p1PyroActive[i])
                    {
                        p1PyroActive[i] = true;
                        Debug.Log($"Player1: {p1Pieces[i].pieceName} đã kích hoạt Pyro Rage!");
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                if (p2Pieces[i].passive == PiecePassive.PyroRage)
                {
                    if (p2TurnsSincePlayed[i] >= 4 && !p2PyroActive[i])
                    {
                        p2PyroActive[i] = true;
                        Debug.Log($"Player2: {p2Pieces[i].pieceName} đã kích hoạt Pyro Rage!");
                    }
                }
            }
        }

        ApplyNextTurnForbidden();
        ProcessBurningCells(currentPlayer);

        if (currentPlayer == 1) player1TemporaryEnergy = 1; else player2TemporaryEnergy = 1;

        // Cập nhật UI khi đổi lượt
        UpdateAllUI();

        // Xử lý logic RestrictArea
        if (restrictOpponentNextTurn && restrictNextPlayer != currentPlayer)
        {
            restrictOpponentNextTurn = false;
            restrictNextPlayer = 0;
            restrictCenterRow = -1; restrictCenterCol = -1;
        }

        // Báo UIManager cập nhật visual RestrictArea
        bool isRestricted = restrictOpponentNextTurn && restrictNextPlayer == currentPlayer;
        UIManager.Instance.UpdateRestrictVisuals(boardSize, isRestricted, restrictCenterRow, restrictCenterCol);

        // --- ADDED FOR AI ---
        // Sau khi đổi lượt xong, kiểm tra xem có phải lượt Bot không
        if (isVsBot && currentPlayer == 2)
        {
            StartCoroutine(BotTurnRoutine());
        }
    }


    // ======================================================
    // 🔥 SPECIAL EFFECTS / PASSIVES
    // ======================================================

    void HandlePassiveOnPlace(PieceData data, int r, int c, int owner)
    {
        switch (data.passive)
        {
            case PiecePassive.RestrictArea:
                restrictOpponentNextTurn = true;
                restrictCenterRow = r; restrictCenterCol = c;
                restrictNextPlayer = (owner == 1) ? 2 : 1;
                Debug.Log($"Player {owner} đặt RestrictArea -> đối thủ lượt sau bị giới hạn 5x5.");
                break;
            case PiecePassive.GainEnergyEvery2Plays:
                int idx = GetTypeIndexForPlayer(owner, data);
                if (idx >= 0)
                {
                    if (owner == 1)
                    {
                        p1PlayCounts[idx]++;
                        if (p1PlayCounts[idx] % 2 == 0)
                        {
                            player1PermanentEnergy++;
                            Debug.Log("Player1 nhận +1 permanent energy từ GainEnergyEvery2Plays.");
                        }
                    }
                    else
                    {
                        p2PlayCounts[idx]++;
                        if (p2PlayCounts[idx] % 2 == 0)
                        {
                            player2PermanentEnergy++;
                            Debug.Log("Player2 nhận +1 permanent energy từ GainEnergyEvery2Plays.");
                        }
                    }
                }
                break;
            case PiecePassive.UnblockableOnFour:
                var ends = GetChainEndsIfLengthN(r, c, owner, 4);
                if (ends != null && ends.Count == 2)
                {
                    forbiddenNextTurnPositions.Clear();
                    foreach (var pos in ends)
                    {
                        if (pos.Item1 >= 0 && pos.Item2 >= 0)
                            forbiddenNextTurnPositions.Add(pos);
                    }
                    Debug.Log("UnblockableOnFour: chặn 2 đầu chuỗi cho lượt tiếp theo.");
                }
                break;
            case PiecePassive.ExplodeOnDeath:
                Debug.Log("ExplodeOnDeath: Chết sẽ phát nổ.");
                break;
            case PiecePassive.EnergySpendGrant:
                if (owner == 1)
                {
                    if (player1EnergySpendAssigned == null)
                    {
                        player1EnergySpendAssigned = (r, c);
                        player1EnergySpent = 0;
                    }
                }
                else
                {
                    if (player2EnergySpendAssigned == null)
                    {
                        player2EnergySpendAssigned = (r, c);
                        player2EnergySpent = 0;
                    }
                }
                break;
            case PiecePassive.PyroRage:
                int idxP = GetTypeIndexForPlayer(owner, data);
                if (idxP < 0) break;
                bool isActive = (owner == 1) ? p1PyroActive[idxP] : p2PyroActive[idxP];
                if (isActive)
                {
                    Debug.Log($"Player{owner} đặt {data.pieceName} trong trạng thái Pyro Rage!");
                    if (owner == 1) p1PyroActive[idxP] = false; else p2PyroActive[idxP] = false;
                    TryApplyBurn(r, c - 1, owner);
                    TryApplyBurn(r, c + 1, owner);
                }
                break;
            case PiecePassive.CrossStrike:
                {
                    if (!hasActivatedCrossStrike)
                    {
                        hasActivatedCrossStrike = true;
                        TriggerCrossStrike(r, c, owner);
                    }
                    break;
                }
            case PiecePassive.DeathMark:
                Debug.Log("DeathMark: Gắn dấu ấn tử vong.");
                break;
            case PiecePassive.IsolationLock:
                bool isIsolated = true;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        if (IsInBounds(r + dr, c + dc) && state[r + dr, c + dc].owner != 0)
                        {
                            isIsolated = false;
                            break;
                        }
                    }
                    if (!isIsolated) break;
                }
                if (isIsolated)
                {
                    Debug.Log($"IsolationLock tại ({r},{c}) kích hoạt!");
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (IsInBounds(nr, nc))
                            {
                                forbiddenForOpponentNextTurn.Add((nr, nc));
                                cells[nr, nc].SetLockIcon(true);
                            }
                        }
                    }
                }
                break;
            case PiecePassive.FourInRowWin:
                Debug.Log("FourInRowWin: Chỉ cần 4 caro liên tiếp để giành lợi thế");
                break;
        }
    }

    private void TriggerCrossStrike(int row, int col, int owner)
    {
        int[,] dirs = new int[,] { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int nr = row + dirs[i, 0];
            int nc = col + dirs[i, 1];
            if (nr < 0 || nr >= boardSize || nc < 0 || nc >= boardSize) continue;
            if (state[nr, nc].owner != 0)
            {
                DestroyCellAt(nr, nc, true, true, true);
            }
        }
    }

    public void ApplyDeathMarkAround(int r, int c, int owner)
    {
        Debug.Log($"☠️ ApplyDeathMarkAround kích hoạt tại ({r},{c}) của Player {owner}");
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr;
                int nc = c + dc;
                if (!IsInBounds(nr, nc)) continue;
                var target = state[nr, nc];
                if (target.owner != 0 && target.owner != owner)
                {
                    target.deathMarkCount++;
                    if (target.deathMarkCount >= 2)
                    {
                        StartCoroutine(DelayedDestroy(nr, nc, 0.05f));
                    }
                    else
                    {
                        cells[nr, nc].ShowDeathMarkVisual();
                    }
                }
            }
        }
    }

    void TryApplyBurn(int r, int c, int owner)
    {
        if (!IsInBounds(r, c)) return;
        var cell = state[r, c];
        if (cell.owner != 0 && cell.owner != owner)
        {
            cell.burnTurns = 2;
            state[r, c] = cell;
            cells[r, c].ShowBurnVisual();
        }
    }

    void ProcessBurningCells(int currentPlayerTurn)
    {
        for (int r = 0; r < boardSize; r++)
        {
            for (int c = 0; c < boardSize; c++)
            {
                var cs = state[r, c];
                if (cs.owner == currentPlayerTurn && cs.burnTurns > 0)
                {
                    cs.burnTurns--;
                    if (cs.burnTurns <= 0)
                    {
                        DestroyCellAt(r, c, true, true, true);
                    }
                    else
                    {
                        state[r, c] = cs;
                    }
                }
            }
        }
    }

    void PlayExplosionEffect(int r, int c)
    {
        if (explosionPrefab != null && IsInBounds(r, c) && cells[r, c] != null)
        {
            Vector3 cellPos = cells[r, c].transform.position;
            cellPos.z = -5f;
            Instantiate(explosionPrefab, cellPos, Quaternion.identity);
        }
    }

    void PlayWinLineExplosion(int r, int c)
    {
        // Hàm này giống hệt PlayExplosionEffect
        // nhưng dùng biến winLineExplosionPrefab mới
        if (winLineExplosionPrefab != null && IsInBounds(r, c) && cells[r, c] != null)
        {
            Vector3 cellPos = cells[r, c].transform.position;
            cellPos.z = -5f;
            Instantiate(winLineExplosionPrefab, cellPos, Quaternion.identity);
        }
    }


    // ======================================================
    // 🧱 BOARD & CELL MANAGEMENT
    // ======================================================

    void CreateBoard(int size)
    {
        cells = new Cell[size, size];
        state = new CellState[size, size];

        // Tìm boardParent
        Transform boardParent = UIManager.Instance.boardParent;

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                GameObject go = Instantiate(cellPrefab, boardParent);
                Cell cell = go.GetComponent<Cell>();
                cell.Init(this, r, c);
                cells[r, c] = cell;
                state[r, c] = new CellState();
            }
        }
    }

    void DestroyCellAt(int r, int c, bool allowLava = true, bool allowExplode = true, bool allowDeathMark = true)
    {
        if (state[r, c].owner == 0) return;

        // Kiểm tra hủy line thắng
        for (int i = activeWinLines.Count - 1; i >= 0; i--)
        {
            ActiveWinLine line = activeWinLines[i];
            if (line.cells.Contains((r, c)))
            {
                if (line.player == 1) p1TypeCompleted[line.pieceTypeIndex] = false;
                else p2TypeCompleted[line.pieceTypeIndex] = false;

                UIManager.Instance.DestroyWinLine(line.lineVisual); // Báo UI hủy
                activeWinLines.RemoveAt(i);
            }
        }

        PieceData pd = state[r, c].pieceData;
        int owner = state[r, c].owner;

        // ExplodeOnDeath
        if (allowExplode && pd != null && pd.passive == PiecePassive.ExplodeOnDeath)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int nr = r + dr;
                    int nc = c + dc;
                    if (IsInBounds(nr, nc))
                    {
                        PlayExplosionEffect(nr, nc);
                        if (!(dr == 0 && dc == 0) && state[nr, nc].owner != 0)
                        {
                            StartCoroutine(DelayedDestroy(nr, nc, 0.1f, true, true, true));
                        }
                    }
                }
            }
        }

        // DeathMark
        if (allowDeathMark && pd != null && pd.passive == PiecePassive.DeathMark)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr;
                    int nc = c + dc;
                    if (!IsInBounds(nr, nc)) continue;
                    var target = state[nr, nc];
                    if (target.owner != 0 && target.owner != owner)
                    {
                        target.deathMarkCount++;
                        if (target.deathMarkCount >= 2)
                        {
                            StartCoroutine(DelayedDestroy(nr, nc, 0.05f, true, true, true));
                        }
                        else
                        {
                            cells[nr, nc].ShowDeathMarkVisual();
                        }
                        state[nr, nc] = target;
                    }
                }
            }
        }

        // EnergySpendGrant
        if (player1EnergySpendAssigned != null && player1EnergySpendAssigned.Value.r == r && player1EnergySpendAssigned.Value.c == c)
        {
            player1EnergySpendAssigned = null;
            player1EnergySpent = 0;
        }
        if (player2EnergySpendAssigned != null && player2EnergySpendAssigned.Value.r == r && player2EnergySpendAssigned.Value.c == c)
        {
            player2EnergySpendAssigned = null;
            player2EnergySpent = 0;
        }

        bool turnsIntoLava = (pd != null && pd.passive == PiecePassive.LavaSpawnOnDeath);

        cells[r, c].ClearCell(); // Xóa visual

        // Xử lý logic state
        if (!turnsIntoLava || !allowLava)
        {
            state[r, c].owner = 0;
            state[r, c].pieceData = null;
        }
        else
        {
            CellState cs = state[r, c];
            cs.owner = 0;
            cs.pieceData = null;
            cs.isLava = true;
            state[r, c] = cs;
            cells[r, c].SetLavaVisual(true);
        }
    }

    IEnumerator DelayedDestroy(int r, int c, float delay, bool allowLava = true, bool allowExplode = true, bool allowDeathMark = true)
    {
        yield return new WaitForSeconds(delay);
        if (IsInBounds(r, c) && state[r, c].owner != 0)
            DestroyCellAt(r, c, allowLava, allowExplode, allowDeathMark);
    }

    public void SetCellState(int r, int c, CellState cs)
    {
        state[r, c] = cs;
    }

    public void ClearCellState(int r, int c)
    {
        state[r, c].owner = 0;
        state[r, c].pieceData = null;
        state[r, c].burnTurns = 0;
        state[r, c].deathMarkCount = 0;
        state[r, c].isLava = false;
    }


    // ======================================================
    // 🏆 WIN / VICTORY SYSTEM
    // ======================================================


    (List<(int, int)>, (int, int), (int, int)) CheckFiveInRow(int r, int c, int owner, PieceData lineTypePiece)
    {
        int requiredCount = 5;
        bool checkDiagonals = true;

        if (lineTypePiece.passive == PiecePassive.FourInRowWin)
        {
            requiredCount = 4;
            checkDiagonals = false;
        }

        Vector2Int[] dirs = new Vector2Int[] {
        new Vector2Int(0,1), new Vector2Int(1,0),
        new Vector2Int(1,1), new Vector2Int(1,-1)
    };

        foreach (var d in dirs)
        {
            if (!checkDiagonals && d.x != 0 && d.y != 0) continue;

            int count = 1; // Bắt đầu đếm từ 1 (ô vừa đặt)
            int rr = r + d.x, cc = c + d.y;

            // --- Đếm về phía trước ---
            while (IsInBounds(rr, cc) &&
                   state[rr, cc].owner == owner &&
                   DoesPieceMatchLine(lineTypePiece, state[rr, cc].pieceData))
            {
                count++;
                rr += d.x; cc += d.y;
            }
            int end2r = rr - d.x, end2c = cc - d.y;

            // --- Đếm về phía sau ---
            rr = r - d.x; cc = c - d.y;
            while (IsInBounds(rr, cc) &&
                   state[rr, cc].owner == owner &&
                   DoesPieceMatchLine(lineTypePiece, state[rr, cc].pieceData))
            {
                count++;
                rr -= d.x; cc -= d.y;
            }
            int end1r = rr + d.x, end1c = cc + d.y;

            if (count >= requiredCount)
            {
                List<(int, int)> cellsInLine = new List<(int, int)>();
                int currR = end1r, currC = end1c;
                for (int i = 0; i < count; i++) // Lấy đủ số con
                {
                    cellsInLine.Add((currR, currC));
                    currR += d.x;
                    currC += d.y;
                }
                return (cellsInLine, (end1r, end1c), (end2r, end2c));
            }
        }
        return (null, (0, 0), (0, 0));
    }

    void MarkTypeCompleted(int player, PieceData pd)
    {
        int idx = GetTypeIndexForPlayer(player, pd);
        if (idx < 0) return;
        if (player == 1) p1TypeCompleted[idx] = true; else p2TypeCompleted[idx] = true;
    }

    bool CheckVictory(int player)
    {
        int cnt = 0;
        if (player == 1)
        {
            for (int i = 0; i < 3; i++) if (p1TypeCompleted[i]) cnt++;
        }
        else
        {
            for (int i = 0; i < 3; i++) if (p2TypeCompleted[i]) cnt++;
        }
        return cnt >= 2;
    }

    // ======================================================
    // 🧠 HELPERS & UTILITIES
    // ======================================================

    void ApplyNextTurnForbidden()
    {
        forbiddenThisTurn.Clear();
        foreach (var p in forbiddenNextTurnPositions) forbiddenThisTurn.Add(p);
        forbiddenNextTurnPositions.Clear();

        // IsolationLock   
        foreach (var p in forbiddenForOpponentThisTurn)
        {
            if (IsInBounds(p.Item1, p.Item2))
            {
                cells[p.Item1, p.Item2].SetLockIcon(false);
            }
        }
        forbiddenForOpponentThisTurn.Clear();
    
        foreach (var p in forbiddenForOpponentNextTurn)
        {
            forbiddenForOpponentThisTurn.Add(p);
        }
        forbiddenForOpponentNextTurn.Clear();
    }

    List<(int, int)> GetChainEndsIfLengthN(int r, int c, int owner, int N)
    {
        Vector2Int[] dirs = new Vector2Int[] {
            new Vector2Int(0,1), new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(1,-1)
        };
        foreach (var d in dirs)
        {
            int count = 1;
            int r1 = r + d.x, c1 = c + d.y;
            while (IsInBounds(r1, c1) && state[r1, c1].owner == owner && state[r1, c1].pieceData == state[r, c].pieceData) { count++; r1 += d.x; c1 += d.y; }
            int end2r = r1, end2c = c1;
            int r2 = r - d.x, c2 = c - d.y;
            while (IsInBounds(r2, c2) && state[r2, c2].owner == owner && state[r2, c2].pieceData == state[r, c].pieceData) { count++; r2 -= d.x; c2 -= d.y; }
            int end1r = r2, end1c = c2;
            if (count >= N)
            {
                List<(int, int)> res = new List<(int, int)>();
                if (IsInBounds(end1r, end1c)) res.Add((end1r, end1c)); else res.Add((-1, -1));
                if (IsInBounds(end2r, end2c)) res.Add((end2r, end2c)); else res.Add((-1, -1));
                return res;
            }
        }
        return null;
    }

    bool IsInBounds(int r, int c)
    {
        return r >= 0 && r < boardSize && c >= 0 && c < boardSize;
    }

    int GetTypeIndexForPlayer(int player, PieceData pd)
    {
        if (player == 1)
        {
            for (int i = 0; i < 3; i++) if (p1Pieces[i] == pd) return i;
        }
        else
        {
            for (int i = 0; i < 3; i++) if (p2Pieces[i] == pd) return i;
        }
        return -1;
    }

    private bool DoesPieceMatchLine(PieceData lineType, PieceData cellPiece)
    {
        // Một ô cờ được tính là "khớp" nếu:
        // Nó chính là loại quân cờ của line (A == A)
        if (cellPiece == lineType)
            return true;

        // Nó là quân Wildcard (Quân Wildcard khớp với mọi loại line)
        if (cellPiece.passive == PiecePassive.WildCardCaro)
            return true;

        return false;
    }

    private void CancelActiveSkillMode()
    {
        isTeleportMode = false;
        teleportSourceCell = null;
        isSwapMode = false;
        swapCell1 = null;

        UIManager.Instance.ShowSkillMessage("Đã hủy kỹ năng.", 2f);
    }

    IEnumerator BotTurnRoutine()
    {
        yield return new WaitForSeconds(1.0f);

        if (aiController == null) yield break;

        int energy = GetTotalEnergyForPlayer(2);
        
        // --- [QUAN TRỌNG] CHUẨN BỊ DỮ LIỆU LUẬT CẤM GỬI CHO BOT ---
        // 1. Kiểm tra xem Bot có bị dính chiêu RestrictArea (Giam cầm) không
        bool botIsRestricted = (restrictOpponentNextTurn && restrictNextPlayer == 2);
        
        // 2. Gọi hàm AI mới với đầy đủ thông tin:
        // - forbiddenThisTurn: Danh sách các ô bị khóa (IsolationLock, Unblockable...)
        // - botIsRestricted: Có bị giam vùng không
        // - restrictCenterRow/Col: Tâm vùng giam
        AIController.MoveResult move = aiController.GetBotMove(
            state, 
            p2Pieces, 
            p2TypeCompleted, 
            energy, 
            isHardBot, 
            2,
            forbiddenThisTurn,  // <--- QUAN TRỌNG: Gửi danh sách cấm
            botIsRestricted,    // <--- QUAN TRỌNG: Gửi trạng thái bị giam
            restrictCenterRow,  
            restrictCenterCol
        );
        // -----------------------------------------------------------

        if (move.row != -1)
        {
            int pieceIdx = -1;
            for(int i=0; i<3; i++) {
                if(p2Pieces[i] == move.pieceToUse) { pieceIdx = i; break; }
            }

            if (pieceIdx != -1)
            {
                OnPieceButtonClicked(2, pieceIdx); 
                yield return new WaitForSeconds(0.3f); 
                
                Cell targetCell = cells[move.row, move.col];
                OnCellClicked(targetCell);
            }
        }
        else
        {
            Debug.Log("Bot không tìm được nước đi hợp lệ -> Buộc phải bỏ lượt");
            // Nếu Bot bị khóa hết đường, chuyển lượt lại cho Player 1 để game không bị treo
            EndTurnAndSwitch(); 
        }
    }
}

public class ActiveWinLine
{
    public int player;
    public int pieceTypeIndex;
    public GameObject lineVisual;
    public List<(int, int)> cells = new List<(int, int)>();
}