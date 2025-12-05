using System;
using System.Collections.Generic;

namespace IEVRModManager.Models
{
    public class LastInstallInfo
    {
        public string GamePath { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
        public List<string> Mods { get; set; } = new List<string>();
        public DateTime AppliedAt { get; set; } = DateTime.MinValue;

        public static LastInstallInfo Empty()
        {
            return new LastInstallInfo
            {
                GamePath = string.Empty,
                Files = new List<string>(),
                Mods = new List<string>(),
                AppliedAt = DateTime.MinValue
            };
        }
    }
}
