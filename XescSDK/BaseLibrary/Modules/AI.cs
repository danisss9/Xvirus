using Microsoft.ML;
using PeNet;
using System;
using System.IO;
using XescSDK.Model;

namespace XescSDK
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
            ModelInput? peinfo = GetFilePeInfo(filePath);
            if (peinfo == null)
                return -1;
            
            return model?.Predict(peinfo).Score[1] ?? -1;
        }

        private static ModelInput? GetFilePeInfo(string path)
        {
            if (!PeFile.IsPEFile(path)) return null;
            var file = new PeFile(path);
            if (!file.IsEXE) return null;

            float[] aux = new float[]
            {
                file.FileSize,
                Convert.ToSingle(file.HasValidComDescriptor),
                Convert.ToSingle(file.HasValidExportDir),
                Convert.ToSingle(file.HasValidImportDir),
                Convert.ToSingle(file.HasValidRelocDir),
                Convert.ToSingle(file.HasValidResourceDir),
                Convert.ToSingle(file.HasValidSecurityDir)
            };


            float[] aux2 = new float[3];
            float[] aux3 = new float[3];
            float[] aux4 = new float[3];

            if (file.ImpHash != null)
            {
                aux2 = new float[]
                {
                    Convert.ToSingle((float)Convert.ToInt64(file.ImpHash.Substring(0, 11), 16)),
                    Convert.ToSingle((float)Convert.ToInt64(file.ImpHash.Substring(11, 11), 16)),
                    Convert.ToSingle((float)Convert.ToInt64(file.ImpHash.Substring(22, 10), 16))
                };

            }

            if (file.ImageNtHeaders != null)
            {

                if (file.ImageNtHeaders.FileHeader != null)
                {
                    aux3 = new float[]
                   {
                         file.ImageNtHeaders.FileHeader.NumberOfSections,
                         file.ImageNtHeaders.FileHeader.SizeOfOptionalHeader,
                         file.ImageNtHeaders.FileHeader.Characteristics
                   };
                }
                if (file.ImageNtHeaders.OptionalHeader != null)
                {
                    aux4 = new float[]
                    {
                        file.ImageNtHeaders.OptionalHeader.AddressOfEntryPoint,
                        file.ImageNtHeaders.OptionalHeader.Magic,
                        file.ImageNtHeaders.OptionalHeader.LoaderFlags
                    };
                }
            }

            float[] result = new float[aux.Length + aux2.Length + aux3.Length + aux4.Length];
            Array.Copy(aux, result, aux.Length);
            Array.Copy(aux2, 0, result, aux.Length, aux2.Length);
            Array.Copy(aux3, 0, result, aux.Length + aux2.Length, aux3.Length);
            Array.Copy(aux4, 0, result, aux.Length + aux2.Length + aux3.Length, aux4.Length);
            return new ModelInput()
            {
                VAR01 = result[0],
                VAR02 = result[1],
                VAR03 = result[2],
                VAR04 = result[3],
                VAR05 = result[4],
                VAR06 = result[5],
                VAR07 = result[6],
                VAR08 = result[7],
                VAR09 = result[8],
                VAR10 = result[9],
                VAR11 = result[10],
                VAR12 = result[11],
                VAR13 = result[12],
                VAR14 = result[13],
                VAR15 = result[14],
                VAR16 = result[15],
                Column18 = @"",
            };
        }
    }
}
