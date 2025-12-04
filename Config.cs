using System;
using System.IO;

namespace IEVRModManager
{
    public static class Config
    {
        public const string AppConfigFile = "config.json";
        public const string ModsDirName = "Mods";
        public const string TmpDirName = "tmp";

        public static string BaseDir
        {
            get
            {
                // Prefer executable directory, fallback to base directory
                if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
                return AppContext.BaseDirectory;
            }
        }

        public static string ConfigPath => Path.Combine(BaseDir, AppConfigFile);
        public static string DefaultModsDir => Path.Combine(BaseDir, ModsDirName);
        public static string DefaultTmpDir => Path.Combine(BaseDir, TmpDirName);

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
    }
}

