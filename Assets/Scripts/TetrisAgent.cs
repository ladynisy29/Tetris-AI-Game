using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class TetrisAgent : Agent
{
    public Board board;

    //public override void OnEpisodeBegin()
    //{
    //    board.tilemap.ClearAllTiles();
    //    board.SpawnPiece();
    //}
    public override void OnEpisodeBegin()
    {
        board.ResetBoard();
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        RectInt bounds = board.Bounds;

        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                sensor.AddObservation(board.tilemap.HasTile(pos) ? 1f : 0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        Piece piece = board.activePiece;

        switch (action)
        {
            case 0:
                piece.Move(Vector2Int.left);
                break;
            case 1:
                piece.Move(Vector2Int.right);
                break;
            case 2:
                piece.Rotate(1);
                break;
            case 3:
                piece.HardDrop();
                break;
        }

        AddReward(-0.001f);

        if (board.linesClearedThisStep > 0)
        {
            AddReward(1f * board.linesClearedThisStep);
            board.linesClearedThisStep = 0;   // 🔥 IMPORTANT
        }

        if (board.IsGameOverFlag)
        {
            AddReward(-1f);
            EndEpisode();
        }
    }
}
