using UnityEngine;

public class TetrisSpawner : MonoBehaviour
{
    public bool humanMode = false;

    public int cols = 3;
    public int rows = 5;
    public float spacingX = 14f;
    public float spacingY = 24f;

    [Header("Human Mode Settings")]
    public float humanStepDelay = 1.0f; // piece fall speed for human play

    public GameObject tetrisPrefab;

    void Awake()
    {
        int spawnCols = humanMode ? 1 : cols;
        int spawnRows = humanMode ? 1 : rows;

        for (int row = 0; row < spawnRows; row++)
        {
            for (int col = 0; col < spawnCols; col++)
            {
                Vector3 pos = new Vector3(col * spacingX, -row * spacingY, 0f);
                GameObject env = Instantiate(tetrisPrefab, pos, Quaternion.identity);

                Board board = env.GetComponentInChildren<Board>();
                if (board != null) board.trainingMode = !humanMode; // single source of truth

                if (humanMode)
                {
                    Piece piece = env.GetComponentInChildren<Piece>();
                    if (piece != null) piece.stepDelay = humanStepDelay; // override fall speed
                }
            }
        }
    }
}



/* using UnityEngine;

public class TetrisSpawner : MonoBehaviour
{
    public GameObject tetrisPrefab;
    public int cols      = 3;
    public int rows      = 5;
    public float spacingX = 14f;
    public float spacingY = 24f;

    void Awake()
    {
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                Vector3 pos = new Vector3(col * spacingX, -row * spacingY, 0f);
                Instantiate(tetrisPrefab, pos, Quaternion.identity);
            }
        }
    }
}
*/