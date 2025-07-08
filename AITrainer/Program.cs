using Microsoft.ML;
using System.Security.Cryptography;
using Xvirus;
using Xvirus.Model;

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

// Create Context
var mlContext = new MLContext();

// Load images from folder
var fullData = LoadImageFromFolder(mlContext, targetFolder);
var splitData = mlContext.Data.TrainTestSplit(fullData, testFraction: 0.2);

// Train Mode
var model = TrainModel(mlContext, splitData.TrainSet);

// Save Model
mlContext.Model.Save(model, splitData.TrainSet.Schema, Path.Combine(outputFolder, "model.ai"));

// Evaluate Model
var metrics = mlContext.MulticlassClassification.Evaluate(splitData.TestSet);
Console.WriteLine("=== MODEL EVALUATION ===");
Console.WriteLine($"Accuracy Macro: {metrics.MacroAccuracy:P2}");
Console.WriteLine($"Accuracy Micro: {metrics.MicroAccuracy:P2}");
Console.WriteLine($"Log Loss: {metrics.LogLoss:F2}");
Console.WriteLine("Confusion Matrix:");
foreach (var row in metrics.ConfusionMatrix.PerClassPrecision)
    Console.WriteLine($"Class Precision: {row:P2}");

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

static IDataView LoadImageFromFolder(MLContext mlContext, string folder)
{
    var res = new List<ModelInput>();
    var allowedImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif" };
    DirectoryInfo rootDirectoryInfo = new DirectoryInfo(folder);
    DirectoryInfo[] subDirectories = rootDirectoryInfo.GetDirectories();

    if (subDirectories.Length == 0)
    {
        throw new Exception("fail to find subdirectories");
    }

    foreach (DirectoryInfo directory in subDirectories)
    {
        var imageList = directory.EnumerateFiles().Where(f => allowedImageExtensions.Contains(f.Extension.ToLower()));
        if (imageList.Count() > 0)
        {
            res.AddRange(imageList.Select(i => new ModelInput
            {
                Label = directory.Name,
                ImageSource = File.ReadAllBytes(i.FullName),
            }));
        }
    }
    return mlContext.Data.LoadFromEnumerable(res);
}

static ITransformer TrainModel(MLContext mlContext, IDataView trainData)
{
    var pipeline = BuildPipeline(mlContext);
    var model = pipeline.Fit(trainData);

    return model;
}

static IEstimator<ITransformer> BuildPipeline(MLContext mlContext)
{
    // Data process configuration with pipeline data transformations
    var pipeline = mlContext.Transforms.Conversion.MapValueToKey(outputColumnName: @"Label", inputColumnName: @"Label", addKeyValueAnnotationsAsText: false)
                            .Append(mlContext.MulticlassClassification.Trainers.ImageClassification(labelColumnName: @"Label", scoreColumnName: @"Score", featureColumnName: @"ImageSource"))
                            .Append(mlContext.Transforms.Conversion.MapKeyToValue(outputColumnName: @"PredictedLabel", inputColumnName: @"PredictedLabel"));

    return pipeline;
}