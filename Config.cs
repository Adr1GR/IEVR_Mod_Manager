using System;
using System.IO;

namespace IEVRModManager
{
    /// <summary>
    /// Provides static configuration constants and paths used throughout the application.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// The name of the application configuration file.
        /// </summary>
        public const string AppConfigFile = "config.json";
        
        /// <summary>
        /// The name of the last installation record file.
        /// </summary>
        public const string LastInstallFile = "last_install.json";
        
        /// <summary>
        /// The name of the mods directory.
        /// </summary>
        public const string ModsDirName = "Mods";
        
        /// <summary>
        /// The name of the temporary directory.
        /// </summary>
        public const string TmpDirName = "tmp";
        
        /// <summary>
        /// The name of the shared storage directory.
        /// </summary>
        public const string SharedStorageDirName = "storage";
        
        /// <summary>
        /// The name of the CPK files directory within shared storage.
        /// </summary>
        public const string SharedStorageCpkDirName = "cpk";
        
        /// <summary>
        /// The name of the Viola CLI directory within shared storage.
        /// </summary>
        public const string SharedStorageViolaDirName = "viola";
        
        /// <summary>
        /// The name of the backup directory.
        /// </summary>
        public const string BackupDirName = "backup";

        private static string? _baseDir;
        private static readonly object _baseDirLock = new object();

        /// <summary>
        /// Gets the base directory path where application data is stored.
        /// </summary>
        public static string BaseDir
        {
            get
            {
                if (_baseDir == null)
                {
                    lock (_baseDirLock)
                    {
                        if (_baseDir == null)
                        {
                            _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".ievrModManager");
                            EnsureDirectoryExists(_baseDir);
                        }
                    }
                }
                return _baseDir;
            }
        }

        /// <summary>
        /// Gets the full path to the configuration file.
        /// </summary>
        public static string ConfigPath => Path.Combine(BaseDir, AppConfigFile);
        
        /// <summary>
        /// Gets the full path to the last installation record file.
        /// </summary>
        public static string LastInstallPath => Path.Combine(BaseDir, LastInstallFile);
        
        /// <summary>
        /// Gets the default mods directory path, creating it if it doesn't exist.
        /// </summary>
        public static string DefaultModsDir => EnsureDirectory(Path.Combine(BaseDir, ModsDirName));
        
        /// <summary>
        /// Gets the default temporary directory path, creating it if it doesn't exist.
        /// </summary>
        public static string DefaultTmpDir => EnsureDirectory(Path.Combine(BaseDir, TmpDirName));
        
        /// <summary>
        /// Gets the shared storage directory path, creating it if it doesn't exist.
        /// </summary>
        public static string SharedStorageDir => EnsureDirectory(Path.Combine(BaseDir, SharedStorageDirName));
        
        /// <summary>
        /// Gets the shared CPK directory path, creating it if it doesn't exist.
        /// </summary>
        public static string SharedStorageCpkDir => EnsureDirectory(Path.Combine(SharedStorageDir, SharedStorageCpkDirName));
        
        /// <summary>
        /// Gets the shared Viola CLI directory path, creating it if it doesn't exist.
        /// </summary>
        public static string SharedStorageViolaDir => EnsureDirectory(Path.Combine(SharedStorageDir, SharedStorageViolaDirName));
        
        /// <summary>
        /// Gets the backup directory path, creating it if it doesn't exist.
        /// </summary>
        public static string BackupDir => EnsureDirectory(Path.Combine(BaseDir, BackupDirName));
        

        /// <summary>
        /// The default window title for the application.
        /// </summary>
        public const string WindowTitle = "IEVR Mod Manager";
        
        /// <summary>
        /// The default window width in pixels.
        /// </summary>
        public const int WindowWidth = 1200;
        
        /// <summary>
        /// The default window height in pixels.
        /// </summary>
        public const int WindowHeight = 800;
        
        /// <summary>
        /// The minimum window width in pixels.
        /// </summary>
        public const int WindowMinWidth = 1000;
        
        /// <summary>
        /// The minimum window height in pixels.
        /// </summary>
        public const int WindowMinHeight = 600;

        /// <summary>
        /// The URL to the latest Viola release on GitHub.
        /// </summary>
        public const string ViolaReleaseUrl = "https://github.com/Adr1GR/Viola/releases/tag/v1.4.2";
        
        /// <summary>
        /// The URL to the CPK list directory on GitHub.
        /// </summary>
        public const string CpkListUrl = "https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list";
        
        /// <summary>
        /// The URL to GameBanana mods page for Infinite Wealth VR.
        /// </summary>
        public const string GameBananaModsUrl = "https://gamebanana.com/mods/games/20069";

        private static string EnsureDirectory(string path)
        {
            EnsureDirectoryExists(path);
            return path;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}

