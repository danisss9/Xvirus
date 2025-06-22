using System.IO;
using System.Security.Cryptography;
using Xvirus;

// Mal Files
ProcessFolder("D:\\Projects\\Atool\\Toolbox\\AI Manager\\Samples\\malware", "D:\\Projects\\Atool\\Toolbox\\Samples\\mal");
//ProcessFolder("C:\\\\Users\\Dani\\Desktop\\malware", "D:\\Projects\\Atool\\Toolbox\\Samples\\mal");

// Safe Files
ProcessFolder("D:\\Projects\\Atool\\Toolbox\\AI Manager\\Samples\\safe", "D:\\Projects\\Atool\\Toolbox\\Samples\\safe");
//ProcessFolder("C:\\\\Users\\Dani\\Desktop\\safe", "D:\\Projects\\Atool\\Toolbox\\Samples\\safe");

static void ProcessFolder(string srcRoot, string dstRoot)
{
    // Make sure the target directory exists:
    Directory.CreateDirectory(dstRoot);

    if(!Directory.Exists(srcRoot))
    {
        Console.WriteLine("Folder does not exist! " + srcRoot);
        return;
    }

    // Enumerate every file under srcRoot:
    int count = 0;
    foreach (var filePath in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
    {
        // Check if exe
        bool isExecutable = false;
        using (var stream = File.OpenRead(filePath))
        {
            using var reader = new BinaryReader(stream);
            try
            {
                var bytes = reader.ReadChars(2);
                isExecutable = bytes[0] == 'M' && bytes[1] == 'Z';
            }
            catch (ArgumentException) { }
        }

        if (!isExecutable) continue;

        // Get image file
        byte[] imageFile = AI.GetFileImageBytes(filePath);

        // Get md5 hash
        var fileInfo = new FileInfo(filePath);
        string? hash = null;
        using (var md5 = MD5.Create())
        {
            using var stream = Utils.ReadFile(filePath, fileInfo.Length);
            var checksum = md5.ComputeHash(stream);
            hash = BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }

        // Get target file path
        string fileName = hash + ".png";
        string targetPath = Path.Combine(dstRoot, fileName);

        // Write File
        File.WriteAllBytes(targetPath, imageFile);

        count++;
        Console.WriteLine($"Processed {filePath}.");
    }

    Console.WriteLine($"Processed {count} files in {srcRoot}");
}