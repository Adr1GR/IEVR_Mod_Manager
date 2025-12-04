"""Downloads window for displaying download links."""
import tkinter as tk
from tkinter import ttk
import webbrowser
from ..config import VIOLA_RELEASE_URL, CPK_LIST_URL
from .base_window import BaseWindow
from .theme import Theme


class DownloadsWindow(BaseWindow):
    """Window for displaying download links."""
    
    def __init__(self, parent):
        """
        Initialize the downloads window.
        
        Args:
            parent: Parent window
        """
        super().__init__(parent, "Downloads", "600x300")
        
        # Main frame
        main_frame = self.create_main_frame()
        main_frame.columnconfigure(0, weight=1)
        
        # Title
        self.create_title_label(main_frame, "Download Links")
        
        # Links container
        links_frame = tk.Frame(main_frame, bg=Theme.BG_COLOR)
        links_frame.grid(row=1, column=0, sticky="ew", pady=10)
        links_frame.columnconfigure(0, weight=1)
        
        self._build_downloads_content(links_frame)
        
        # Close button
        close_btn = ttk.Button(
            main_frame,
            text="Close",
            command=self.destroy,
            style="Primary.TButton",
            width=15
        )
        close_btn.grid(row=2, column=0, pady=(20, 0))
    
    def _build_downloads_content(self, parent):
        """Build the downloads links content."""
        link_font = (Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE)
        link_fg = Theme.ACCENT_COLOR
        link_hover = Theme.ACCENT_HOVER
        
        def make_link(label, url, row_idx):
            container = tk.Frame(parent, bg=Theme.BG_COLOR, highlightthickness=0)
            container.grid(row=row_idx, column=0, sticky="w", pady=8)
            
            icon = "🔗"
            link_label = tk.Label(
                container,
                text=f"{icon} {label}",
                fg=link_fg,
                cursor="hand2",
                font=link_font,
                bg=Theme.BG_COLOR,
                anchor="w",
                highlightthickness=0
            )
            link_label.pack(side="left")
            
            def on_enter(e):
                link_label.config(fg=link_hover, font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE, "underline"))
            
            def on_leave(e):
                link_label.config(fg=link_fg, font=link_font)
            
            link_label.bind("<Button-1>", lambda e: webbrowser.open_new(url))
            link_label.bind("<Enter>", on_enter)
            link_label.bind("<Leave>", on_leave)
        
        make_link("Download Viola.CLI-Portable.exe", VIOLA_RELEASE_URL, 0)
        make_link("Download cpk_list.cfg.bin", CPK_LIST_URL, 1)

