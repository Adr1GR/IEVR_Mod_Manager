using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IEVRModManager.Managers
{
    public class ViolaIntegration
    {
        private readonly Action<string> _logCallback;
        private Process? _currentProcess;
        private bool _isRunning;

        public ViolaIntegration(Action<string>? logCallback = null)
        {
            _logCallback = logCallback ?? (_ => { });
        }

        public async Task<bool> MergeModsAsync(string violaCliPath, string cfgBinPath,
            List<string> modPaths, string outputDir)
        {
            if (!File.Exists(violaCliPath))
            {
                _logCallback("Error: violacli.exe not found");
                return false;
            }

            if (!File.Exists(cfgBinPath))
            {
                _logCallback("Error: cpk_list.cfg.bin not found");
                return false;
            }

            outputDir = Path.GetFullPath(outputDir);
            Directory.CreateDirectory(outputDir);

            var cmd = new List<string> { "-m", "merge", "-p", "PC", "--cl", cfgBinPath };
            cmd.AddRange(modPaths);
            cmd.AddRange(new[] { "-o", outputDir });

            var commandLine = string.Join(" ", cmd.Select(QuoteArgument));
            _logCallback($"Executing command:\n{violaCliPath} {commandLine}");

            try
            {
                _isRunning = true;
                var startInfo = new ProcessStartInfo
                {
                    FileName = violaCliPath,
                    Arguments = string.Join(" ", cmd.Select(QuoteArgument)),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _currentProcess = Process.Start(startInfo);
                if (_currentProcess == null)
                {
                    _logCallback("Failed to start violacli process");
                    _isRunning = false;
                    return false;
                }

                // Read output asynchronously to avoid blocking
                _ = Task.Run(() =>
                {
                    if (_currentProcess?.StandardOutput != null)
                    {
                        string? line;
                        while ((line = _currentProcess.StandardOutput.ReadLine()) != null)
                        {
                            _logCallback(line);
                        }
                    }
                });

                await _currentProcess.WaitForExitAsync();
                var exitCode = _currentProcess.ExitCode;
                _logCallback($"violacli finished with code {exitCode}");

                _currentProcess = null;
                _isRunning = false;

                return exitCode == 0;
            }
            catch (FileNotFoundException ex)
            {
                _logCallback($"Execution error: {ex.Message}");
                _isRunning = false;
                return false;
            }
            catch (Exception ex)
            {
                _logCallback($"Unexpected error: {ex.Message}");
                _isRunning = false;
                return false;
            }
        }

        public bool CopyMergedFiles(string tmpDataDir, string gameDataDir)
        {
            if (!Directory.Exists(tmpDataDir))
            {
                _logCallback($"{tmpDataDir} was not found. Aborting.");
                return false;
            }

            _logCallback($"Copying {tmpDataDir} -> {gameDataDir} (overwriting if needed)...");

            Directory.CreateDirectory(gameDataDir);

            try
            {
                CopyDirectory(tmpDataDir, gameDataDir, true);
                _logCallback("Copy completed.");
                return true;
            }
            catch (Exception ex)
            {
                _logCallback($"Error copying files: {ex.Message}");
                return false;
            }
        }

        public bool CleanupTemp(string tmpDataDir)
        {
            try
            {
                if (Directory.Exists(tmpDataDir))
                {
                    Directory.Delete(tmpDataDir, true);
                    _logCallback($"Removed temporary folder {tmpDataDir}.");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logCallback($"Could not remove {tmpDataDir}: {ex.Message}");
                return false;
            }
        }

        public bool IsRunning => _isRunning;

        public void Stop()
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill();
                }
                catch
                {
                    // Ignore errors when stopping process
                }
                _currentProcess = null;
            }
            _isRunning = false;
        }

        private static string QuoteArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return "\"\"";

            if (arg.Contains(' ') || arg.Contains('\t') || arg.Contains('"'))
            {
                return "\"" + arg.Replace("\"", "\\\"") + "\"";
            }

            return arg;
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
        {
            var dir = new DirectoryInfo(sourceDir);
            var dirs = dir.GetDirectories();

            Directory.CreateDirectory(destDir);

            foreach (var file in dir.GetFiles())
            {
                var targetFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
            }

            foreach (var subDir in dirs)
            {
                var newDestDir = Path.Combine(destDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestDir, overwrite);
            }
        }
    }
}

