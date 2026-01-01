using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IEVRModManager.Managers
{
    /// <summary>
    /// Provides integration with the Viola CLI tool for merging mods.
    /// </summary>
    public class ViolaIntegration : IDisposable
    {
        private readonly Action<string> _logCallback;
        private readonly Action<int, string>? _progressCallback;
        private Process? _currentProcess;
        private bool _isRunning;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ViolaIntegration"/> class.
        /// </summary>
        /// <param name="logCallback">Optional callback for log messages.</param>
        /// <param name="progressCallback">Optional callback for progress updates (progress percentage, status message).</param>
        public ViolaIntegration(Action<string>? logCallback = null, Action<int, string>? progressCallback = null)
        {
            _logCallback = logCallback ?? (_ => { });
            _progressCallback = progressCallback;
        }

        /// <summary>
        /// Merges multiple mods using the Viola CLI tool.
        /// </summary>
        /// <param name="violaCliPath">The path to the violacli.exe executable.</param>
        /// <param name="cfgBinPath">The path to the cpk_list.cfg.bin configuration file.</param>
        /// <param name="modPaths">The list of mod directory paths to merge.</param>
        /// <param name="outputDir">The output directory where merged files will be placed.</param>
        /// <returns><c>true</c> if the merge operation completed successfully; otherwise, <c>false</c>.</returns>
        public async Task<bool> MergeModsAsync(string violaCliPath, string cfgBinPath,
            List<string> modPaths, string outputDir)
        {
            if (!ValidateInputs(violaCliPath, cfgBinPath))
            {
                return false;
            }

            outputDir = Path.GetFullPath(outputDir);
            Directory.CreateDirectory(outputDir);

            var commandArgs = BuildMergeCommandArgs(cfgBinPath, modPaths, outputDir);
            LogCommandExecution(violaCliPath, commandArgs);

            return await ExecuteMergeProcessAsync(violaCliPath, commandArgs);
        }

        private bool ValidateInputs(string violaCliPath, string cfgBinPath)
        {
            if (!File.Exists(violaCliPath))
            {
                _logCallback("Error: violacli.exe not found");
                _progressCallback?.Invoke(0, "ViolaExeNotFound");
                return false;
            }

            if (!File.Exists(cfgBinPath))
            {
                _logCallback("Error: cpk_list.cfg.bin not found");
                _progressCallback?.Invoke(0, "CpkListNotFound");
                return false;
            }

            return true;
        }

        private List<string> BuildMergeCommandArgs(string cfgBinPath, List<string> modPaths, string outputDir)
        {
            var cmd = new List<string> { "-m", "merge", "-p", "PC", "--cl", cfgBinPath };
            cmd.AddRange(modPaths);
            cmd.AddRange(new[] { "-o", outputDir });
            return cmd;
        }

        private void LogCommandExecution(string violaCliPath, List<string> commandArgs)
        {
            var commandLine = string.Join(" ", commandArgs.Select(QuoteArgument));
            _logCallback($"Executing command:\n{violaCliPath} {commandLine}");
            _progressCallback?.Invoke(10, "MergingMods");
        }

        private async Task<bool> ExecuteMergeProcessAsync(string violaCliPath, List<string> commandArgs)
        {
            try
            {
                _isRunning = true;
                var startInfo = CreateProcessStartInfo(violaCliPath, commandArgs);

                _currentProcess = Process.Start(startInfo);
                if (_currentProcess == null)
                {
                    _logCallback("Failed to start violacli process");
                    _progressCallback?.Invoke(0, "FailedToStartViolaProcess");
                    _isRunning = false;
                    return false;
                }

                StartOutputReading();
                var exitCode = await WaitForProcessCompletionAsync();

                return HandleProcessCompletion(exitCode);
            }
            catch (FileNotFoundException ex)
            {
                HandleProcessError($"Execution error: {ex.Message}", $"ExecutionError:{ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                HandleProcessError($"Unexpected error: {ex.Message}", $"UnexpectedError:{ex.Message}");
                return false;
            }
        }

        private ProcessStartInfo CreateProcessStartInfo(string violaCliPath, List<string> commandArgs)
        {
            return new ProcessStartInfo
            {
                FileName = violaCliPath,
                Arguments = string.Join(" ", commandArgs.Select(QuoteArgument)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        private void StartOutputReading()
        {
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
        }

        private async Task<int> WaitForProcessCompletionAsync()
        {
            // Start a periodic progress update task that gradually increases from 10% to 45%
            // This provides visual feedback that the process is still running
            // The progress oscillates between 10% and 45% to show the process is active
            using var cts = new CancellationTokenSource();
            var progressTask = Task.Run(async () =>
            {
                const int startProgress = 10;
                const int endProgress = 45;
                const int updateIntervalMs = 150; // Update every 150ms for smoother progress
                const int cycleDurationMs = 30000; // Complete cycle over 30 seconds
                const int steps = cycleDurationMs / updateIntervalMs;
                
                try
                {
                    int cycle = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (_currentProcess == null || _currentProcess.HasExited)
                            break;
                        
                        // Calculate progress: oscillate between startProgress and endProgress
                        // Each cycle takes 30 seconds, then repeats
                        int stepInCycle = cycle % steps;
                        var progress = startProgress + (int)((endProgress - startProgress) * (stepInCycle / (double)steps));
                        _progressCallback?.Invoke(progress, "MergingMods");
                        
                        await Task.Delay(updateIntervalMs, cts.Token);
                        cycle++;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when process completes
                }
            }, cts.Token);

            // Wait for process to complete
            await _currentProcess!.WaitForExitAsync();
            
            // Cancel progress updates
            cts.Cancel();
            
            var exitCode = _currentProcess.ExitCode;
            _logCallback($"violacli finished with code {exitCode}");
            return exitCode;
        }

        private bool HandleProcessCompletion(int exitCode)
        {
            DisposeProcess();
            _isRunning = false;

            if (exitCode == 0)
            {
                _progressCallback?.Invoke(50, "MergeCompletedCopyingFiles");
            }
            else
            {
                _progressCallback?.Invoke(0, $"MergeFailedWithCode:{exitCode}");
            }

            return exitCode == 0;
        }

        private void HandleProcessError(string logMessage, string progressMessage)
        {
            _logCallback(logMessage);
            _progressCallback?.Invoke(0, progressMessage);
            DisposeProcess();
            _isRunning = false;
        }

        /// <summary>
        /// Copies merged files from the temporary directory to the game data directory.
        /// </summary>
        /// <param name="tmpDataDir">The temporary directory containing merged files.</param>
        /// <param name="gameDataDir">The game data directory where files will be copied.</param>
        /// <returns><c>true</c> if the copy operation completed successfully; otherwise, <c>false</c>.</returns>
        public bool CopyMergedFiles(string tmpDataDir, string gameDataDir)
        {
            if (!Directory.Exists(tmpDataDir))
            {
                _logCallback($"{tmpDataDir} was not found. Aborting.");
                _progressCallback?.Invoke(0, $"TmpDataNotFound:{tmpDataDir}");
                return false;
            }

            _logCallback($"Copying {tmpDataDir} -> {gameDataDir} (overwriting if needed)...");
            _progressCallback?.Invoke(50, "CopyingFiles");

            Directory.CreateDirectory(gameDataDir);

            try
            {
                var totalFiles = CountFiles(tmpDataDir);
                var copiedFiles = 0;
                CopyDirectory(tmpDataDir, gameDataDir, true, totalFiles, ref copiedFiles);
                _logCallback("Copy completed.");
                _progressCallback?.Invoke(90, "CleaningUpTemporaryFiles");
                return true;
            }
            catch (Exception ex)
            {
                _logCallback($"Error copying files: {ex.Message}");
                _progressCallback?.Invoke(0, $"FailedToCopyMergedFiles:{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cleans up the temporary directory by deleting its contents and recreating it.
        /// </summary>
        /// <param name="tmpDir">The temporary directory to clean up.</param>
        /// <returns><c>true</c> if the cleanup completed successfully; otherwise, <c>false</c>.</returns>
        public bool CleanupTemp(string tmpDir)
        {
            try
            {
                if (Directory.Exists(tmpDir))
                {
                    Directory.Delete(tmpDir, true);
                    Directory.CreateDirectory(tmpDir);
                    _logCallback($"Cleaned temporary folder {tmpDir}.");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logCallback($"Could not remove {tmpDir}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a merge operation is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Stops the current merge operation if one is running.
        /// </summary>
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
            }
            DisposeProcess();
            _isRunning = false;
        }

        /// <summary>
        /// Disposes the current process if it exists.
        /// </summary>
        private void DisposeProcess()
        {
            if (_currentProcess != null)
            {
                try
                {
                    if (!_currentProcess.HasExited)
                    {
                        _currentProcess.Kill();
                    }
                }
                catch
                {
                    // Ignore errors when disposing process
                }
                finally
                {
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }
        }

        /// <summary>
        /// Releases all resources used by the ViolaIntegration instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the ViolaIntegration instance and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DisposeProcess();
                }
                _disposed = true;
            }
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

        private void CopyDirectory(string sourceDir, string destDir, bool overwrite, int totalFiles, ref int copiedFiles)
        {
            var dir = new DirectoryInfo(sourceDir);
            Directory.CreateDirectory(destDir);

            CopyFilesInDirectory(dir, destDir, overwrite, totalFiles, ref copiedFiles);
            CopySubdirectories(dir, destDir, overwrite, totalFiles, ref copiedFiles);
        }

        private void CopyFilesInDirectory(DirectoryInfo dir, string destDir, bool overwrite, int totalFiles, ref int copiedFiles)
        {
            foreach (var file in dir.GetFiles())
            {
                var targetFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
                copiedFiles++;
                
                UpdateCopyProgress(copiedFiles, totalFiles);
            }
        }

        private void CopySubdirectories(DirectoryInfo dir, string destDir, bool overwrite, int totalFiles, ref int copiedFiles)
        {
            foreach (var subDir in dir.GetDirectories())
            {
                var newDestDir = Path.Combine(destDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestDir, overwrite, totalFiles, ref copiedFiles);
            }
        }

        private void UpdateCopyProgress(int copiedFiles, int totalFiles)
        {
            // Update progress: 50% to 90% for copying (40% range)
            if (totalFiles > 0 && _progressCallback != null)
            {
                const int copyStartProgress = 50;
                const int copyProgressRange = 40;
                var progress = copyStartProgress + (int)((copiedFiles / (double)totalFiles) * copyProgressRange);
                _progressCallback(progress, $"CopyingFiles:{copiedFiles}:{totalFiles}");
            }
        }

        private static int CountFiles(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}

