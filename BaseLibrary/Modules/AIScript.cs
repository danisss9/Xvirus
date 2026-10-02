using LLama;
using LLama.Common;
using LLama.Sampling;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xvirus.Model;

namespace Xvirus
{
    /// <summary>
    /// Script focused AI scanner. Classifies .bat, .cmd, .ps1, .py, .js and .vbs files with a
    /// Qwen Coder GGUF model running on CPU via LLamaSharp. The model output is grammar
    /// constrained to exactly one of "malicious", "suspicious" or "benign".
    /// </summary>
    public class AIScript
    {
        private const string ModelFileName = "scriptmodel.gguf";
        private const uint ContextSize = 4096;
        private const int MaxScriptChars = 12000;
        private const int MaxTokens = 8;
        private const string ClassificationGrammar = "root ::= \"malicious\" | \"suspicious\" | \"benign\"";

        private static readonly string[] ScriptExtensions = { ".bat", ".cmd", ".ps1", ".py", ".js", ".vbs" };

        private readonly object inferenceLock = new();
        private LLamaWeights? model;
        private StatelessExecutor? executor;

        public AIScript(SettingsDTO settings)
        {
            Load(settings);
        }

        public void Load(SettingsDTO settings)
        {
            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, ModelFileName);

            if (!settings.EnableAIScan || !File.Exists(path))
                return;

            try
            {
                var parameters = new ModelParams(path)
                {
                    ContextSize = ContextSize,
                    GpuLayerCount = 0 // CPU only inference
                };

                model = LLamaWeights.LoadFromFile(parameters);
                executor = new StatelessExecutor(model, parameters);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                Unload();
            }
        }

        public void Unload()
        {
            lock (inferenceLock)
            {
                executor = null;
                model?.Dispose();
                model = null;
            }
        }

        public static bool IsScriptFile(string filePath)
        {
            return Array.IndexOf(ScriptExtensions, Path.GetExtension(filePath).ToLowerInvariant()) != -1;
        }

        /// <summary>
        /// Classifies a script file with the LLM. Returns 1.0 for malicious, 0.5 for suspicious,
        /// 0.0 for benign and -1.0 when the model is not loaded or inference failed.
        /// </summary>
        public float ScanFile(string filePath)
        {
            if (executor == null)
                return -1;

            try
            {
                var prompt = BuildPrompt(filePath);

                lock (inferenceLock)
                {
                    var verdict = Classify(prompt).GetAwaiter().GetResult();
                    return verdict switch
                    {
                        "malicious" => 1f,
                        "suspicious" => 0.5f,
                        "benign" => 0f,
                        _ => -1f
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return -1;
            }
        }

        private async Task<string> Classify(string prompt)
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = MaxTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0f,
                    Grammar = new Grammar(ClassificationGrammar, "root")
                }
            };

            var output = new StringBuilder();
            await foreach (var token in executor!.InferAsync(prompt, inferenceParams))
                output.Append(token);

            return output.ToString().Trim().ToLowerInvariant();
        }

        private static string BuildPrompt(string filePath)
        {
            var content = File.ReadAllText(filePath);
            if (content.Length > MaxScriptChars)
                content = content.Substring(0, MaxScriptChars);

            // Script content is attacker controlled: strip the chat template special tokens so a
            // malicious script cannot inject a fake assistant answer into the prompt.
            content = content
                .Replace("<|im_start|>", "", StringComparison.OrdinalIgnoreCase)
                .Replace("<|im_end|>", "", StringComparison.OrdinalIgnoreCase)
                .Replace("<|endoftext|>", "", StringComparison.OrdinalIgnoreCase);

            var user = "Classify this script. Reply with only one word: benign, suspicious or malicious.\n" +
                       "benign = normal harmless script (typical admin or user automation).\n" +
                       "suspicious = unusual or potentially harmful but could be legitimate.\n" +
                       "malicious = clearly harmful: destroys or steals data, stealthily downloads and executes payloads, evades analysis, installs persistence.\n\n" +
                       "Script:\n" + content + "\nOne-word classification:";

            // Qwen2.5 chat template: raw completion prompts make the model repeat the label
            // list instead of answering, so the prompt is wrapped in the template it was
            // trained with.
            return "<|im_start|>system\nYou are a script security classifier. You answer with exactly one word.<|im_end|>\n" +
                   "<|im_start|>user\n" + user + "<|im_end|>\n" +
                   "<|im_start|>assistant\n";
        }
    }
}
