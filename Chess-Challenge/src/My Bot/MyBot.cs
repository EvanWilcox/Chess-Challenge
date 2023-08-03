using System.Linq;
using ChessChallenge.API;

public class MyBotV3 : IChessBot
{
    int[] PAWN_SCORES = new[] { 0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,
                                0   ,   0   ,   0   ,   -10 ,   -10 ,   0   ,   0   ,   0   ,
                                10  ,   5   ,   5   ,   10  ,   10  ,   5   ,   5   ,   10  ,
                                5   ,   5   ,   10  ,   20  ,   20  ,   10  ,   5   ,   5   ,
                                5   ,   5   ,   5   ,   10  ,   10  ,   5   ,   5   ,   5   ,
                                10  ,   10  ,   10  ,   20  ,   20  ,   10  ,   10  ,   10  ,
                                20  ,   20  ,   20  ,   30  ,   30  ,   20  ,   20  ,   20  ,
                                0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   , };
    int[] KNIGHT_SCORES = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 10, 10, 10, 10, 5, 0, 0, 10, 20, 20, 20, 20, 10, 0, 0, 10, 20, 25, 25, 20, 10, 0, 0, 10, 20, 25, 25, 20, 10, 0, 0, 10, 20, 20, 20, 20, 10, 0, 0, 5, 10, 10, 10, 10, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    int[] BISHOP_SCORES = new[] { 0, 5, 5, 5, 5, 5, 5, 0, 5, 0, 0, 10, 10, 0, 0, 5, 5, 0, 10, 20, 20, 10, 0, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 20, 20, 20, 20, 10, 5, 5, 20, 20, 20, 20, 20, 20, 5, 5, 5, 0, 0, 0, 0, 5, 5, 0, 5, 5, 5, 5, 5, 5, 0 };
    int[] ROOK_SCORES = new[] { 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 10, 10, 10, 10, 10, 10, 10, 10, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 0, 0, 0, 20, 20, 0, 0, 0 };
    int[] QUEEN_SCORES = new[] { 0, 5, 5, 5, 5, 5, 5, 0, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 0, 5, 5, 5, 5, 5, 5, 0 };
    int[] KING_SCORES = new[] { 30, 50, 40, 40, 40, 40, 50, 30, 30, 30, 30, 30, 30, 30, 30, 30, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    int[] PIECE_SCORES = new[] { 100, 300, 310, 500, 900, 10000 };
    int[][] POS_SCORES;
    int MAX_DEPTH = 5;

    public MyBotV3() { POS_SCORES = new[] { PAWN_SCORES, KNIGHT_SCORES, BISHOP_SCORES, ROOK_SCORES, QUEEN_SCORES, KING_SCORES }; }

    double evaluate(Board board)
    {
        double material = 0;
        double position = 0;
        PieceList[] pieces = board.GetAllPieceLists();

        foreach (PieceList piece_list in pieces)
        {
            foreach (Piece piece in piece_list)
            {
                material += PIECE_SCORES[(int)piece.PieceType - 1] * (piece.IsWhite ? 1 : -1);
                position += POS_SCORES[(int)piece.PieceType - 1][piece.IsWhite ? piece.Square.Index : piece.Square.Index ^ 56] * (piece.IsWhite ? 1 : -1);
            }
        }

        return material + (position * 2 * (1 - ((double)board.PlyCount / 50)));
    }

    public Move Think(Board board, Timer timer)
    {
        bool myColor = board.IsWhiteToMove;

        (Move, double) negaMax(int depth_left, double alpha, double beta, bool color)
        {
            Move[] moves = board.GetLegalMoves();

            // Return early if only one move available, no need to search. 
            if (moves.Length == 1) return (moves[0], 0);

            // Terminal Conditions
            if (depth_left == 0) return (Move.NullMove, ((color == myColor) ? 1 : -1) * evaluate(board));
            if (board.IsInCheckmate()) return (Move.NullMove, ((color == myColor ? 1 : -1) * (board.IsWhiteToMove ? -1 : 1) * (10000 - MAX_DEPTH + depth_left)));
            if (board.IsDraw()) return (Move.NullMove, 0);

            // Initialize Search
            Move best_move = moves[0];
            double best_score = double.NegativeInfinity;

            // Sort Moves
            Move[] captures = board.GetLegalMoves(true);
            Move[] sorted = captures.Concat(moves.Except(captures)).ToArray();

            foreach (Move move in sorted)
            {
                board.MakeMove(move);
                (Move pv_move, double score) = negaMax(depth_left - 1, -beta, -alpha, !color);
                score = -score;
                board.UndoMove(move);

                if (score > best_score) { best_score = score; best_move = move; }
                if (best_score > alpha) alpha = best_score;
                if (alpha >= beta) break;

                if (depth_left == MAX_DEPTH) { System.Console.WriteLine(move + " " + score); }
            }

            return (best_move, best_score);
        }

        (Move move, double score) = negaMax(MAX_DEPTH, double.NegativeInfinity, double.PositiveInfinity, true);
        // Console.WriteLine(move + " : " + score);

        return move;
    }
}
