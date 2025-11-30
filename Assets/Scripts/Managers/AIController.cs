using System.Collections.Generic;
using UnityEngine;

public class AIController : MonoBehaviour
{
    private GameManager gameManager;
    private int boardSize;

    public struct MoveResult
    {
        public int row;
        public int col;
        public PieceData pieceToUse;
        public int score;
    }

    public void Init(GameManager gm, int size)
    {
        this.gameManager = gm;
        this.boardSize = size;
    }

    public MoveResult GetBotMove(CellState[,] currentState, PieceData[] botPieces, bool[] botTypeCompleted, int currentEnergy, bool isHardMode, int botPlayerIndex, 
                                 HashSet<(int, int)> forbiddenCells, bool isRestricted, int rCenter, int cCenter)
    {
        List<(int, int)> candidates = GetCandidateMoves(currentState, forbiddenCells, isRestricted, rCenter, cCenter);

        if (candidates.Count == 0) return new MoveResult { row = -1, score = -1 };

        // Lọc các quân cờ có thể chơi
        List<PieceData> playablePieces = new List<PieceData>();
        for (int i = 0; i < botPieces.Length; i++)
        {
            // Chỉ dùng quân chưa thắng và đủ năng lượng
            // (Lưu ý: Active Skill như Teleport/Swap tạm thời Bot chưa dùng được vì logic phức tạp, nên coi như quân thường)
            if (!botTypeCompleted[i] && botPieces[i].energyCost <= currentEnergy)
            {
                playablePieces.Add(botPieces[i]);
            }
        }
        
        // Fallback: nếu hết quân để thắng, lấy đại quân rẻ nhất để chặn
        if (playablePieces.Count == 0)
        {
            foreach(var p in botPieces) if(p.energyCost <= currentEnergy) playablePieces.Add(p);
        }
        
        if (playablePieces.Count == 0) return new MoveResult { row = -1 }; 

        MoveResult bestMove = new MoveResult { row = -1, score = int.MinValue };

        // --- LOGIC BOT ---
        foreach (var piece in playablePieces)
        {
            // Nếu là quân Active Skill (Teleport/Swap), Bot tạm thời bỏ qua hoặc dùng như quân thường
            // Vì Bot cần logic chọn 2 bước (chọn quân -> chọn mục tiêu) mà hiện tại ta chỉ làm 1 bước.
            
            foreach (var pos in candidates)
            {
                int r = pos.Item1;
                int c = pos.Item2;
                
                // Đánh giá điểm số dựa trên Passive
                int score = EvaluateMove(currentState, r, c, piece, botPlayerIndex);

                // Yếu tố ngẫu nhiên nhẹ để Bot không quá máy móc
                score += Random.Range(0, 5);

                if (score > bestMove.score)
                {
                    bestMove.row = r;
                    bestMove.col = c;
                    bestMove.pieceToUse = piece;
                    bestMove.score = score;
                }
            }
        }

        return bestMove;
    }

