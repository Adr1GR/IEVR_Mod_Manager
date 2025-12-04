# IEVR Mod Manager

A graphical mod manager for **Inazuma Eleven Victory Road** that allows you to easily install, manage, and apply multiple mods to your game.

<img width="1202" height="832" alt="image" src="https://github.com/user-attachments/assets/1d759c98-6222-47bc-aa17-5957d2d4f1c1" />

**Version 1.1** - Features a modern dark theme interface with improved usability.

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

---

## For Users

### Requirements

Before using the Mod Manager, make sure you have:

1. **Inazuma Eleven Victory Road** installed on your system
2. **Viola.CLI-Portable.exe** - Download from [Viola releases](https://github.com/skythebro/Viola/releases/latest)
3. **cpk_list.cfg.bin** - Download from [cpk_list repository](https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list)
   - Make sure to download the version matching your game version (e.g., `1_4_1_cpk_list.cfg.bin` for game version 1.4.1)

### Installation

1. Download the latest release of **IEVR Mod Manager**
2. Extract the files to a folder of your choice
3. Run `IEVRModManager.exe`

### First Time Setup

When you first launch the Mod Manager, you need to configure the following paths:

1. Click the **"⚙️ Configuration"** button at the bottom of the window to open the Configuration window

2. Configure the following paths:
   - **Game Path**: Click "Browse" next to "🎮 Game path:" and select your Inazuma Eleven Victory Road installation folder
     - Example: `C:\Program Files (x86)\Steam\steamapps\common\INAZUMA ELEVEN Victory Road`
   - **cpk_list.cfg.bin Path**: Click "Browse" next to "📄 cpk_list.cfg.bin:" and select the `cpk_list.cfg.bin` file you downloaded
     - Make sure this matches your game version!
   - **Viola.CLI-Portable.exe Path**: Click "Browse" next to "⚙️ Viola.CLI-Portable.exe:" and select the `Viola.CLI-Portable.exe` file you downloaded

3. You can also access download links by clicking the **"📥 Downloads"** button to quickly download required files

These settings are automatically saved and will be remembered for future sessions.

### Using the Mod Manager

#### Mod List

The main window displays all mods found in the `Mods/` folder. Each mod shows:
- **✓**: Whether the mod is enabled (✓ = enabled, ✗ = disabled)
- **Mod Name**: Display name of the mod
- **Version**: Version of the mod
- **Game Ver.**: Game version the mod is designed for
- **Author**: Mod creator's name

#### Basic Operations

- **Enable/Disable a Mod**: 
  - Double-click on a mod row, or
  - Click on the ✓/✗ column (first column) to toggle it on/off
- **Scan Mods**: Click "🔍 Scan Mods" to refresh the mod list (useful after adding new mods)
- **Move Up/Down**: Select a mod and use "⬆️ Move Up" or "⬇️ Move Down" to change its priority
  - Mods higher in the list have higher priority and will override conflicting files from mods below
- **Enable All / Disable All**: Use "✅ Enable All" or "❌ Disable All" to quickly enable or disable all mods
- **Open Mods Folder**: Click "📂 Open Mods Folder" to open the `Mods/` folder in Windows Explorer

#### Installing Mods

1. Download a mod from a trusted source
2. Extract the mod folder to the `Mods/` directory (located in the same folder as `IEVRModManager.exe`)
3. Click "Scan Mods" in the Mod Manager to refresh the list
4. The new mod should appear in the list

### Applying Mods

1. Make sure all your desired mods are **enabled** (showing ✓ in the first column)
2. Arrange mod priority using "⬆️ Move Up" and "⬇️ Move Down" if needed
3. Click **"✓ Apply Changes"** button
4. Wait for the process to complete - you can monitor progress in the Activity Log panel at the bottom
5. When you see "MODS APPLIED!!" in the log, the mods have been successfully installed

**Important Notes:**
- The Mod Manager merges all enabled mods and applies them to your game's `data` folder
- If no mods are enabled, it will restore the original `cpk_list.cfg.bin` file
- Always make sure your game is closed before applying mods
- The process may take a few minutes depending on the number and size of mods

### Troubleshooting

**Problem: "Invalid game path" error**
- Make sure you've selected the correct game installation folder
- The folder should contain a `data` subfolder

**Problem: "violacli.exe not found" error**
- Click the **"📥 Downloads"** button to access download links
- Download Viola.CLI-Portable.exe from the provided link
- Make sure you've configured the correct path to the executable in the Configuration window

**Problem: "Invalid cpk_list.cfg.bin path" error**
- Click the **"📥 Downloads"** button to access download links
- Download the correct `cpk_list.cfg.bin` file for your game version
- Make sure the file path is correct in the Configuration window

**Problem: Mods not appearing after installation**
- Click "🔍 Scan Mods" to refresh the list
- Make sure the mod folder is directly inside the `Mods/` folder, not in a subfolder
- Check that the mod folder contains a `data` folder

**Problem: Game crashes or mods don't work**
- Verify that the mods are compatible with your game version
- Check the mod's `GameVersion` field matches your game version
- Try disabling mods one by one to identify conflicts
- Make sure mod priority is set correctly (mods that should override others should be higher in the list)

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

The `data/` folder should mirror the game's `data/` folder structure. For example:
- Text files: `data/common/text/[language]/[file].cfg.bin`
- Textures: `data/dx11/[category]/[file].g4tx`
- Game parameters: `data/common/property/global_param/[file].cfg.bin`

### Creating a Mod

1. **Create a mod folder** in the `Mods/` directory
   - Use a descriptive name (e.g., `MyAwesomeMod`)
   - Avoid spaces and special characters if possible

2. **Create the `data/` folder** inside your mod folder
   - This is where all your modded game files will go

3. **Add your modded files** to the `data/` folder, maintaining the same directory structure as the game

4. **Create `mod_data.json`** in the root of your mod folder (see [Mod Metadata](#mod-metadata) below)

5. **Include `cpk_list.cfg.bin`** in your mod's `data/` folder
   - This file tells the game which CPK files to load
   - You can copy it from the `cpk_list` repository and modify it if needed

### Mod Metadata

Create a `mod_data.json` file in your mod's root folder with the following structure:

```json
{
    "Name": "Display Name of Your Mod",
    "Author": "Your Name or Username",
    "ModVersion": "1.0",
    "GameVersion": "1.4.1"
}
```

**Fields:**
- **Name** (required): The display name shown in the Mod Manager. If omitted, the folder name will be used.
- **Author** (optional): Your name or username. Leave empty string `""` if you don't want to specify.
- **ModVersion** (optional): Version of your mod (e.g., "1.0", "2.3"). Leave empty string `""` if not applicable.
- **GameVersion** (optional): Game version this mod is designed for (e.g., "1.4.1"). Leave empty string `""` if not version-specific.

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

When two mods modify the same file:
- The mod with **higher priority** (higher in the list) will override the lower priority mod's version
- Users can reorder mods using "Move Up" and "Move Down" buttons

**Recommendations:**
- If your mod is meant to override others, document that it should be placed higher in the priority list
- If your mod is a base/foundation mod, it should typically be lower in priority
- Consider documenting recommended mod order in your mod's description

---

## Support

If you encounter issues or have questions:
- Check the troubleshooting section above
- Review the log output in the Mod Manager for error messages
- Ensure all requirements are properly installed and configured

---

## License

[Add your license information here]

## Credits

- Mod Manager created by [Your Name]
- Uses [Viola](https://github.com/skythebro/Viola) for CPK merging
