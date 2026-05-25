using UnityEngine;
using System.Collections.Generic;

public class WaterManager : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Material waterMaterial;         // Assign your solid blue material here

    [Header("Chunk Settings")]
    public int chunkSize = 40;             // World units per chunk
    public int viewRadius = 2;             // Chunks rendered in each direction (2 = 5x5 grid = 25 chunks)
    public int resolution = 24;           // Subdivisions per chunk — higher = smoother waves, less grid aliasing

    private Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Vector2Int lastPlayerChunk = new Vector2Int(int.MaxValue, 0);

    void Start()
    {
        if (player == null && Camera.main != null)
            player = Camera.main.transform;

        // Built-in RP: enable depth texture so the Ocean shader can sample
        // _CameraDepthTexture for the depth-based shallow/deep colour blend.
        if (Camera.main != null)
            Camera.main.depthTextureMode = DepthTextureMode.Depth;

        UpdateChunks(true);
    }

    void Update()
    {
        if (player == null) return;

        Vector2Int currentChunk = WorldToChunk(player.position);
        if (currentChunk != lastPlayerChunk)
            UpdateChunks(false);
    }


    void UpdateChunks(bool forceRebuild)
    {
        Vector2Int playerChunk = WorldToChunk(player.position);
        lastPlayerChunk = playerChunk;

        // Mark all existing chunks for potential removal
        HashSet<Vector2Int> neededChunks = new();

        for (int x = -viewRadius; x <= viewRadius; x++)
        for (int z = -viewRadius; z <= viewRadius; z++)
        {
            Vector2Int coord = new Vector2Int(playerChunk.x + x, playerChunk.y + z);
            neededChunks.Add(coord);

            if (!activeChunks.ContainsKey(coord))
                SpawnChunk(coord);
        }

        // Remove chunks that are too far
        List<Vector2Int> toRemove = new();
        foreach (var kv in activeChunks)
            if (!neededChunks.Contains(kv.Key))
                toRemove.Add(kv.Key);

        foreach (var key in toRemove)
        {
            Destroy(activeChunks[key]);
            activeChunks.Remove(key);
        }
    }

    void SpawnChunk(Vector2Int coord)
    {
        Vector3 worldPos = new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);
        GameObject chunk = new GameObject($"WaterChunk_{coord.x}_{coord.y}");
        chunk.transform.parent = transform;
        chunk.transform.position = worldPos;

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();

        Mesh mesh = CreateFlatPlaneMesh(chunkSize);
        mf.mesh = mesh;
        mr.material = waterMaterial;

        // Add our animator script to apply wave displacement to this chunk's plane
        WaterChunk wc = chunk.AddComponent<WaterChunk>();
        wc.Initialize(mesh);

        activeChunks[coord] = chunk;
    }

    Mesh CreateFlatPlaneMesh(int size)
    {
        Mesh mesh = new Mesh();
        int res = resolution;
        int verts = (res + 1) * (res + 1);

        Vector3[] vertices = new Vector3[verts];
        Vector2[] uvs = new Vector2[verts];
        int[] tris = new int[res * res * 6];

        float step = (float)size / res;
        int i = 0;
        for (int z = 0; z <= res; z++)
        for (int x = 0; x <= res; x++)
        {
            vertices[i] = new Vector3(x * step - size / 2f, 0f, z * step - size / 2f);
            uvs[i] = new Vector2((float)x / res, (float)z / res);
            i++;
        }

        int t = 0;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            int bl = z * (res + 1) + x;
            tris[t++] = bl; tris[t++] = bl + res + 1; tris[t++] = bl + 1;
            tris[t++] = bl + 1; tris[t++] = bl + res + 1; tris[t++] = bl + res + 2;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.MarkDynamic(); // Optimizes for frequent vertex updates
        return mesh;
    }

    Vector2Int WorldToChunk(Vector3 pos) =>
        new Vector2Int(Mathf.RoundToInt(pos.x / chunkSize), Mathf.RoundToInt(pos.z / chunkSize));
}

// Animates wave mesh vertices using the WaveManager sine waves.
public class WaterChunk : MonoBehaviour
{
    private Mesh        mesh;
    private Vector3[]   baseVertices;
    private Vector3[]   displacedVertices;
    private WaveManager waveManager;

    public void Initialize(Mesh m)
    {
        mesh              = m;
        baseVertices      = mesh.vertices;
        displacedVertices = new Vector3[baseVertices.Length];
        waveManager       = WaveManager.Instance;
    }

    void Update()
    {
        if (waveManager == null) return;

        float   t         = Time.time;
        Vector3 chunkPos  = transform.position;
        var     waves     = waveManager.waves;
        int     waveCount = waves.Length;
        int     vertCount = baseVertices.Length;
        float   steepness = waveManager.steepness;

        for (int i = 0; i < vertCount; i++)
        {
            Vector3 b      = baseVertices[i];
            float   worldX = chunkPos.x + b.x;
            float   worldZ = chunkPos.z + b.z;
            float   dispX  = 0f;
            float   dispY  = 0f;
            float   dispZ  = 0f;

            for (int w = 0; w < waveCount; w++)
            {
                float ph = waves[w].k * (waves[w].dir.x * worldX
                         + waves[w].dir.y * worldZ)
                         - waves[w].speed * t + waves[w].phase;
                
                float cos = Mathf.Cos(ph);
                float sin = Mathf.Sin(ph);

                dispX += steepness * waves[w].amplitude * waves[w].dir.x * cos;
                dispY += waves[w].amplitude * sin;
                dispZ += steepness * waves[w].amplitude * waves[w].dir.y * cos;
            }

            displacedVertices[i].x = b.x + dispX;
            displacedVertices[i].y = b.y + dispY;
            displacedVertices[i].z = b.z + dispZ;
        }

        mesh.vertices = displacedVertices;
        // Normals are handled by the normal map in the Ocean shader.
    }
}