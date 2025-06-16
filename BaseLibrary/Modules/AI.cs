using Microsoft.ML;
using System;
using System.IO;
using Xvirus.Model;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

namespace Xvirus
{
    public class AI
    {
        private readonly PredictionEngine<ModelInput, ModelOutput>? model;

        public AI(SettingsDTO settings)
        {
            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, "model.ai");

            if(File.Exists(path))
            {
                var mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(path, out var _);
                model = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(mlModel);
            }
        }

        public float ScanFile(string filePath)
        {
            ModelInput? input = GetModelInput(filePath);
            if (input == null)
                return -1;
            
            return model?.Predict(input).Score[1] ?? -1;
        }

        // Uses 256 width, PNG format and grayscale pixels
        private ModelInput GetModelInput(string filePath)
        {
            int width = 256;
            byte[] binaryData = File.ReadAllBytes(filePath);
            int height = (int)Math.Ceiling((double)binaryData.Length / width);

            // Use Rgba32 to match Node.js behavior more closely
            using var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < width; x++)
                    {
                        int dataIndex = y * width + x;
                        byte value = dataIndex < binaryData.Length ? binaryData[dataIndex] : (byte)0;

                        // Create grayscale color same as Node.js: rgb(value, value, value)
                        pixelRow[x] = new Rgba32(value, value, value, 255);
                    }
                }
            });

            // Use PNG encoder settings that might match Node.js better
            var encoder = new PngEncoder()
            {
                ColorType = PngColorType.RgbWithAlpha,
                BitDepth = PngBitDepth.Bit8,
                CompressionLevel = PngCompressionLevel.NoCompression
            };

            using var memoryStream = new MemoryStream();
            image.Save(memoryStream, encoder);

            return new ModelInput()
            {
                ImageSource = memoryStream.ToArray(),
            };
        }
    }
}
