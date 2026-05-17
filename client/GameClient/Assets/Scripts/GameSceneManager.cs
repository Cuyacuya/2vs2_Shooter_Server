using UnityEngine;

public class GameSceneManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    private void Start()
    {
        SpawnTestPlayers();
    }

    private void SpawnTestPlayers()
    {
        Vector3[] positions =
        {
            new Vector3(-4, 1, -3),
            new Vector3(-4, 1,  3),
            new Vector3( 4, 1, -3),
            new Vector3( 4, 1,  3)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject player = Instantiate(playerPrefab, positions[i], Quaternion.identity);
            player.name = $"Player_{i + 1}";
        }
    }
}