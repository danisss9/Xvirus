using Microsoft.ML;
using System;
using System.IO;
using Xvirus.Model;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Linq;
using SixLabors.ImageSharp.Processing;

namespace Xvirus
{
    public class AI
    {
        private readonly PredictionEngine<ModelInput, ModelOutput>? model;

        public AI(SettingsDTO settings)
        {
            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, "model.ai");

            if(settings.EnableAIScan && File.Exists(path))
            {
                var mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(path, out var _);
                model = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(mlModel);
            }
        }

        public float ScanFile(string filePath)
        {
            if (model == null)
                return -1;


            ModelInput input = new ModelInput()
            {
                ImageSource = GetFileImageBytes(filePath),
            };

            var prediction = model.Predict(input);
            return prediction.PredictedLabel == "mal" ? prediction.Score.Max() : prediction.Score.Min();
        }

        public static byte[] GetFileImageBytes(string filePath)
        {
            byte[] binaryData = File.ReadAllBytes(filePath);

            var sizeInKB = binaryData.Length / 1024;
            int width;
            if (sizeInKB <= 10)
            {
                width = 32;
            }
            else if (sizeInKB <= 30)
            {
                width = 64;
            }
            else if (sizeInKB <= 60)
            {
                width = 128;
            }   
            else if (sizeInKB <= 100)
            {
                width = 256;
            }
            else if (sizeInKB <= 200)
            {
                width = 384;
            }
            else if (sizeInKB <= 1000)
            {
                width = 512;
            }
            else if (sizeInKB <= 1500)
            {
                width = 1024;
            }
            else
            {
                width = 2048;
            }
            int height = (int)Math.Ceiling((double)binaryData.Length / width);
         
           
            using var image = new Image<L8>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<L8> pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < width; x++)
                    {
                        int dataIndex = y * width + x;
                        byte value = dataIndex < binaryData.Length ? binaryData[dataIndex] : (byte)0;

                        // Create grayscale color
                        pixelRow[x] = new L8(value);
                    }
                }
            });

            // Resize to 224x224 using nearest neighbor interpolation
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(224, 224),
                Mode = ResizeMode.Pad,
                Sampler = KnownResamplers.NearestNeighbor,
                PadColor = Color.Black
            }));

            // Use PNG encoder
            var encoder = new PngEncoder()
            {
                ColorType = PngColorType.Grayscale,
                BitDepth = PngBitDepth.Bit8,
                CompressionLevel = PngCompressionLevel.NoCompression
            };

            using var memoryStream = new MemoryStream();
            image.Save(memoryStream, encoder);

            return memoryStream.ToArray();
        }
    }
}
