using System;
using System.IO;
using UnityEngine;

public class ScreenshotMaker : MonoBehaviour
{
    [SerializeField] DiskDrive _diskDrive;

    [SerializeField] RenderTexture _rt;
    [SerializeField] Camera _cam;

    int screenshotNumber = 0; // TODO remember this number across playthroughs

    // TODO screenshots should be saved to PATH

    public void OnEnable()
    {
        LMCInputManager.Screenshot += SaveScreenShotToDesktop;
    }

    public void OnDisable()
    {
        LMCInputManager.Screenshot -= SaveScreenShotToDesktop;
    }

    // turns the render texture into a texture2d and saves it to the players desktop as a png
    public void SaveScreenShotToDesktop()
    {
        _cam.Render();
        Texture2D tex;

        tex = new Texture2D(_rt.width, _rt.height);
        var oldRt = RenderTexture.active;
        RenderTexture.active = _rt;
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);

        tex.Apply();
        RenderTexture.active = oldRt;

        string path = _diskDrive.FilePath;

        while (File.Exists(path + $"\\LMC-77 {screenshotNumber}.png")) screenshotNumber++; // jump over existing screenshots

        File.WriteAllBytes(path + $"\\LMC-77 {screenshotNumber}.png", tex.EncodeToPNG());
        screenshotNumber++;

        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
    }


}
