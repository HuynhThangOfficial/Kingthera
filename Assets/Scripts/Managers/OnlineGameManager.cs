using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

[RequireComponent(typeof(NetworkObject))]
public class OnlineGameManager : NetworkBehaviour
{
    public static OnlineGameManager Instance;

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
    private CellState[,] state; // Server only

    private NetworkVariable<int> netCurrentPlayer = new NetworkVariable<int>(1);

    // Biến thời gian và trạng thái game
    private NetworkVariable<float> netTurnTimer = new NetworkVariable<float>(120f);
    private NetworkVariable<bool> netIsGameActive = new NetworkVariable<bool>(false);   

    // NetworkList để đồng bộ quân cờ 
    public NetworkList<int> netP1PieceIDs;
    public NetworkList<int> netP2PieceIDs;

    // Energy
    private int player1PermanentEnergy, player2PermanentEnergy;
    private int player1TemporaryEnergy, player2TemporaryEnergy;
    private int player1EnergySpent, player2EnergySpent;
    private (int r, int c)? player1EnergySpendAssigned, player2EnergySpendAssigned;

    // Piece & Logic
    private PieceData[] p1Pieces = new PieceData[3];
    private PieceData[] p2Pieces = new PieceData[3];
    private PieceData lastSelectedP1, lastSelectedP2;
    private PieceData localSelectedPiece;

    // Counters
    private int[] p1TurnsSincePlayed = new int[3], p2TurnsSincePlayed = new int[3];
    private int[] p1PlayCounts = new int[3], p2PlayCounts = new int[3];
    private bool[] p1TypeCompleted = new bool[3], p2TypeCompleted = new bool[3];
    private bool[] p1PyroActive = new bool[3], p2PyroActive = new bool[3];
    private List<ActiveWinLine> activeWinLines = new List<ActiveWinLine>();
    public bool p1CrossStrikeUsed = false;
    public bool p2CrossStrikeUsed = false;

    // Restrictions
    private bool restrictOpponentNextTurn = false;
    private int restrictCenterRow = -1, restrictCenterCol = -1, restrictNextPlayer = 0;
    private HashSet<(int, int)> forbiddenThisTurn = new(), forbiddenNextTurnPositions = new();
    private HashSet<(int, int)> forbiddenForOpponentNextTurn = new(), forbiddenForOpponentThisTurn = new();

    // Active Skills
    private bool isTeleportMode = false;
    private Cell teleportSourceCell = null;
    private bool isSwapMode = false;
    private Cell swapCell1 = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        // Khởi tạo NetworkList
        netP1PieceIDs = new NetworkList<int>();
        netP2PieceIDs = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        // 1. Tạo bàn cờ (Visual) trước
        CreateBoard(boardSize);
        // Đăng ký với UI để tránh lỗi Null khi vẽ
        UIManager.Instance.RegisterOnlineGameManager(this, cells);
        UIManager.Instance.SetupBoardVisuals(boardSize, tileLight, tileDark);

        // 2. Xử lý dữ liệu quân cờ
        if (IsServer)
        {
            // SERVER: Lấy dữ liệu từ MainMenu và đẩy vào NetworkList
            InitializePieceDataServer();
            StartNewGameServer();
        }
        else
        {
            // CLIENT: Lắng nghe dữ liệu từ Server gửi về
            // Nếu dữ liệu đã có sẵn (do vào sau), load luôn
            if (netP1PieceIDs.Count > 0 && netP2PieceIDs.Count > 0)
            {
                LoadPiecesFromNetworkList();
            }
            // Đăng ký sự kiện để khi list thay đổi (hoặc mới vào) sẽ load
            netP1PieceIDs.OnListChanged += OnPieceListChanged;
        }

