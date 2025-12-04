"""Main application window."""
import os
import shutil
import threading
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

from ..config import (
    DEFAULT_MODS_DIR, DEFAULT_TMP_DIR, WINDOW_TITLE, WINDOW_SIZE, WINDOW_MIN_SIZE
)
from ..models import ModEntry
from ..config_manager import ConfigManager
from ..mod_manager import ModManager
from ..viola_integration import ViolaIntegration
from .mods_panel import ModsPanel
from .log_panel import LogPanel
from .downloads_window import DownloadsWindow
from .config_paths_window import ConfigPathsWindow
from .theme import Theme
from .window_utils import center_window


class MainWindow(tk.Tk):
    """Main application window."""
    
    def __init__(self):
        """Initialize the main window."""
        super().__init__()
        self.title(WINDOW_TITLE)
        self.geometry(WINDOW_SIZE)
        self.minsize(*WINDOW_MIN_SIZE)
        
        # Set window background color immediately
        self.configure(bg=Theme.BG_COLOR)
        
        # Center window on screen
        center_window(self)
        
        # Managers
        self.config_manager = ConfigManager()
        self.mod_manager = ModManager()
        self.viola = ViolaIntegration(log_callback=self._log)
        
        # State
        self.game_path = tk.StringVar()
        self.cfgbin_path = tk.StringVar()
        self.violacli_path = tk.StringVar()
        self.tmp_dir = tk.StringVar(value=DEFAULT_TMP_DIR)
        
        self.mod_entries: list[ModEntry] = []
        self.saved_mods = []
        
        # UI
        self._build_ui()
        
        # Setup callbacks
        self._setup_callbacks()
        
        # Load configuration
        self._load_config()
        self.scan_mods()
        
        # Cleanup temp directory
        self._cleanup_temp_dir()
        
        # Handle window close
        self.protocol("WM_DELETE_WINDOW", self._on_close)
    
    def _build_ui(self):
        """Build the user interface."""
        # Configure ttk styles using Theme manager
        self.style = Theme.configure_style(self)
        
        # Grid configuration for responsiveness
        # Row 0: Title
        # Row 1: Center frame (expandable, minimum height) - mods list
        # Row 2: Log panel (fixed size, but can shrink)
        # Row 3: Bottom frame (fixed size) - buttons
        self.grid_rowconfigure(0, weight=0)  # Title - fixed size
        self.grid_rowconfigure(1, weight=1, minsize=450)  # Center section - expandable with larger minimum
        self.grid_rowconfigure(2, weight=0, minsize=150)  # Log panel - fixed but can shrink
        self.grid_rowconfigure(3, weight=0)  # Bottom section
        self.grid_columnconfigure(0, weight=1)
        
        # Title at the top
        self._build_title()
        
        # Center frame (mods and controls)
        self._build_center_frame()
        
        # Log panel - fixed height to prevent it from taking too much space
        self.log_panel = LogPanel(self)
        self.log_panel.frame.grid(row=2, column=0, sticky="ew", padx=12, pady=(8, 12))
        
        # Bottom frame (buttons: Downloads, Configuration, Exit)
        self._build_bottom_frame()
        
        # Force background color application after all widgets are created
        self._apply_background_colors()
    
    def _build_title(self):
        """Build the title label at the top."""
        title_frame = tk.Frame(self, bg=Theme.BG_COLOR)
        title_frame.grid(row=0, column=0, sticky="ew", padx=12, pady=(12, 8))
        
        title_label = tk.Label(
            title_frame,
            text="IE:VR Mod Manager 1.1",
            font=(Theme.FONT_FAMILY, 18, "bold"),
            bg=Theme.BG_COLOR,
            fg=Theme.TEXT_COLOR
        )
        title_label.pack()
    
    
    def _open_downloads_window(self):
        """Open the downloads window."""
        DownloadsWindow(self)
    
    def _open_config_paths_window(self):
        """Open the configuration paths window."""
        ConfigPathsWindow(
            self,
            self.game_path,
            self.cfgbin_path,
            self.violacli_path,
            self.tmp_dir,
            self._save_config
        )
    
    def _build_center_frame(self):
        """Build the center frame with mods list and controls."""
        frm_center = tk.Frame(self, bg=Theme.BG_COLOR, padx=12, pady=12)
        frm_center.grid(row=1, column=0, sticky="nsew", padx=12, pady=(0, 8))
        # Ensure center frame expands and contracts properly
        frm_center.grid_rowconfigure(0, weight=1, minsize=200)
        frm_center.grid_columnconfigure(0, weight=3, minsize=400)
        frm_center.grid_columnconfigure(1, weight=1, minsize=180)
        
        # Mods panel (left)
        frm_mods_apply = tk.Frame(frm_center, bg=Theme.BG_COLOR)
        frm_mods_apply.grid(row=0, column=0, sticky="nsew", padx=(0, 8))
        frm_mods_apply.grid_rowconfigure(0, weight=1)
        frm_mods_apply.grid_columnconfigure(0, weight=1)
        
        mods_frame = tk.Frame(frm_mods_apply, bg=Theme.BG_COLOR)
        mods_frame.grid(row=0, column=0, sticky="nsew")
        mods_frame.grid_rowconfigure(0, weight=1)
        mods_frame.grid_columnconfigure(0, weight=1)
        
        self.mods_panel = ModsPanel(mods_frame, on_double_click=self._on_mod_double_click)
        self.mods_panel.frame.grid(row=0, column=0, sticky="nsew")
        
        # Quick action buttons between mods table and apply button
        quick_actions_frame = tk.Frame(frm_mods_apply, bg=Theme.BG_COLOR)
        quick_actions_frame.grid(row=1, column=0, sticky="ew", pady=(8, 8))
        quick_actions_frame.grid_columnconfigure(0, weight=1)
        quick_actions_frame.grid_columnconfigure(1, weight=1)
        
        scan_btn = ttk.Button(quick_actions_frame, text="🔍 Scan Mods", 
                             command=self.scan_mods, style="Primary.TButton")
        scan_btn.grid(row=0, column=0, sticky="ew", padx=(0, 4))
        
        open_folder_btn = ttk.Button(quick_actions_frame, text="📂 Open Mods Folder", 
                                     command=self.open_mods_folder, style="Primary.TButton")
        open_folder_btn.grid(row=0, column=1, sticky="ew", padx=(4, 0))
        
        # Apply button with better styling
        apply_btn = ttk.Button(frm_mods_apply, text="✓ Apply Changes", 
                              command=self.apply_mods, style="Accent.TButton")
        apply_btn.grid(row=2, column=0, sticky="ew", pady=(0, 0))
        # Change cursor to hand on hover
        apply_btn.bind("<Enter>", lambda e: apply_btn.config(cursor="hand2"))
        apply_btn.bind("<Leave>", lambda e: apply_btn.config(cursor=""))
        
        # Controls panel (right) - Mod management actions
        ctrl_frame = ttk.LabelFrame(frm_center, text="Mod Actions", padding=10)
        ctrl_frame.grid(row=0, column=1, sticky="nsew")
        ctrl_frame.columnconfigure(0, weight=1)
        ctrl_frame.grid_rowconfigure(0, weight=0, minsize=50)
        
        # Group buttons visually - only mod management buttons here
        buttons_group1 = [
            ("⬆️ Move Up", self.move_up),
            ("⬇️ Move Down", self.move_down)
        ]
        
        buttons_group2 = [
            ("✅ Enable All", self.enable_all),
            ("❌ Disable All", self.disable_all)
        ]
        
        row_idx = 0
        for group in [buttons_group1, buttons_group2]:
            # Add separator if not first group
            if row_idx > 0:
                separator = ttk.Separator(ctrl_frame, orient="horizontal")
                separator.grid(row=row_idx, column=0, sticky="ew", pady=8)
                row_idx += 1
            
            for text, cmd in group:
                btn = ttk.Button(ctrl_frame, text=text, command=cmd, 
                               style="Primary.TButton", width=18)
                btn.grid(row=row_idx, column=0, sticky="ew", pady=4)
                row_idx += 1
        
        # Add flexible space at bottom to keep buttons at top
        ctrl_frame.grid_rowconfigure(row_idx, weight=1)
    
    def _build_bottom_frame(self):
        """Build the bottom frame with Downloads, Configuration, and Exit buttons."""
        frm_bottom = tk.Frame(self, bg=Theme.BG_COLOR, padx=12, pady=12)
        frm_bottom.grid(row=3, column=0, sticky="ew", padx=12, pady=(0, 12))
        frm_bottom.grid_columnconfigure(2, weight=1)  # Expand middle column to push Exit to right
        
        # Downloads button (left side)
        downloads_btn = ttk.Button(
            frm_bottom,
            text="📥 Downloads",
            command=self._open_downloads_window,
            style="Primary.TButton",
            width=20
        )
        downloads_btn.grid(row=0, column=0, sticky="w", padx=(0, 6))
        
        # Configuration button (left side)
        config_btn = ttk.Button(
            frm_bottom,
            text="⚙️ Configuration",
            command=self._open_config_paths_window,
            style="Primary.TButton",
            width=20
        )
        config_btn.grid(row=0, column=1, sticky="w", padx=(6, 0))
        
        # Exit button (right side)
        exit_btn = ttk.Button(frm_bottom, text="✕ Exit", command=self._on_close,
                             style="Primary.TButton", width=15)
        exit_btn.grid(row=0, column=2, sticky="e")
    
    def _apply_background_colors(self):
        """Force application of background colors to all widgets."""
        # Update styles first - ensure all ttk styles have background
        self.update_idletasks()
        
        # Re-configure styles to ensure they're applied
        Theme._configure_frame_style(self.style)
        Theme._configure_labelframe_style(self.style)
        Theme._configure_label_style(self.style)
        
        # Configure separator
        Theme._safe_configure(self.style, "TSeparator", background=Theme.BORDER_COLOR)
        
        # Refresh style mappings to ensure dark theme
        Theme.refresh_style_mappings(self.style)
        
        # Apply background to all widgets recursively (both tk and ttk)
        Theme.apply_background_colors(self, style=self.style)
        
        # Force update after applying colors
        self.update_idletasks()
        
        # Additional pass: explicitly configure LabelFrame widgets
        # Use after_idle to ensure this runs after all widgets are fully rendered
        self.after_idle(self._force_labelframe_styles)
    
    def _force_labelframe_styles(self):
        """Force LabelFrame widgets to use dark theme."""
        from tkinter import ttk
        
        def apply_style_safe(widget, style_name):
            """Safely apply style to widget."""
            try:
                widget.configure(style=style_name)
                return True
            except Exception:
                return False
        
        def configure_widgets(widget):
            """Recursively configure ttk widgets with styles."""
            if isinstance(widget, ttk.LabelFrame):
                apply_style_safe(widget, "TLabelFrame")
                # Configure child frames
                for child in widget.winfo_children():
                    if isinstance(child, ttk.Frame):
                        apply_style_safe(child, "TFrame")
            elif isinstance(widget, ttk.Frame):
                apply_style_safe(widget, "TFrame")
            
            # Recursively process children
            try:
                for child in widget.winfo_children():
                    configure_widgets(child)
            except Exception:
                pass
        
        configure_widgets(self)
        self.update_idletasks()
        
        # One more pass after a short delay to catch any widgets created later
        self.after(100, lambda: configure_widgets(self))
    
    def _setup_callbacks(self):
        """Setup trace callbacks for auto-saving."""
        self.game_path.trace_add("write", lambda *args: self._save_config())
        self.cfgbin_path.trace_add("write", lambda *args: self._save_config())
        self.violacli_path.trace_add("write", lambda *args: self._save_config())
        self.tmp_dir.trace_add("write", lambda *args: self._save_config())
    
    # ---------- Browse helpers ----------
    def browse_game(self):
        """Browse for game directory."""
        path = filedialog.askdirectory(title="Select the game root folder")
        if path:
            self.game_path.set(os.path.abspath(path))
    
    def browse_cfgbin(self):
        """Browse for cpk_list.cfg.bin file."""
        path = filedialog.askopenfilename(
            title="Select cpk_list.cfg.bin",
            filetypes=[("cfg.bin", "*.cfg.bin"), ("All", "*.*")]
        )
        if path:
            self.cfgbin_path.set(os.path.abspath(path))
    
    def browse_violacli(self):
        """Browse for Viola.CLI-Portable.exe file."""
        path = filedialog.askopenfilename(
            title="Select violacli.exe",
            filetypes=[("exe", "*.exe"), ("All", "*.*")]
        )
        if path:
            self.violacli_path.set(os.path.abspath(path))
    
    def open_mods_folder(self):
        """Open the mods folder in file explorer."""
        path = os.path.abspath(DEFAULT_MODS_DIR)
        if os.path.exists(path):
            os.startfile(path)
        else:
            messagebox.showinfo("Info", f"{path} does not exist")
    
    # ---------- Mod management ----------
    def scan_mods(self):
        """Scan for mods and refresh the display."""
        self.mod_entries = self.mod_manager.scan_mods(
            saved_mods=self.saved_mods,
            existing_entries=self.mod_entries
        )
        
        # Setup enabled change callbacks
        for me in self.mod_entries:
            me.enabled.trace_add("write", self._make_mod_enabled_trace(me))
        
        self.mods_panel.set_mod_entries(self.mod_entries)
        self._save_config()
    
    def _make_mod_enabled_trace(self, mod_entry: ModEntry):
        """Create a trace callback for mod enabled state changes."""
        def _on_change(*args):
            try:
                self.mods_panel.update_mod_display(mod_entry)
            except Exception:
                pass
            self._save_config()
        return _on_change
    
    def _on_mod_double_click(self, iid: str):
        """Handle double-click on mod entry."""
        idx = self.mods_panel.get_selected_index()
        if idx is not None:
            me = self.mod_entries[idx]
            me.enabled.set(not me.enabled.get())
    
    def move_up(self):
        """Move selected mod up in priority."""
        idx = self.mods_panel.get_selected_index()
        if idx is None or idx <= 0:
            return
        
        self.mod_entries[idx - 1], self.mod_entries[idx] = (
            self.mod_entries[idx], self.mod_entries[idx - 1]
        )
        self.mods_panel.set_mod_entries(self.mod_entries)
        self.mods_panel.select_mod(self.mod_entries[idx - 1])
        self._save_config()
    
    def move_down(self):
        """Move selected mod down in priority."""
        idx = self.mods_panel.get_selected_index()
        if idx is None or idx >= len(self.mod_entries) - 1:
            return
        
        self.mod_entries[idx + 1], self.mod_entries[idx] = (
            self.mod_entries[idx], self.mod_entries[idx + 1]
        )
        self.mods_panel.set_mod_entries(self.mod_entries)
        self.mods_panel.select_mod(self.mod_entries[idx + 1])
        self._save_config()
    
    def enable_all(self):
        """Enable all mods."""
        for me in self.mod_entries:
            me.enabled.set(True)
        self._save_config()
    
    def disable_all(self):
        """Disable all mods."""
        for me in self.mod_entries:
            me.enabled.set(False)
        self._save_config()
    
    # ---------- Apply mods ----------
    def apply_mods(self):
        """Apply enabled mods to the game."""
        if self.viola.is_running():
            messagebox.showinfo("Info", "A process is already running.")
            return
        
        # Validate paths
        game_path = self.game_path.get().strip()
        cfgbin = self.cfgbin_path.get().strip()
        violacli = self.violacli_path.get().strip()
        
        if not game_path or not os.path.isdir(game_path):
            messagebox.showerror("Error", "Invalid game path.")
            return
        if not cfgbin or not os.path.exists(cfgbin):
            messagebox.showerror("Error", "Invalid cpk_list.cfg.bin path.")
            return
        if not violacli or not os.path.exists(violacli):
            messagebox.showerror("Error", "violacli.exe not found. Please configure its path.")
            return
        
        # Get enabled mods
        enabled_mods = self.mod_manager.get_enabled_mods(self.mod_entries)
        
        # If no mods enabled, restore original cpk_list.cfg.bin
        if not enabled_mods:
            target_cpk = os.path.join(game_path, "data", "cpk_list.cfg.bin")
            try:
                os.makedirs(os.path.dirname(target_cpk), exist_ok=True)
                shutil.copy2(cfgbin, target_cpk)
                self._log("CHANGES APPLIED!! No mods selected.", "success")
            except Exception as e:
                self._log(f"Error applying changes: {e}", "error")
            return
        
        # Merge mods
        tmp_root = os.path.abspath(
            self.tmp_dir.get() if isinstance(self.tmp_dir, tk.StringVar) else DEFAULT_TMP_DIR
        )
        os.makedirs(tmp_root, exist_ok=True)
        
        # Run merge in background thread
        thread = threading.Thread(
            target=self._run_merge_and_copy,
            args=(violacli, cfgbin, enabled_mods, tmp_root, game_path),
            daemon=True
        )
        thread.start()
    
    def _run_merge_and_copy(self, violacli: str, cfgbin: str, mod_paths: list[str],
                           tmp_root: str, game_path: str):
        """Run merge and copy operations in background thread."""
        try:
            # Merge mods
            success = self.viola.merge_mods(violacli, cfgbin, mod_paths, tmp_root)
            
            if not success:
                self._log("violacli returned error; aborting copy.", "error")
                return
            
            # Copy merged files
            tmp_data = os.path.join(tmp_root, "data")
            dest_data = os.path.join(game_path, "data")
            
            if self.viola.copy_merged_files(tmp_data, dest_data):
                self.viola.cleanup_temp(tmp_data)
                self._log("MODS APPLIED!!", "success")
            else:
                self._log("Failed to copy merged files.", "error")
                
        except Exception as e:
            self._log(f"Unexpected error: {e}", "error")
    
    # ---------- Configuration ----------
    def _load_config(self):
        """Load configuration from file."""
        config = self.config_manager.load()
        
        self.game_path.set(config.get("game_path", ""))
        self.cfgbin_path.set(config.get("cfgbin_path", ""))
        self.violacli_path.set(config.get("violacli_path", ""))
        self.tmp_dir.set(config.get("tmp_dir", DEFAULT_TMP_DIR))
        self.saved_mods = config.get("mods", [])
        
        self._log(f"Configuration loaded from {self.config_manager.config_path}", "info")
    
    def _save_config(self):
        """Save configuration to file."""
        success = self.config_manager.save(
            game_path=self.game_path.get(),
            cfgbin_path=self.cfgbin_path.get(),
            violacli_path=self.violacli_path.get(),
            tmp_dir=self.tmp_dir.get(),
            mod_entries=self.mod_entries
        )
        
        if success:
            self._log(f"Configuration saved to {self.config_manager.config_path}", "success")
        else:
            messagebox.showerror("Error", "Could not save configuration.")
            self._log("Could not save configuration.", "error")
    
    # ---------- Utilities ----------
    def _log(self, text: str, level: str = "info"):
        """Log a message to the log panel."""
        def append():
            self.log_panel.log(text, level)
        self.after(0, append)
    
    def _cleanup_temp_dir(self):
        """Clean up temporary directory on startup."""
        tmp_root = (
            self.tmp_dir.get() 
            if isinstance(self.tmp_dir, tk.StringVar) 
            else DEFAULT_TMP_DIR
        )
        if os.path.isdir(tmp_root):
            try:
                shutil.rmtree(tmp_root)
            except Exception as e:
                print(f"Could not clean temporary folder {tmp_root}: {e}")
        os.makedirs(tmp_root, exist_ok=True)
    
    def _on_close(self):
        """Handle window close event."""
        if self.viola.is_running():
            if not messagebox.askyesno(
                "Exit", 
                "There is an operation in progress. Are you sure you want to exit?"
            ):
                return
            self.viola.stop()
        
        self._save_config()
        self.destroy()

