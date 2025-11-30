using UnityEngine;
using UnityEngine.UI;

public class PlayerManager : MonoBehaviour
{
    public Transform player1ButtonParent;
    public Transform player2ButtonParent;

    void Start()
    {
        ApplyPlayerPieces();
    }

    void ApplyPlayerPieces()
    {
        // 🟢 Player 1
        PieceButton[] p1Buttons = player1ButtonParent.GetComponentsInChildren<PieceButton>(true);
        for (int i = 0; i < p1Buttons.Length; i++)
        {
            // Đảm bảo không bị lỗi IndexOutOfRange nếu số lượng nút khác số lượng piece data
            if (i < PieceSelectionData.player1Pieces.Length && PieceSelectionData.player1Pieces[i] != null)
            {
                var piece = PieceSelectionData.player1Pieces[i];
                p1Buttons[i].pieceData = piece;
                p1Buttons[i].GetComponent<Image>().sprite = piece.sprite;
            }
        }

        // 🔴 Player 2
        PieceButton[] p2Buttons = player2ButtonParent.GetComponentsInChildren<PieceButton>(true);
        for (int i = 0; i < p2Buttons.Length; i++)
        {
            if (i < PieceSelectionData.player2Pieces.Length && PieceSelectionData.player2Pieces[i] != null)
            {
                var piece = PieceSelectionData.player2Pieces[i];
                p2Buttons[i].pieceData = piece;
                p2Buttons[i].GetComponent<Image>().sprite = piece.sprite;
            }
        }
    }
}
