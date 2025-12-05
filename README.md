# IEVR Mod Manager 1.3

A mod manager for **Inazuma Eleven Victory Road**.

<img width="800" height="535" alt="image" src="https://github.com/user-attachments/assets/d83ae72d-57fd-4893-a954-ebe1dc72253d" />

---

## Table of Contents

- [For Users](#for-users)
  - [Requirements](#requirements)
  - [Installation](#installation)
  - [First Time Setup](#first-time-setup)
  - [Using the Mod Manager](#using-the-mod-manager)
  - [Applying Mods](#applying-mods)
  - [Troubleshooting](#troubleshooting)
- [For Mod Developers](#for-mod-developers)
  - [Mod Structure](#mod-structure)
  - [Creating a Mod](#creating-a-mod)
  - [Mod Metadata](#mod-metadata)
  - [Mod Priority](#mod-priority)
- [For Developers](#for-developers)
  - [Development Requirements](#development-requirements)
  - [Building the Project](#building-the-project)
  - [Running the Project](#running-the-project)
  - [Project Structure](#project-structure)

---

## For Users

### Requirements

- **Inazuma Eleven Victory Road** installed on your system
- **Viola.CLI-Portable.exe** - Download from [Viola releases](https://github.com/skythebro/Viola/releases/latest)
- **cpk_list.cfg.bin** - Download from [cpk_list repository](https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list)
  - Download the version matching your game version (e.g., `1_4_1_cpk_list.cfg.bin` for game version 1.4.1)

### Installation

1. Download the latest release of **IEVR Mod Manager**
2. Extract the files to a folder of your choice
3. Run `IEVRModManager.exe`

### First Time Setup

Configure the following paths via the **Configuration** button:

- **Game Path**: Select your Inazuma Eleven Victory Road installation folder
  - Example: `C:\Program Files (x86)\Steam\steamapps\common\INAZUMA ELEVEN Victory Road`
- **cpk_list.cfg.bin Path**: Select the `cpk_list.cfg.bin` file matching your game version
- **Viola.CLI-Portable.exe Path**: Select the `Viola.CLI-Portable.exe` executable

Access download links via the **Downloads** button. Settings are saved automatically.

### Using the Mod Manager

The main window displays all mods found in the `Mods/` folder. Each mod shows:
- Enabled status (✓ = enabled, ✗ = disabled)
- Display name
- Version
- Game version compatibility
- Author

**Operations:**
- **Enable/Disable**: Double-click a mod row or click the ✓/✗ column
- **Scan Mods**: Refresh the mod list after adding new mods
- **Move Up/Down**: Change mod priority (higher priority mods override conflicting files)
- **Enable All / Disable All**: Quickly toggle all mods
- **Open Mods Folder**: Open the `Mods/` directory in Windows Explorer

**Installing Mods:**
1. Download a mod from a trusted source (e.g., [GameBanana](https://gamebanana.com/mods/games/20069))
2. Extract the mod folder to the `Mods/` directory
   - **Important:** The mod folder structure must be: `ModFolderName/data/` (the `data` folder must be directly inside the mod folder)
   - If the downloaded mod has a different structure, reorganize it so the `data` folder is at the root of the mod folder
3. Click "Scan Mods" to refresh the list

### Applying Mods

1. Ensure desired mods are enabled
2. Arrange mod priority if needed
3. Click **"Apply Changes"**
4. Wait for completion (monitor progress in Activity Log)
5. A confirmation popup will appear when mods are successfully applied

**Notes:**
- The Mod Manager merges all enabled mods and applies them to your game's `data` folder
- If no mods are enabled, it restores the original `cpk_list.cfg.bin` file
- Always close the game before applying mods
- Process duration depends on the number and size of mods

### Troubleshooting

**"Invalid game path" error**
- Verify the correct game installation folder is selected
- The folder must contain a `data` subfolder

**"violacli.exe not found" error**
- Use the **Downloads** button to access download links
- Configure the correct path in the Configuration window

**"Invalid cpk_list.cfg.bin path" error**
- Download the correct `cpk_list.cfg.bin` file for your game version
- Verify the file path in the Configuration window

**Mods not appearing after installation**
- Click "Scan Mods" to refresh the list
- Ensure the mod folder is directly inside `Mods/`, not in a subfolder
- **Verify the mod structure:** The mod folder must contain a `data` folder directly inside it (structure: `ModFolderName/data/`)
  - If you see `ModFolderName/ModFolderName/data/`, move the inner folder up one level

**Game crashes or mods don't work**
- Verify mod compatibility with your game version
- Check the mod's `GameVersion` field matches your game version
- Disable mods one by one to identify conflicts
- Ensure mod priority is set correctly

---

## For Mod Developers

### Mod Structure

A mod must follow this directory structure:

```
YourModName/
├── mod_data.json          (Required - Mod metadata)
└── data/                  (Required - Mod files)
    ├── cpk_list.cfg.bin   (Required - CPK list file)
    └── [other game files] (Optional - Any files you want to modify)
```

**Important for mod distribution:** When users download and extract your mod, they should get a folder structure where `data/` is directly inside the mod folder. The extracted structure must be `ModFolderName/data/`, not `ModFolderName/ModFolderName/data/`.

The `data/` folder should mirror the game's `data/` folder structure:
- Text files: `data/common/text/[language]/[file].cfg.bin`
- Textures: `data/dx11/[category]/[file].g4tx`
- Game parameters: `data/common/property/global_param/[file].cfg.bin`

### Creating a Mod

1. Create a mod folder in the `Mods/` directory (use a descriptive name, avoid spaces)
2. Create the `data/` folder inside your mod folder
3. Add your modded files to `data/`, maintaining the same directory structure as the game
4. Create `mod_data.json` in the root of your mod folder (see [Mod Metadata](#mod-metadata))
5. Include `cpk_list.cfg.bin` in your mod's `data/` folder (copy from the `cpk_list` repository and modify if needed)

### Mod Metadata

Create a `mod_data.json` file in your mod's root folder:

```json
{
    "Name": "Display Name of Your Mod",
    "Author": "Your Name or Username",
    "ModVersion": "1.0",
    "GameVersion": "1.4.1"
}
```

**Fields:**
- **Name** (required): Display name shown in the Mod Manager. If omitted, the folder name is used.
- **Author** (optional): Your name or username. Use empty string `""` if not specified.
- **ModVersion** (optional): Version of your mod (e.g., "1.0", "2.3"). Use empty string `""` if not applicable.
- **GameVersion** (optional): Game version this mod is designed for (e.g., "1.4.1"). Use empty string `""` if not version-specific.

**Example:**
```json
{
    "Name": "Spanish Translation Patch",
    "Author": "Adr1GR",
    "ModVersion": "1.2",
    "GameVersion": "1.4.1"
}
```

### Mod Priority

Mod priority determines which mod's files take precedence when there are conflicts:
- **Higher priority** = Mods listed higher in the Mod Manager
- **Lower priority** = Mods listed lower in the Mod Manager

When two mods modify the same file, the mod with higher priority (higher in the list) overrides the lower priority mod's version. Users can reorder mods using "Move Up" and "Move Down" buttons.

**Recommendations:**
- If your mod is meant to override others, document that it should be placed higher in the priority list
- Base/foundation mods should typically be lower in priority
- Document recommended mod order in your mod's description

---

## For Developers

### Development Requirements

- **.NET 8.0 SDK** or higher
- **Windows** (WPF only works on Windows)
- **Visual Studio 2022** or **Visual Studio Code** with C# extension
- **Git** (optional, for version control)

### Building the Project

**From Visual Studio:**
1. Open `IEVRModManager.csproj` in Visual Studio
2. Build > Build Solution (Ctrl+Shift+B)
3. Executable generated in `bin\Debug\net8.0-windows\` or `bin\Release\net8.0-windows\`

**From command line:**

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish self-contained (single executable, no .NET required)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Publish standard (requires .NET Runtime)
dotnet publish -c Release -r win-x64 --self-contained false
```

### Running the Project

**From Visual Studio:** Press F5 or click Start

**From command line:**
```bash
dotnet run
```

**Run compiled executable:**
```bash
.\bin\Release\net8.0-windows\IEVRModManager.exe
```

### Project Structure

```
IEVRModManager/
├── IEVRModManager.csproj      # C# project file
├── App.xaml / App.xaml.cs      # Application entry point
├── MainWindow.xaml / .cs       # Main window
├── Config.cs                   # Constants and configuration
├── Models/                     # Data models
│   ├── ModEntry.cs
│   └── AppConfig.cs
├── Managers/                   # Business logic
│   ├── ConfigManager.cs
│   ├── ModManager.cs
│   └── ViolaIntegration.cs
├── Windows/                    # Secondary windows
│   ├── ConfigPathsWindow.xaml/.cs
│   ├── DownloadsWindow.xaml/.cs
│   └── SuccessMessageWindow.xaml/.cs
└── Themes/                     # Styles and themes
    └── DarkTheme.xaml
```

### Useful Commands

```bash
dotnet clean          # Clean build files
dotnet restore        # Restore dependencies
dotnet --info         # View project information
dotnet list package   # View project references
```

### Technologies Used

- **.NET 8.0** - Development framework
- **WPF (Windows Presentation Foundation)** - Graphical interface
- **C#** - Programming language
- **System.Text.Json** - JSON serialization
- **System.IO** - File operations

### Development Notes

- Project uses **nullable reference types** (`nullable enable`)
- Configuration saved in `config.json` in the application base directory
- Mods scanned from `Mods/` folder in the base directory
- Temporary files created in `tmp/` folder in the base directory
- `config.json` format is compatible with previous versions

---

## Credits

- Mod Manager created by [Your Name]
- Uses [Viola](https://github.com/skythebro/Viola) for CPK merging
- Mods available on [GameBanana](https://gamebanana.com/mods/games/20069)
