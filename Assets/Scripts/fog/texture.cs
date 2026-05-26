using UnityEngine;

public class NoiseTexture3D : MonoBehaviour
{
    public int resolution = 32;
    public float scale = 1f;
    public Material fogMaterial;

    void Start()
    {
        Texture3D texture = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;

        Color[] colors = new Color[resolution * resolution * resolution];

        for (int z = 0; z < resolution; z++)
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float xf = (float)x / resolution * scale;
            float yf = (float)y / resolution * scale;
            float zf = (float)z / resolution * scale;

            float noise = Mathf.PerlinNoise(xf, yf) * 0.5f +
                          Mathf.PerlinNoise(yf, zf) * 0.5f;

            noise = Mathf.Clamp01(noise);
            colors[x + y * resolution + z * resolution * resolution] = new Color(noise, noise, noise, noise);
        }

        texture.SetPixels(colors);
        texture.Apply();

        fogMaterial.SetTexture("_FogNoise", texture);
    }
}