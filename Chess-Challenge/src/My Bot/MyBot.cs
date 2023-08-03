using System.Linq;
using System.Collections.Generic;
using ChessChallenge.API;
using System;

public class TimeUpV6 : System.Exception { }


public class MyBotV6 : IChessBot
{
    private const int OPENING = 0, MIDDLE = 1, ENDGAME = 2;

    private static readonly ulong[,] packedScores =
    {
        {0xCDE1E1EBFFEBCE00, 0xD7D7D7F5FFF5D800, 0xE1D7D7F5FFF5E200, 0xEBCDCDFAFFF5E200},
        {0xE1E1E1F604F5D80A, 0xEBD7D80009FFEC0A, 0xF5D7D8000A000014, 0xFFCDCE000A00001E},
        {0xE1E1E1F5FAF5E232, 0xF5D7D80000000032, 0x13D7D80500050A32, 0x1DCDCE05000A0F32},
        {0xE1E1E1FAFAF5E205, 0xF5D7D80000050505, 0x1DD7D80500050F0A, 0x27CDCE05000A1419},
        {0xE1EBEBFFFAF5E200, 0xF5E1E20000000000, 0x1DE1E205000A0F00, 0x27D7D805000A1414},
        {0xE1F5F5F5FAF5E205, 0xF5EBEC05000A04FB, 0x13EBEC05000A09F6, 0x1DEBEC05000A0F00},
        {0xE21413F5FAF5D805, 0xE21414000004EC0A, 0x000000050000000A, 0x00000000000004EC},
        {0xCE1413EBFFEBCE00, 0xE21E1DF5FFF5D800, 0xE20A09F5FFF5E200, 0xE1FFFFFB04F5E200},
    };

    private static readonly int[,] mvv_lva =
    {
        {105,205,305,405,505},
        {104,204,304,404,504},
        {103,203,303,403,503},
        {102,202,302,402,502},
        {101,201,301,401,501},
        {100,200,300,400,500},
    };

    private const sbyte EXACT = 0, LOWERBOUND = -1, UPPERBOUND = 1, INVALID = -2;

    public struct Transposition
    {
        public Transposition(ulong zHash, double eval, int d)
        {
            zobristHash = zHash;
            evaluation = eval;
            depth = d;
            move = Move.NullMove;
            flag = INVALID;
        }

        public ulong zobristHash = 0;
        public double evaluation = 0;
        public int depth = 0;
        public Move move = Move.NullMove;
        public int flag = INVALID;
    };


    private static ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB

    private Transposition[] m_TPTable = new Transposition[k_TpMask + 1];

    private int PieceSquareScore(int type, bool isWhite, int rank, int file)
    {
        if (file > 3) file = 7 - file;
        if (!isWhite) rank = 7 - rank;
        sbyte unpackedData = unchecked((sbyte)((packedScores[rank, file] >> 8 * ((int)type - 1)) & 0xFF));
        return isWhite ? unpackedData : -unpackedData;
    }

    int[] PIECE_VALUES = { 100, 300, 310, 500, 900, 10000 };


    double evaluate(Board board, bool color)
    {
        double material = 0;
        double position = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        foreach (PieceList piece_list in pieceLists)
            foreach (Piece piece in piece_list)
            {
                material += PIECE_VALUES[(int)piece.PieceType - 1] * (piece.IsWhite ? 1 : -1);
                position += PieceSquareScore((int)piece.PieceType - 1, piece.IsWhite, piece.Square.Rank, piece.Square.File);
            }

        return (material + (position)) * (color ? 1 : -1);
    }

    Move[] OrderMoves(Board board, Move[] moves)
    {
        Move pv_move = m_TPTable[board.ZobristKey & k_TpMask].move; // Either a pv_move or Move.NullMove
        List<int> scores = new();

        foreach (Move move in moves)
        {
            int score = 0;
            if (move == pv_move) score += 10000;
            else if (move.IsCapture) score += mvv_lva[(int)move.MovePieceType - 1, (int)move.CapturePieceType - 1];
            if (move.IsPromotion) score += PIECE_VALUES[(int)move.PromotionPieceType - 1];

            scores.Add(score);
        }

        Array.Sort(scores.ToArray(), moves);
        return moves.Reverse().ToArray();
    }

