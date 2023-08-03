using System.Linq;
using System.Collections.Generic;
using ChessChallenge.API;
using System;

public class TimeUpV7 : Exception { }



public class MyBotV7 : IChessBot
{
    //                                      Null, Pawn, Knight, Bishop, Rook, Queen, King // King is 0 for MVV-LVA
    private readonly int[] PIECE_VALUES = { 0, 100, 320, 330, 500, 900, 0 };
    private readonly ulong[,] packedScores =
    {
        {0xFFCE13EBEBEBCE00, 0xFFD81DF5EBF5D800, 0xFFE1FFF5EBF5E200, 0xFFEBFFFB09F5E200},
        {0xFFE1F5F5FAF5D805, 0xFFEBEC000004EC0A, 0xFFF5EC000000000A, 0xFFFFEBFFFFFFFFEC},
        {0x09E1F5F5FAF5E1FB, 0x09F5EC000009FFFB, 0x0A13EC05000A09F6, 0x0A1DEC05000A0F00},
        {0x13E1EBFAFAF5E200, 0x13F5E20000000500, 0x141DE205000A0F00, 0x1427D805000A1414},
        {0x1DE1E1FFFAF5E205, 0x1DF5D80000050005, 0x1E1DD80500050F0A, 0x1E27CE05000A1419},
        {0x27E1E1F5FAF5E20A, 0x27F5D8050000050A, 0x2813D80500050A14, 0x281DCE05000A0F1E},
        {0x31E1E1F604F5D832, 0x31E1D80009FFEC32, 0x31FFD8050A000032, 0x31FFCE000A000532},
        {0xFFCDE1EBFFEBCE00, 0xFFE1D7F5FFF5D800, 0xFFE1D7F5FFF5E200, 0xFFE1CDFAFFF5E200},
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

    private static readonly ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB

    private readonly Transposition[] m_TPTable = new Transposition[k_TpMask + 1];

    private ref Transposition GetTransposition(ulong key) { return ref m_TPTable[key & k_TpMask]; }

    private readonly Move[,] killerMoves = new Move[20, 2];

    static bool IsCheck(Board board, Move move)
    {
        board.MakeMove(move);
        bool check = board.IsInCheck();
        board.UndoMove(move);
        return check;
    }

    private int PieceSquareScore(int type, bool isWhite, int rank, int file)
    {
        if (file > 3) file = 7 - file;
        if (!isWhite) rank = 7 - rank;
        sbyte unpackedData = unchecked((sbyte)((packedScores[rank, file] >> 8 * (type - 1)) & 0xFF));
        return isWhite ? unpackedData : -unpackedData;
    }

    static bool IsEndGame(Board board)
    {
        int piece_count = 0;
        sbyte[] indexes = { 1, 2, 3, 4, 7, 8, 9, 10 }; // Knights, Bishops, Rooks, Queens
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (int index in indexes) piece_count += pieces[index].Count;
        return piece_count < 7;
    }


    double Evaluate(Board board, bool color)
    {
        double score = 100 * (board.IsWhiteToMove && board.PlyCount < 21 ? 1 : 0);

        PieceList[] pieceLists = board.GetAllPieceLists();

        foreach (PieceList piece_list in pieceLists)
        {
            int offset = 0;
            if (IsEndGame(board))
            {
                if (piece_list.TypeOfPieceInList == PieceType.Pawn) offset = 7; else if (piece_list.TypeOfPieceInList == PieceType.King) offset = 1;
            }
            foreach (Piece piece in piece_list)
            {
                score += PIECE_VALUES[(int)piece.PieceType] * (piece.IsWhite ? 1 : -1) * 2;
                // score += PieceSquareScore((int)piece.PieceType, piece.IsWhite, piece.Square.Rank, piece.Square.File);
                score += PieceSquareScore((int)piece.PieceType + offset, piece.IsWhite, piece.Square.Rank, piece.Square.File);
            }
        }

        return score * (color ? 1 : -1);
    }

    Move[] OrderMoves(Board board, Move[] moves, int depth)
    {
        Transposition t_entry = GetTransposition(board.ZobristKey);
        List<int> scores = new();

        foreach (Move move in moves)
        {
            int score = 0;
            if (move.Equals(t_entry.move) && t_entry.zobristHash == board.ZobristKey) score += 20000;
            else if (IsCheck(board, move)) score += 10000;
            else if (move.IsCapture) score += 10 * (int)move.CapturePieceType - (int)move.MovePieceType;
            if (move.IsPromotion) score += PIECE_VALUES[(int)move.MovePieceType];
            try { if (move == killerMoves[depth, 0] || move == killerMoves[depth, 1]) score += 1000; } catch (Exception) { }

            scores.Add(score);
        }

        Array.Sort(scores.ToArray(), moves);
        return moves.Reverse().ToArray();
    }

    // void PrintPV(string fen)
    // {
    //     Board board = Board.CreateBoardFromFEN(fen);
    //     Transposition t_entry = GetTransposition(board.ZobristKey);
    //     while (t_entry.move != Move.NullMove)
    //     {
    //         Console.Write(t_entry.move + " ");

    //         board.MakeMove(t_entry.move);
    //         t_entry = GetTransposition(board.ZobristKey);
    //     }
    //     Console.WriteLine();
    // }


    public Move Think(Board board, Timer timer)
    {
        int search_time = timer.MillisecondsRemaining / 50;
        //int nodes = 0;
        string fen = board.GetFenString();
        ulong og_zobristKey = board.ZobristKey;

        double negaMax(int depth_left, double alpha, double beta, bool color)
        {
            //nodes++;

            // Check time
            if (timer.MillisecondsElapsedThisTurn > search_time) throw new TimeUpV7();

            // Generate Moves
            Move[] moves = board.GetLegalMoves();

            // Terminal Conditions
            if (moves.Length == 0 && board.IsInCheck()) return -10000 - depth_left;
            if (board.IsDraw()) return 0;
            if (depth_left == 0) return quiesce(alpha, beta, color);

            // Check for entry in transposition table
            double old_alpha = alpha;
            ref Transposition t_entry = ref GetTransposition(board.ZobristKey);
            if ((t_entry.flag != INVALID) && (t_entry.zobristHash == board.ZobristKey) && (t_entry.depth >= depth_left))
            {
                if (t_entry.flag == EXACT) return t_entry.evaluation;
                if (t_entry.flag == LOWERBOUND) alpha = Math.Max(alpha, t_entry.evaluation);
                else if (t_entry.flag == UPPERBOUND) beta = Math.Min(beta, t_entry.evaluation);
                if (alpha >= beta) return t_entry.evaluation;
            }

            // Initialize Search
            Move best_move = moves[0];
            double best_score = double.NegativeInfinity;

            foreach (Move move in OrderMoves(board, moves, depth_left))
            {
                board.MakeMove(move);
                double score = -negaMax(depth_left - 1, -beta, -alpha, !color);
                board.UndoMove(move);

                if (score > best_score) { best_score = score; best_move = move; }
                if (best_score > alpha) alpha = best_score;
                if (alpha >= beta) { killerMoves[depth_left, 1] = killerMoves[depth_left, 0]; killerMoves[depth_left, 0] = best_move; break; }
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
            //nodes++;
            // Check time
            if (timer.MillisecondsElapsedThisTurn > search_time) throw new TimeUpV7();

            ref Transposition t_entry = ref GetTransposition(board.ZobristKey);
            if ((t_entry.flag != INVALID) && (t_entry.zobristHash == board.ZobristKey) && (t_entry.depth >= 0))
            {
                if (t_entry.flag == EXACT) return t_entry.evaluation;
                if (t_entry.flag == LOWERBOUND) alpha = Math.Max(alpha, t_entry.evaluation);
                else if (t_entry.flag == UPPERBOUND) beta = Math.Min(beta, t_entry.evaluation);
                if (alpha >= beta) return t_entry.evaluation;
            }

            alpha = Math.Max(Evaluate(board, color), alpha);
            if (alpha >= beta) return beta;

            foreach (Move move in OrderMoves(board, board.GetLegalMoves(!board.IsInCheck()), 0))
            {
                board.MakeMove(move);
                double score = -quiesce(-beta, -alpha, !color);
                board.UndoMove(move);

                alpha = Math.Max(score, alpha);
                if (alpha >= beta) break;
            }
            return alpha;
        }

        Transposition t_entry = GetTransposition(og_zobristKey);

        // Iterative Deepening Search
        sbyte search_depth = 1;
        while (timer.MillisecondsElapsedThisTurn < search_time / 2 && search_depth < 20) try
            {
                double score = negaMax(search_depth++, double.NegativeInfinity, double.PositiveInfinity, board.IsWhiteToMove);
                t_entry = GetTransposition(og_zobristKey);
                // PrintPV(fen);
                Console.WriteLine(search_depth - 1 + "\t" + t_entry.move + "\t" + score + "\t"); // + nodes + " " + collisions); // + " " + Phase(board));
                //nodes = 0;
            }
            catch (TimeUpV7) { }
        //Console.WriteLine();

        return t_entry.move;
    }
}
