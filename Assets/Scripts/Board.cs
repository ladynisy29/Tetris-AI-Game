using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(-1)]
public class Board : MonoBehaviour
{
    public Tilemap tilemap { get; private set; }
    public Piece activePiece { get; private set; }

    public TetrominoData[] tetrominoes;
    public Vector2Int boardSize = new Vector2Int(10, 20);
    public Vector3Int spawnPosition = new Vector3Int(-1, 8, 0);

    // --- Added: Next piece preview (1-piece lookahead) ---
    public TetrominoData NextData { get; private set; }

    public bool IsGameOverFlag { get; private set; }

    public int linesClearedThisStep = 0;
    public int curriculumPreFillRows = 0;

    public bool trainingMode = true;

    public RectInt Bounds
    {
        get
        {
            Vector2Int position = new Vector2Int(-boardSize.x / 2, -boardSize.y / 2);
            return new RectInt(position, boardSize);
        }
    }

    private void Awake()
    {
        tilemap = GetComponentInChildren<Tilemap>();
        activePiece = GetComponentInChildren<Piece>();

        for (int i = 0; i < tetrominoes.Length; i++)
        {
            tetrominoes[i].Initialize();
        }
    }

    private void Start()
    {
        // Initialize the first "next" piece so the first spawn uses it
        NextData = GetRandomTetromino();
        SpawnPiece();
    }

    // --- Added: random tetromino sampling helper ---
    private TetrominoData GetRandomTetromino()
    {
        int random = Random.Range(0, tetrominoes.Length);
        return tetrominoes[random];
    }

    public void SpawnPiece()
    {
        // Use the previewed next piece
        TetrominoData data = NextData;

        // Roll the next preview for the following spawn
        NextData = GetRandomTetromino();

        activePiece.Initialize(this, spawnPosition, data);

        if (IsValidPosition(activePiece, spawnPosition))
        {
            Set(activePiece);
        }
        else
        {
            GameOver();
        }
    }

    public void GameOver()
    {
        tilemap.ClearAllTiles();
        IsGameOverFlag = true;
    }

    public void Set(Piece piece)
    {
        for (int i = 0; i < piece.cells.Length; i++)
        {
            Vector3Int tilePosition = piece.cells[i] + piece.position;
            tilemap.SetTile(tilePosition, piece.data.tile);
        }
    }

    public void Clear(Piece piece)
    {
        for (int i = 0; i < piece.cells.Length; i++)
        {
            Vector3Int tilePosition = piece.cells[i] + piece.position;
            tilemap.SetTile(tilePosition, null);
        }
    }

    public bool IsValidPosition(Piece piece, Vector3Int position)
    {
        RectInt bounds = Bounds;

        for (int i = 0; i < piece.cells.Length; i++)
        {
            Vector3Int tilePosition = piece.cells[i] + position;

            if (!bounds.Contains((Vector2Int)tilePosition))
            {
                return false;
            }

            if (tilemap.HasTile(tilePosition))
            {
                return false;
            }
        }

        return true;
    }

    public void ClearLines()
    {
        RectInt bounds = Bounds;
        int row = bounds.yMin;

        while (row < bounds.yMax)
        {
            if (IsLineFull(row))
            {
                LineClear(row);
            }
            else
            {
                row++;
            }
        }
    }

    public bool IsLineFull(int row)
    {
        RectInt bounds = Bounds;

        for (int col = bounds.xMin; col < bounds.xMax; col++)
        {
            Vector3Int position = new Vector3Int(col, row, 0);
            if (!tilemap.HasTile(position))
            {
                return false;
            }
        }

        return true;
    }

    public void LineClear(int row)
    {
        linesClearedThisStep++;

        RectInt bounds = Bounds;

        for (int col = bounds.xMin; col < bounds.xMax; col++)
        {
            Vector3Int position = new Vector3Int(col, row, 0);
            tilemap.SetTile(position, null);
        }

        while (row < bounds.yMax)
        {
            for (int col = bounds.xMin; col < bounds.xMax; col++)
            {
                Vector3Int position = new Vector3Int(col, row + 1, 0);
                TileBase above = tilemap.GetTile(position);

                position = new Vector3Int(col, row, 0);
                tilemap.SetTile(position, above);
            }

            row++;
        }
    }

    public void ResetBoard()
    {
        tilemap.ClearAllTiles();
        linesClearedThisStep = 0;
        IsGameOverFlag = false;

        // Curriculum: pre-fill bottom rows with one gap each to force line clears
        if (trainingMode && curriculumPreFillRows > 0)
        {
            System.Random rng = new System.Random();
            for (int row = 0; row < curriculumPreFillRows; row++)
            {
                int gapCol = rng.Next(0, Bounds.width);
                for (int x = Bounds.xMin; x < Bounds.xMax; x++)
                {
                    if ((x - Bounds.xMin) != gapCol)
                    {
                        Vector3Int pos = new Vector3Int(x, Bounds.yMin + row, 0);
                        tilemap.SetTile(pos, tetrominoes[0].tile);
                    }
                }
            }
        }

        // Reset next piece at the start of each episode
        NextData = GetRandomTetromino();

        SpawnPiece();
    }
}