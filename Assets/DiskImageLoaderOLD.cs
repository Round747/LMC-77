using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class DiskImageLoaderOLD : MonoBehaviour
{
    [SerializeField] RenderTexture _rt;
    [SerializeField] Camera _cam;

    // TODO disk images should be created with the FORMAT command. The current disk in the drive should be named and remembered

    // TODO path should be determined by the PATH command

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // convert disk ui to texture2d, convert that to png bytes, and append the data bytes with its length at the end
    public void SaveDiskImage(byte[] data, string name)
    {
        // turn render texture into texture2d
        _cam.Render();
        Texture2D tex;
        tex = new Texture2D(_rt.width, _rt.height);
        var oldRt = RenderTexture.active;
        RenderTexture.active = _rt;
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = oldRt;

        string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        byte[] imageBytes = tex.EncodeToPNG();
        byte[] length = BitConverter.GetBytes(data.Length); // turn length of data to 4 bytes

        byte[] newArray = new byte[imageBytes.Length + data.Length + 4]; // new file is the image + data + 4 bytes for data length
        Array.Copy(imageBytes, 0, newArray, 0, imageBytes.Length); // move image bytes into new array
        Array.Copy(data, 0, newArray, imageBytes.Length, data.Length); // move data bytes in after image bytes
        Array.Copy(length, 0, newArray, imageBytes.Length + data.Length, 4); // move length to end of array

        if (File.Exists(path + $"\\{name}.png")) File.SetAttributes(path + $"\\{name}.png", File.GetAttributes(path + $"\\{name}.png") & ~FileAttributes.ReadOnly); // disable read only for re-writing
        File.WriteAllBytes(path + $"\\{name}.png", newArray);
        File.SetAttributes(path + $"\\{name}.png", FileAttributes.ReadOnly); // editiing image removes bytes after it, attempt to prevent this


        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
    }

    // load file and extract appended data
    public byte[] LoadDiskImage(string name)
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        byte[] file = File.ReadAllBytes(path + $"\\{name}.png");

        int length = BitConverter.ToInt32(file[^4..]); // length is last 4 bytes of data

        return file[^(length + 4)..^4];
    }
}
