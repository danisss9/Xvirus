using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using Xvirus.Model;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Linq;

namespace Xvirus
{
    public class AI
    {
        private readonly InferenceSession? model;

        public AI(SettingsDTO settings)
        {
            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, "model.ai");

            if (settings.EnableAIScan && File.Exists(path))
            {
                model = new InferenceSession(path);
            }
        }

        public float ScanFile(string filePath)
        {
            if (model == null)
                return -1;

            var inputData = GetInputData(filePath);
            var inputTensor = new DenseTensor<float>(inputData, [1, 3, 224, 224]);
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };

            using var results = model.Run(inputs);
            if (results[0]?.Value is IEnumerable<float> predictions)
            {
                var scores = predictions.ToArray();
                if (scores.Length == 2)
                {
                    var benignScore = scores[0];
                    var malwareScore = scores[1];
                    var maxScore = Math.Max(benignScore, malwareScore);

                    var expBenign = (float)Math.Exp(benignScore - maxScore);
                    var expMalware = (float)Math.Exp(malwareScore - maxScore);
                    var sumExp = expBenign + expMalware;

                    var malwareProbability = expMalware / sumExp;
                    return Math.Clamp(malwareProbability, 0f, 1f);
                }
            }

            return -1;
        }

        public static float[] GetInputData(string filePath)
        {
            var binaryData = File.ReadAllBytes(filePath);

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
            var height = (int)Math.Ceiling((double)binaryData.Length / width);

            using var image = new Image<L8>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);

                    for (int x = 0; x < width; x++)
                    {
                        var dataIndex = y * width + x;
                        var value = dataIndex < binaryData.Length ? binaryData[dataIndex] : (byte)0;

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

            // Prepare input tensor (1, 3, 224, 224) for RGB model
            var inputData = new float[1 * 3 * 224 * 224];

            // ImageNet normalization constants from train_export_tf.py
            var mean = new float[] { 0.485f, 0.456f, 0.406f };
            var std = new float[] { 0.229f, 0.224f, 0.225f };

            // Extract pixels directly from resized image and normalize with ImageNet stats
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < 224; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < 224; x++)
                    {
                        // Get grayscale pixel value [0-255]
                        var pixelValue = pixelRow[x].PackedValue / 255.0f;

                        // Convert grayscale to RGB by replicating across 3 channels
                        var pixelIndex = y * 224 + x;
                        for (int c = 0; c < 3; c++)
                        {
                            // Apply ImageNet normalization
                            inputData[c * 224 * 224 + pixelIndex] =
                                (pixelValue - mean[c]) / std[c];
                        }
                    }
                }
            });

            return inputData;
        }
    }
}
