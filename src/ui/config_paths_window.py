"""Configuration paths window for managing paths."""
import os
import tkinter as tk
from tkinter import ttk, filedialog
from ..config import DEFAULT_TMP_DIR
from .base_window import BaseWindow
from .theme import Theme


class ConfigPathsWindow(BaseWindow):
    """Window for managing configuration paths."""
    
    def __init__(self, parent, game_path_var, cfgbin_path_var, violacli_path_var, tmp_dir_var, save_callback):
        """
        Initialize the configuration paths window.
        
        Args:
            parent: Parent window
            game_path_var: StringVar for game path
            cfgbin_path_var: StringVar for cfgbin path
            violacli_path_var: StringVar for violacli path
            tmp_dir_var: StringVar for tmp directory
            save_callback: Callback function to save configuration
        """
        super().__init__(parent, "Configuration", "800x400")
        
        # Store references
        self.game_path_var = game_path_var
        self.cfgbin_path_var = cfgbin_path_var
        self.violacli_path_var = violacli_path_var
        self.tmp_dir_var = tmp_dir_var
        self.save_callback = save_callback
        
        # Main frame
        main_frame = self.create_main_frame()
        main_frame.columnconfigure(1, weight=1)
        
        # Title
        self.create_title_label(main_frame, "Configuration", columnspan=3, pady=(0, 10))
        
        # Info label about auto-save
        info_label = tk.Label(
            main_frame,
            text="Configuration is saved automatically",
            font=(Theme.FONT_FAMILY, 8, "italic"),
            fg=Theme.TIMESTAMP_COLOR,
            bg=Theme.BG_COLOR,
            anchor="w",
            highlightthickness=0
        )
        info_label.grid(row=1, column=0, columnspan=3, sticky="w", pady=(0, 15), padx=4)
        
        # Path rows
        self._build_path_rows(main_frame)
        
        # Buttons frame
        buttons_frame = tk.Frame(main_frame, bg=Theme.BG_COLOR)
        buttons_frame.grid(row=5, column=0, columnspan=3, pady=(20, 0))
        
        close_btn = ttk.Button(
            buttons_frame,
            text="Close",
            command=self.destroy,
            style="Primary.TButton",
            width=15
        )
        close_btn.pack(side="right", padx=(10, 0))
    
    def _build_path_rows(self, parent):
        """Build the path configuration rows."""
        def make_browse_row(label_text, var, browse_cmd, row_idx, icon="📁"):
            # Label with icon
            label_frame = tk.Frame(parent, bg=Theme.BG_COLOR, highlightthickness=0)
            label_frame.grid(row=row_idx, column=0, sticky="w", pady=8, padx=(0, 8))
            ttk.Label(
                label_frame,
                text=f"{icon} {label_text}",
                font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_NORMAL),
                width=22,
                anchor="w"
            ).pack(side="left")
            
            # Entry with better styling
            entry = ttk.Entry(parent, textvariable=var, width=50)
            entry.grid(row=row_idx, column=1, sticky="ew", pady=8, padx=4)
            
            # Browse button with better styling
            btn = ttk.Button(
                parent,
                text="Browse...",
                command=browse_cmd,
                style="Primary.TButton",
                width=12
            )
            btn.grid(row=row_idx, column=2, sticky="ew", pady=8, padx=(4, 0))
        
        make_browse_row("Game path:", self.game_path_var, self.browse_game, 2, "🎮")
        make_browse_row("cpk_list.cfg.bin:", self.cfgbin_path_var, self.browse_cfgbin, 3, "📄")
        make_browse_row("Viola.CLI-Portable.exe:", self.violacli_path_var, self.browse_violacli, 4, "⚙️")
    
    def browse_game(self):
        """Browse for game directory."""
        path = filedialog.askdirectory(title="Select the game root folder")
        if path:
            self.game_path_var.set(os.path.abspath(path))
            if self.save_callback:
                self.save_callback()
    
    def browse_cfgbin(self):
        """Browse for cpk_list.cfg.bin file."""
        path = filedialog.askopenfilename(
            title="Select cpk_list.cfg.bin",
            filetypes=[("cfg.bin", "*.cfg.bin"), ("All", "*.*")]
        )
        if path:
            self.cfgbin_path_var.set(os.path.abspath(path))
            if self.save_callback:
                self.save_callback()
    
    def browse_violacli(self):
        """Browse for Viola.CLI-Portable.exe file."""
        path = filedialog.askopenfilename(
            title="Select violacli.exe",
            filetypes=[("exe", "*.exe"), ("All", "*.*")]
        )
        if path:
            self.violacli_path_var.set(os.path.abspath(path))
            if self.save_callback:
                self.save_callback()

