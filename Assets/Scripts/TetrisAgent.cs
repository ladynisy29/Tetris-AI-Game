using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Analytics;

public class TetrisAgent : Agent
{
    public Board board;

    // -------------------------------------------------------
    // Demo / Visualization
    // -------------------------------------------------------
    [Header("Placement Execution")]
    [Tooltip("If true, execute the chosen placement over time (slide + fall) so it's visible.")]
    public bool animatePlacement = false;

    [Tooltip("Delay (in fixed steps) between each downward fall step while animating.")]
    [SerializeField] private int fallStepInterval = 1;

    [Tooltip("Delay (in fixed steps) between each horizontal move while animating.")]
    [SerializeField] private int slideStepInterval = 0;

    // -------------------------------------------------------
    // Reward weights (tetris17)
    // -------------------------------------------------------

    // Line clears
    private const float W_LINE_1 = 9f * 1.6f;
    private const float W_LINE_2 = 27f * 1.6f;
    private const float W_LINE_3 = 54f * 1.6f;
    private const float W_LINE_4 = 108f * 1.6f;

    // Holes
    // NOTE: We now use FULL delta (cur - prev). With negative weight:
    //  - holes increase => negative
    //  - holes decrease => positive
    private const float W_HOLES_DELTA = -1.5f;
    private const float W_HOLES_TOTAL = -0.15f;

    // Board quality (deltas)
    private const float W_BUMPINESS = -1.0f;
    private const float W_AGG_HEIGHT = -0.3f;

    // Anchor (absolute) penalties (small)
    private const float W_AGG_HEIGHT_TOTAL = -0.05f;
    private const float W_BUMPINESS_TOTAL = -0.05f;

    // Early flattening penalty (only early game)
    private const float W_MAX_HEIGHT_DIFF = -0.15f;
    // Blockades (blocks above buried holes)
    private const float W_BLOCKADES_DELTA = -2.0f;
    private const float W_BLOCKADES_TOTAL = -0.15f;

    // Encourage placing low
    private const float W_LOW_PLACE = 0.02f;

    // Optional: reward clearing when stack is low
    private const float W_LOW_CLEAR = 0.15f;

    // Bottom-row progress shaping
    private const int ROW_PROGRESS_ROWS = 6;      // look at bottom 6 rows
    private const float W_ROW_PROGRESS = 0.25f;   // small-ish
    private float prevRowFillScore = 0f;

    // Terminal penalty
    private const float TERMINAL_BASE = -0.5f;
    private const float TERMINAL_EARLY_EXTRA = -1.5f;

    // -------------------------------------------------------
    // Episode stats
    // -------------------------------------------------------
    private int piecesPlaced = 0;
    private int episodeLines = 0;
    private int episodeCount = 0;

    private float prevHoles = 0f;
    private float prevBumpiness = 0f;
    private float prevAggHeight = 0f;
    private float prevBlockades = 0f;

    // -------------------------------------------------------
    // One-shot execution state
    // -------------------------------------------------------
    private bool executingPlan = false;
    private int plannedRotation = 0;          // 0..3
    private int plannedTargetX = 0;           // absolute tilemap x
    private int fixedStepsSinceMove = 0;

    public bool debugLogs = false;

    // -------------------------------------------------------
    public override void OnEpisodeBegin()
    {
        if (board == null) return;

        board.trainingMode = true;
        board.ResetBoard();

        // Reset episode counters/state FIRST
        piecesPlaced = 0;
        episodeLines = 0;

        executingPlan = false;
        fixedStepsSinceMove = 0;

        // Initialize prev metrics from the fresh board state (prevents first-step delta spikes)
        RectInt bounds = board.Bounds;

        int holesInit, blockInit;
        GetCavityHolesAndBlockades(bounds, out holesInit, out blockInit);
        prevHoles = holesInit;
        prevBlockades = blockInit;

        int[] heights = GetColumnHeights(bounds);
        prevAggHeight = GetAggregateHeight(heights);
        prevBumpiness = GetBumpiness(heights);

        prevRowFillScore = GetBottomRowFillScore(bounds, ROW_PROGRESS_ROWS);
    }

