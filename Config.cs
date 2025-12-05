using System;
using System.IO;

namespace IEVRModManager
{
    public static class Config
    {
        public const string AppConfigFile = "config.json";
        public const string LastInstallFile = "last_install.json";
        public const string ModsDirName = "Mods";
        public const string TmpDirName = "tmp";
        public const string SharedStorageDirName = "storage";
        public const string SharedStorageCpkDirName = "cpk";
        public const string SharedStorageViolaDirName = "viola";
        public const string BackupDirName = "backup";

        private static readonly string BaseDataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".ievrModManager");

        public static string BaseDir
        {
            get
            {
                EnsureDirectoryExists(BaseDataPath);
                return BaseDataPath;
            }
        }

        public static string ConfigPath => Path.Combine(BaseDir, AppConfigFile);
        public static string LastInstallPath => Path.Combine(BaseDir, LastInstallFile);
        public static string DefaultModsDir => EnsureDirectory(Path.Combine(BaseDir, ModsDirName));
        public static string DefaultTmpDir => EnsureDirectory(Path.Combine(BaseDir, TmpDirName));
        public static string SharedStorageDir => EnsureDirectory(Path.Combine(BaseDir, SharedStorageDirName));
        public static string SharedStorageCpkDir => EnsureDirectory(Path.Combine(SharedStorageDir, SharedStorageCpkDirName));
        public static string SharedStorageViolaDir => EnsureDirectory(Path.Combine(SharedStorageDir, SharedStorageViolaDirName));
        public static string BackupDir => EnsureDirectory(Path.Combine(BaseDir, BackupDirName));

        // UI Configuration
        public const string WindowTitle = "IEVR Mod Manager";
        public const int WindowWidth = 1200;
        public const int WindowHeight = 800;
        public const int WindowMinWidth = 1000;
        public const int WindowMinHeight = 600;

        // Links
        public const string ViolaReleaseUrl = "https://github.com/skythebro/Viola/releases/latest";
        public const string CpkListUrl = "https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list";
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

