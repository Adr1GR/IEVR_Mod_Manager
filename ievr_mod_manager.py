#!/usr/bin/env python3
import os
import sys
import json
import shutil
import subprocess
import threading
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import webbrowser

APP_CONFIG = "config.json"
MODS_DIRNAME = "Mods"
TMP_DIRNAME = "tmp"

if getattr(sys, "frozen", False):
    BASE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(__file__)

CONFIG_PATH = os.path.join(BASE_DIR, APP_CONFIG)
DEFAULT_MODS_DIR = os.path.join(BASE_DIR, MODS_DIRNAME)
DEFAULT_TMP_DIR = os.path.join(BASE_DIR, TMP_DIRNAME)


class ModEntry:
    def __init__(self, name, path, enabled=True):
        self.name = name
        self.path = path
        self.enabled = tk.BooleanVar(value=enabled)


class IEVRModManager(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("IEVR Mod Manager")
        self.geometry("1100x760")

        # Config state
        self.game_path = tk.StringVar()
        self.cfgbin_path = tk.StringVar()
        self.violacli_path = tk.StringVar()
        self.tmp_dir = tk.StringVar(value=DEFAULT_TMP_DIR)
        
        self.saved_mods = []
        self.mod_entries = []

        self._running = False
        self._proc = None
        self._thread = None

        self._build_ui()

        self.game_path.trace_add("write", lambda *args: self._save_config())
        self.cfgbin_path.trace_add("write", lambda *args: self._save_config())
        self.violacli_path.trace_add("write", lambda *args: self._save_config())
        self.tmp_dir.trace_add("write", lambda *args: self._save_config())

        self._load_config()
        self.scan_mods()

    # ---------- UI ----------
    def _build_ui(self):
        self.style = ttk.Style(self)
        self.style.theme_use("clam")

        self.style.configure("Accent.TButton", foreground="white", background="#007acc", font=("Segoe UI", 10, "bold"))
        self.style.map("Accent.TButton",
                       background=[("active", "#005f99")],
                       foreground=[("disabled", "#ccc")])

        # Usar grid para toda la UI principal
        self.grid_rowconfigure(1, weight=1)
        self.grid_columnconfigure(0, weight=1)

        # Top frame for paths
        frm_top = ttk.Frame(self, padding=10)
        frm_top.grid(row=0, column=0, sticky="ew")
        frm_top.columnconfigure(1, weight=1)

        # === DOWNLOAD LINKS ===
        link_font = ("Segoe UI", 9, "underline")
        link_fg = "#23518c"
        link_pad = {"pady": (0, 7)}
        def make_link(parent, label, url, row_idx):
            l = tk.Label(parent, text=label, fg=link_fg, cursor="hand2", font=link_font)
            l.grid(row=row_idx, column=0, columnspan=3, sticky="w", **link_pad)
            l.bind("<Button-1>", lambda e: webbrowser.open_new(url))
        make_link(frm_top, "Download Viola.CLI-Portable.exe", "https://github.com/skythebro/Viola/releases/latest", 0)
        make_link(frm_top, "Download cpk_list.cfg.bin", "https://github.com/Adr1GR/IEVR_Mod_Manager/tree/main/cpk_list", 1)
        link_row_offset = 2

        def make_browse_row(parent, label_text, var, browse_cmd, row_idx):
            ttk.Label(parent, text=label_text, width=20, anchor="w").grid(row=row_idx, column=0, sticky="w", pady=2, padx=0)
            e = ttk.Entry(parent, textvariable=var)
            e.grid(row=row_idx, column=1, sticky="ew", pady=2, padx=2)
            parent.columnconfigure(1, weight=1)
            ttk.Button(parent, text="Browse", command=browse_cmd).grid(row=row_idx, column=2, sticky="ew", pady=2, padx=2)

        make_browse_row(frm_top, "Game path:", self.game_path, self.browse_game, link_row_offset)
        make_browse_row(frm_top, "cpk_list.cfg.bin path:", self.cfgbin_path, self.browse_cfgbin, link_row_offset+1)
        make_browse_row(frm_top, "Viola.CLI-Portable.exe path:", self.violacli_path, self.browse_violacli, link_row_offset+2)

        # Main center frame
        frm_center = ttk.Frame(self, padding=10)
        frm_center.grid(row=1, column=0, sticky="nsew")
        frm_center.grid_rowconfigure(0, weight=1)
        frm_center.grid_columnconfigure(0, weight=3)
        frm_center.grid_columnconfigure(1, weight=1)

        # Mods & Apply panel (left)
        frm_mods_apply = ttk.Frame(frm_center)
        frm_mods_apply.grid(row=0, column=0, sticky="nsew", padx=(0, 6))
        frm_mods_apply.grid_rowconfigure(0, weight=1)
        frm_mods_apply.grid_columnconfigure(0, weight=1)

        # Mods frame
        mods_frame = ttk.LabelFrame(frm_mods_apply, text="Mods (folder: Mods/) - Mods should be ordered by priority", padding=5)
        mods_frame.grid(row=0, column=0, sticky="nsew")
        mods_frame.grid_rowconfigure(0, weight=1)
        mods_frame.grid_columnconfigure(0, weight=1)

        self.style.configure("Treeview", rowheight=24, font=("Segoe UI", 10))
        self.style.configure("Treeview.Heading", font=("Segoe UI", 10, "bold"))

        self.tree = ttk.Treeview(mods_frame, columns=("enabled", "name", "path"), show="headings", selectmode="browse")
        self.tree.heading("enabled", text="Enabled")
        self.tree.heading("name", text="Name")
        self.tree.heading("path", text="Path")
        self.tree.column("enabled", width=80, anchor="center")
        self.tree.column("name", width=220, anchor="w")
        self.tree.column("path", width=540, anchor="w")
        self.tree.grid(row=0, column=0, sticky="nsew")

        scrollbar = ttk.Scrollbar(mods_frame, orient="vertical", command=self.tree.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.bind("<Double-1>", self._on_tree_double_click)

        ttk.Button(frm_mods_apply, text="Apply Changes", command=self.apply_mods, style="Accent.TButton").grid(row=1, column=0, sticky="ew", pady=6)

        ctrl_frame = ttk.LabelFrame(frm_center, text="Actions", padding=5, width=220)
        ctrl_frame.grid(row=0, column=1, sticky="ns")
        ctrl_frame.grid_propagate(False)

        btns = [
            ("Scan Mods", self.scan_mods),
            ("Move Up", self.move_up),
            ("Move Down", self.move_down),
            #("Toggle Selected", self.toggle_selected),
            ("Enable All", self.enable_all),
            ("Disable All", self.disable_all),
            ("Open Mods Folder", self.open_mods_folder)
        ]
        for idx, (text, cmd) in enumerate(btns):
            ttk.Button(ctrl_frame, text=text, command=cmd).grid(row=idx, column=0, sticky="ew", pady=3)
        ctrl_frame.grid_rowconfigure(len(btns), weight=1)

        # Log frame (abajo)
        frm_log = ttk.LabelFrame(self, text="Log", padding=5)
        frm_log.grid(row=2, column=0, sticky="nsew", padx=10, pady=(6,10))
        frm_log.grid_rowconfigure(0, weight=1)
        frm_log.grid_columnconfigure(0, weight=1)
        self.txt_log = tk.Text(frm_log, height=12, wrap="none", font=("Consolas", 10))
        self.txt_log.grid(row=0, column=0, sticky="nsew", padx=2, pady=2)

        # Botonera inferior
        frm_bottom = ttk.Frame(self, padding=10)
        frm_bottom.grid(row=3, column=0, sticky="ew")
        #ttk.Button(frm_bottom, text="Save Config", command=self._save_config).grid(row=0, column=0, sticky="w")
        #ttk.Button(frm_bottom, text="Reload Config", command=self._load_config).grid(row=0, column=1, padx=6, sticky="w")
        ttk.Button(frm_bottom, text="Exit", command=self._on_close).grid(row=0, column=2, sticky="e")
        frm_bottom.grid_columnconfigure(0, weight=1)
        frm_bottom.grid_columnconfigure(1, weight=1)
        frm_bottom.grid_columnconfigure(2, weight=1)

    # ---------- browse helpers ----------
    def browse_game(self):
        p = filedialog.askdirectory(title="Select the game root folder")
        if p:
            self.game_path.set(os.path.abspath(p))

    def browse_cfgbin(self):
        p = filedialog.askopenfilename(title="Select cpk_list.cfg.bin", filetypes=[("cfg.bin", "*.cfg.bin"), ("All", "*.*")])
        if p:
            self.cfgbin_path.set(os.path.abspath(p))

    def browse_violacli(self):
        p = filedialog.askopenfilename(title="Select violacli.exe", filetypes=[("exe", "*.exe"), ("All", "*.*")])
        if p:
            self.violacli_path.set(os.path.abspath(p))

    def open_mods_folder(self):
        path = DEFAULT_MODS_DIR
        path = os.path.abspath(path)
        os.startfile(path) if os.path.exists(path) else messagebox.showinfo("Info", f"{path} does not exist")

    def open_tmp_folder(self):
        path = self.tmp_dir.get() if isinstance(self.tmp_dir, tk.StringVar) else DEFAULT_TMP_DIR
        path = os.path.abspath(path)
        os.makedirs(path, exist_ok=True)
        os.startfile(path)

    def scan_mods(self):
        mods_root = DEFAULT_MODS_DIR
        mods_root = os.path.abspath(mods_root)
        os.makedirs(mods_root, exist_ok=True)

        names = [n for n in os.listdir(mods_root) if os.path.isdir(os.path.join(mods_root, n))]

        old_map = {me.name: me.enabled.get() for me in self.mod_entries}
        saved_map = {m["name"]: m.get("enabled", True) for m in self.saved_mods} if self.saved_mods else {}

        saved_order = [m["name"] for m in self.saved_mods] if self.saved_mods else []
        ordered_names = saved_order + [n for n in names if n not in saved_order]

        self.mod_entries = []
        for n in ordered_names:
            mod_path = os.path.join(mods_root, n)
            if n in old_map:
                enabled = old_map[n]
            elif n in saved_map:
                enabled = saved_map[n]
            else:
                enabled = True
            me = ModEntry(n, mod_path, enabled=enabled)
            me.enabled.trace_add("write", self._make_mod_enabled_trace(me))
            self.mod_entries.append(me)

        self._refresh_mod_rows()
        self._save_config()


    def _make_mod_enabled_trace(self, mod_entry):
        def _on_change(*args):
            try:
                if mod_entry.name in self.tree.get_children(""):
                    self.tree.set(mod_entry.name, "enabled", "Yes" if mod_entry.enabled.get() else "No")
            except Exception:
                pass
            self._save_config()
        return _on_change

    def _refresh_mod_rows(self):
        for row in self.tree.get_children():
            self.tree.delete(row)
        for idx, me in enumerate(self.mod_entries):
            iid = me.name if me.name not in self.tree.get_children() else f"{me.name}__{idx}"
            if iid in self.tree.get_children():
                iid = f"{me.name}__{idx}"
            self.tree.insert("", "end", iid=me.name, values=("Yes" if me.enabled.get() else "No", me.name, me.path))
        self._save_config()

    def _get_selected_index(self):
        sel = self.tree.selection()
        if not sel:
            return None
        iid = sel[0]
        for idx, me in enumerate(self.mod_entries):
            if me.name == iid:
                return idx
        for idx, me in enumerate(self.mod_entries):
            if iid.startswith(me.name):
                return idx
        return None

    def move_up(self):
        idx = self._get_selected_index()
        if idx is None:
            return
        if idx <= 0:
            return
        self.mod_entries[idx - 1], self.mod_entries[idx] = self.mod_entries[idx], self.mod_entries[idx - 1]
        self._refresh_mod_rows()
        self.tree.selection_set(self.mod_entries[idx - 1].name)
        self._save_config()

    def move_down(self):
        idx = self._get_selected_index()
        if idx is None:
            return
        if idx >= len(self.mod_entries) - 1:
            return
        self.mod_entries[idx + 1], self.mod_entries[idx] = self.mod_entries[idx], self.mod_entries[idx + 1]
        self._refresh_mod_rows()
        self.tree.selection_set(self.mod_entries[idx + 1].name)
        self._save_config()

    def toggle_selected(self):
        idx = self._get_selected_index()
        if idx is None:
            return
        me = self.mod_entries[idx]
        me.enabled.set(not me.enabled.get())
        self.tree.selection_set(me.name)
        self._save_config()

    def _on_tree_double_click(self, event):
        iid = self.tree.identify_row(event.y)
        if not iid:
            return
        self.tree.selection_set(iid)
        idx = self._get_selected_index()
        if idx is None:
            return
        me = self.mod_entries[idx]
        me.enabled.set(not me.enabled.get())

    def enable_all(self):
        for me in self.mod_entries:
            me.enabled.set(True)
        self._save_config()

    def disable_all(self):
        for me in self.mod_entries:
            me.enabled.set(False)
        self._save_config()

    # ---------- apply mods ----------
    def apply_mods(self):
        if self._running:
            messagebox.showinfo("Info", "A process is already running.")
            return

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

        ordered = [me.path for me in self.mod_entries if me.enabled.get()]
        if not ordered:
            target_cpk = os.path.join(game_path, "data", "cpk_list.cfg.bin")
            try:
                os.makedirs(os.path.dirname(target_cpk), exist_ok=True)
                shutil.copy2(cfgbin, target_cpk)
                self._log(f"CHANGES APPLIED!!. No mods selected.")
            except Exception as e:
                self._log(f"Error applying changes: {e}")
            return
        tmp_root = self.tmp_dir.get() if isinstance(self.tmp_dir, tk.StringVar) else DEFAULT_TMP_DIR
        tmp_root = os.path.abspath(tmp_root)
        os.makedirs(tmp_root, exist_ok=True)
        cmd = [violacli, "-m", "merge", "-p", "PC", "--cl", cfgbin] + ordered + ["-o", tmp_root]
        self._running = True
        self._thread = threading.Thread(target=self._run_merge_and_copy, args=(cmd, tmp_root, game_path), daemon=True)
        self._thread.start()

    def _run_merge_and_copy(self, cmd, tmp_root, game_path):
        try:
            self._log(f"Executing command:\n{' '.join(shlex_quote(x) for x in cmd)}")
            CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            startupinfo = None
            if os.name == "nt":
                try:
                    si = subprocess.STARTUPINFO()
                    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                    startupinfo = si
                except Exception:
                    startupinfo = None

            proc = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
                startupinfo=startupinfo
            )
            self._proc = proc
            for line in proc.stdout:
                self._log(line.rstrip())
            proc.wait()
            rc = proc.returncode
            self._log(f"violacli finished with code {rc}")

            if rc != 0:
                self._log("violacli returned error; aborting copy.")
                return

            tmp_data = os.path.join(tmp_root, "data")
            if not os.path.exists(tmp_data) or not os.path.isdir(tmp_data):
                self._log(f"{tmp_data} was not found after merge. Aborting.")
                return

            dest_data = os.path.join(game_path, "data")
            self._log(f"Copying {tmp_data} -> {dest_data} (overwriting if needed)...")

            os.makedirs(dest_data, exist_ok=True)

            try:
                shutil.copytree(tmp_data, dest_data, dirs_exist_ok=True)
            except TypeError:
                for root, dirs, files in os.walk(tmp_data):
                    rel = os.path.relpath(root, tmp_data)
                    target_dir = os.path.join(dest_data, rel) if rel != "." else dest_data
                    os.makedirs(target_dir, exist_ok=True)
                    for f in files:
                        srcf = os.path.join(root, f)
                        dstf = os.path.join(target_dir, f)
                        try:
                            shutil.copy2(srcf, dstf)
                        except Exception as e:
                            self._log(f"Error copying {srcf} -> {dstf}: {e}")

            self._log("Copy completed.")

            try:
                shutil.rmtree(tmp_data)
                self._log(f"Removed temporary folder {tmp_data}.")
                self._log(f"MODS APPLIED!!")
            except Exception as e:
                self._log(f"Could not remove {tmp_data}: {e}")

        except FileNotFoundError as e:
            self._log(f"Execution error: {e}")
        except Exception as e:
            self._log(f"Unexpected error: {e}")
        finally:
            self._proc = None
            self._running = False

    # ---------- config (load/save) ----------
    def _load_config(self):
        if os.path.exists(CONFIG_PATH):
            try:
                with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                    cfg = json.load(f)
                self.game_path.set(cfg.get("game_path", ""))
                self.cfgbin_path.set(cfg.get("cfgbin_path", ""))
                self.violacli_path.set(cfg.get("violacli_path", ""))
                self.tmp_dir.set(cfg.get("tmp_dir", DEFAULT_TMP_DIR))
                self.saved_mods = cfg.get("mods", [])
                self._log(f"Configuration loaded from {CONFIG_PATH}")
            except Exception as e:
                self._log(f"Error loading config: {e}")
                self.tmp_dir.set(DEFAULT_TMP_DIR)
                self.saved_mods = []
        else:
            self._save_config()
        self.scan_mods()

    def _save_config(self):
        cfg = {
            "game_path": self.game_path.get(),
            "cfgbin_path": self.cfgbin_path.get(),
            "violacli_path": self.violacli_path.get(),
            "tmp_dir": self.tmp_dir.get(),
            "mods": [{"name": me.name, "enabled": me.enabled.get()} for me in self.mod_entries]
        }
        try:
            with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(cfg, f, ensure_ascii=False, indent=2)
            self._log(f"Configuration saved to {CONFIG_PATH}")
        except Exception as e:
            messagebox.showerror("Error", f"Could not save configuration: {e}")

    # ---------- utilities ----------
    def _log(self, text):
        def append():
            self.txt_log.insert("end", str(text) + "\n")
            self.txt_log.see("end")
        self.after(0, append)

    def _on_close(self):
        if self._running:
            if not messagebox.askyesno("Exit", "There is an operation in progress. Are you sure you want to exit?"):
                return
        self._save_config()
        self.destroy()


def shlex_quote(s):
    try:
        import shlex
        return shlex.quote(str(s))
    except Exception:
        return str(s)


if __name__ == "__main__":
    app = IEVRModManager()
    app.mainloop()
