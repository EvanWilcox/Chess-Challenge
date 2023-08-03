using System.Linq;
using ChessChallenge.API;

public class TimeUpV5 : System.Exception { }

public class MyBotV5 : IChessBot
{
    private const sbyte EXACT = 0, LOWERBOUND = -1, UPPERBOUND = 1, INVALID = -2;

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

    private int PieceScore(int type, bool isWhite, int rank, int file)
    {
        //Because the arrays are 8x4, we need to mirror across the files.
        if (file > 3) file = 7 - file;
        //Additionally, if we're checking black pieces, we need to flip the board vertically.
        if (!isWhite) rank = 7 - rank;
        int unpackedData = 0;
        // ulong bytemask = 0xFF;
        //first we shift the mask to select the correct byte              ↓
        //We then bitwise-and it with PackedScores            ↓
        //We finally have to "un-shift" the resulting data to properly convert back       ↓
        //We convert the result to an sbyte, then to an int, to ensure it converts properly.
        unpackedData = (sbyte)((packedScores[rank, file] >> 8 * ((int)type - 1)) & 0xFF);
        unpackedData = (sbyte)((byte)unpackedData | (0b10000000 & unpackedData));
        //inverting eval scores for black pieces
        if (!isWhite) unpackedData *= -1;
        return unpackedData;
    }

    int[] PIECE_SCORES = new[] { 100, 300, 310, 500, 900, 10000 };
    // int MAX_DEPTH = 7;
    int TIME_PER_MOVE = 1000;

    double evaluate(Board board)
    {
        double material = 0;
        double position = 0;
        PieceList[] pieces = board.GetAllPieceLists();

        foreach (PieceList piece_list in pieces)
            foreach (Piece piece in piece_list)
            {
                material += PIECE_SCORES[(int)piece.PieceType - 1] * (piece.IsWhite ? 1 : -1);
                position += PieceScore((int)piece.PieceType - 1, piece.IsWhite, piece.Square.Rank, piece.Square.File);
            }

        return material + (position * 2); // * (1 - ((double)board.PlyCount / 50)));
    }

    Move[] OrderMoves(Board board, Move[] moves, Move pv_move)
    {
        System.Collections.Generic.List<int> scores = new();

        foreach (Move move in moves)
        {
            int score = 0;
            if (move == pv_move) score = 10000;
            else
            {
                if (move.IsCapture) score += 10 * PIECE_SCORES[(int)board.GetPiece(move.TargetSquare).PieceType - 1] - PIECE_SCORES[(int)board.GetPiece(move.StartSquare).PieceType - 1];
                if (move.IsPromotion) score += PIECE_SCORES[(int)move.PromotionPieceType - 1];
                // else if (board.SquareIsAttackedByOpponent(move.TargetSquare)) score -= PIECE_SCORES[(int)move.MovePieceType - 1];
            }
            scores.Add(score);
        }

        System.Array.Sort(scores.ToArray(), moves);
        return moves.Reverse().ToArray();
    }

    public Move Think(Board board, Timer timer)
    {
        bool myColor = board.IsWhiteToMove;
        sbyte search_depth = 1;
        int nodes = 0;

        (Move, double) negaMax(Move pv_move, int depth_left, double alpha, double beta, bool color)
        {
            nodes++;

            // Check time
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) throw new TimeUpV5();

            // Generate Moves
            Move[] moves = board.GetLegalMoves();

            // Terminal Conditions
            if (board.IsInCheckmate()) return (Move.NullMove, ((color == myColor ? 1 : -1) * (board.IsWhiteToMove ? -1 : 1) * (10000 + depth_left)));
            if (board.IsDraw()) return (Move.NullMove, 0);
            if (depth_left == 0) return (Move.NullMove, quiesce(alpha, beta, color));

            // Initialize Search
            Move best_move = pv_move;
            double best_score = double.NegativeInfinity;

            foreach (Move move in OrderMoves(board, moves, pv_move))
            {
                board.MakeMove(move);
                (Move pv_move_, double score) = negaMax(Move.NullMove, depth_left - 1, -beta, -alpha, !color);
                score = -score;
                board.UndoMove(move);

                if (score > best_score) { best_score = score; best_move = move; }
                if (best_score > alpha) alpha = best_score;
                if (alpha >= beta) break;
            }

            return (best_move, best_score);
        }

        double quiesce(double alpha, double beta, bool color)
        {
            nodes++;
            // Check time
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) throw new TimeUpV5();

            double eval = (color == myColor ? 1 : -1) * evaluate(board);
            if (eval >= beta) return beta;
            if (alpha < eval) alpha = eval;

            foreach (Move move in OrderMoves(board, board.GetLegalMoves(true), Move.NullMove))
            {
                board.MakeMove(move);
                double score = -quiesce(-beta, -alpha, !color);
                board.UndoMove(move);

                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        // Iterative Deepening Search
        Move move = board.GetLegalMoves()[0];
        double score = 0;

        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE / 2) try
            {
                (move, score) = negaMax(move, search_depth, double.NegativeInfinity, double.PositiveInfinity, true);
                System.Console.WriteLine(search_depth + " " + move + " " + nodes);
                nodes = 0;
                search_depth++;
            }
            catch (TimeUpV5) { }
        System.Console.WriteLine();

        return move;
    }
}