    // -------------------------------------------------------
    // Observations
    // -------------------------------------------------------
    public override void CollectObservations(VectorSensor sensor)
    {
        if (board == null) return;

        RectInt bounds = board.Bounds;
        int width = bounds.width;
        int height = bounds.height;

        // 1) Raw grid
        for (int y = bounds.yMin; y < bounds.yMax; y++)
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                sensor.AddObservation(board.tilemap.HasTile(new Vector3Int(x, y, 0)) ? 1f : 0f);

        // 2) Aggregate features
        int[] colH = GetColumnHeights(bounds);
        sensor.AddObservation((float)GetAggregateHeight(colH) / (width * height));
        sensor.AddObservation((float)GetHoles(bounds, colH) / (width * height));
        sensor.AddObservation((float)GetBumpiness(colH) / (width * height));

        // 3) Per-column heights
        for (int i = 0; i < colH.Length; i++)
            sensor.AddObservation((float)colH[i] / height);

        // 4) Active piece context + next piece
        if (board.activePiece != null)
        {
            sensor.AddObservation((float)(board.activePiece.position.y - bounds.yMin) / height);
            sensor.AddObservation((float)(board.activePiece.position.x - bounds.xMin) / width);

            sensor.AddObservation(board.activePiece.rotationIndex / 3f);
            sensor.AddObservation((int)board.activePiece.data.tetromino / 6f);

            sensor.AddObservation((int)board.NextData.tetromino / 6f);

            var cells = board.activePiece.cells;
            int cellCount = Mathf.Min(cells.Length, 4);

            for (int i = 0; i < 4; i++)
            {
                if (i < cellCount)
                {
                    sensor.AddObservation(cells[i].x / 2f);
                    sensor.AddObservation(cells[i].y / 2f);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }
            }
        }
        else
        {
            for (int i = 0; i < 13; i++)
                sensor.AddObservation(0f);
        }
    }

    // -------------------------------------------------------
    // ACTION SPACE (One-shot placement):
    // DiscreteActions[0] rotation in {0..3}
    // DiscreteActions[1] target column in {0..9}
    // -------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (board == null || board.activePiece == null) return;

        if (board.IsGameOverFlag)
        {
            AddScaledTerminalPenalty();
            EndEpisodeWithStats();
            return;
        }

        if (executingPlan) return;

        RectInt bounds = board.Bounds;

        plannedRotation = Mathf.Clamp(actions.DiscreteActions[0], 0, 3);
        int targetCol = Mathf.Clamp(actions.DiscreteActions[1], 0, bounds.width - 1);
        plannedTargetX = bounds.xMin + targetCol;

        executingPlan = true;
        fixedStepsSinceMove = 0;

