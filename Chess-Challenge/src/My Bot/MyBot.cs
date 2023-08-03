using System.Linq;
using System.Collections.Generic;
using ChessChallenge.API;

public class TimeUpV4 : System.Exception { }

public class MyBotV4 : IChessBot
{

    int[] PAWN_SCORES = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -10, -10, 0, 0, 0, 10, 5, 5, 10, 10, 5, 5, 10, 5, 5, 10, 20, 20, 10, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 10, 10, 10, 20, 20, 10, 10, 10, 20, 20, 20, 30, 30, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, };
    int[] KNIGHT_SCORES = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 10, 10, 10, 10, 5, 0, 0, 10, 20, 20, 20, 20, 10, 0, 0, 10, 20, 25, 25, 20, 10, 0, 0, 10, 20, 25, 25, 20, 10, 0, 0, 10, 20, 20, 20, 20, 10, 0, 0, 5, 10, 10, 10, 10, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    int[] BISHOP_SCORES = new[] { 0, 5, 5, 5, 5, 5, 5, 0, 5, 0, 0, 10, 10, 0, 0, 5, 5, 0, 10, 20, 20, 10, 0, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 20, 20, 20, 20, 10, 5, 5, 20, 20, 20, 20, 20, 20, 5, 5, 5, 0, 0, 0, 0, 5, 5, 0, 5, 5, 5, 5, 5, 5, 0 };
    int[] ROOK_SCORES = new[] { 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 10, 10, 10, 10, 10, 10, 10, 10, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 5, 5, 5, 10, 10, 5, 5, 5, 0, 0, 0, 20, 20, 0, 0, 0 };
    int[] QUEEN_SCORES = new[] { 0, 5, 5, 5, 5, 5, 5, 0, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 10, 20, 20, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 5, 10, 10, 10, 10, 10, 10, 5, 0, 5, 5, 5, 5, 5, 5, 0 };
    int[] KING_SCORES = new[] { 30, 50, 40, 40, 40, 40, 50, 30, 30, 30, 30, 30, 30, 30, 30, 30, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    int[] PIECE_SCORES = new[] { 100, 300, 310, 500, 900, 10000 };
    int[][] POS_SCORES;
    // int MAX_DEPTH = 7;
    int TIME_PER_MOVE = 1000;

    public MyBotV4()
    {
        POS_SCORES = new[] { PAWN_SCORES, KNIGHT_SCORES, BISHOP_SCORES, ROOK_SCORES, QUEEN_SCORES, KING_SCORES };
    }

    double evaluate(Board board)
    {
        double material = 0;
        double position = 0;
        PieceList[] pieces = board.GetAllPieceLists();



        foreach (PieceList piece_list in pieces)
            foreach (Piece piece in piece_list)
            {
                material += PIECE_SCORES[(int)piece.PieceType - 1] * (piece.IsWhite ? 1 : -1);
                position += POS_SCORES[(int)piece.PieceType - 1][piece.IsWhite ? piece.Square.Index : piece.Square.Index ^ 56] * (piece.IsWhite ? 1 : -1);
            }

        return material + (position * 2); // * (1 - ((double)board.PlyCount / 50)));
    }

    Move[] OrderMoves(Board board, Move[] moves, Move pv_move)
    {
        List<int> scores = new List<int> { };

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
        int search_depth = 1;
        int nodes = 0;

        (Move, double) negaMax(Move pv_move, int depth_left, double alpha, double beta, bool color)
        {
            nodes++;

            // Check time
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) throw new TimeUpV4();

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
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) throw new TimeUpV4();

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

        // for (; timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE; search_depth++) try
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE / 2) try
            {
                (move, score) = negaMax(move, search_depth, double.NegativeInfinity, double.PositiveInfinity, true);
                //System.Console.WriteLine(search_depth + " " + move + " " + nodes);
                nodes = 0;
                search_depth++;
            }
            catch (TimeUpV4) { }
        //System.Console.WriteLine();


        return move;
    }
}

