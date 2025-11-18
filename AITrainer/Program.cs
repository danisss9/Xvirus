using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xvirus;

var malFolder = GetArgument("--malware");
var safeFolder = GetArgument("--safe");
var targetFolder = GetArgument("--target");
var outputFolder = GetArgument("--output");
var deleteImages = GetArgument("--delete");

if (malFolder == null)
    throw new ArgumentException("Not provided malware folder argument '--malware %path%', this is where the malware files for training should be");

if (safeFolder == null)
    throw new ArgumentException("Not provided safe folder argument '--safe %path%', this is where the benign files for training should be");

if (targetFolder == null)
    throw new ArgumentException("Not provided target folder argument '--target %path%', this is where the training images files will be saved");

if (outputFolder == null)
    throw new ArgumentException("Not provided output folder argument '--output %path%', this is where the AI model file will be saved");

// Mal Files
ProcessFolder(malFolder, Path.Join(targetFolder, "mal"));

// Safe Files
ProcessFolder(safeFolder, Path.Join(targetFolder, "safe"));

// Call python script to train ML model (pass the arguments)
string scriptArgs = $"--target {QuoteIfNeeded(targetFolder)} --output {QuoteIfNeeded(outputFolder)} --epochs 10 --patience 5 --batch-size 32";
RunPythonScriptFromOutput("train_export_tf.py", scriptArgs);

// Delete target folder
if (!string.IsNullOrEmpty(deleteImages) && deleteImages.Equals("true", StringComparison.OrdinalIgnoreCase))
    Directory.Delete(targetFolder, true);

static string? GetArgument(string key)
{
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == key)
            return args[i + 1];
    }
    return null;
}

static void ProcessFolder(string srcRoot, string dstRoot)
{
    // Make sure the target directory exists:
    Directory.CreateDirectory(dstRoot);

    if (!Directory.Exists(srcRoot))
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

static int RunPythonScriptFromOutput(string scriptFileName, string scriptArgs, int timeoutMs = 0)
{
    // scriptFileName is relative to the app output folder, e.g., "train_export_tf.py"
    string appFolder = AppContext.BaseDirectory!;
    string scriptPath = Path.Combine(appFolder, scriptFileName);
    if (!File.Exists(scriptPath))
    {
        Console.Error.WriteLine($"Script not found: {scriptPath}");
        return -1;
    }

    // Prefer "py" on Windows, otherwise "python" or "python3"
    string pythonExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "py" : "python3";
    // If you want to force "python" change the string above.

    string args = $"{QuoteIfNeeded(scriptPath)} {scriptArgs}";

    var psi = new ProcessStartInfo
    {
        FileName = pythonExe,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = false,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    using var process = new Process { StartInfo = psi };
    process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    if (timeoutMs > 0)
    {
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { }
            Console.Error.WriteLine("Python process timed out and was killed.");
            return -2;
        }
    }
    else
    {
        process.WaitForExit();
    }

    return process.ExitCode;
}

static string QuoteIfNeeded(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