    // --- HÀM CHẤM ĐIỂM THÔNG MINH (HEURISTIC) ---
    int EvaluateMove(CellState[,] state, int r, int c, PieceData piece, int botPlayer)
    {
        int totalScore = 0;
        int opponent = (botPlayer == 1) ? 2 : 1;

        // 1. TẤN CÔNG: Tính điểm tạo chuỗi cho bản thân
        // (Truyền piece vào để biết luật FourInRow hoặc WildCard)
        totalScore += CalculateLines(state, r, c, botPlayer, piece) * 12;

        // 2. PHÒNG THỦ: Tính điểm chặn chuỗi đối phương
        // (Giả định đối phương dùng quân thường để tính mức độ nguy hiểm)
        totalScore += CalculateLines(state, r, c, opponent, null) * 10;

        // 3. ĐIỂM CHIẾN THUẬT THEO PASSIVE (CHIÊU THỨC)
        switch (piece.passive)
        {
            case PiecePassive.CrossStrike:
                // Nếu đặt con này, đếm xem diệt được bao nhiêu địch xung quanh
                int enemiesKilled = CountEnemiesAround(state, r, c, 1, opponent); // 1 ô xung quanh
                totalScore += enemiesKilled * 4000; // Ưu tiên diệt địch cao
                break;

            case PiecePassive.ExplodeOnDeath:
                // Con này nên đặt vào chỗ mà đối phương SẼ chặn (để nó nổ)
                // Hoặc đặt vào chỗ đông đúc
                int crowdScore = CountPiecesAround(state, r, c, 1);
                totalScore += crowdScore * 100; 
                break;

            case PiecePassive.IsolationLock:
                // Nên đặt ở chỗ vắng vẻ để khóa được nhiều ô trống
                int emptyAround = 8 - CountPiecesAround(state, r, c, 1);
                if (emptyAround == 8) totalScore += 500; // Rất tốt để chiếm đất
                break;

            case PiecePassive.RestrictArea:
                // Giam cầm: Tốt khi đối phương đang có xu hướng lan rộng
                totalScore += 200;
                break;

            case PiecePassive.UnblockableOnFour:
                // Logic này đã được xử lý trong CalculateLines (tăng điểm cho chuỗi 3)
                break;
                
            case PiecePassive.WildCardCaro:
                // WildCard rất quý, nên ưu tiên dùng để NỐI 2 chuỗi lẻ tẻ
                // (Logic này đã nằm trong CalculateLines nhờ khả năng match all)
                break;
                
            case PiecePassive.WinLineExplode:
                // Ưu tiên dùng con này để kết thúc trận (đã tính trong Attack)
                // Nhưng cũng cộng thêm điểm phá hoại
                totalScore += 100; 
                break;
        }

        return totalScore;
    }

    // --- LOGIC TÍNH ĐIỂM CHUỖI (Đã cập nhật luật Passive) ---
    int CalculateLines(CellState[,] state, int r, int c, int player, PieceData specificPiece)
    {
        int score = 0;
        
        // Cấu hình luật dựa trên quân cờ
        int winThreshold = 5;
        bool checkDiagonals = true;
        bool isWildCard = false;
        bool isUnblockable = false;

        if (specificPiece != null)
        {
            if (specificPiece.passive == PiecePassive.FourInRowWin)
            {
                winThreshold = 4; // Chỉ cần 4 con
                checkDiagonals = false; // Không tính chéo
            }
            if (specificPiece.passive == PiecePassive.WildCardCaro) isWildCard = true;
            if (specificPiece.passive == PiecePassive.UnblockableOnFour) isUnblockable = true;
        }

        int[] dx = { 1, 0, 1, 1 };
        int[] dy = { 0, 1, 1, -1 };

        for (int i = 0; i < 4; i++) 
        {
            // Nếu là FourInRowWin thì bỏ qua 2 hướng chéo (index 2 và 3)
            if (!checkDiagonals && (i == 2 || i == 3)) continue;

            int count = 1; 
            int openEnds = 0;
            
            // Hướng Dương (+)
            int tr = r + dx[i], tc = c + dy[i];
            while (IsValid(tr, tc) && state[tr, tc].owner == player)
            {
                // Kiểm tra tính tương thích quân cờ
                PieceData neighborPiece = state[tr, tc].pieceData;
                
                // Nếu mình là Wildcard -> Ok hết
                // Nếu bạn là Wildcard -> Ok hết
                // Nếu cả 2 là quân thường -> Phải cùng loại (trừ khi đối phương thì chỉ cần check owner)
                if (specificPiece != null) // Đang tính điểm cho Bot
                {
                     if (!isWildCard && 
                         neighborPiece != specificPiece && 
                         neighborPiece.passive != PiecePassive.WildCardCaro) 
                         break;
                }
                
                count++; tr += dx[i]; tc += dy[i];
            }
            if (IsValid(tr, tc) && state[tr, tc].owner == 0 && !state[tr, tc].isLava) openEnds++;

            // Hướng Âm (-)
            tr = r - dx[i]; tc = c - dy[i];
            while (IsValid(tr, tc) && state[tr, tc].owner == player)
            {
                PieceData neighborPiece = state[tr, tc].pieceData;
                if (specificPiece != null)
                {
                     if (!isWildCard && 
                         neighborPiece != specificPiece && 
                         neighborPiece.passive != PiecePassive.WildCardCaro) 
                         break;
                }
                count++; tr -= dx[i]; tc -= dy[i];
            }
            if (IsValid(tr, tc) && state[tr, tc].owner == 0 && !state[tr, tc].isLava) openEnds++;

            // --- TÍNH ĐIỂM ---
            if (count >= winThreshold) score += 100000; // THẮNG
            else if (count == winThreshold - 1) // Sắp thắng (Vd: 4/5 hoặc 3/4)
            {
                if (openEnds >= 1) 
                {
                    if (isUnblockable) score += 50000; // Unblockable mà được 3 con (thoáng) là coi như thắng
                    else score += 15000; 
                }
                if (openEnds == 2) score += 30000; 
            }
            else if (count == winThreshold - 2) // Vd: 3/5 hoặc 2/4
            {
                if (openEnds == 2) score += 5000;
                if (openEnds == 1) score += 500;
            }
            else if (count == 2 && openEnds == 2) score += 100;
        }
        return score;
    }

