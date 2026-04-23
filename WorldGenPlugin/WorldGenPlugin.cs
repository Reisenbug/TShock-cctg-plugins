using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace WorldGenPlugin
{
    [ApiVersion(2, 1)]
    public class WorldGenPlugin : TerrariaPlugin
    {
        public override string Name => "WorldGenPlugin";
        public override string Author => "stardust";
        public override string Description => "Generate a new world via subprocess without affecting the running server.";
        public override Version Version => new Version(1, 0, 0);

        private bool _generating;
        private Process _currentProcess;
        private string _currentExpectedPath;

        public static WorldGenPlugin Instance { get; private set; }
        public bool IsGenerating => _generating;

        public WorldGenPlugin(Main game) : base(game)
        {
            Instance = this;
        }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("worldgen.generate", GenWorld, "genworld"));
            Commands.ChatCommands.Add(new Command("worldgen.generate", KillGenWorld, "killgenworld"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == GenWorld);
                Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == KillGenWorld);
                try
                {
                    if (_currentProcess != null && !_currentProcess.HasExited)
                    {
                        _currentProcess.Kill(true);
                        _currentProcess.WaitForExit(3000);
                    }
                    _currentProcess?.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }

        private void KillGenWorld(CommandArgs args)
        {
            if (!_generating)
            {
                args.Player.SendInfoMessage("No world generation in progress.");
                return;
            }

            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(true);
                    _currentProcess.WaitForExit(5000);
                    TShock.Log.ConsoleInfo("[WorldGenPlugin] Generation process killed.");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[WorldGenPlugin] Failed to kill process: {ex.Message}");
            }

            // Delete incomplete world file
            if (!string.IsNullOrEmpty(_currentExpectedPath))
            {
                try
                {
                    if (File.Exists(_currentExpectedPath))
                    {
                        File.Delete(_currentExpectedPath);
                        TShock.Log.ConsoleInfo($"[WorldGenPlugin] Deleted incomplete world: {_currentExpectedPath}");
                    }
                    string twldPath = Path.ChangeExtension(_currentExpectedPath, ".twld");
                    if (File.Exists(twldPath))
                        File.Delete(twldPath);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[WorldGenPlugin] Failed to delete incomplete world: {ex.Message}");
                }
            }

            _generating = false;
            _currentProcess = null;
            _currentExpectedPath = null;
            args.Player.SendSuccessMessage("World generation terminated.");
        }

        // /genworld <filename> [small|medium|large] [seed] [corrupt|crimson|random]
        private void GenWorld(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /genworld <filename> [small|medium|large] [seed] [corrupt|crimson|random]");
                return;
            }

            if (_generating)
            {
                args.Player.SendErrorMessage("A world generation is already in progress.");
                return;
            }

            string filename = args.Parameters[0];
            if (!filename.EndsWith(".wld", StringComparison.OrdinalIgnoreCase))
                filename += ".wld";

            string savePath = Path.Combine(Main.WorldPath, filename);
            if (File.Exists(savePath))
            {
                args.Player.SendErrorMessage($"World file already exists: {savePath}");
                return;
            }

            // Parse size: n/new menu uses 1=small 2=medium 3=large
            string sizeStr = args.Parameters.Count >= 2 ? args.Parameters[1].ToLower() : "small";
            string sizeNum;
            switch (sizeStr)
            {
                case "small": sizeNum = "1"; break;
                case "medium": sizeNum = "2"; break;
                case "large": sizeNum = "3"; break;
                default:
                    args.Player.SendErrorMessage("Invalid size. Use: small, medium, large");
                    return;
            }

            string seed = args.Parameters.Count >= 3 ? args.Parameters[2] : "";

            // Parse evil: TerrariaServer menu uses 1=random 2=corrupt 3=crimson
            string evilStr = args.Parameters.Count >= 4 ? args.Parameters[3].ToLower() : "random";
            string evilNum;
            switch (evilStr)
            {
                case "random": evilNum = "1"; break;
                case "corrupt": evilNum = "2"; break;
                case "crimson": evilNum = "3"; break;
                default:
                    args.Player.SendErrorMessage("Invalid evil type. Use: corrupt, crimson, random");
                    return;
            }

            // Find TShock.Server executable
            string serverExe = FindServerExecutable();
            if (serverExe == null)
            {
                args.Player.SendErrorMessage("Cannot find TShock.Server executable.");
                return;
            }

            string worldName = Path.GetFileNameWithoutExtension(filename);

            _generating = true;
            TShock.Log.ConsoleInfo($"[WorldGenPlugin] Generating {sizeStr} world \"{worldName}\" ({evilStr}) in background...");

            // Run in background thread
            new Thread(() => DoGenerateSubprocess(serverExe, worldName, sizeNum, evilNum, seed, savePath))
            {
                Name = "WorldGenPlugin Worker",
                IsBackground = true
            }.Start();
        }

        private void DoGenerateSubprocess(string serverExe, string worldName, string sizeNum, string evilNum, string seed, string expectedPath)
        {
            Process process = null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));

                // Use -autocreate + -world + -autoshutdown for non-interactive world generation
                string args = $"-autocreate {sizeNum} -world \"{expectedPath}\" -worldname \"{worldName}\" -autoshutdown -rest-enabled false";

                var startInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (serverExe.StartsWith("dotnet:"))
                {
                    startInfo.FileName = "dotnet";
                    startInfo.Arguments = $"{serverExe.Substring(7)} {args}";
                }
                else
                {
                    startInfo.FileName = serverExe;
                    startInfo.Arguments = args;
                }

                TShock.Log.ConsoleInfo($"[WorldGenPlugin] Starting subprocess: {startInfo.FileName} {startInfo.Arguments}");

                process = Process.Start(startInfo);
                if (process == null)
                {
                    TShock.Log.ConsoleError("[WorldGenPlugin] Failed to start subprocess.");
                    return;
                }
                _currentProcess = process;
                _currentExpectedPath = expectedPath;

                process.OutputDataReceived += (s, e) => { };
                process.ErrorDataReceived += (s, e) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait up to 10 minutes for generation to complete
                bool exited = process.WaitForExit(600_000);
                if (!exited)
                {
                    TShock.Log.ConsoleError("[WorldGenPlugin] Subprocess timed out after 10 minutes, killing.");
                    try { process.Kill(true); } catch { }
                    process.WaitForExit(5000);
                }

                if (File.Exists(expectedPath) && new FileInfo(expectedPath).Length > 0)
                {
                    TShock.Log.ConsoleInfo($"[WorldGenPlugin] World generated: {expectedPath}");
                }
                else
                {
                    TShock.Log.ConsoleError($"[WorldGenPlugin] World file not found. Expected: {expectedPath}");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[WorldGenPlugin] Generation failed: {ex.Message}");
                TShock.Log.ConsoleError(ex.StackTrace);
            }
            finally
            {
                try
                {
                    if (process != null && !process.HasExited)
                        process.Kill(true);
                    process?.Dispose();
                }
                catch { }

                _generating = false;
                _currentProcess = null;
                _currentExpectedPath = null;
            }
        }

        private string FindServerExecutable()
        {
            string appDir = AppContext.BaseDirectory;

            string dll = Path.Combine(appDir, "TShock.Server.dll");
            if (File.Exists(dll))
                return "dotnet:" + dll;

            string native = Path.Combine(appDir, "TShock.Server");
            if (File.Exists(native))
                return native;
            native = Path.Combine(appDir, "TShock.Server.exe");
            if (File.Exists(native))
                return native;

            return null;
        }
    }
}
