using System;
using System.IO;

namespace IEVRModManager.Helpers
{
    /// <summary>
    /// Provides utility methods for file system operations.
    /// </summary>
    public static class FileSystemHelper
    {
        /// <summary>
        /// Ensures that the specified directory exists, creating it if necessary.
        /// </summary>
        /// <param name="path">The directory path to ensure exists.</param>
        /// <exception cref="IOException">Thrown when there's an error creating the directory.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when access to the directory is denied.</exception>
        /// <remarks>
        /// This method does not log errors to avoid circular dependencies during initialization.
        /// Callers should handle logging if needed.
        /// </remarks>
        public static void EnsureDirectoryExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
