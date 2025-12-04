"""Window utility functions."""
import tkinter as tk


def center_window(window):
    """
    Center a window on the screen.
    
    Args:
        window: Window to center
    """
    window.update_idletasks()
    width = window.winfo_width()
    height = window.winfo_height()
    x = (window.winfo_screenwidth() // 2) - (width // 2)
    y = (window.winfo_screenheight() // 2) - (height // 2)
    window.geometry(f"{width}x{height}+{x}+{y}")


def create_main_frame(parent: tk.Widget, padx: int = 20, pady: int = 20) -> tk.Frame:
    """
    Create a main frame with standard styling.
    
    Args:
        parent: Parent widget
        padx: Horizontal padding
        pady: Vertical padding
        
    Returns:
        Configured Frame widget
    """
    from .theme import Theme
    
    frame = tk.Frame(parent, bg=Theme.BG_COLOR, padx=padx, pady=pady)
    frame.pack(fill="both", expand=True)
    return frame


def create_title_label(parent: tk.Widget, text: str, row: int = 0, column: int = 0, 
                       columnspan: int = 1, pady: tuple = (0, 20)) -> tk.Label:
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
    from .theme import Theme
    
    label = tk.Label(
        parent,
        text=text,
        font=(Theme.FONT_FAMILY, Theme.FONT_SIZE_TITLE, "bold"),
        bg=Theme.BG_COLOR,
        fg=Theme.TEXT_COLOR
    )
    label.grid(row=row, column=column, columnspan=columnspan, sticky="w", pady=pady)
    return label

