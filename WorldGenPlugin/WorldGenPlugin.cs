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
            string inputFile = null;
            Process process = null;
            bool worldCreated = false;

            try
            {
                // Create temp input file with TerrariaServer interactive commands
                inputFile = Path.GetTempFileName();
                using (var writer = new StreamWriter(inputFile))
                {
                    writer.WriteLine("n");          // Create new world
                    writer.WriteLine(sizeNum);      // World size
                    writer.WriteLine("1");          // Difficulty (normal)
                    writer.WriteLine(evilNum);      // Evil type
                    writer.WriteLine(worldName);    // World name
                    writer.WriteLine(seed);         // Seed (blank = random)
                    writer.WriteLine();             // Extra enter for 1.4.5+
                    writer.WriteLine("exit");       // Exit after creation
                }

                TShock.Log.ConsoleInfo($"[WorldGenPlugin] Starting subprocess: {serverExe}, save dir: {Main.WorldPath}, expected: {expectedPath}");

                var startInfo = new ProcessStartInfo
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                string worldDir = Main.WorldPath;
                if (serverExe.StartsWith("dotnet:"))
                {
                    startInfo.FileName = "dotnet";
                    startInfo.Arguments = $"{serverExe.Substring(7)} -worldselectpath \"{worldDir}\"";
                }
                else
                {
                    startInfo.FileName = serverExe;
                    startInfo.Arguments = $"-worldselectpath \"{worldDir}\"";
                }

                process = Process.Start(startInfo);
                if (process == null)
                {
                    TShock.Log.ConsoleError("[WorldGenPlugin] Failed to start subprocess.");
                    return;
                }
                _currentProcess = process;
                _currentExpectedPath = expectedPath;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        TShock.Log.ConsoleInfo($"[WorldGen-Sub] {e.Data}");
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        TShock.Log.ConsoleError($"[WorldGen-Sub-Err] {e.Data}");
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Feed input commands
                string inputText = File.ReadAllText(inputFile);
                process.StandardInput.Write(inputText);
                process.StandardInput.Flush();
                process.StandardInput.Close();

                string wldFilename = Path.GetFileName(expectedPath);
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (string.IsNullOrEmpty(home))
                    home = Environment.GetEnvironmentVariable("HOME") ?? "/root";
                string[] searchDirs = new[]
                {
                    Path.GetDirectoryName(expectedPath),
                    Path.Combine(home, ".local/share/Terraria/Worlds"),
                    "/root/.local/share/Terraria/Worlds",
                    Path.Combine(Main.WorldPath, "Worlds"),
                    Path.Combine(AppContext.BaseDirectory, "Worlds"),
                };

                int maxWait = 600;
                int waited = 0;
                string foundPath = null;

                while (waited < maxWait)
                {
                    if (process.HasExited)
                        break;

                    Thread.Sleep(2000);
                    waited += 2;
                }

                Thread.Sleep(2000);

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                }
                catch { }

                foreach (string dir in searchDirs)
                {
                    if (dir == null) continue;
                    string candidate = Path.Combine(dir, wldFilename);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
                    {
                        foundPath = candidate;
                        worldCreated = true;
                        break;
                    }
                }

                if (worldCreated)
                {
                    if (foundPath != expectedPath)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));
                        File.Move(foundPath, expectedPath);
                        TShock.Log.ConsoleInfo($"[WorldGenPlugin] World found at {foundPath}, moved to {expectedPath}");
                    }
                    else
                    {
                        TShock.Log.ConsoleInfo($"[WorldGenPlugin] World generated: {expectedPath}");
                    }
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
                // Cleanup
                if (inputFile != null)
                {
                    try { File.Delete(inputFile); } catch { }
                }

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
