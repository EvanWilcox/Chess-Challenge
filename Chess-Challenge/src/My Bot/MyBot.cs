using System;
using System.Linq;
using ChessChallenge.API;

public class MyBotV2 : IChessBot
{

    int[] PAWN_SCORES = new[] {
        0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,
        0   ,   0   ,   0   ,   -10 ,   -10 ,   0   ,   0   ,   0   ,
        10  ,   5   ,   5   ,   10  ,   10  ,   5   ,   5   ,   10  ,
        5   ,   5   ,   10  ,   20  ,   20  ,   10  ,   5   ,   5   ,
        5   ,   5   ,   5   ,   10  ,   10  ,   5   ,   5   ,   5   ,
        10  ,   10  ,   10  ,   20  ,   20  ,   10  ,   10  ,   10  ,
        20  ,   20  ,   20  ,   30  ,   30  ,   20  ,   20  ,   20  ,
        0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,
    };
    int[] KNIGHT_SCORES = new[] {
        0   ,   -10 ,   0   ,   0   ,   0   ,   0   ,   -10 ,   0   ,
        0   ,   0   ,   0   ,   5   ,   5   ,   0   ,   0   ,   0   ,
        0   ,   0   ,   10  ,   10  ,   10  ,   10  ,   0   ,   0   ,
        0   ,   0   ,   10  ,   20  ,   20  ,   10  ,   5   ,   0   ,
        5   ,   10  ,   15  ,   20  ,   20  ,   15  ,   10  ,   5   ,
        5   ,   10  ,   10  ,   20  ,   20  ,   10  ,   10  ,   5   ,
        0   ,   0   ,   5   ,   10  ,   10  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,
    };
    int[] BISHOP_SCORES = new[] {
        0   ,   0   ,   -10 ,   0   ,   0   ,   -10 ,   0   ,   0   ,
        0   ,   0   ,   0   ,   10  ,   10  ,   0   ,   0   ,   0   ,
        0   ,   0   ,   10  ,   15  ,   15  ,   10  ,   0   ,   0   ,
        0   ,   10  ,   15  ,   20  ,   20  ,   15  ,   10  ,   0   ,
        0   ,   10  ,   15  ,   20  ,   20  ,   15  ,   10  ,   0   ,
        0   ,   0   ,   10  ,   15  ,   15  ,   10  ,   0   ,   0   ,
        0   ,   0   ,   0   ,   10  ,   10  ,   0   ,   0   ,   0   ,
        0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,   0   ,
    };
    int[] ROOK_SCORES = new[] {
        -10 ,   -10 ,   -10 ,   15  ,   15  ,   15  ,   -10 ,   -10 ,
        0   ,   0   ,   5   ,   20  ,   20  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   20  ,   20  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   20  ,   20  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   20  ,   20  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   20  ,   20  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   10  ,   10  ,   5   ,   0   ,   0   ,
        0   ,   0   ,   5   ,   10  ,   10  ,   5   ,   0   ,   0   ,
    };
    int[] QUEEN_SCORES = new[] {
        -20 ,-10    ,-10    , -5    , -5    ,-10    ,-10    ,-20    ,
        -10 ,  0    ,  0    ,  0    ,  0    ,  0    ,  0    ,-10    ,
        -10 ,  0    ,  5    ,  5    ,  5    ,  5    ,  0    ,-10    ,
        -5  ,  0    ,  5    ,  5    ,  5    ,  5    ,  0    , -5    ,
        -5  ,  0    ,  5    ,  5    ,  5    ,  5    ,  0    , -5    ,
        -10 ,  0    ,  5    ,  5    ,  5    ,  5    ,  0    ,-10    ,
        -10 ,  0    ,  0    ,  0    ,  0    ,  0    ,  0    ,-10    ,
        -20 ,-10    ,-10    , -5    , -5    ,-10    ,-10    ,-20    ,
    };
    int[] KING_SCORES = new[] {
         5  ,    5  ,    15,    -10 ,   -10 ,   -10 ,    15,     5  ,
        -30 ,   -30 ,   -30 ,   -30 ,   -30 ,   -30 ,   -30 ,   -30 ,
        -50 ,   -50 ,   -50 ,   -50 ,   -50 ,   -50 ,   -50 ,   -50 ,
        -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,
        -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,
        -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,
        -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,
        -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,   -70 ,
    };
    int[] PIECE_SCORES = new[] { 0, 100, 300, 310, 500, 900, 10000 };

