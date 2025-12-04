"""Theme and style configuration for the application."""
import tkinter as tk
from tkinter import ttk


class Theme:
    """Application theme configuration."""
    
    # Color scheme
    BG_COLOR = "#1e1e1e"
    ACCENT_COLOR = "#0078d4"
    ACCENT_HOVER = "#005a9e"
    SUCCESS_COLOR = "#4ec9b0"
    ERROR_COLOR = "#f48771"
    INFO_COLOR = "#569cd6"
    WARNING_COLOR = "#dcdcaa"
    TEXT_COLOR = "#d4d4d4"
    BORDER_COLOR = "#3e3e3e"
    SELECTED_BG = "#2d2d2d"
    TIMESTAMP_COLOR = "#808080"
    
    # Fonts
    FONT_FAMILY = "Segoe UI"
    FONT_MONO = "Consolas"
    FONT_SIZE_NORMAL = 9
    FONT_SIZE_LARGE = 10
    FONT_SIZE_TITLE = 14
    
    @staticmethod
    def _safe_configure(style: ttk.Style, style_name: str, **kwargs):
        """
        Safely configure a style with fallback.
        
        Args:
            style: ttk.Style instance
            style_name: Name of the style to configure
            **kwargs: Style configuration options
        """
        try:
            style.configure(style_name, **kwargs)
        except Exception:
            # Fallback: try minimal configuration
            try:
                minimal_kwargs = {k: v for k, v in kwargs.items() 
                                if k in ('background', 'foreground')}
                if minimal_kwargs:
                    style.configure(style_name, **minimal_kwargs)
            except Exception:
                pass
    
    @staticmethod
    def _safe_map(style: ttk.Style, style_name: str, **kwargs):
        """
        Safely map style states.
        
        Args:
            style: ttk.Style instance
            style_name: Name of the style to map
            **kwargs: Style mapping options
        """
        try:
            style.map(style_name, **kwargs)
        except Exception:
            pass
    
    @staticmethod
    def _get_dark_background_map():
        """Get common dark background mapping for all states."""
        return {
            "background": [("active", Theme.BG_COLOR),
                          ("disabled", Theme.BG_COLOR),
                          ("!disabled", Theme.BG_COLOR),
                          ("", Theme.BG_COLOR)],
            "lightcolor": [("", Theme.BG_COLOR)],
            "darkcolor": [("", Theme.BG_COLOR)],
            "troughcolor": [("", Theme.BG_COLOR)]
        }
    
    @staticmethod
    def configure_style(root) -> ttk.Style:
        """
        Configure ttk styles for dark mode theme.
        
        Args:
            root: Root window or toplevel window
            
        Returns:
            Configured ttk.Style instance
        """
        style = ttk.Style(root)
        
        # Try to use a theme that supports dark mode better
        try:
            style.theme_use("alt")
        except Exception:
            try:
                style.theme_use("clam")
            except Exception:
                pass
        
        # Base frame style
        Theme._configure_frame_style(style)
        
        # Label style
        Theme._configure_label_style(style)
        
        # Entry style
        Theme._configure_entry_style(style)
        
        # Button styles
        Theme._configure_button_styles(style)
        
        # Treeview style
        Theme._configure_treeview_style(style)
        
        # LabelFrame style
        Theme._configure_labelframe_style(style)
        
        # Scrollbar style
        Theme._configure_scrollbar_style(style)
        
        return style
    
    @staticmethod
    def _configure_frame_style(style: ttk.Style):
        """Configure TFrame style."""
        Theme._safe_configure(style, "TFrame",
                            background=Theme.BG_COLOR,
                            borderwidth=0,
                            troughcolor=Theme.BG_COLOR,
                            lightcolor=Theme.BG_COLOR,
                            darkcolor=Theme.BG_COLOR)
        Theme._safe_map(style, "TFrame", **Theme._get_dark_background_map())
    
    @staticmethod
    def _configure_label_style(style: ttk.Style):
        """Configure TLabel style."""
        Theme._safe_configure(style, "TLabel",
                            background=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_NORMAL))
        Theme._safe_map(style, "TLabel",
                       background=[("", Theme.BG_COLOR), ("active", Theme.BG_COLOR)],
                       foreground=[("", Theme.TEXT_COLOR)])
    
    @staticmethod
    def _configure_entry_style(style: ttk.Style):
        """Configure TEntry style."""
        Theme._safe_configure(style, "TEntry",
                            fieldbackground=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            borderwidth=1,
                            relief="solid",
                            padding=4,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_NORMAL),
                            bordercolor=Theme.BORDER_COLOR)
        Theme._safe_map(style, "TEntry",
                       bordercolor=[("focus", Theme.ACCENT_COLOR), ("", Theme.BORDER_COLOR)],
                       fieldbackground=[("", Theme.BG_COLOR)],
                       lightcolor=[("", Theme.BG_COLOR)],
                       darkcolor=[("", Theme.BG_COLOR)])
    
    @staticmethod
    def _configure_button_styles(style: ttk.Style):
        """Configure button styles."""
        # Accent button (primary action)
        Theme._safe_configure(style, "Accent.TButton",
                            foreground="white",
                            background=Theme.ACCENT_COLOR,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE, "bold"),
                            padding=10,
                            borderwidth=0,
                            focuscolor="none")
        Theme._safe_map(style, "Accent.TButton",
                       background=[("active", Theme.ACCENT_HOVER), ("pressed", "#004578")],
                       foreground=[("disabled", "#ccc")])
        
        # Primary button (secondary action)
        Theme._safe_configure(style, "Primary.TButton",
                            foreground=Theme.TEXT_COLOR,
                            background=Theme.BG_COLOR,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_NORMAL),
                            padding=8,
                            borderwidth=1,
                            relief="solid",
                            bordercolor=Theme.BORDER_COLOR)
        Theme._safe_map(style, "Primary.TButton",
                       background=[("active", Theme.SELECTED_BG), ("", Theme.BG_COLOR)],
                       bordercolor=[("active", Theme.ACCENT_COLOR), ("", Theme.BORDER_COLOR)],
                       lightcolor=[("", Theme.BG_COLOR)],
                       darkcolor=[("", Theme.BG_COLOR)])
    
    @staticmethod
    def _configure_treeview_style(style: ttk.Style):
        """Configure Treeview style."""
        Theme._safe_configure(style, "Treeview",
                            rowheight=32,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE),
                            background=Theme.BG_COLOR,
                            fieldbackground=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            borderwidth=1,
                            relief="solid",
                            bordercolor=Theme.BORDER_COLOR)
        Theme._safe_configure(style, "Treeview.Heading",
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE, "bold"),
                            background=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            borderwidth=1,
                            relief="solid",
                            bordercolor=Theme.BORDER_COLOR)
        Theme._safe_map(style, "Treeview",
                       background=[("selected", Theme.SELECTED_BG), ("", Theme.BG_COLOR)],
                       fieldbackground=[("", Theme.BG_COLOR)],
                       bordercolor=[("", Theme.BORDER_COLOR)])
        Theme._safe_map(style, "Treeview.Heading",
                       background=[("", Theme.BG_COLOR)],
                       foreground=[("", Theme.TEXT_COLOR)],
                       bordercolor=[("", Theme.BORDER_COLOR)])
    
    @staticmethod
    def _configure_labelframe_style(style: ttk.Style):
        """Configure TLabelFrame style."""
        Theme._safe_configure(style, "TLabelFrame",
                            background=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE, "bold"),
                            borderwidth=2,
                            relief="solid",
                            bordercolor=Theme.BORDER_COLOR,
                            lightcolor=Theme.BG_COLOR,
                            darkcolor=Theme.BG_COLOR,
                            troughcolor=Theme.BG_COLOR)
        Theme._safe_configure(style, "TLabelFrame.Label",
                            background=Theme.BG_COLOR,
                            foreground=Theme.TEXT_COLOR,
                            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_LARGE, "bold"))
        map_data = Theme._get_dark_background_map()
        map_data["bordercolor"] = [("", Theme.BORDER_COLOR)]
        Theme._safe_map(style, "TLabelFrame", **map_data)
    
    @staticmethod
    def _configure_scrollbar_style(style: ttk.Style):
        """Configure TScrollbar style."""
        Theme._safe_configure(style, "TScrollbar",
                            background=Theme.BORDER_COLOR,
                            troughcolor=Theme.BG_COLOR,
                            borderwidth=1,
                            arrowcolor=Theme.TEXT_COLOR,
                            darkcolor=Theme.BORDER_COLOR,
                            lightcolor=Theme.BORDER_COLOR)
        Theme._safe_map(style, "TScrollbar",
                       background=[("active", Theme.SELECTED_BG), ("", Theme.BORDER_COLOR)],
                       troughcolor=[("", Theme.BG_COLOR)],
                       darkcolor=[("", Theme.BORDER_COLOR)],
                       lightcolor=[("", Theme.BORDER_COLOR)])
    
    @staticmethod
    def refresh_style_mappings(style: ttk.Style):
        """
        Refresh style mappings to ensure dark theme is applied.
        
        Args:
            style: ttk.Style instance to refresh
        """
        Theme._safe_map(style, "TLabelFrame", **Theme._get_dark_background_map())
        Theme._safe_map(style, "TFrame", **Theme._get_dark_background_map())
    
    @staticmethod
    def apply_background_colors(widget, bg_color: str = None, style: ttk.Style = None):
        """
        Recursively apply background color to widget and children.
        
        Args:
            widget: Widget to apply colors to
            bg_color: Background color (defaults to Theme.BG_COLOR)
            style: ttk.Style instance to configure ttk widgets
        """
        import tkinter as tk
        from tkinter import ttk
        
        if bg_color is None:
            bg_color = Theme.BG_COLOR
        
        def apply_recursive(w):
            try:
                # Handle tkinter widgets
                if isinstance(w, (tk.Frame, tk.Label, tk.Button)):
                    if isinstance(w, tk.Frame):
                        w.configure(bg=bg_color, highlightthickness=0)
                    elif isinstance(w, tk.Label):
                        if w.cget("cursor") != "hand2":
                            w.configure(bg=bg_color, highlightthickness=0)
                
                # Handle ttk widgets - force style application
                elif isinstance(w, ttk.Frame):
                    try:
                        if style:
                            w.configure(style="TFrame")
                    except Exception:
                        pass
                elif isinstance(w, ttk.LabelFrame):
                    try:
                        if style:
                            w.configure(style="TLabelFrame")
                    except Exception:
                        pass
                elif isinstance(w, ttk.Label):
                    try:
                        if style:
                            w.configure(style="TLabel")
                    except Exception:
                        pass
                elif isinstance(w, ttk.Separator):
                    try:
                        if style:
                            style.configure("TSeparator", background=Theme.BORDER_COLOR)
                    except Exception:
                        pass
            except Exception:
                pass
            
            try:
                for child in w.winfo_children():
                    apply_recursive(child)
            except Exception:
                pass
        
        apply_recursive(widget)

