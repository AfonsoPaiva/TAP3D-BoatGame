using UnityEngine;

[RequireComponent(typeof(Camera))]
public class VolumetricFog : MonoBehaviour
{
    public Material fogMaterial;
    Camera cam;

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        Graphics.Blit(src, dst, fogMaterial);
    }

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }
}