"""Base window class for common window functionality."""
import tkinter as tk
from tkinter import ttk
from .theme import Theme
from .window_utils import center_window


class BaseWindow(tk.Toplevel):
    """Base class for application windows with common functionality."""
    
    def __init__(self, parent, title: str, 
                 geometry: str = None, resizable: tuple = (False, False)):
        """
        Initialize the base window.
        
        Args:
            parent: Parent window
            title: Window title
            geometry: Window geometry string (widthxheight)
            resizable: Tuple of (width_resizable, height_resizable)
        """
        super().__init__(parent)
        self.title(title)
        
        if geometry:
            self.geometry(geometry)
        
        self.resizable(*resizable)
        
        # Configure window background
        self.configure(bg=Theme.BG_COLOR)
        
        # Configure styles
        self.style = Theme.configure_style(self)
        
        # Center window on screen
        center_window(self)
    
    def create_main_frame(self, padx: int = 20, pady: int = 20) -> tk.Frame:
        """
        Create a main frame with standard styling.
        
        Args:
            padx: Horizontal padding
            pady: Vertical padding
            
        Returns:
            Configured Frame widget
        """
        frame = tk.Frame(self, bg=Theme.BG_COLOR, padx=padx, pady=pady)
        frame.pack(fill="both", expand=True)
        return frame
    
    def create_title_label(self, parent: tk.Widget, text: str, row: int = 0, 
                          column: int = 0, columnspan: int = 1, 
                          pady: tuple = (0, 20)) -> tk.Label:
        """
        Create a title label with standard styling.
        
        Args:
            parent: Parent widget
            text: Label text
            row: Grid row
            column: Grid column
            columnspan: Grid columnspan
            pady: Vertical padding tuple
            
        Returns:
            Configured Label widget
        """
        label = tk.Label(
            parent,
            text=text,
            font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_TITLE, "bold"),
            bg=Theme.BG_COLOR,
            fg=Theme.TEXT_COLOR
        )
        label.grid(row=row, column=column, columnspan=columnspan, sticky="w", pady=pady)
        return label

