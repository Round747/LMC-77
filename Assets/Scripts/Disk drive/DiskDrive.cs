using System;
using System.IO;
using UnityEngine;

public class DiskDrive : MonoBehaviour
{
    public FileLoader FileLoader;

    // -------------- //

    // TODO save in game folder? persistant datapath?
    public string DefaultPath;
    // this path can be changed by the PATH command, but at its highest is the desktop
    public string FilePath; // file path disk images will be saved to and loaded from. Screenshots will also be saved here

    public readonly int BytesPerSecond = 500; // transfer speed of the disk drive

    public void Awake()
    {
        Initialise();
    }

    void Initialise()
    {
        if (Application.platform == RuntimePlatform.WindowsEditor) DefaultPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        else DefaultPath = Path.GetDirectoryName(Application.dataPath);
        FilePath = DefaultPath; // default path is desktop, any PATH is relative to this folder
    }
}
