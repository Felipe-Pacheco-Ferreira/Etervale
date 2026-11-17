// Etervale fork: the WebGPU profile of the browser runtime (monogame/issues/022-webgpu-effects.md).

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame.Effect
{
    /// <summary>
    /// Compiles an effect for the browser runtime's WebGPU backend. Each shader goes through the Vulkan profile's DXC build,
    /// which gives the effect its reflection (constant buffers, textures and samplers, vertex inputs, storage resources)
    /// and the binding header the runtime reads; after that header come the vertex inputs' locations and the WGSL, compiled
    /// by Slang from the same preprocessed HLSL instead of DXC's SPIR-V.
    /// </summary>
    sealed class WebGpuShaderProfile : VulkanShaderProfile
    {
        // The WGSL bindings of a stage's registers, all in bind group 0 (the runtime's MGWGPU_BINDING_* constants): b0 at
        // the stage's base, t registers at +32, s registers at +48 (WGSL gives a texture and a sampler distinct bindings,
        // where Vulkan combines a t and an s register of the same number), u registers at +64. The pixel stage starts at
        // 128, so both stages of a pipeline share one bind group without overlapping.
        internal const int TextureBinding = 32;
        internal const int SamplerBinding = 48;
        internal const int StorageBinding = 64;
        internal const int PixelStageBase = 128;

        static readonly Regex VertexInputStruct = new(@"struct vertexInput_\d+\s*\{(?<fields>[^}]*)\}", RegexOptions.Compiled);
        static readonly Regex LocatedField = new(@"@location\((?<location>\d+)\)\s+(?<name>\w+)_\d+\s*:\s*(?<type>[^,]+),", RegexOptions.Compiled);

        public WebGpuShaderProfile()
            : base("WebGPU", 81)
        {
        }

        internal override void AddMacros(Dictionary<string, string> macros)
        {
            base.AddMacros(macros);
            macros.Add("WEBGPU", "1");
        }

        // After the binding header: the number of vertex inputs, each as (usage, semantic index, location), then the WGSL.
        private protected override void WriteShaderCode(BinaryWriter writer, ShaderData shaderData, string hlslFile, MgfxShaderStage stage, string shaderModel,
            ref string errorsAndWarnings)
        {
            var wgslFile = Path.ChangeExtension(hlslFile, ".wgsl");
            var reflectionFile = Path.ChangeExtension(hlslFile, ".slang.json");
            try
            {
                var result = Slang.Run(SlangArguments(stage, hlslFile, wgslFile, reflectionFile), out var stdout, out var stderr);
                errorsAndWarnings += stderr;
                if (result != 0)
                {
                    errorsAndWarnings += $"slangc returned error code '{result}'.\n";
                    errorsAndWarnings += stdout;
                    throw new ShaderCompilerException();
                }

                var wgsl = File.ReadAllText(wgslFile);
                var inputs = stage == MgfxShaderStage.Vertex ? VertexInputs(wgsl, File.ReadAllText(reflectionFile), ref errorsAndWarnings) : [];
                writer.Write(inputs.Count);
                foreach (var (usage, index, location) in inputs)
                {
                    writer.Write((uint)usage);
                    writer.Write((uint)index);
                    writer.Write((uint)location);
                }

                writer.Write(Encoding.UTF8.GetBytes(wgsl));
            }
            finally
            {
                File.Delete(wgslFile);
                File.Delete(reflectionFile);
            }
        }

        // DXC's Vulkan build flips Y (-fvk-invert-y); WebGPU has Direct3D's clip space and framebuffer origin, so the WGSL
        // keeps HLSL's. -fvk-use-dx-layout gives the constant buffers the offsets the reflection (and the framework) use.
        // Warning 39001 is a constant buffer of the other stage sharing a register: the stages have separate bases here.
        internal static string SlangArguments(MgfxShaderStage stage, string hlslFile, string wgslFile, string reflectionFile)
        {
            var stageName = stage switch
            {
                MgfxShaderStage.Vertex => "vertex",
                MgfxShaderStage.Pixel => "fragment",
                _ => "compute",
            };
            var stageBase = stage == MgfxShaderStage.Pixel ? PixelStageBase : 0;
            return $"\"{hlslFile}\" -lang hlsl -target wgsl -entry main -stage {stageName} -fvk-use-dx-layout " +
                $"-fvk-b-shift {stageBase} all -fvk-t-shift {stageBase + TextureBinding} all -fvk-s-shift {stageBase + SamplerBinding} all " +
                $"-fvk-u-shift {stageBase + StorageBinding} all -warnings-disable 39001 -o \"{wgslFile}\" -reflection-json \"{reflectionFile}\"";
        }

        // The semantic of every location of the WGSL vertex input. Slang's locations do not always follow its reflection's
        // order, so each WGSL field (Slang names it <HLSL field>_<n>) is matched to the reflection's field of that name.
        internal static List<(VertexElementUsage Usage, int Index, int Location)> VertexInputs(string wgsl, string reflectionJson, ref string errorsAndWarnings)
        {
            var inputs = new List<(VertexElementUsage, int, int)>();
            var block = VertexInputStruct.Match(wgsl);
            if (!block.Success)
                return inputs;

            var semantics = ReflectedSemantics(reflectionJson);
            foreach (Match field in LocatedField.Matches(block.Groups["fields"].Value))
            {
                var name = field.Groups["name"].Value;
                if (field.Groups["type"].Value.TrimStart().StartsWith("mat", StringComparison.Ordinal))
                {
                    errorsAndWarnings += $"Vertex input '{name}': WebGPU has no matrix vertex inputs; pass its rows as float4 inputs.\n";
                    throw new ShaderCompilerException();
                }

                if (!semantics.TryGetValue(name, out var semantic))
                {
                    errorsAndWarnings += $"Vertex input '{name}': Slang's reflection gives it no semantic.\n";
                    throw new ShaderCompilerException();
                }

                var location = int.Parse(field.Groups["location"].Value, CultureInfo.InvariantCulture);
                inputs.Add((UsageOf(semantic.Name, ref errorsAndWarnings), semantic.Index, location));
            }

            return inputs;
        }

        // Field name -> (semantic name, semantic index) of the entry point's varying inputs, struct fields or parameters.
        static Dictionary<string, (string Name, int Index)> ReflectedSemantics(string reflectionJson)
        {
            var semantics = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(reflectionJson);
            foreach (var entryPoint in document.RootElement.GetProperty("entryPoints").EnumerateArray())
            {
                if (!entryPoint.TryGetProperty("parameters", out var parameters))
                    continue;

                foreach (var parameter in parameters.EnumerateArray())
                {
                    if (parameter.TryGetProperty("type", out var type) && type.TryGetProperty("fields", out var fields))
                    {
                        foreach (var field in fields.EnumerateArray())
                            AddSemantic(field, semantics);
                    }
                    else
                    {
                        AddSemantic(parameter, semantics);
                    }
                }
            }

            return semantics;
        }

        static void AddSemantic(JsonElement variable, Dictionary<string, (string, int)> semantics)
        {
            if (!variable.TryGetProperty("semanticName", out var semanticName) || !variable.TryGetProperty("name", out var name))
                return;

            var index = variable.TryGetProperty("semanticIndex", out var semanticIndex) ? semanticIndex.GetInt32() : 0;
            semantics[name.GetString() ?? ""] = (semanticName.GetString() ?? "", index);
        }
    }
}
