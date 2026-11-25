using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using Xvirus.Model;

namespace Xvirus
{
    /// <summary>
    /// Script focused AI scanner. Classifies .bat, .cmd, .ps1, .py, .js and .vbs files with a
    /// Qwen Coder GGUF model running on CPU via LLamaSharp. The model output is grammar
    /// constrained to exactly one of "malicious", "suspicious" or "benign". Requires both
    /// <see cref="SettingsDTO.EnableAIScan"/> and <see cref="SettingsDTO.EnableAIScriptScan"/>.
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

        // LLamaSharp resolves its llama.cpp backend (libllama.so) through relative paths such as
        // "runtimes/linux-x64/native/<variant>/libllama.so" or "runtimes/linux-arm64/native/libllama.so",
        // which it only searches in the process base directory and the assembly location. Under NativeAOT the assembly location is empty
        // and in an embedded node module the base directory is the node executable's, so without an
        // explicit search directory the backend is never found. Register the SDK base folder once,
        // before the first native call, so the backend is found wherever the engine is deployed.
        private static readonly object nativeSearchLock = new();
        private static bool nativeSearchConfigured;

        private static void ConfigureNativeSearch()
        {
            lock (nativeSearchLock)
            {
                if (nativeSearchConfigured)
                    return;

                try
                {
                    // Throws InvalidOperationException if the backend already loaded without this
                    // configuration (can only happen on reloads after a successful first load,
                    // where the already-registered search directories keep applying).
                    NativeLibraryConfig.All.WithSearchDirectories([Utils.CurrentDir]);
                }
                catch (InvalidOperationException) { }

                try
                {
                    var avxLevel = DetectAvxLevelForAot();
                    if (avxLevel != null)
                        NativeLibraryConfig.All.WithAvx(avxLevel.Value);
                }
                catch (InvalidOperationException) { }

                nativeSearchConfigured = true;
            }
        }

        // LLamaSharp picks the llama.cpp CPU backend (runtimes/<rid>/native/{noavx,avx,avx2,avx512})
        // from Avx.IsSupported / Avx2.IsSupported / Avx512F.IsSupported. Under NativeAOT those are
        // compile-time constants for the ISAs the binary was compiled for (x64 baseline by default),
        // so they are always false and LLamaSharp silently loads the "noavx" backend, which is ~30x
        // slower. Detect the level at runtime from CPUID plus the OS-enabled register state instead,
        // mirroring LLamaSharp's own rules. Returns null to keep LLamaSharp's detection (JIT, where
        // IsSupported reflects the actual CPU, or on platforms where the OS state cannot be read).
        private static AvxLevel? DetectAvxLevelForAot()
        {
            if (RuntimeFeature.IsDynamicCodeCompiled || !X86Base.IsSupported)
                return null;

            ulong osState; // XCR0-style mask of register state the OS saves/restores
            if (OperatingSystem.IsWindows())
                osState = GetEnabledXStateFeatures();
            else if (OperatingSystem.IsLinux())
                osState = ReadLinuxCpuFlagsAsXState();
            else
                return null;

            var (maxLeaf, _, _, _) = X86Base.CpuId(0, 0);
            var (_, _, ecx1, _) = X86Base.CpuId(1, 0);
            var (_, ebx7, ecx7, _) = maxLeaf >= 7 ? X86Base.CpuId(7, 0) : (0, 0, 0, 0);

            const ulong YmmState = 0b110;       // SSE + AVX (XCR0 bits 1, 2)
            const ulong ZmmState = 0b1110_0110; // + opmask, ZMM_Hi256, Hi16_ZMM (XCR0 bits 5, 6, 7)

            bool osXsave = (ecx1 & (1 << 27)) != 0;
            bool avx = osXsave && (ecx1 & (1 << 28)) != 0 && (osState & YmmState) == YmmState;
            bool avx2 = avx && (ebx7 & (1 << 5)) != 0;
            bool avx512 = avx2 && (osState & ZmmState) == ZmmState
                && (ebx7 & (1 << 16)) != 0  // AVX512F
                && (ebx7 & (1 << 30)) != 0  // AVX512BW
                && (ecx7 & (1 << 1)) != 0   // AVX512_VBMI
                && (ecx7 & (1 << 11)) != 0; // AVX512_VNNI

            return avx512 ? AvxLevel.Avx512 : avx2 ? AvxLevel.Avx2 : avx ? AvxLevel.Avx : AvxLevel.None;
        }

        // Linux only reports avx/avx512f in /proc/cpuinfo when the kernel enabled the matching
        // register state, so translate those flags into the equivalent XCR0 bits.
        private static ulong ReadLinuxCpuFlagsAsXState()
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (!line.StartsWith("flags", StringComparison.Ordinal))
                        continue;

                    var flags = line.Substring(line.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    ulong state = 0;
                    if (Array.IndexOf(flags, "avx") != -1)
                        state |= 0b110;
                    if (Array.IndexOf(flags, "avx512f") != -1)
                        state |= 0b1110_0000;
                    return state;
                }
            }
            catch (Exception) { }
            return 0;
        }

        [DllImport("kernel32.dll")]
        private static extern ulong GetEnabledXStateFeatures();

        public AIScript(SettingsDTO settings)
        {
            Load(settings);
        }

        public void Load(SettingsDTO settings)
        {
            // Release the previous model before loading a new one on reloads (or when disabled).
            Unload();

            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, ModelFileName);

            if (!settings.EnableAIScriptScan || !File.Exists(path))
                return;

            try
            {
                ConfigureNativeSearch();

                // llama.cpp prints model load and runtime details directly to stderr.
                // Route those messages to a no-op callback so nothing reaches the console;
                // load failures still throw managed exceptions which are logged below.
                NativeLogConfig.llama_log_set((_, _) => { });

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
                Logger.LogMessage($"Script AI scan disabled: '{ModelFileName}' was found at '{path}' but the LLamaSharp backend failed to load.");
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
                    // Run the async inference on the thread pool and block on it there. LLamaSharp
                    // awaits internally without ConfigureAwait(false), so blocking directly on a
                    // thread that has a SynchronizationContext (the Node.js main thread under
                    // node-api-dotnet, a UI thread) deadlocks: the continuation after the prompt
                    // decode is posted back to the blocked thread and never runs.
                    var verdict = Task.Run(() => Classify(prompt)).GetAwaiter().GetResult();
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
            await foreach (var token in executor!.InferAsync(prompt, inferenceParams).ConfigureAwait(false))
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