    // Đếm số kẻ địch xung quanh (Cho CrossStrike)
    int CountEnemiesAround(CellState[,] state, int r, int c, int range, int enemyPlayer)
    {
        int count = 0;
        int[] dx = { 1, -1, 0, 0 }; // 4 hướng dấu cộng
        int[] dy = { 0, 0, 1, -1 };

        for(int i=0; i<4; i++)
        {
            int nr = r + dx[i];
            int nc = c + dy[i];
            if(IsValid(nr, nc) && state[nr, nc].owner == enemyPlayer)
            {
                count++;
            }
        }
        return count;
    }

    // Đếm mật độ quân cờ xung quanh (Cho Explode/Isolation)
    int CountPiecesAround(CellState[,] state, int r, int c, int range)
    {
        int count = 0;
        for (int dr = -range; dr <= range; dr++)
        {
            for (int dc = -range; dc <= range; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr;
                int nc = c + dc;
                if (IsValid(nr, nc) && state[nr, nc].owner != 0)
                {
                    count++;
                }
            }
        }
        return count;
    }

    List<(int, int)> GetCandidateMoves(CellState[,] state, HashSet<(int, int)> forbidden, bool isRestricted, int rCenter, int cCenter)
    {
        HashSet<(int, int)> candidates = new HashSet<(int, int)>();
        int range = 2; 
        int minR = 0, maxR = boardSize - 1, minC = 0, maxC = boardSize - 1;
        if (isRestricted) {
            minR = Mathf.Max(0, rCenter - 2); maxR = Mathf.Min(boardSize - 1, rCenter + 2);
            minC = Mathf.Max(0, cCenter - 2); maxC = Mathf.Min(boardSize - 1, cCenter + 2);
        }

        for (int r = 0; r < boardSize; r++) {
            for (int c = 0; c < boardSize; c++) {
                if (state[r, c].owner != 0) {
                    for (int dr = -range; dr <= range; dr++) {
                        for (int dc = -range; dc <= range; dc++) {
                            int nr = r + dr; int nc = c + dc;
                            if (IsValid(nr, nc) && state[nr, nc].owner == 0 && !state[nr, nc].isLava) {
                                if (forbidden.Contains((nr, nc))) continue;
                                if (isRestricted) { if (nr < minR || nr > maxR || nc < minC || nc > maxC) continue; }
                                candidates.Add((nr, nc));
                            }
                        }
                    }
                }
            }
        }
        if (candidates.Count == 0 && isRestricted) {
             for (int r = minR; r <= maxR; r++) {
                 for (int c = minC; c <= maxC; c++) {
                     if (state[r, c].owner == 0 && !state[r, c].isLava && !forbidden.Contains((r, c))) candidates.Add((r, c));
                 }
             }
        }
        return new List<(int, int)>(candidates);
    }

    bool IsValid(int r, int c) => r >= 0 && r < boardSize && c >= 0 && c < boardSize;
}