    int[][] POS_SCORES;

    int MAX_DEPTH = 5;

    public MyBotV2()
    {
        POS_SCORES = new[] { new int[] { }, PAWN_SCORES, KNIGHT_SCORES, BISHOP_SCORES, ROOK_SCORES, QUEEN_SCORES, KING_SCORES };
    }

    double evaluate(Board board)
    {
        int mirror(int square) { return square ^ 56; }

        double material = 0;
        double position = 0;
        double mobility = 0;
        PieceList[] pieces = board.GetAllPieceLists();

        foreach (PieceList piece_list in pieces)
        {
            foreach (Piece piece in piece_list)
            {
                material += PIECE_SCORES[(int)piece.PieceType] * (piece.IsWhite ? 1 : -1);

                if (piece.IsWhite)
                {
                    position += POS_SCORES[(int)piece.PieceType][piece.Square.Index];
                }
                else
                {
                    position -= POS_SCORES[(int)piece.PieceType][mirror(piece.Square.Index)];
                }
            }
        }

        mobility += board.GetLegalMoves().Length * (board.IsWhiteToMove ? 1 : -1);
        // Console.WriteLine("Mobility " + mobility);

        if (board.TrySkipTurn())
        {
            mobility += board.GetLegalMoves().Length * (board.IsWhiteToMove ? 1 : -1);
            board.UndoSkipTurn();
            // Console.WriteLine("Skipped " + mobility);
        }
        else
        {
            mobility = 0;
        }

        // Console.WriteLine("Mobility: " + mobility);

        return (material) + (position * 2) + (mobility);
    }

    public Move Think(Board board, Timer timer)
    {
        bool myColor = board.IsWhiteToMove;

        Move negaMaxHandler(double alpha, double beta)
        {
            Move[] moves = board.GetLegalMoves();
            if (moves.Length == 1) return moves[0]; // Return early if only one move available, no need to search. 

            Move best_move = moves[0];
            double best_score = double.NegativeInfinity;

            Move[] captures = board.GetLegalMoves(true);
            Move[] sorted = captures.Concat(moves.Except(captures)).ToArray();

            foreach (Move move in sorted)
            {
                board.MakeMove(move);
                double score = -negaMax(MAX_DEPTH - 1, -beta, -alpha, false); // I don't know why color=false, don't change it. 
                board.UndoMove(move);

                if (score > best_score)
                {
                    best_score = score;
                    best_move = move;
                }
                if (best_score > alpha) alpha = best_score;
                if (alpha >= beta) break;

                // Console.WriteLine(move + " " + score);
            }


            return best_move;
        }

        double negaMax(int depth_left, double alpha, double beta, bool color)
        {
            if (depth_left == 0) return ((color == myColor) ? 1 : -1) * evaluate(board);
            if (board.IsDraw()) return 0;

            double best_score = double.NegativeInfinity;

            Move[] moves = board.GetLegalMoves();
            Move[] captures = board.GetLegalMoves(true);
            Move[] sorted = captures.Concat(moves.Except(captures)).ToArray();

            foreach (Move move in sorted)
            {
                board.MakeMove(move);
                double score = -negaMax(depth_left - 1, -beta, -alpha, !color);
                board.UndoMove(move);

                best_score = Math.Max(score, best_score);
                alpha = Math.Max(alpha, best_score);

                if (alpha >= beta) break;
            }

            return best_score;
        }



        // double quiesce(double alpha, double beta, bool color)
        // {
        //     int offset = (color == myColor) ? 1 : -1;
        //     double eval = offset * evaluate(board);
        //     if (eval >= beta) return beta;
        //     if (alpha < eval) alpha = eval;

        //     foreach (Move move in board.GetLegalMoves(true))
        //     {
        //         board.MakeMove(move);
        //         double score = -quiesce(-beta, -alpha, !color);
        //         board.UndoMove(move);

        //         if (score >= beta) return beta;

        //         int BIG_DELTA = 900; // queen value
        //         if (move.IsPromotion) BIG_DELTA += 775;
        //         if (score < alpha - BIG_DELTA) return alpha;

        //         if (score > alpha) alpha = score;
        //     }
        //     return alpha;
        // }



        return negaMaxHandler(double.NegativeInfinity, double.PositiveInfinity);
    }
}
