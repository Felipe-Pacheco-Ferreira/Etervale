// Etervale fork: runs Slang for the WebGPU profile (monogame/issues/022-webgpu-effects.md).

using System.Runtime.InteropServices;

namespace MonoGame.Effect
{
    /// <summary>
    /// The Slang compiler that writes the WGSL of the WebGPU profile. <c>monogame/pipeline/fetch-slang.sh</c> (or
    /// <c>.ps1</c>) puts the release of <see cref="Version"/> for the host into <c>monogame/pipeline/tools/slang</c>, and
    /// the compiler project copies it next to its assembly, so MGCB and the tests find it in the same place.
    /// </summary>
    internal static class Slang
    {
        /// <summary>The Slang release the fetch scripts pin.</summary>
        internal const string Version = "2026.22";

        private static readonly Lazy<string> Executable = new(Locate);

        private static string Locate()
        {
            var name = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";
            var folder = Path.GetDirectoryName(typeof(Slang).Assembly.Location) ?? AppContext.BaseDirectory;
            var path = Path.Combine(folder, "slang", "bin", name);
            if (File.Exists(path))
                return path;

            throw new FileNotFoundException(
                $"Slang {Version} ({RuntimeInformation.OSArchitecture}) was not found at {path}: run monogame/pipeline/fetch-slang.sh " +
                "(or fetch-slang.ps1 on Windows), then build the effect compiler again.");
        }

        /// <summary>Runs slangc with <paramref name="arguments"/>; returns its exit code.</summary>
        public static int Run(string arguments, out string stdout, out string stderr) =>
            Dxc.RunTool(Executable.Value, arguments, out stdout, out stderr);
    }
}
