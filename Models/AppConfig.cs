using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace IEVRModManager.Models
{
    public class AppConfig : INotifyPropertyChanged
    {
        private string _gamePath = string.Empty;
        private string _cfgBinPath = string.Empty;
        private string _violaCliPath = string.Empty;
        private string _tmpDir = string.Empty;
        private string _selectedCpkName = string.Empty;

        public string GamePath
        {
            get => _gamePath;
            set { _gamePath = value; OnPropertyChanged(); }
        }

        public string CfgBinPath
        {
            get => _cfgBinPath;
            set { _cfgBinPath = value; OnPropertyChanged(); }
        }

        public string ViolaCliPath
        {
            get => _violaCliPath;
            set { _violaCliPath = value; OnPropertyChanged(); }
        }

        public string SelectedCpkName
        {
            get => _selectedCpkName;
            set { _selectedCpkName = value; OnPropertyChanged(); }
        }

        public string TmpDir
        {
            get => _tmpDir;
            set { _tmpDir = value; OnPropertyChanged(); }
        }

        public List<ModData> Mods { get; set; } = new List<ModData>();

        public static AppConfig Default()
        {
            return new AppConfig
            {
                GamePath = string.Empty,
                CfgBinPath = string.Empty,
                ViolaCliPath = string.Empty,
                SelectedCpkName = string.Empty,
                TmpDir = Config.DefaultTmpDir,
                Mods = new List<ModData>()
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