    public Move Think(Board board, Timer timer)
    {
        int search_time = timer.MillisecondsRemaining / 50;
        bool myColor = board.IsWhiteToMove;
        sbyte search_depth = 1;
        string fen = board.GetFenString();

        double negaMax(int depth_left, double alpha, double beta, bool color)
        {
            // Check time
            if (timer.MillisecondsElapsedThisTurn > search_time) throw new TimeUpV6();

            // Terminal Conditions
            if (board.IsInCheckmate()) return -10000 + search_depth - depth_left;
            if (board.IsDraw()) return 0;
            if (depth_left == 0) return quiesce(alpha, beta, color);

            // Check for entry in transposition table
            double old_alpha = alpha;
            ref Transposition t_entry = ref m_TPTable[board.ZobristKey & k_TpMask];
            if (t_entry.flag != INVALID && t_entry.zobristHash == board.ZobristKey && t_entry.depth >= depth_left)
            {
                if (t_entry.flag == EXACT) return t_entry.evaluation;
                if (t_entry.flag == LOWERBOUND) alpha = Math.Max(alpha, t_entry.evaluation);
                else if (t_entry.flag == UPPERBOUND) beta = Math.Min(beta, t_entry.evaluation);
                if (alpha >= beta) return t_entry.evaluation;
            }

            // Generate Moves
            Move[] moves = board.GetLegalMoves();

            // Initialize Search
            Move best_move = moves[0];
            double best_score = double.NegativeInfinity;

            foreach (Move move in OrderMoves(board, moves))
            {
                board.MakeMove(move);
                double score = -negaMax(depth_left - 1, -beta, -alpha, !color); // negate score before using it. 
                board.UndoMove(move);

                if (score > best_score) { best_score = score; best_move = move; }
                if (best_score > alpha) alpha = best_score;
                if (alpha >= beta) break;
            }

            // Store in Transposition Table
            t_entry.zobristHash = board.ZobristKey;
            if (best_score < old_alpha) t_entry.flag = UPPERBOUND; else if (best_score >= beta) t_entry.flag = LOWERBOUND; else t_entry.flag = EXACT;
            t_entry.evaluation = best_score;
            t_entry.depth = depth_left;
            t_entry.move = best_move;

            return best_score;
        }

        double quiesce(double alpha, double beta, bool color)
        {
            // Check time
            if (timer.MillisecondsElapsedThisTurn > search_time) throw new TimeUpV6();

            ref Transposition t_entry = ref m_TPTable[board.ZobristKey & k_TpMask];
            if (t_entry.flag != INVALID && t_entry.zobristHash == board.ZobristKey && t_entry.depth >= 0)
            {
                if (t_entry.flag == EXACT) return t_entry.evaluation;
                if (t_entry.flag == LOWERBOUND) alpha = Math.Max(alpha, t_entry.evaluation);
                else if (t_entry.flag == UPPERBOUND) beta = Math.Min(beta, t_entry.evaluation);
                if (alpha >= beta) return t_entry.evaluation;
            }

            alpha = Math.Max(evaluate(board, color), alpha);
            if (alpha >= beta) return beta;

            foreach (Move move in OrderMoves(board, board.GetLegalMoves(!board.IsInCheck())))
            {
                board.MakeMove(move);
                double score = -quiesce(-beta, -alpha, !color);
                board.UndoMove(move);

                alpha = Math.Max(score, alpha);
                if (alpha >= beta) break;
            }
            return alpha;
        }

        // Iterative Deepening Search

        while (timer.MillisecondsElapsedThisTurn < search_time / 2 && search_depth < 7) try
            {
                double score = negaMax(search_depth, double.NegativeInfinity, double.PositiveInfinity, board.IsWhiteToMove);
                search_depth++;
            }
            catch (TimeUpV6) { }

        return m_TPTable[Board.CreateBoardFromFEN(fen).ZobristKey & k_TpMask].move;
    }
}
