using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class TimeUp : Exception { }

public class MyBot : IChessBot
{
    //                                      Null, Pawn, Knight, Bishop, Rook, Queen, King // King is 0 for MVV-LVA
    private readonly int[] PIECE_VALUES = { 0, 100, 320, 330, 500, 900, 1000 };
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

    private struct Transposition
    {
        public Transposition(ulong zHash, double eval, int d)
        {
            zobristHash = zHash;
            evaluation = eval;
            depth = d;
            move = Move.NullMove;
            flag = -2;
        }

        public ulong zobristHash = 0;
        public double evaluation = 0;
        public int depth = 0;
        public Move move = Move.NullMove;
        public int flag = -2;
    };

    private readonly Transposition[] tTable = new Transposition[0x7FFFFF + 1];

    private ref Transposition GetTransposition(ulong key) { return ref tTable[key & 0x7FFFFF]; }

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

    private static int NonPawnPieceCount(Board board)
    {
        return BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard & ~(board.GetPieceBitboard(PieceType.Pawn, false) | board.GetPieceBitboard(PieceType.Pawn, true))) - 2;
    }

    private double Evaluate(Board board, bool color)
    {
        bool isEndGame = NonPawnPieceCount(board) < 7;
        double score = board.IsWhiteToMove && !isEndGame ? 100 : 0;

        PieceList[] pieceLists = board.GetAllPieceLists();
        foreach (PieceList piece_list in pieceLists)
        {
            int offset = 0;
            if (isEndGame) if (piece_list.TypeOfPieceInList == PieceType.Pawn) offset = 7; else if (piece_list.TypeOfPieceInList == PieceType.King) offset = 1;

            foreach (Piece piece in piece_list)
            {
                score += PIECE_VALUES[(int)piece.PieceType] * (piece.IsWhite ? 2 : -2);
                score += PieceSquareScore((int)piece.PieceType + offset, piece.IsWhite, piece.Square.Rank, piece.Square.File);
            }
        }

        return color ? score : -score;
    }

    private Move[] OrderMoves(Board board, Move[] moves, int depth)
    {
        Transposition t_entry = GetTransposition(board.ZobristKey);
        List<int> scores = new();

        foreach (Move move in moves)
        {
            int score;
            if (move == t_entry.move && t_entry.zobristHash == board.ZobristKey) score = -10000;
            else if (move.IsCapture) score = -10 * (int)move.CapturePieceType + (int)move.MovePieceType; // MVV-LVA 0 - -9000
            else if (move == killerMoves[depth, 0]) score = 1000;
            else if (move == killerMoves[depth, 1]) score = 1001;
            else if (move.IsPromotion) score = 2000 - PIECE_VALUES[(int)move.MovePieceType];
            else if (IsCheck(board, move)) score = 5000;
            else score = 10000;

            scores.Add(score);
        }

        Array.Sort(scores.ToArray(), moves);
        return moves.ToArray();
    }

    public Move Think(Board board, Timer timer)
    {
        int search_time = timer.MillisecondsRemaining / 40 + timer.IncrementMilliseconds / 3;
        ulong og_zobristKey = board.ZobristKey;

        double NegaMaxQSearch(int depth_left, double alpha, double beta, bool color, bool qSearch)
        {
            // Check time
            if (timer.MillisecondsElapsedThisTurn > search_time) throw new TimeUp();

            // Generate Moves
            var moves = board.GetLegalMoves(qSearch && !board.IsInCheck());

            if (!qSearch)
            {
                // Terminal Conditions
                if (moves.Length == 0 && board.IsInCheck()) return -10000 - depth_left;
                if (board.IsDraw()) return 0;
                if (depth_left == 0) return NegaMaxQSearch(0, alpha, beta, color, true);
            }
            else
            {
                // Eval for Q Search
                alpha = Math.Max(Evaluate(board, color), alpha);
                if (alpha >= beta) return beta;
            }

            // Check for entry in transposition table
            double old_alpha = alpha;
            ref Transposition t_entry = ref GetTransposition(board.ZobristKey);
            if (t_entry.flag != -2 && t_entry.zobristHash == board.ZobristKey && t_entry.depth >= depth_left)
            {
                if (t_entry.flag == 0) return t_entry.evaluation;
                if (t_entry.flag == -1) alpha = Math.Max(alpha, t_entry.evaluation);
                else if (t_entry.flag == 1) beta = Math.Min(beta, t_entry.evaluation);
                if (alpha >= beta) return t_entry.evaluation;
            }

            // Initialize Search
            double best_score = double.NegativeInfinity;
            foreach (Move move in OrderMoves(board, moves, depth_left))
            {
                board.MakeMove(move);
                double score = -NegaMaxQSearch(qSearch ? 0 : depth_left - 1, -beta, -alpha, !color, qSearch);
                board.UndoMove(move);

                if (score > best_score)
                {
                    best_score = score;
                    if (!qSearch) t_entry.move = move;
                }

                alpha = Math.Max(best_score, alpha);
                if (alpha >= beta)
                {
                    // Keep best two Killer Moves
                    if (!qSearch && !move.IsCapture)
                    {
                        killerMoves[depth_left, 1] = killerMoves[depth_left, 0];
                        killerMoves[depth_left, 0] = move;
                    }

                    break;
                }
            }

            if (!qSearch)
            {
                // Store in Transposition Table
                t_entry.zobristHash = board.ZobristKey;
                if (best_score < old_alpha) t_entry.flag = 1; else if (best_score >= beta) t_entry.flag = -1; else t_entry.flag = 0;
                t_entry.evaluation = best_score;
                t_entry.depth = depth_left;
            }

            return alpha;
        }

        // Iterative Deepening Search
        sbyte search_depth = 1;
        while (timer.MillisecondsElapsedThisTurn < search_time / 2 && search_depth < 20) try
            {
                NegaMaxQSearch(search_depth++, double.NegativeInfinity, double.PositiveInfinity, board.IsWhiteToMove, false);
                // Transposition t_entry = GetTransposition(og_zobristKey);
                // Console.WriteLine(search_depth - 1 + "\t" + t_entry.move + "\t" + t_entry.evaluation);
            }
            catch (TimeUp) { }
        // Console.WriteLine();

        return GetTransposition(og_zobristKey).move;
    }
}