        // 3. Update UI
        UpdateUIClientRpc(1, 0, 1, 0, netCurrentPlayer.Value);
        UIManager.Instance.ShowStartMessage(netCurrentPlayer.Value);
    }

    public override void OnNetworkDespawn()
    {
        // Hủy đăng ký sự kiện để tránh lỗi
        netP1PieceIDs.OnListChanged -= OnPieceListChanged;
    }


    private void InitializePieceDataServer()
    {
        // Lấy từ PieceSelectionData (chỉ Server cần biết cái này chính xác lúc đầu)
        if (PieceSelectionData.player1Pieces != null)
        {
            foreach (var p in PieceSelectionData.player1Pieces) netP1PieceIDs.Add(p.id);
        }
        if (PieceSelectionData.player2Pieces != null)
        {
            foreach (var p in PieceSelectionData.player2Pieces) netP2PieceIDs.Add(p.id);
        }

        // Server load vào biến cục bộ
        p1Pieces = PieceSelectionData.player1Pieces;
        p2Pieces = PieceSelectionData.player2Pieces;

        // Setup Button cho Host
        UIManager.Instance.SetupPieceButtons(p1Pieces, p2Pieces);
    }

    private void OnPieceListChanged(NetworkListEvent<int> changeEvent)
    {
        // Khi nhận đủ 3 quân mỗi bên thì load
        if (netP1PieceIDs.Count == 3 && netP2PieceIDs.Count == 3)
        {
            LoadPiecesFromNetworkList();
        }
    }

    private void LoadPiecesFromNetworkList()
    {
        // Convert ID -> PieceData
        p1Pieces = ConvertIDsToPieces(netP1PieceIDs);
        p2Pieces = ConvertIDsToPieces(netP2PieceIDs);

        // Sau khi có dữ liệu, mới setup nút bấm -> KHÔNG CÒN LỖI NULL
        UIManager.Instance.SetupPieceButtons(p1Pieces, p2Pieces);
        Debug.Log("Client đã load xong dữ liệu quân cờ!");
    }

    private PieceData[] ConvertIDsToPieces(NetworkList<int> ids)
    {
        List<PieceData> list = new List<PieceData>();
        foreach (int id in ids)
        {
            var piece = MultiplayerRoomManager.Instance.allPiecesLibrary.FirstOrDefault(p => p.id == id);
            if (piece != null) list.Add(piece);
        }
        return list.ToArray();
    }

    // ======================================================
    // ⚙️ UPDATE & INPUT
    // ======================================================

    private void Update()
    {
        // Xử lý Input hủy skill (Giữ nguyên)
        if (Input.GetMouseButtonDown(1))
        {
            if (isTeleportMode || isSwapMode) CancelActiveSkillMode();
        }

        // 🔥 LOGIC TIMER MỚI 🔥

        // SERVER: Chịu trách nhiệm trừ giờ và kiểm tra thua cuộc
        if (IsServer && netIsGameActive.Value)
        {
            netTurnTimer.Value -= Time.deltaTime;

            if (netTurnTimer.Value <= 0)
            {
                // Hết giờ! Người chơi hiện tại thua -> Người kia thắng
                int winner = (netCurrentPlayer.Value == 1) ? 2 : 1;

                Debug.Log($"Player {netCurrentPlayer.Value} hết giờ! Player {winner} thắng.");

                // Dừng game và hiện bảng thắng
                netIsGameActive.Value = false;
                ShowWinUIClientRpc(winner);
            }
        }

        // CLIENT (Cả Host và Client): Cập nhật số hiển thị lên màn hình
        // Chỉ hiện khi game đang diễn ra
        if (netIsGameActive.Value)
        {
            UIManager.Instance.UpdateTimerUI(netTurnTimer.Value);
        }
        else
        {
            // Nếu game dừng/chưa bắt đầu, hiện "--" hoặc 120
            UIManager.Instance.UpdateTimerUI(120);
        }
    }

    public void OnPieceButtonClicked(int player, int index)
    {
        if (NetworkManager.Singleton.LocalClientId != (ulong)(player - 1)) return;
        if (player != netCurrentPlayer.Value) return;

        PieceData newPiece = (player == 1) ? p1Pieces[index] : p2Pieces[index];

        if (newPiece.passive == PiecePassive.Active_TeleportAlly && newPiece == localSelectedPiece)
        {
            if (GetTotalEnergyForPlayer_Client(player) >= 1)
            {
                isTeleportMode = true; teleportSourceCell = null; isSwapMode = false; swapCell1 = null;
                UIManager.Instance.ShowSkillMessage("Dịch chuyển: Chọn 1 quân đồng minh.", 0);
                return;
            }
            else { UIManager.Instance.ShowSkillMessage("Không đủ năng lượng!", 2f); return; }
        }
        else if (newPiece.passive == PiecePassive.Active_SwapAllies && newPiece == localSelectedPiece)
        {
            if (GetTotalEnergyForPlayer_Client(player) >= 1)
            {
                isSwapMode = true; swapCell1 = null; isTeleportMode = false; teleportSourceCell = null;
                UIManager.Instance.ShowSkillMessage("Hoán đổi: Chọn quân đồng minh thứ 1.", 0);
                return;
            }
            else { UIManager.Instance.ShowSkillMessage("Không đủ năng lượng!", 2f); return; }
        }

        SelectPieceServerRpc(player, index);
    }

    public void OnCellClicked(Cell cell)
    {
        int localPlayerId = (int)NetworkManager.Singleton.LocalClientId + 1;
        if (localPlayerId != netCurrentPlayer.Value) return;

        if (isTeleportMode) { HandleTeleportClient(cell); return; }
        if (isSwapMode) { HandleSwapClient(cell); return; }

        PlacePieceServerRpc(cell.row, cell.col);
    }

    // ======================================================
    // 📡 SERVER RPCs
    // ======================================================

    [ServerRpc(RequireOwnership = false)]
    private void SelectPieceServerRpc(int player, int index)
    {
        if (player != netCurrentPlayer.Value) return;

        PieceData newPiece = (player == 1) ? p1Pieces[index] : p2Pieces[index];
        PieceData lastSelected = (player == 1) ? lastSelectedP1 : lastSelectedP2;

        if (lastSelected == null)
        {
            if (TrySpendTemporaryEnergy(player, 1)) SetLastSelected(player, newPiece);
        }
        else if (lastSelected != newPiece)
        {
            if (TrySpendAnyEnergy(player, 1)) SetLastSelected(player, newPiece);
        }

        SyncAllUI();
        ConfirmSelectionClientRpc(newPiece.id, player);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlacePieceServerRpc(int r, int c)
    {
        int p = netCurrentPlayer.Value;
        PieceData selected = (p == 1) ? lastSelectedP1 : lastSelectedP2;
        if (selected == null) return;
        if (state[r, c].isLava) return;

        bool success = false;

        if (state[r, c].owner != 0)
        {
            if (selected.passive == PiecePassive.AllyReplace && state[r, c].owner == p)
            {
                if (TrySpendAnyEnergy(p, selected.energyCost))
                {
                    bool explode = (state[r, c].pieceData != null && state[r, c].pieceData.passive == PiecePassive.ExplodeOnDeath);
                    DestroyCellAt(r, c, false, true, true);
                    PlacePieceLogic(r, c, p, selected);
                    if (explode) StartCoroutine(DelayedDestroy(r, c, 0.1f, false, false, false));
                    success = true;
                }
            }
        }
        else
        {
            if (IsCellForbidden(r, c, p)) return;
            if (TrySpendAnyEnergy(p, selected.energyCost))
            {
                PlacePieceLogic(r, c, p, selected);
                success = true;
            }
        }

        if (success) PostPlacementLogic(r, c, p, selected);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TeleportPieceServerRpc(int fromR, int fromC, int toR, int toC)
    {
        int p = netCurrentPlayer.Value;
        if (state[fromR, fromC].owner != p || state[toR, toC].owner != 0) return;

        if (TrySpendAnyEnergy(p, 1))
        {
            CellState sourceState = state[fromR, fromC];
            ClearCellLogic(fromR, fromC);
            PlacePieceLogic(toR, toC, sourceState.owner, sourceState.pieceData);
            state[toR, toC].burnTurns = sourceState.burnTurns;
            state[toR, toC].deathMarkCount = sourceState.deathMarkCount;

            UpdateCellVisualClientRpc(toR, toC, sourceState.owner, sourceState.pieceData.id,
                sourceState.deathMarkCount > 0, sourceState.burnTurns > 0, false);

            SyncAllUI();
            ShowMessageClientRpc("Dịch chuyển thành công!", p);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SwapPieceServerRpc(int r1, int c1, int r2, int c2)
    {
        int p = netCurrentPlayer.Value;
        if (state[r1, c1].owner != p || state[r2, c2].owner != p) return;

        if (TrySpendAnyEnergy(p, 1))
        {
            CellState s1 = state[r1, c1];
            CellState s2 = state[r2, c2];
            state[r1, c1] = s2;
            state[r2, c2] = s1;

            UpdateCellVisualClientRpc(r1, c1, s2.owner, s2.pieceData.id, s2.deathMarkCount > 0, s2.burnTurns > 0, false);
            UpdateCellVisualClientRpc(r2, c2, s1.owner, s1.pieceData.id, s1.deathMarkCount > 0, s1.burnTurns > 0, false);

            SyncAllUI();
            ShowMessageClientRpc("Hoán đổi thành công!", p);
        }
    }

    // ======================================================
    // ⚙️ SERVER GAME LOGIC
    // ======================================================

    private void StartNewGameServer()
    {
        player1TemporaryEnergy = 1; player2TemporaryEnergy = 1;
        player1PermanentEnergy = 0; player2PermanentEnergy = 0;

        for (int r = 0; r < boardSize; r++) for (int c = 0; c < boardSize; c++) state[r, c] = new CellState();

        forbiddenThisTurn.Clear(); forbiddenForOpponentThisTurn.Clear();

        netIsGameActive.Value = true;
        netTurnTimer.Value = 120f;
        SyncAllUI();
    }

    private void PlacePieceLogic(int r, int c, int owner, PieceData piece)
    {
        state[r, c] = new CellState { owner = owner, pieceData = piece };
        UpdateCellVisualClientRpc(r, c, owner, piece.id, false, false, false);
    }

    private void ClearCellLogic(int r, int c)
    {
        state[r, c] = new CellState();
        UpdateCellVisualClientRpc(r, c, 0, -1, false, false, false);
    }

    private void PostPlacementLogic(int r, int c, int owner, PieceData piece)
    {
        if (owner == 1)
        {
            for (int i = 0; i < 3; i++) { if (p1Pieces[i] == piece) p1TurnsSincePlayed[i] = 0; else p1TurnsSincePlayed[i]++; }
        }
        else
        {
            for (int i = 0; i < 3; i++) { if (p2Pieces[i] == piece) p2TurnsSincePlayed[i] = 0; else p2TurnsSincePlayed[i]++; }
        }

        HandlePassiveOnPlace(piece, r, c, owner);
        CheckWinCondition(r, c, owner, piece);
        EndTurnServer();
    }

    private void CheckWinCondition(int r, int c, int owner, PieceData piece)
    {
        List<(int, int)> cellsInLine = null;
        (int, int) start = (0, 0);
        (int, int) end = (0, 0);
        PieceData pieceThatWon = null;

        if (piece.passive == PiecePassive.WildCardCaro)
        {
            PieceData[] allied = (owner == 1) ? p1Pieces : p2Pieces;
            foreach (var p in allied)
            {
                if (p.passive == PiecePassive.WildCardCaro) continue;
                var res = CheckFiveInRow(r, c, owner, p);
                if (res.Item1 != null)
                {
                    cellsInLine = res.Item1; start = res.Item2; end = res.Item3;
                    pieceThatWon = p;
                    break;
                }
            }
        }
        else
        {
            var res = CheckFiveInRow(r, c, owner, piece);
            if (res.Item1 != null)
            {
                cellsInLine = res.Item1; start = res.Item2; end = res.Item3;
                pieceThatWon = piece;
            }
        }

        if (cellsInLine != null)
        {
            int idx = GetTypeIndexForPlayer(owner, pieceThatWon);
            bool completed = (owner == 1) ? p1TypeCompleted[idx] : p2TypeCompleted[idx];

            if (!completed)
            {
                MarkTypeCompleted(owner, pieceThatWon);
                DrawWinLineClientRpc(start.Item1, start.Item2, end.Item1, end.Item2, owner);
                activeWinLines.Add(new ActiveWinLine { player = owner, pieceTypeIndex = idx, cells = cellsInLine });

                if (pieceThatWon.passive == PiecePassive.WinLineExplode)
                {
                    HashSet<(int, int)> cellsToDestroy = new HashSet<(int, int)>();
                    Vector2Int[] cross = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };

                    foreach (var cellPos in cellsInLine)
                    {
                        foreach (var dir in cross)
                        {
                            int nr = cellPos.Item1 + dir.x; int nc = cellPos.Item2 + dir.y;
                            if (IsInBounds(nr, nc) && state[nr, nc].owner != 0 && !cellsInLine.Contains((nr, nc)))
                                cellsToDestroy.Add((nr, nc));
                        }
                    }
                    foreach (var des in cellsToDestroy)
                    {
                        PlayWinLineExplosionClientRpc(des.Item1, des.Item2);
                        StartCoroutine(DelayedDestroy(des.Item1, des.Item2, 0.1f, true, true, true));
                    }
                }
            }
        }

        if (CheckVictory(owner)) ShowWinUIClientRpc(owner);
    }

    private void EndTurnServer()
    {
        if (netCurrentPlayer.Value == 1) player1TemporaryEnergy = 0; else player2TemporaryEnergy = 0;

        netCurrentPlayer.Value = (netCurrentPlayer.Value == 1) ? 2 : 1;

        netTurnTimer.Value = 120f;

        if (netCurrentPlayer.Value == 1)
        {
            for (int i = 0; i < 3; i++) if (p1Pieces[i].passive == PiecePassive.PyroRage && p1TurnsSincePlayed[i] >= 4 && !p1PyroActive[i]) p1PyroActive[i] = true;
        }
        else
        {
            for (int i = 0; i < 3; i++) if (p2Pieces[i].passive == PiecePassive.PyroRage && p2TurnsSincePlayed[i] >= 4 && !p2PyroActive[i]) p2PyroActive[i] = true;
        }

        ProcessBurningCells(netCurrentPlayer.Value);
        ApplyNextTurnForbidden();

        if (netCurrentPlayer.Value == 1) player1TemporaryEnergy = 1; else player2TemporaryEnergy = 1;

        if (restrictOpponentNextTurn && restrictNextPlayer != netCurrentPlayer.Value)
        {
            restrictOpponentNextTurn = false; restrictCenterRow = -1;
        }
        bool isRestricted = restrictOpponentNextTurn && restrictNextPlayer == netCurrentPlayer.Value;

        SyncAllUI();
        UpdateRestrictVisualClientRpc(isRestricted, restrictCenterRow, restrictCenterCol);
    }

    void DestroyCellAt(int r, int c, bool allowLava, bool allowExplode, bool allowDeathMark)
    {
        if (state[r, c].owner == 0) return;

        for (int i = activeWinLines.Count - 1; i >= 0; i--)
        {
            if (activeWinLines[i].cells.Contains((r, c)))
            {
                if (activeWinLines[i].player == 1) p1TypeCompleted[activeWinLines[i].pieceTypeIndex] = false;
                else p2TypeCompleted[activeWinLines[i].pieceTypeIndex] = false;

                DestroyWinLineClientRpc(activeWinLines[i].player, activeWinLines[i].pieceTypeIndex);
                activeWinLines.RemoveAt(i);
            }
        }

        PieceData pd = state[r, c].pieceData;
        int owner = state[r, c].owner;

        if (allowExplode && pd != null && pd.passive == PiecePassive.ExplodeOnDeath)
        {
            for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    int nr = r + dr, nc = c + dc;
                    if (IsInBounds(nr, nc))
                    {
                        PlayExplosionClientRpc(nr, nc);
                        if (!(dr == 0 && dc == 0) && state[nr, nc].owner != 0)
                            StartCoroutine(DelayedDestroy(nr, nc, 0.1f, true, true, true));
                    }
                }
        }

        if (allowDeathMark && pd != null && pd.passive == PiecePassive.DeathMark)
        {
            for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (IsInBounds(nr, nc) && state[nr, nc].owner != 0 && state[nr, nc].owner != owner)
                    {
                        state[nr, nc].deathMarkCount++;
                        if (state[nr, nc].deathMarkCount >= 2) StartCoroutine(DelayedDestroy(nr, nc, 0.05f, true, true, true));
                        else UpdateCellVisualClientRpc(nr, nc, state[nr, nc].owner, state[nr, nc].pieceData.id, true, false, false);
                    }
                }
        }

        if (player1EnergySpendAssigned != null && player1EnergySpendAssigned.Value.r == r && player1EnergySpendAssigned.Value.c == c) { player1EnergySpendAssigned = null; player1EnergySpent = 0; }
        if (player2EnergySpendAssigned != null && player2EnergySpendAssigned.Value.r == r && player2EnergySpendAssigned.Value.c == c) { player2EnergySpendAssigned = null; player2EnergySpent = 0; }

        bool turnsIntoLava = (pd != null && pd.passive == PiecePassive.LavaSpawnOnDeath);
        ClearCellLogic(r, c);

        if (turnsIntoLava && allowLava)
        {
            state[r, c].isLava = true;
            UpdateCellVisualClientRpc(r, c, 0, -1, false, false, true);
        }
    }

    void HandlePassiveOnPlace(PieceData data, int r, int c, int owner)
    {
        switch (data.passive)
        {
            case PiecePassive.RestrictArea:
                restrictOpponentNextTurn = true; restrictCenterRow = r; restrictCenterCol = c;
                restrictNextPlayer = (owner == 1) ? 2 : 1;
                break;
            case PiecePassive.GainEnergyEvery2Plays:
                int idx = GetTypeIndexForPlayer(owner, data);
                if (owner == 1) { p1PlayCounts[idx]++; if (p1PlayCounts[idx] % 2 == 0) player1PermanentEnergy++; }
                else { p2PlayCounts[idx]++; if (p2PlayCounts[idx] % 2 == 0) player2PermanentEnergy++; }
                break;
            case PiecePassive.UnblockableOnFour:
                var ends = GetChainEndsIfLengthN(r, c, owner, 4);
                if (ends != null && ends.Count == 2) foreach (var pos in ends) forbiddenNextTurnPositions.Add(pos);
                break;
            case PiecePassive.EnergySpendGrant:
                if (owner == 1 && player1EnergySpendAssigned == null) { player1EnergySpendAssigned = (r, c); player1EnergySpent = 0; }
                if (owner == 2 && player2EnergySpendAssigned == null) { player2EnergySpendAssigned = (r, c); player2EnergySpent = 0; }
                break;
            case PiecePassive.PyroRage:
                int idxP = GetTypeIndexForPlayer(owner, data);
                bool active = (owner == 1) ? p1PyroActive[idxP] : p2PyroActive[idxP];
                if (active)
                {
                    if (owner == 1) p1PyroActive[idxP] = false; else p2PyroActive[idxP] = false;
                    TryApplyBurn(r, c - 1, owner); TryApplyBurn(r, c + 1, owner);
                }
                break;
            case PiecePassive.CrossStrike:
                if (owner == 1)
                {
                    if (!p1CrossStrikeUsed) // Nếu chưa dùng
                    {
                        p1CrossStrikeUsed = true; // Đánh dấu là đã dùng
                        TriggerCrossStrike(r, c, owner); // Kích hoạt nổ
                        Debug.Log("P1 kích hoạt CrossStrike lần đầu (và duy nhất)!");
                    }
                }
                else // Player 2
                {
                    if (!p2CrossStrikeUsed)
                    {
                        p2CrossStrikeUsed = true;
                        TriggerCrossStrike(r, c, owner);
                        Debug.Log("P2 kích hoạt CrossStrike lần đầu (và duy nhất)!");
                    }
                }
                break;
            case PiecePassive.IsolationLock:
                bool iso = true;
                for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        if (IsInBounds(r + dr, c + dc) && state[r + dr, c + dc].owner != 0 && state[r + dr, c + dc].owner != owner) { iso = false; break; }
                    }
                if (iso)
                {
                    for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            if (IsInBounds(r + dr, c + dc))
                            {
                                forbiddenForOpponentNextTurn.Add((r + dr, c + dc));
                                UpdateLockVisualClientRpc(r + dr, c + dc, true);
                            }
                        }
                }
                break;
        }
    }

    void TriggerCrossStrike(int r, int c, int owner)
    {
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int nr = r + dirs[i, 0]; int nc = c + dirs[i, 1];
            if (IsInBounds(nr, nc) && state[nr, nc].owner != 0) DestroyCellAt(nr, nc, true, true, true);
        }
    }

    void TryApplyBurn(int r, int c, int owner)
    {
        if (IsInBounds(r, c) && state[r, c].owner != 0 && state[r, c].owner != owner)
        {
            state[r, c].burnTurns = 2;
            UpdateCellVisualClientRpc(r, c, state[r, c].owner, state[r, c].pieceData.id, false, true, false);
        }
    }

    bool IsCellForbidden(int r, int c, int p)
    {
        if (forbiddenThisTurn.Contains((r, c))) return true;
        if (forbiddenForOpponentThisTurn.Contains((r, c))) return true;
        if (restrictOpponentNextTurn && restrictNextPlayer == p)
        {
            int half = 2;
            if (!(r >= restrictCenterRow - half && r <= restrictCenterRow + half && c >= restrictCenterCol - half && c <= restrictCenterCol + half)) return true;
        }
        return false;
    }

    (List<(int, int)>, (int, int), (int, int)) CheckFiveInRow(int r, int c, int owner, PieceData lineType)
    {
        int req = (lineType.passive == PiecePassive.FourInRowWin) ? 4 : 5;
        bool diag = (lineType.passive != PiecePassive.FourInRowWin);
        Vector2Int[] dirs = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, -1) };
        foreach (var d in dirs)
        {
            if (!diag && d.x != 0 && d.y != 0) continue;
            int count = 1;
            int rr = r + d.x, cc = c + d.y;
            while (IsInBounds(rr, cc) && state[rr, cc].owner == owner && DoesPieceMatchLine(lineType, state[rr, cc].pieceData)) { count++; rr += d.x; cc += d.y; }
            int end2r = rr - d.x, end2c = cc - d.y;
            rr = r - d.x; cc = c - d.y;
            while (IsInBounds(rr, cc) && state[rr, cc].owner == owner && DoesPieceMatchLine(lineType, state[rr, cc].pieceData)) { count++; rr -= d.x; cc -= d.y; }
            int end1r = rr + d.x, end1c = cc + d.y;
            if (count >= req)
            {
                List<(int, int)> list = new List<(int, int)>();
                int cr = end1r, cl = end1c;
                for (int i = 0; i < count; i++) { list.Add((cr, cl)); cr += d.x; cl += d.y; }
                return (list, (end1r, end1c), (end2r, end2c));
            }
        }
        return (null, (0, 0), (0, 0));
    }

    // ======================================================
    // 📡 CLIENT RPCs
    // ======================================================

    [ClientRpc]
    private void UpdateCellVisualClientRpc(int r, int c, int owner, int pieceID, bool deathMark, bool burn, bool lava)
    {
        Cell cell = cells[r, c];
        cell.ClearCell();
        if (lava) { cell.SetLavaVisual(true); return; }
        if (owner != 0)
        {
            PieceData pd = MultiplayerRoomManager.Instance.allPiecesLibrary.FirstOrDefault(p => p.id == pieceID);
            if (pd != null) cell.PlacePiece(pd.sprite, owner, pd);
        }
        if (deathMark) cell.ShowDeathMarkVisual();
        if (burn) cell.ShowBurnVisual();
    }

    [ClientRpc]
    private void UpdateUIClientRpc(int t1, int p1, int t2, int p2, int currPlayer)
    {
        UIManager.Instance.UpdateEnergyUI(t1 + p1, t2 + p2);
        UIManager.Instance.UpdateTurnUI(currPlayer, null, null, p1Pieces, p2Pieces);
    }

    [ClientRpc]
    private void ConfirmSelectionClientRpc(int pieceID, int player)
    {
        if (NetworkManager.Singleton.LocalClientId == (ulong)(player - 1))
        {
            PieceData pd = MultiplayerRoomManager.Instance.allPiecesLibrary.FirstOrDefault(p => p.id == pieceID);
            localSelectedPiece = pd;
        }
    }

    [ClientRpc]
    private void ShowMessageClientRpc(string msg, int playerTarget)
    {
        if (NetworkManager.Singleton.LocalClientId == (ulong)(playerTarget - 1)) UIManager.Instance.ShowSkillMessage(msg, 3f);
    }

    [ClientRpc]
    private void PlayExplosionClientRpc(int r, int c)
    {
        if (explosionPrefab) Instantiate(explosionPrefab, cells[r, c].transform.position, Quaternion.identity);
    }

    [ClientRpc]
    private void PlayWinLineExplosionClientRpc(int r, int c)
    {
        if (winLineExplosionPrefab) Instantiate(winLineExplosionPrefab, cells[r, c].transform.position, Quaternion.identity);
    }

    [ClientRpc]
    private void UpdateLockVisualClientRpc(int r, int c, bool active)
    {
        if (IsInBounds(r, c)) cells[r, c].SetLockIcon(active);
    }

    [ClientRpc]
    private void UpdateRestrictVisualClientRpc(bool active, int r, int c)
    {
        UIManager.Instance.UpdateRestrictVisuals(boardSize, active, r, c);
    }

    [ClientRpc]
    private void DrawWinLineClientRpc(int r1, int c1, int r2, int c2, int p)
    {
        UIManager.Instance.DrawWinLine(r1, c1, r2, c2, p);
    }

    [ClientRpc]
    private void DestroyWinLineClientRpc(int p, int pieceIdx)
    {
        UIManager.Instance.ClearAllWinLines();
    }

    [ClientRpc]
    private void ShowWinUIClientRpc(int p)
    {
        UIManager.Instance.ShowWinUI(p);
    }

    // ======================================================
    // 🛠️ UTILITIES
    // ======================================================
    private void CreateBoard(int size)
    {
        cells = new Cell[size, size];
        if (IsServer) state = new CellState[size, size];
        Transform parent = UIManager.Instance.boardParent;
        foreach (Transform child in parent) Destroy(child.gameObject);
        for (int r = 0; r < size; r++) for (int c = 0; c < size; c++)
            {
                GameObject go = Instantiate(cellPrefab, parent);
                Cell cell = go.GetComponent<Cell>();
                cell.InitOnline(this, r, c);
                cells[r, c] = cell;
            }
    }

    private void SyncAllUI()
    {
        UpdateUIClientRpc(player1TemporaryEnergy, player1PermanentEnergy, player2TemporaryEnergy, player2PermanentEnergy, netCurrentPlayer.Value);
    }

    bool IsInBounds(int r, int c) { return r >= 0 && r < boardSize && c >= 0 && c < boardSize; }

    bool TrySpendAnyEnergy(int p, int cost)
    {
        int temp = (p == 1) ? player1TemporaryEnergy : player2TemporaryEnergy;
        int perm = (p == 1) ? player1PermanentEnergy : player2PermanentEnergy;
        if (temp + perm < cost) return false;
        int used = Mathf.Min(cost, temp);
        if (p == 1) { player1TemporaryEnergy -= used; player1PermanentEnergy -= (cost - used); }
        else { player2TemporaryEnergy -= used; player2PermanentEnergy -= (cost - used); }
        return true;
    }

    bool TrySpendTemporaryEnergy(int p, int cost)
    {
        if (p == 1 && player1TemporaryEnergy >= cost) { player1TemporaryEnergy -= cost; return true; }
        if (p == 2 && player2TemporaryEnergy >= cost) { player2TemporaryEnergy -= cost; return true; }
        return false;
    }

    void SetLastSelected(int p, PieceData piece) { if (p == 1) lastSelectedP1 = piece; else lastSelectedP2 = piece; }

    int GetTotalEnergyForPlayer_Client(int p)
    {
        string textValue = "";

        if (p == 1)
        {
            if (UIManager.Instance.player1EnergyText != null)
                textValue = UIManager.Instance.player1EnergyText.text;
        }
        else
        {
            if (UIManager.Instance.player2EnergyText != null)
                textValue = UIManager.Instance.player2EnergyText.text;
        }


        if (int.TryParse(textValue, out int result))
        {
            return result;
        }

        return 0;
    }

    int GetTypeIndexForPlayer(int p, PieceData pd)
    {
        PieceData[] pieces = (p == 1) ? p1Pieces : p2Pieces;
        for (int i = 0; i < 3; i++) if (pieces[i].id == pd.id) return i;
        return -1;
    }

    void MarkTypeCompleted(int p, PieceData pd)
    {
        int idx = GetTypeIndexForPlayer(p, pd);
        if (p == 1) p1TypeCompleted[idx] = true; else p2TypeCompleted[idx] = true;
    }

    bool CheckVictory(int p)
    {
        int cnt = 0;
        bool[] completed = (p == 1) ? p1TypeCompleted : p2TypeCompleted;
        foreach (bool b in completed) if (b) cnt++;
        return cnt >= 2;
    }

    void ApplyNextTurnForbidden()
    {
        forbiddenThisTurn.Clear();
        foreach (var p in forbiddenNextTurnPositions) forbiddenThisTurn.Add(p);
        forbiddenNextTurnPositions.Clear();
        foreach (var p in forbiddenForOpponentThisTurn) UpdateLockVisualClientRpc(p.Item1, p.Item2, false);
        forbiddenForOpponentThisTurn.Clear();
        foreach (var p in forbiddenForOpponentNextTurn) forbiddenForOpponentThisTurn.Add(p);
        forbiddenForOpponentNextTurn.Clear();
    }

    void ProcessBurningCells(int p)
    {
        for (int r = 0; r < boardSize; r++) for (int c = 0; c < boardSize; c++)
            {
                if (state[r, c].owner == p && state[r, c].burnTurns > 0)
                {
                    state[r, c].burnTurns--;
                    if (state[r, c].burnTurns <= 0) DestroyCellAt(r, c, true, true, true);
                }
            }
    }

    IEnumerator DelayedDestroy(int r, int c, float delay, bool l, bool e, bool d)
    {
        yield return new WaitForSeconds(delay);
        if (IsInBounds(r, c) && state[r, c].owner != 0) DestroyCellAt(r, c, l, e, d);
    }

    private bool DoesPieceMatchLine(PieceData lineType, PieceData cellPiece)
    {
        if (cellPiece.id == lineType.id) return true;
        if (cellPiece.passive == PiecePassive.WildCardCaro) return true;
        return false;
    }

    List<(int, int)> GetChainEndsIfLengthN(int r, int c, int owner, int N)
    {
        Vector2Int[] dirs = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, -1) };
        foreach (var d in dirs)
        {
            int count = 1;
            int rr = r + d.x, cc = c + d.y;
            while (IsInBounds(rr, cc) && state[rr, cc].owner == owner && state[rr, cc].pieceData == state[r, c].pieceData) { count++; rr += d.x; cc += d.y; }
            int end2r = rr - d.x, end2c = cc - d.y;
            rr = r - d.x; cc = c - d.y;
            while (IsInBounds(rr, cc) && state[rr, cc].owner == owner && state[rr, cc].pieceData == state[r, c].pieceData) { count++; rr -= d.x; cc -= d.y; }
            int end1r = rr + d.x, end1c = cc + d.y;
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

    private void HandleTeleportClient(Cell cell)
    {
        if (teleportSourceCell == null)
        {
            teleportSourceCell = cell; UIManager.Instance.ShowSkillMessage("Chọn đích...", 0);
        }
        else
        {
            if (!cell.IsEmpty()) return;
            TeleportPieceServerRpc(teleportSourceCell.row, teleportSourceCell.col, cell.row, cell.col);
            isTeleportMode = false; teleportSourceCell = null;
        }
    }

    private void HandleSwapClient(Cell cell)
    {
        if (swapCell1 == null)
        {
            swapCell1 = cell; UIManager.Instance.ShowSkillMessage("Chọn quân 2...", 0);
        }
        else
        {
            SwapPieceServerRpc(swapCell1.row, swapCell1.col, cell.row, cell.col);
            isSwapMode = false; swapCell1 = null;
        }
    }

    private void CancelActiveSkillMode()
    {
        isTeleportMode = false; isSwapMode = false;
        teleportSourceCell = null; swapCell1 = null;
        UIManager.Instance.ShowSkillMessage("Đã hủy.", 2f);
    }
}