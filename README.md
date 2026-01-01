# IEVR Mod Manager 1.7.2

A mod manager for **Inazuma Eleven Victory Road**.

<img width="800" height="535" alt="Screenshot 2025-12-05 220919" src="https://github.com/user-attachments/assets/2ac49875-33ef-417a-bbc9-65bc4cd0c640" />


---

## Table of Contents

- [For Users](#for-users)
  - [Requirements](#requirements)
  - [Installation](#installation)
  - [First Time Setup](#first-time-setup)
  - [Using the Mod Manager](#using-the-mod-manager)
  - [Applying Mods](#applying-mods)
  - [Mod Profiles](#mod-profiles)
  - [Customization](#customization)
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

---

## For Users

### Requirements

- **.NET 8 Desktop Runtime** (needed to run the app; small download from Microsoft)
- **Inazuma Eleven Victory Road** installed on your system
- **Viola.CLI-Portable.exe** - Download from [Viola releases](https://github.com/skythebro/Viola/releases/latest) and place a single copy in the shared `viola` folder (see First Time Setup)
- **cpk_list.cfg.bin** - Use the **Download cpk_list** button (recommended) or download from the [cpk_list repository](https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list) and copy it into the shared `cpk` folder (you can keep multiple versions)

### Installation

1. Download the latest release of **IEVR Mod Manager**
2. Extract the files to a folder of your choice
3. If not already installed, install the **.NET 8 Desktop Runtime** (x64)
3. Run `IEVRModManager.exe`

### First Time Setup

The app stores its data in `%AppData%\.ievrModManager\` (configuration, mods, temp files, and shared downloads).

1. Open **Configuration** and set **Game Path** (root folder that contains `data`).
2. Prepare the shared **cpk** folder:
   - Click **Download cpk_list** to fetch available `cpk_list.cfg.bin` files into `%AppData%\.ievrModManager\storage\cpk`, **or** copy your own file there.
   - Choose the desired file in the dropdown on the main window (selection is saved).
3. Prepare the shared **Viola** folder:
   - Place exactly one `Viola.CLI-Portable.exe` in `%AppData%\.ievrModManager\storage\viola`.
   - The app auto-detects it; if there are multiple `.exe` files you will be asked to keep only one.
4. Mods folder location:
   - Mods are loaded from `%AppData%\.ievrModManager\Mods`. Use **Open Mods Folder** to go directly to that directory.
5. (Optional) Customize appearance:
   - Set your preferred **Theme** (System/Dark/Light) and **Language** (System/English/Español) in Configuration
   - Changes require an application restart
6. Settings are saved automatically. Use **Downloads** for quick links to Viola, cpk_list, and GameBanana.

### Using the Mod Manager

The main window shows all mods found in `%AppData%\.ievrModManager\Mods` (top = highest priority). Each mod shows:
- Enabled status (✓ = enabled, ✗ = disabled)
- Display name
- Version
- Game version compatibility
- Author

**Operations:**
- **Enable/Disable**: Double-click a mod row or click the ✓/✗ column
- **Scan Mods**: Refresh after adding/removing mods in the Mods folder
- **Move Up/Down**: Change mod priority (higher overrides lower on conflicts)
- **Enable All / Disable All**: Quickly toggle every mod
- **Open Mods Folder**: Opens the Mods directory in Explorer
- **Browse Mods**: Opens the GameBanana browser to discover and download mods directly
- **cpk selector + Download cpk_list**: Pick which `cpk_list.cfg.bin` to use and download missing files directly into the shared storage
- **Mod Profiles**: Save and quickly switch between different mod configurations (see [Mod Profiles](#mod-profiles))

**Installing Mods:**
1. Download a mod from a trusted source (e.g., [GameBanana](https://gamebanana.com/mods/games/20069))
   - Use the **Browse Mods** button to open the integrated GameBanana browser
2. Extract the mod folder to `%AppData%\.ievrModManager\Mods` (use **Open Mods Folder**)
   - **Important:** Folder structure must be `ModFolderName/data/` (the `data` folder directly inside the mod folder)
   - If the archive has `ModFolderName/ModFolderName/data/`, move the inner folder up one level
3. Click **Scan Mods** to refresh the list

### Applying Mods

1. Enable the mods you want and arrange priority.
2. Choose the `cpk_list.cfg.bin` from the dropdown (fetched from the shared `cpk` folder).
3. Click **Apply Changes**.
4. The app will:
   - Ask for confirmation before creating or restoring backups
   - Warn you about `data/packs` edits and file conflicts so you can cancel if needed
   - Use the selected `cpk_list.cfg.bin` and the Viola executable from shared storage
   - Merge mods, copy results into the game `data` folder, and remove leftover files from the previous install
   - If **no mods** are enabled, it simply restores the selected `cpk_list.cfg.bin` to the game
5. A popup confirms success and lists the mods that were applied.

**Notes:**
- Always close the game before applying mods
- Duration depends on the number and size of mods
- The app will automatically minimize when launching the game (if no pending changes alerts are shown)

### Mod Profiles

Mod Profiles allow you to save and quickly switch between different mod configurations. This is useful when you want to:
- Switch between different gameplay setups (e.g., competitive vs. casual)
- Test different mod combinations
- Quickly restore a previous working configuration

**Using Mod Profiles:**

1. **Create a Profile:**
   - Configure your mods (enable/disable, set priority, select CPK version)
   - Click the **Profile** dropdown and select **Manage Profiles**
   - Enter a profile name and click **Save**

2. **Load a Profile:**
   - Select a profile from the **Profile** dropdown
   - The app will automatically apply the saved mod configuration and CPK selection

3. **Manage Profiles:**
   - Click **Manage Profiles** to open the profile manager
   - Load, save, or delete profiles as needed
   - The last applied profile is automatically loaded when you restart the app

**Profile Features:**
- Saves enabled/disabled state for all mods
- Saves mod priority order
- Saves selected CPK version
- Automatically restores the last applied profile on startup

### Customization

**Theme Selection:**
- Open **Configuration** and select your preferred theme:
  - **System**: Follows your Windows theme (light/dark)
  - **Dark**: Always use dark theme
  - **Light**: Always use light theme
- Theme changes require an application restart to take effect

**Language Selection:**
- Open **Configuration** and select your preferred language:
  - **System**: Uses your Windows language settings
  - **English**: English interface
  - **Español**: Spanish interface
- Language changes require an application restart to take effect
- Additional languages can be added by creating new YAML files in the `Resources` folder

### Troubleshooting

**"Invalid game path" error**
- Select the correct game root folder (must contain `data`)

**"No Viola executable found" or multiple executables**
- Keep exactly one `.exe` inside `%AppData%\.ievrModManager\storage\viola`
- Use **Downloads** to get the official Viola release, then place it there

**"Invalid cpk_list.cfg.bin" error**
- Download/refresh via **Download cpk_list** or copy the right file into `%AppData%\.ievrModManager\storage\cpk`
- Re-select it in the dropdown if necessary

**Mods not appearing**
- Click **Scan Mods**
- Ensure the mod folder sits directly inside `%AppData%\.ievrModManager\Mods` (not another subfolder)
- Check the structure: `ModFolderName/data/`

**Game crashes or mods don't work**
- Verify mod compatibility with your game version
- Disable mods one by one to find conflicts
- Adjust mod priority (higher overrides lower)

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

1. Create a mod folder inside `%AppData%\.ievrModManager\Mods` (use a descriptive name, avoid spaces)
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
    "GameVersion": "1.4.1",
    "ModLink": "https://example.com/your-mod-page"
}
```

**Fields:**
- **Name** (required): Display name shown in the Mod Manager. If omitted, the folder name is used.
- **Author** (optional): Your name or username. Use empty string `""` if not specified.
- **ModVersion** (optional): Version of your mod (e.g., "1.0", "2.3"). Use empty string `""` if not applicable.
- **GameVersion** (optional): Game version this mod is designed for (e.g., "1.4.1"). Use empty string `""` if not version-specific.
- **ModLink** (optional): Direct link to the mod page (e.g., GameBanana URL) shown in the manager for quick access.

**Example:**
```json
{
    "Name": "Spanish Translation Patch",
    "Author": "Adr1GR",
    "ModVersion": "1.2",
    "GameVersion": "1.4.1",
    "ModLink": "https://gamebanana.com/mods/637376"
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

# Publish (recommended): single-file, framework-dependent (small download, requires .NET 8 Desktop Runtime)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

# Publish self-contained (bundles .NET, larger ~150 MB)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
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

### Building for Linux (AppImage)

**⚠️ Important:** This project currently uses **WPF (Windows Presentation Foundation)**, which is Windows-only. To build for Linux, you need to migrate to a cross-platform framework like **Avalonia UI** or **.NET MAUI**.

**Current Status:**
- ❌ WPF does not work on Linux
- ✅ Build scripts are prepared for when migration is complete
- 📖 See `MIGRATION_TO_LINUX.md` for detailed migration guide

**After Migration to Avalonia UI:**

1. **Prepare the build environment (Linux):**
   ```bash
   # Install .NET 8 SDK
   # Install appimagetool (will be downloaded automatically by build script)
   ```

2. **Run the Linux build script:**
   ```bash
   ./build-linux.sh 1.8.0
   ```

3. **The script will:**
   - Update version numbers
   - Compile for Linux-x64
   - Create AppImage structure
   - Generate `IEVRModManager-1.8.0-x86_64.AppImage`

4. **Test the AppImage:**
   ```bash
   chmod +x IEVRModManager-1.8.0-x86_64.AppImage
   ./IEVRModManager-1.8.0-x86_64.AppImage
   ```

**For more information:**
- See `MIGRATION_TO_LINUX.md` for migration guide
- See `build-linux.sh` for the build script
- See `prepare-linux-build.ps1` to identify Windows-specific code

### Technologies Used

- **.NET 8.0** - Development framework
- **WPF (Windows Presentation Foundation)** - Graphical interface
- **C#** - Programming language
- **System.Text.Json** - JSON serialization
- **YamlDotNet** - YAML parsing for localization
- **System.IO** - File operations

### Development Notes

- Project uses **nullable reference types** (`nullable enable`)
- App data root: `%AppData%\.ievrModManager\`
  - `config.json`, `last_install.json`
  - `Mods/` (user mods), `tmp/` (merge workspace), `storage/cpk` (cpk_list), `storage/viola` (Viola executable), `Profiles/` (mod profiles)
- Default mods directory is `%AppData%\.ievrModManager\Mods`
- `config.json` format remains compatible with previous versions
- Localization files are stored as YAML files in `Resources/` directory (e.g., `Strings.yaml`, `Strings.es-ES.yaml`)
- Themes are defined in `Themes/` directory (`DarkTheme.xaml`, `LightTheme.xaml`)

---

## Credits

- Mod Manager created by Adr1GR
- Uses [Viola](https://github.com/skythebro/Viola) for CPK merging
- Mods available on [GameBanana](https://gamebanana.com/mods/games/20069)
