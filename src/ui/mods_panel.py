"""Mods list panel component."""
import tkinter as tk
from tkinter import ttk
from typing import List, Optional, Callable
from ..models import ModEntry
from .theme import Theme


class ModsPanel:
    """Panel for displaying and managing mods list."""
    
    def __init__(self, parent, on_double_click: Optional[Callable] = None):
        """
        Initialize the mods panel.
        
        Args:
            parent: Parent widget
            on_double_click: Optional callback for double-click events
        """
        self.on_double_click = on_double_click
        self.mod_entries: List[ModEntry] = []
        
        # Create frame with better styling
        # Don't specify style here - it will be applied by MainWindow._apply_background_colors()
        self.frame = ttk.LabelFrame(
            parent, 
            text="Mods (Priority Order)", 
            padding=8
        )
        self.frame.grid_rowconfigure(0, weight=1)
        self.frame.grid_columnconfigure(0, weight=1)
        
        # Container for treeview and scrollbar
        tree_container = ttk.Frame(self.frame)
        tree_container.grid(row=0, column=0, sticky="nsew")
        tree_container.grid_rowconfigure(0, weight=1)
        tree_container.grid_columnconfigure(0, weight=1)
        
        # Create treeview with better styling
        self.tree = ttk.Treeview(
            tree_container,
            columns=("enabled", "display_name", "mod_version", "game_version", "author"),
            show="headings",
            selectmode="browse"
        )
        
        # Configure columns with better widths
        self.tree.heading("enabled", text="✓")
        self.tree.heading("display_name", text="Mod Name")
        self.tree.heading("mod_version", text="Version")
        self.tree.heading("game_version", text="Game Ver.")
        self.tree.heading("author", text="Author")
        
        self.tree.column("enabled", width=50, anchor="center", stretch=False)
        self.tree.column("display_name", width=250, anchor="w", minwidth=150)
        self.tree.column("mod_version", width=100, anchor="center", minwidth=80)
        self.tree.column("game_version", width=100, anchor="center", minwidth=80)
        self.tree.column("author", width=180, anchor="w", minwidth=120)
        
        self.tree.grid(row=0, column=0, sticky="nsew")
        
        # Scrollbar with better styling
        scrollbar = ttk.Scrollbar(tree_container, orient="vertical", command=self.tree.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.tree.configure(yscrollcommand=scrollbar.set)
        
        # Bind events
        if on_double_click:
            self.tree.bind("<Double-1>", self._on_double_click)
        
        # Bind single click for toggle
        self.tree.bind("<Button-1>", self._on_single_click)
    
    def set_mod_entries(self, mod_entries: List[ModEntry]):
        """
        Set the mod entries to display.
        
        Args:
            mod_entries: List of ModEntry objects
        """
        self.mod_entries = mod_entries
        self._refresh_display()
    
    def _refresh_display(self):
        """Refresh the treeview display."""
        # Clear existing items
        for item in self.tree.get_children():
            self.tree.delete(item)
        
        # Add mod entries with visual indicators
        for idx, me in enumerate(self.mod_entries):
            enabled_icon = "✓" if me.enabled.get() else "✗"
            
            # Determine tags based on enabled state and row index
            tags_list = [f"enabled_{me.enabled.get()}"]
            if idx % 2 == 0:
                tags_list.append("even")
            else:
                tags_list.append("odd")
            
            item_id = self.tree.insert("", "end", iid=me.name, values=(
                enabled_icon,
                me.display_name,
                me.mod_version or "—",
                me.game_version or "—",
                me.author or "—"
            ), tags=tuple(tags_list))
        
        # Configure tag colors
        # Enabled/disabled colors (foreground) - these apply to all columns
        self.tree.tag_configure("enabled_True", foreground=Theme.SUCCESS_COLOR)
        self.tree.tag_configure("enabled_False", foreground=Theme.ERROR_COLOR)
        # Row background colors - using same background color as app
        self.tree.tag_configure("even", background=Theme.BG_COLOR)
        self.tree.tag_configure("odd", background=Theme.BG_COLOR)
        
        # Selected state tags - maintain foreground colors with selection background
        # These tags MUST have foreground color set to override the default selection style
        self.tree.tag_configure("selected_enabled_True", foreground=Theme.SUCCESS_COLOR, 
                               background=Theme.SELECTED_BG)
        self.tree.tag_configure("selected_enabled_False", foreground=Theme.ERROR_COLOR, 
                               background=Theme.SELECTED_BG)
        # Row tags for selected state - only set background
        self.tree.tag_configure("selected_even", background=Theme.SELECTED_BG)
        self.tree.tag_configure("selected_odd", background=Theme.SELECTED_BG)
        
        # Bind selection event to update tags
        self.tree.bind("<<TreeviewSelect>>", self._on_selection_change)
        
        # After items are inserted, update selection tags if any item is selected
        selected_items = self.tree.selection()
        if selected_items:
            self.tree.after_idle(lambda: self._on_selection_change(None))
    
    def update_mod_display(self, mod_entry: ModEntry):
        """
        Update the display for a specific mod entry.
        
        Args:
            mod_entry: ModEntry to update
        """
        if mod_entry.name in self.tree.get_children(""):
            enabled_icon = "✓" if mod_entry.enabled.get() else "✗"
            self.tree.set(mod_entry.name, "enabled", enabled_icon)
            
            # Update tags by triggering selection change handler which will rebuild all tags correctly
            # This ensures the enabled tag is updated and selection state is maintained
            self.tree.after_idle(lambda: self._on_selection_change(None))
    
    def get_selected_index(self) -> Optional[int]:
        """
        Get the index of the selected mod.
        
        Returns:
            Index of selected mod, or None if none selected
        """
        selection = self.tree.selection()
        if not selection:
            return None
        
        iid = selection[0]
        for idx, me in enumerate(self.mod_entries):
            if me.name == iid:
                return idx
        
        return None
    
    def select_mod(self, mod_entry: ModEntry):
        """
        Select a mod in the treeview.
        
        Args:
            mod_entry: ModEntry to select
        """
        self.tree.selection_set(mod_entry.name)
    
    def _on_single_click(self, event):
        """Handle single click event - toggle enabled state if clicking on enabled column."""
        region = self.tree.identify_region(event.x, event.y)
        if region == "cell":
            column = self.tree.identify_column(event.x)
            if column == "#1":  # Enabled column
                iid = self.tree.identify_row(event.y)
                if iid and self.on_double_click:
                    self.tree.selection_set(iid)
                    self.on_double_click(iid)
    
    def _on_selection_change(self, event):
        """Handle selection change to maintain foreground colors."""
        # Get all items
        all_items = self.tree.get_children()
        
        # Update tags for all items
        for item_id in all_items:
            # Find the mod entry to get enabled state
            enabled_tag = None
            row_tag = None
            
            for idx, me in enumerate(self.mod_entries):
                if me.name == item_id:
                    enabled_tag = f"enabled_{me.enabled.get()}"
                    row_tag = "even" if idx % 2 == 0 else "odd"
                    break
            
            if not enabled_tag:
                continue
            
            # Check if this item is selected
            if item_id in self.tree.selection():
                # Selected: use selected tags with foreground color
                # Order matters: background tags first, then foreground color tag last (has priority)
                new_tags = [
                    f"selected_{row_tag}",  # Background for selected state
                    f"selected_{enabled_tag}",  # Foreground color + background (this has the color!)
                    enabled_tag  # Ensure foreground color is applied (last = highest priority)
                ]
            else:
                # Not selected: use normal tags
                new_tags = [
                    row_tag,  # Background
                    enabled_tag  # Foreground color
                ]
            
            self.tree.item(item_id, tags=tuple(new_tags))
    
    def _on_double_click(self, event):
        """Handle double-click event."""
        iid = self.tree.identify_row(event.y)
        if not iid:
            return
        
        self.tree.selection_set(iid)
        if self.on_double_click:
            self.on_double_click(iid)