        if (!animatePlacement)
        {
            ExecutePlacementInstant();
        }
        // else: visual execution in FixedUpdate
    }

    // -------------------------------------------------------
    // Visualization execution loop
    // -------------------------------------------------------
    private void FixedUpdate()
    {
        if (board == null || board.activePiece == null || board.IsGameOverFlag) return;

        if (!executingPlan)
        {
            RequestDecision();
            return;
        }

        if (!animatePlacement) return;

        fixedStepsSinceMove++;

        Piece current = board.activePiece;
        board.Clear(current);

        // 1) Rotate toward plannedRotation (one step per interval)
        if (current.rotationIndex != plannedRotation)
        {
            if (fixedStepsSinceMove >= Mathf.Max(1, slideStepInterval + 1))
            {
                current.Rotate(1);
                fixedStepsSinceMove = 0;
            }
            board.Set(current);
            return;
        }

        // 2) Slide toward plannedTargetX
        int dx = plannedTargetX - current.position.x;
        if (dx != 0)
        {
            if (fixedStepsSinceMove >= Mathf.Max(1, slideStepInterval + 1))
            {
                TryMove(current, dx > 0 ? Vector2Int.right : Vector2Int.left);
                fixedStepsSinceMove = 0;
            }
            board.Set(current);
            return;
        }

        // 3) Fall down
        if (fixedStepsSinceMove >= Mathf.Max(1, fallStepInterval))
        {
            bool canDown = board.IsValidPosition(current, current.position + Vector3Int.down);
            if (canDown)
            {
                TryMove(current, Vector2Int.down);
                fixedStepsSinceMove = 0;
                board.Set(current);
                return;
            }

            // Can't go down -> lock
            board.Set(current);
            LockCurrentPiece(current);
            return;
        }

        board.Set(current);
    }

    // -------------------------------------------------------
    // Instant placement (training speed)
    // -------------------------------------------------------
    private void ExecutePlacementInstant()
    {
        if (board == null || board.activePiece == null) return;

        Piece current = board.activePiece;
        board.Clear(current);

        // Rotate to desired rotation (try up to 4 times)
        for (int i = 0; i < 4; i++)
        {
            if (current.rotationIndex == plannedRotation) break;
            current.Rotate(1);
        }

        // Slide horizontally toward target x
        int safety = 50;
        while (current.position.x != plannedTargetX && safety-- > 0)
        {
            int dx = plannedTargetX - current.position.x;
            bool moved = TryMove(current, dx > 0 ? Vector2Int.right : Vector2Int.left);

            // If blocked, stop trying to reach that column (prevents wasting 50 iterations).
            if (!moved) break;
        }

        // Fall down until cannot
        safety = 200;
        while (board.IsValidPosition(current, current.position + Vector3Int.down) && safety-- > 0)
        {
            if (!TryMove(current, Vector2Int.down)) break;
        }

        board.Set(current);
        LockCurrentPiece(current);
    }

    // -------------------------------------------------------
    // Locking + rewards
    // -------------------------------------------------------
    private void LockCurrentPiece(Piece current)
    {
        // Ensure piece is on board
        board.Set(current);

        // Clear and accumulate line clears in board.linesClearedThisStep
        board.ClearLines();

        HandlePieceLockedRewards();

        // Spawn next
        board.SpawnPiece();

        executingPlan = false;
        fixedStepsSinceMove = 0;

        if (board.IsGameOverFlag)
        {
            AddScaledTerminalPenalty();
            EndEpisodeWithStats();
            return;
        }

        if (board.activePiece != null)
            board.Set(board.activePiece);
    }

    private void HandlePieceLockedRewards()
    {
        RectInt bounds = board.Bounds;
        float norm = bounds.width * bounds.height;

        // --- Line clears ---
        int clearedNow = 0;
        if (board.linesClearedThisStep > 0)
        {
            clearedNow = board.linesClearedThisStep;
            episodeLines += clearedNow;

            float r = clearedNow == 1 ? W_LINE_1
                    : clearedNow == 2 ? W_LINE_2
                    : clearedNow == 3 ? W_LINE_3
                    : W_LINE_4;

            AddReward(r);
            board.linesClearedThisStep = 0;
        }

        piecesPlaced++;

        // --- Metrics ---
        int[] heights = GetColumnHeights(bounds);
        float curAggH = GetAggregateHeight(heights);
        float curBump = GetBumpiness(heights);
        float maxH = GetMaxHeight(heights);
        int heightRange = GetHeightRange(heights);

        // NEW: true cavity holes + blockades
        int holesInt, blockadesInt;
        GetCavityHolesAndBlockades(bounds, out holesInt, out blockadesInt);
        float curHoles = holesInt;
        float curBlockades = blockadesInt;

        // --- Bottom-row progress shaping ---
        // Do NOT punish decreases (line clears + gravity can decrease fill score).
        float rowFillScore = GetBottomRowFillScore(bounds, ROW_PROGRESS_ROWS);
        float dRow = rowFillScore - prevRowFillScore;
        AddReward(W_ROW_PROGRESS * Mathf.Max(0f, dRow));
        prevRowFillScore = rowFillScore;

        // --- Early flattening penalty (first ~8 pieces only) ---
        float earlyFactor = Mathf.Clamp01(1f - (piecesPlaced / 8f));
        AddReward(earlyFactor * W_MAX_HEIGHT_DIFF * (heightRange / (float)bounds.height));

        // --- Low-clear bonus ---
        if (clearedNow > 0)
        {
            float lowFactor = 1.0f - (maxH / bounds.height);
            AddReward(W_LOW_CLEAR * clearedNow * lowFactor);
        }

        // --- Holes (FULL delta) ---
        float dHoles = curHoles - prevHoles;
        AddReward(W_HOLES_DELTA * (dHoles / norm));
        AddReward(W_HOLES_TOTAL * (curHoles / norm));

        // --- Blockades (NEW) ---
        // To use this, add the two constants W_BLOCKADES_DELTA / W_BLOCKADES_TOTAL near your other weights.
        // We'll store prev blockades in prevBumpiness? No — add a new prevBlockades field (see note below).
        // For now, we compute delta vs. a stored field.
        float dBlock = curBlockades - prevBlockades;
        AddReward(W_BLOCKADES_DELTA * (dBlock / norm));
        AddReward(W_BLOCKADES_TOTAL * (curBlockades / norm));

        // --- Absolute (TOTAL) anchors ---
        AddReward(W_AGG_HEIGHT_TOTAL * (curAggH / norm));
        AddReward(W_BUMPINESS_TOTAL * (curBump / norm));

        // --- Delta shaping ---
        AddReward(W_AGG_HEIGHT * ((curAggH - prevAggHeight) / norm));
        AddReward(W_BUMPINESS * ((curBump - prevBumpiness) / norm));

        // --- Place low ---
        AddReward(W_LOW_PLACE * (1.0f - maxH / bounds.height));

        // --- TensorBoard stats ---
        var stats = Academy.Instance.StatsRecorder;
        stats.Add("tetris/lines_cleared", clearedNow, StatAggregationMethod.Average);
        stats.Add("tetris/max_height", maxH, StatAggregationMethod.Average);
        stats.Add("tetris/holes", curHoles, StatAggregationMethod.Average);
        stats.Add("tetris/blockades", curBlockades, StatAggregationMethod.Average);

        // --- Update prevs ---
        prevHoles = curHoles;
        prevAggHeight = curAggH;
        prevBumpiness = curBump;
        prevBlockades = curBlockades;
    }

    // -------------------------------------------------------
    // Terminal penalty
    // -------------------------------------------------------
    private void AddScaledTerminalPenalty()
    {
        float earlyDeathFactor = Mathf.Clamp01(1f - piecesPlaced / 50f);
        float pen = TERMINAL_BASE + TERMINAL_EARLY_EXTRA * earlyDeathFactor;
        AddReward(pen);
    }

    // -------------------------------------------------------
    // Episode stats
    // -------------------------------------------------------
    private void RecordEpisodeStats()
    {
        var stats = Academy.Instance.StatsRecorder;

        stats.Add("tetris/episode_lines", episodeLines, StatAggregationMethod.Average);
        stats.Add("tetris/episode_pieces", piecesPlaced, StatAggregationMethod.Average);

        float linesPerPiece = (piecesPlaced > 0) ? (episodeLines / (float)piecesPlaced) : 0f;
        stats.Add("tetris/lines_per_piece", linesPerPiece, StatAggregationMethod.Average);

        episodeCount++;
        stats.Add("tetris/episode_count", episodeCount, StatAggregationMethod.MostRecent);
    }

    private void EndEpisodeWithStats()
    {
        if (debugLogs) Debug.Log($"EP END: piecesPlaced={piecesPlaced} lines={episodeLines}");
        RecordEpisodeStats();
        EndEpisode();
    }

    // -------------------------------------------------------
    // Heuristic (demo)
    // -------------------------------------------------------
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;

        int rot = 0;
        if (Input.GetKey(KeyCode.Alpha1)) rot = 0;
        if (Input.GetKey(KeyCode.Alpha2)) rot = 1;
        if (Input.GetKey(KeyCode.Alpha3)) rot = 2;
        if (Input.GetKey(KeyCode.Alpha4)) rot = 3;

        int col = 4;
        if (Input.GetKey(KeyCode.Alpha0)) col = 0;
        if (Input.GetKey(KeyCode.Alpha9)) col = 9;

        d[0] = rot;
        d[1] = col;
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------
    private bool TryMove(Piece piece, Vector2Int dir)
    {
        Vector3Int before = piece.position;
        bool ok = piece.Move(dir);
        return ok && piece.position != before;
    }

    private int[] GetColumnHeights(RectInt bounds)
    {
        int[] heights = new int[bounds.width];
        for (int col = 0; col < bounds.width; col++)
        {
            int x = bounds.xMin + col;
            for (int y = bounds.yMax - 1; y >= bounds.yMin; y--)
            {
                if (board.tilemap.HasTile(new Vector3Int(x, y, 0)))
                {
                    heights[col] = y - bounds.yMin + 1;
                    break;
                }
            }
        }
        return heights;
    }

    private int GetAggregateHeight(int[] h)
    {
        int t = 0; foreach (int v in h) t += v; return t;
    }

    private int GetMaxHeight(int[] h)
    {
        int m = 0; foreach (int v in h) if (v > m) m = v; return m;
    }

    private int GetHeightRange(int[] heights)
    {
        int min = heights[0];
        int max = heights[0];
        foreach (int h in heights)
        {
            if (h < min) min = h;
            if (h > max) max = h;
        }
        return max - min;
    }

    private int GetBumpiness(int[] heights)
    {
        int b = 0;
        for (int i = 0; i < heights.Length - 1; i++)
            b += Mathf.Abs(heights[i] - heights[i + 1]);
        return b;
    }

    private int GetHoles(RectInt bounds, int[] heights)
    {
        // Backwards-compatible signature:
        // We'll compute cavity holes (true holes) and ignore heights.
        // If you want blockades too, call GetCavityHolesAndBlockades(...) instead.
        int holes, blockades;
        GetCavityHolesAndBlockades(bounds, out holes, out blockades);
        return holes;
    }

    private float GetBottomRowFillScore(RectInt bounds, int rows)
    {
        int width = bounds.width;
        int yStart = bounds.yMin;
        int yEnd = Mathf.Min(bounds.yMin + rows, bounds.yMax);

        float score = 0f;

        for (int y = yStart; y < yEnd; y++)
        {
            int filled = 0;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                if (board.tilemap.HasTile(new Vector3Int(x, y, 0))) filled++;

            score += (float)filled / width;
        }

        return score / rows;
    }

    private void GetCavityHolesAndBlockades(RectInt bounds, out int holes, out int blockades)
    {
        int w = bounds.width;
        int h = bounds.height;

        // solid[x,y] = is there a tile at this cell
        bool[,] solid = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            int yy = bounds.yMin + y;
            for (int x = 0; x < w; x++)
            {
                int xx = bounds.xMin + x;
                solid[x, y] = board.tilemap.HasTile(new Vector3Int(xx, yy, 0));
            }
        }

        // reachable[x,y] = empty cell reachable from the top via 4-neighbor connectivity
        bool[,] reachable = new bool[w, h];
        var q = new System.Collections.Generic.Queue<Vector2Int>();

        // Start flood-fill from all EMPTY cells in the top row
        int topY = h - 1;
        for (int x = 0; x < w; x++)
        {
            if (!solid[x, topY])
            {
                reachable[x, topY] = true;
                q.Enqueue(new Vector2Int(x, topY));
            }
        }

        // 4-neighbor BFS
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            int x = p.x;
            int y = p.y;

            // neighbors
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);
        }

        void TryEnqueue(int nx, int ny)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            if (reachable[nx, ny]) return;
            if (solid[nx, ny]) return; // only traverse empty cells
            reachable[nx, ny] = true;
            q.Enqueue(new Vector2Int(nx, ny));
        }

        // Holes are EMPTY cells NOT reachable from top
        holes = 0;
        bool[,] isHole = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!solid[x, y] && !reachable[x, y])
                {
                    holes++;
                    isHole[x, y] = true;
                }
            }
        }

        // Blockades: filled cells above any hole in the same column
        // Standard definition: scan bottom->top; once we've seen a hole, any filled above counts as blockade.
        blockades = 0;
        for (int x = 0; x < w; x++)
        {
            bool seenHole = false;
            for (int y = 0; y < h; y++) // bottom (0) -> top (h-1)
            {
                if (isHole[x, y]) seenHole = true;
                else if (seenHole && solid[x, y]) blockades++;
            }
        }
    }
}