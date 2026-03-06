using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;

public class DiskImageSaver : MonoBehaviour
{
    readonly string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // TODO path should be set by the PATH command, but desktop by default

    [SerializeField] RenderTexture _rt; // texture disk image is saved to
    [SerializeField] Camera _cam; // camera looking at floppy disk image

    // disk image data is encoded with steganography, where the bytes are hidden in the colour values of each pixel
    // one byte is stored in one pixel, where two bits are placed in the least significant bits of the rgb and a colour channels

    //   r       g       b       a
    // [0][1]  [2][3]  [4][5]  [6][7]

    // TODO handle address, either desktop or set by path

    // takes a snapshot of the floppy disk image which has been edited by the user and creates a texture2d
    // the save data is written into the colour data of the image and then it is saved to the correct device and address
    public void SaveDiskImageToComputer(byte[] diskData)
    {
        // this is also code I don't understand, but it converts the render texture into a texture2d
        Texture2D tex;

        _cam.Render();
        tex = new Texture2D(_rt.width, _rt.height);
        var oldRt = RenderTexture.active;
        RenderTexture.active = _rt;
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = oldRt;

        int dataIndex = 0;

        Texture2D newTexture = tex;

        // loop through all the pixels until the end of the data
        for (; dataIndex < diskData.Length; dataIndex++)
        {
            int y = dataIndex / _rt.width;
            int x = dataIndex % _rt.width;

            Color32 pixel = newTexture.GetPixel(x, y);
            byte currentByte = diskData[dataIndex];

            pixel.r &= 0b11111100; // clear bits
            byte byteR = (byte)(currentByte & 0b00000011); // grab bits from data
            pixel.r |= byteR; // add in bits

            pixel.g &= 0b11111100;
            byte byteG = (byte)((currentByte & 0b00001100) >> 2); // shift down to insert into lowest bits
            pixel.g |= byteG;

            pixel.b &= 0b11111100;
            byte byteB = (byte)((currentByte & 0b00110000) >> 4);
            pixel.b |= byteB;

            pixel.a &= 0b11111100;
            byte byteA = (byte)((currentByte & 0b11000000) >> 6);
            pixel.a |= byteA;

            newTexture.SetPixel(x, y, pixel); // reinsert modified pixel
        }

        byte[] imageBytes = tex.EncodeToPNG();

        File.WriteAllBytes(path + $"\\test.png", imageBytes);

        // remove texture after finished
        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
    }

    public byte[] LoadDiskImageFromComputer()
    {
        byte[] imageData = File.ReadAllBytes(path + $"\\test.png");

        // grab disk image from computer and convert to texture2d
        Texture2D texture = new Texture2D(_rt.width, _rt.height);
        texture.LoadImage(imageData);

        List<byte> result = new List<byte>();

        for (int i = 0; i < testArray.Length; i++)
        {
            int y = i / _rt.width;
            int x = i % _rt.width;

            Color32 pixel = texture.GetPixel(x, y);

            byte currentByte;

            byte r = (byte)(pixel.r & 0b00000011);
            byte g = (byte)((pixel.g & 0b00000011) << 2);
            byte b = (byte)((pixel.b & 0b00000011) << 4);
            byte a = (byte)((pixel.a & 0b00000011) << 6);

            currentByte = (byte)(r | g | b | a); // merge all bytes

            result.Add(currentByte);
        }

        return result.ToArray();
    }

    readonly byte[] testArray = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 128, 64, 123, 88, 29, 94, 77, 255, 255, 255, 255, 255, 255, 255, 255 };
    // byte[] testArray;

    public IEnumerator Start()
    {
        // testArray = new byte[1000];
        SaveDiskImageToComputer(testArray);

        yield return new WaitForSeconds(2);

        byte[] fromDisk = LoadDiskImageFromComputer();

        foreach (byte b in fromDisk) print(b);
    }
}
