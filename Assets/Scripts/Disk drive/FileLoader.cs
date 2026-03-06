using System.IO;
using UnityEngine;

public class FileLoader : MonoBehaviour
{
    [SerializeField] DiskDrive _diskDrive;

    // ---------------- //

    // loads and saves binary files instead of disk images.
    // in future this will probably be used to let the user create a virtual disk from a binary file, 
    // which can then be saved as a disk image

    public byte[] LoadFileFromComputer(string fileName)
    {
        return File.ReadAllBytes(_diskDrive.FilePath + $"\\{fileName}.lmcprg");
    }

    public void SaveFileToComputer(byte[] bytes, string fileName)
    {
        File.WriteAllBytes(_diskDrive.FilePath + $"\\{fileName}.lmcprg", bytes);
    }
}
