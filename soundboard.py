"""
TikTok Livestream Soundboard
Drag & Drop + Full Keyboard Layout + F-Keys + Numpad
"""
import customtkinter as ctk
import pygame
import keyboard
import json
import os
import sys
import threading
import windnd
from tkinter import filedialog

CONFIG_FILE = "soundboard_config.json"
SOUNDS_DIR = "sounds"
AUDIO_EXTS = {".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma"}

# --- Layout definitions ---
# Each section: (label, rows, row_offsets, color, accent)
FKEY_ROW = ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"]

KB_ROWS = [
    ["1","2","3","4","5","6","7","8","9","0"],
    ["Q","W","E","R","T","Y","U","I","O","P"],
    ["A","S","D","F","G","H","J","K","L"],
    ["Z","X","C","V","B","N","M"],
]
KB_OFFSETS = [0, 20, 35, 55]

NUMPAD_ROWS = [
    ["Num7","Num8","Num9"],
    ["Num4","Num5","Num6"],
    ["Num1","Num2","Num3"],
    ["Num0","Num.","Num+"],
]

# Hotkey mapping (display_key -> keyboard lib key)
HOTKEY_MAP = {}
for i in range(1, 13):
    HOTKEY_MAP[f"F{i}"] = f"f{i}"
for k in "1234567890":
    HOTKEY_MAP[k] = k
for k in "QWERTYUIOPASDFGHJKLZXCVBNM":
    HOTKEY_MAP[k] = k.lower()
for i in range(10):
    HOTKEY_MAP[f"Num{i}"] = f"num {i}"
HOTKEY_MAP["Num."] = "num decimal"
HOTKEY_MAP["Num+"] = "num plus"

# Color schemes per section
SECTION_COLORS = {
    "fkey":   {"base": "#b8860b", "accent": "#ffd700", "dark": "#3d2e00"},
    "row0":   {"base": "#e94560", "accent": "#ff6b81", "dark": "#4a0e1e"},
    "row1":   {"base": "#0f3460", "accent": "#1a6daa", "dark": "#071a30"},
    "row2":   {"base": "#533483", "accent": "#7c4dbd", "dark": "#2a1545"},
    "row3":   {"base": "#e76f51", "accent": "#f4a261", "dark": "#4a2218"},
    "numpad": {"base": "#2d6a4f", "accent": "#52b788", "dark": "#1a3d2e"},
}


class SoundPad:
    def __init__(self, key="", sound_path="", sound_name="", volume=100):
        self.key = key
        self.sound_path = sound_path
        self.sound_name = sound_name
        self.volume = volume

    def to_dict(self):
        return {"key": self.key, "sound_path": self.sound_path,
                "sound_name": self.sound_name, "volume": self.volume}

    @staticmethod
    def from_dict(d):
        return SoundPad(d.get("key",""), d.get("sound_path",""),
                        d.get("sound_name",""), d.get("volume",100))


class SoundboardApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("🎵 TikTok Soundboard")
        self.geometry("1280x680")
        self.minsize(1100, 600)
        self.configure(fg_color="#0d1117")
        ctk.set_appearance_mode("dark")

        pygame.mixer.init(frequency=44100, size=-16, channels=2, buffer=512)
        pygame.mixer.set_num_channels(48)

        self.pads = {}
        self.widgets = {}
        self.master_volume = 80
        self.hotkeys_active = True

        self._init_all_pads()
        self._load_config()
        self._build_ui()
        self._register_hotkeys()

        windnd.hook_dropfiles(self.winfo_id(), func=self._on_global_drop)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _init_all_pads(self):
        for k in FKEY_ROW:
            self.pads[k] = SoundPad(key=k)
        for row in KB_ROWS:
            for k in row:
                self.pads[k] = SoundPad(key=k)
        for row in NUMPAD_ROWS:
            for k in row:
                self.pads[k] = SoundPad(key=k)

    def _load_config(self):
        try:
            if os.path.exists(CONFIG_FILE):
                with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)
                self.master_volume = data.get("master_volume", 80)
                for pd in data.get("pads", []):
                    k = pd.get("key", "")
                    if k in self.pads:
                        self.pads[k] = SoundPad.from_dict(pd)
        except Exception:
            pass

    def _save_config(self):
        data = {"master_volume": self.master_volume,
                "pads": [p.to_dict() for p in self.pads.values()]}
        try:
            with open(CONFIG_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    # ===================== UI =====================
    def _build_ui(self):
        # ---- Header ----
        header = ctk.CTkFrame(self, fg_color="#161b22", corner_radius=0, height=50)
        header.pack(fill="x")
        header.pack_propagate(False)

        ctk.CTkLabel(header, text="🎵 TikTok Soundboard",
                     font=("Segoe UI", 20, "bold"),
                     text_color="#e94560").pack(side="left", padx=20)

        self.hotkey_var = ctk.BooleanVar(value=True)
        ctk.CTkSwitch(header, text="Global Hotkeys", variable=self.hotkey_var,
                       command=self._toggle_hotkeys, font=("Segoe UI", 11),
                       progress_color="#e94560", fg_color="#30363d"
                       ).pack(side="right", padx=20)

        ctk.CTkButton(header, text="⏹ Stop All", width=100, height=30,
                       fg_color="#da3633", hover_color="#f85149",
                       font=("Segoe UI", 11, "bold"),
                       command=self._stop_all).pack(side="right", padx=5)

        ctk.CTkButton(header, text="🔄 อัปเดต", width=90, height=30,
                       fg_color="#238636", hover_color="#2ea043",
                       font=("Segoe UI", 11, "bold"),
                       command=self._open_updater).pack(side="right", padx=5)

        # ---- Volume + hint bar ----
        vol_bar = ctk.CTkFrame(self, fg_color="#161b22", corner_radius=0, height=38)
        vol_bar.pack(fill="x", pady=(1, 0))
        vol_bar.pack_propagate(False)

        ctk.CTkLabel(vol_bar, text="🔊", font=("Segoe UI", 14)).pack(side="left", padx=(20, 5))
        self.vol_slider = ctk.CTkSlider(vol_bar, from_=0, to=100, number_of_steps=100,
                                         width=180, progress_color="#e94560",
                                         button_color="#fff", button_hover_color="#ff6b81",
                                         command=self._set_master_volume)
        self.vol_slider.set(self.master_volume)
        self.vol_slider.pack(side="left", padx=5)
        self.vol_label = ctk.CTkLabel(vol_bar, text=f"{self.master_volume}%",
                                       font=("Segoe UI", 11, "bold"), width=35)
        self.vol_label.pack(side="left")

        ctk.CTkLabel(vol_bar, text="🎧 ลากไฟล์เสียงวางบนปุ่ม  |  คลิกขวาตั้งค่า",
                     font=("Segoe UI", 10), text_color="#8b949e"
                     ).pack(side="right", padx=20)

        # ---- Main content: keyboard + numpad side by side ----
        content = ctk.CTkFrame(self, fg_color="transparent")
        content.pack(fill="both", expand=True, padx=10, pady=8)

        # Left side: F-keys + main keyboard
        left = ctk.CTkFrame(content, fg_color="transparent")
        left.pack(side="left", fill="both", expand=True)

        # F-key row
        self._build_section_label(left, "Function Keys")
        frow = ctk.CTkFrame(left, fg_color="transparent")
        frow.pack(pady=(0, 6), anchor="w", padx=5)
        for k in FKEY_ROW:
            self._create_key(frow, k, SECTION_COLORS["fkey"], w=70, h=52)

        # Separator
        ctk.CTkFrame(left, height=2, fg_color="#21262d").pack(fill="x", padx=10, pady=2)

        # Main keyboard rows
        self._build_section_label(left, "Main Keyboard")
        for i, row_keys in enumerate(KB_ROWS):
            row_frame = ctk.CTkFrame(left, fg_color="transparent")
            row_frame.pack(pady=2, anchor="w", padx=(KB_OFFSETS[i] + 5, 0))
            cs = SECTION_COLORS[f"row{i}"]
            for k in row_keys:
                self._create_key(row_frame, k, cs)

        # Right side: Numpad
        right = ctk.CTkFrame(content, fg_color="#161b22", corner_radius=12, width=280)
        right.pack(side="right", fill="y", padx=(10, 0))
        right.pack_propagate(False)

        self._build_section_label(right, "Numpad")
        np_inner = ctk.CTkFrame(right, fg_color="transparent")
        np_inner.pack(expand=True, pady=(0, 10))

        cs = SECTION_COLORS["numpad"]
        for row_keys in NUMPAD_ROWS:
            row_frame = ctk.CTkFrame(np_inner, fg_color="transparent")
            row_frame.pack(pady=2)
            for k in row_keys:
                display = k.replace("Num", "")
                self._create_key(row_frame, k, cs, display_text=display)

    def _build_section_label(self, parent, text):
        ctk.CTkLabel(parent, text=text, font=("Segoe UI", 11, "bold"),
                     text_color="#484f58").pack(anchor="w", padx=12, pady=(6, 2))

    def _create_key(self, parent, key, colors, w=72, h=68, display_text=None):
        pad = self.pads[key]
        has_sound = bool(pad.sound_path)
        bg = colors["base"] if has_sound else "#21262d"
        border_c = colors["accent"] if has_sound else "#30363d"

        frame = ctk.CTkFrame(parent, width=w, height=h, fg_color=bg,
                              corner_radius=8, border_width=2, border_color=border_c)
        frame.pack(side="left", padx=2)
        frame.pack_propagate(False)

        # Key label
        show = display_text if display_text else key
        key_lbl = ctk.CTkLabel(frame, text=show,
                                font=("Consolas", 13, "bold"),
                                text_color="#fff" if has_sound else "#8b949e")
        key_lbl.pack(pady=(6, 0))

        # Sound name
        sname = pad.sound_name if pad.sound_name else ""
        if len(sname) > 7:
            sname = sname[:6] + "…"
        name_lbl = ctk.CTkLabel(frame, text=sname, font=("Segoe UI", 8),
                                 text_color="#c9d1d9" if has_sound else "#484f58",
                                 wraplength=w - 10)
        name_lbl.pack(pady=(0, 1))

        # Indicator
        indicator = ctk.CTkFrame(frame, height=3,
                                  fg_color=colors["accent"] if has_sound else "#30363d",
                                  corner_radius=2)
        indicator.pack(fill="x", padx=6, pady=(0, 4), side="bottom")

        self.widgets[key] = {
            "frame": frame, "key_lbl": key_lbl, "name_lbl": name_lbl,
            "indicator": indicator, "colors": colors
        }

        # Bindings
        for widget in [frame, key_lbl, name_lbl]:
            widget.bind("<Button-1>", lambda e, k=key: self._play_sound(k))
            widget.bind("<Button-3>", lambda e, k=key: self._show_pad_menu(k))

        windnd.hook_dropfiles(frame.winfo_id(),
                              func=lambda files, k=key: self._on_pad_drop(files, k))

    def _refresh_key(self, key):
        w = self.widgets[key]
        pad = self.pads[key]
        c = w["colors"]
        has = bool(pad.sound_path)
        w["frame"].configure(fg_color=c["base"] if has else "#21262d",
                             border_color=c["accent"] if has else "#30363d")
        w["key_lbl"].configure(text_color="#fff" if has else "#8b949e")
        sname = pad.sound_name if pad.sound_name else ""
        if len(sname) > 7:
            sname = sname[:6] + "…"
        w["name_lbl"].configure(text=sname,
                                text_color="#c9d1d9" if has else "#484f58")
        w["indicator"].configure(fg_color=c["accent"] if has else "#30363d")

    # ===================== Drag & Drop =====================
    def _on_pad_drop(self, files, key):
        if not files:
            return
        for f in files:
            path = f.decode("utf-8") if isinstance(f, bytes) else str(f)
            if os.path.splitext(path)[1].lower() in AUDIO_EXTS:
                # Schedule on main thread — windnd calls this from a background thread
                self.after(0, lambda p=path, k=key: self._assign_sound(k, p))
                return

    def _on_global_drop(self, files):
        if not files:
            return
        audio = []
        for f in files:
            path = f.decode("utf-8") if isinstance(f, bytes) else str(f)
            if os.path.splitext(path)[1].lower() in AUDIO_EXTS:
                audio.append(path)
        # Schedule on main thread — windnd calls this from a background thread
        self.after(0, lambda a=list(audio): self._assign_global_drop(a))

    def _assign_global_drop(self, audio):
        empty = [k for row in [FKEY_ROW] + KB_ROWS + NUMPAD_ROWS
                 for k in row if not self.pads[k].sound_path]
        for path, key in zip(audio, empty):
            self._assign_sound(key, path)

    def _assign_sound(self, key, path):
        self._unregister_hotkeys()
        pad = self.pads[key]
        pad.sound_path = path
        pad.sound_name = os.path.splitext(os.path.basename(path))[0]
        self._refresh_key(key)
        self._save_config()
        self._register_hotkeys()

    # ===================== Settings Menu =====================
    def _show_pad_menu(self, key):
        pad = self.pads[key]
        c = self.widgets[key]["colors"]

        menu = ctk.CTkToplevel(self)
        menu.title(f"ตั้งค่า [{key}]")
        menu.geometry("380x280")
        menu.resizable(False, False)
        menu.configure(fg_color="#0d1117")
        menu.transient(self)
        menu.grab_set()

        ctk.CTkLabel(menu, text=f"⚙ ตั้งค่าปุ่ม [ {key} ]",
                     font=("Segoe UI", 17, "bold"),
                     text_color=c["accent"]).pack(pady=(12, 10))

        # File
        ctk.CTkLabel(menu, text="ไฟล์เสียง:", font=("Segoe UI", 11),
                     text_color="#c9d1d9").pack(anchor="w", padx=25)
        ff = ctk.CTkFrame(menu, fg_color="transparent")
        ff.pack(fill="x", padx=25, pady=(2, 8))
        fe = ctk.CTkEntry(ff, font=("Segoe UI", 10), fg_color="#161b22", border_color="#30363d")
        fe.pack(side="left", fill="x", expand=True, padx=(0, 5))
        if pad.sound_path:
            fe.insert(0, pad.sound_path)

        def browse():
            p = filedialog.askopenfilename(title="เลือกไฟล์เสียง",
                filetypes=[("Audio","*.mp3 *.wav *.ogg *.flac"),("All","*.*")])
            if p:
                fe.delete(0, "end")
                fe.insert(0, p)
        ctk.CTkButton(ff, text="📂", width=32, height=26, fg_color="#30363d",
                       command=browse).pack(side="right")

        # Name
        ctk.CTkLabel(menu, text="ชื่อแสดง:", font=("Segoe UI", 11),
                     text_color="#c9d1d9").pack(anchor="w", padx=25)
        ne = ctk.CTkEntry(menu, font=("Segoe UI", 10), fg_color="#161b22", border_color="#30363d")
        ne.pack(fill="x", padx=25, pady=(2, 8))
        if pad.sound_name:
            ne.insert(0, pad.sound_name)

        # Volume
        ctk.CTkLabel(menu, text="Volume:", font=("Segoe UI", 11),
                     text_color="#c9d1d9").pack(anchor="w", padx=25)
        vf = ctk.CTkFrame(menu, fg_color="transparent")
        vf.pack(fill="x", padx=25, pady=(2, 8))
        vs = ctk.CTkSlider(vf, from_=0, to=100, number_of_steps=100,
                            width=200, progress_color=c["accent"])
        vs.set(pad.volume)
        vs.pack(side="left", fill="x", expand=True)
        vl = ctk.CTkLabel(vf, text=f"{pad.volume}%", font=("Segoe UI", 10), width=35)
        vl.pack(side="right", padx=5)
        vs.configure(command=lambda v: vl.configure(text=f"{int(v)}%"))

        # Buttons
        bf = ctk.CTkFrame(menu, fg_color="transparent")
        bf.pack(fill="x", padx=25, pady=(5, 12))

        def save():
            self._unregister_hotkeys()
            pad.sound_path = fe.get().strip()
            pad.sound_name = ne.get().strip() or (
                os.path.splitext(os.path.basename(pad.sound_path))[0] if pad.sound_path else "")
            pad.volume = int(vs.get())
            self._refresh_key(key)
            self._save_config()
            self._register_hotkeys()
            menu.destroy()

        def clear():
            self._unregister_hotkeys()
            pad.sound_path = ""
            pad.sound_name = ""
            pad.volume = 100
            self._refresh_key(key)
            self._save_config()
            self._register_hotkeys()
            menu.destroy()

        ctk.CTkButton(bf, text="💾 บันทึก", fg_color="#238636", hover_color="#2ea043",
                       font=("Segoe UI", 12, "bold"), command=save
                       ).pack(side="left", expand=True, padx=(0, 5))
        ctk.CTkButton(bf, text="🗑 ล้าง", fg_color="#da3633", hover_color="#f85149",
                       font=("Segoe UI", 12, "bold"), command=clear
                       ).pack(side="right", expand=True, padx=(5, 0))

    # ===================== Audio =====================
    def _play_sound(self, key):
        pad = self.pads.get(key)
        if not pad or not pad.sound_path or not os.path.exists(pad.sound_path):
            return
        try:
            sound = pygame.mixer.Sound(pad.sound_path)
            vol = (pad.volume / 100) * (self.master_volume / 100)
            sound.set_volume(vol)
            ch = sound.play()
            self._flash(key, True)
            threading.Thread(target=self._wait_done, args=(key, ch), daemon=True).start()
        except Exception as e:
            print(f"Error: {e}")

    def _wait_done(self, key, ch):
        while ch.get_busy():
            pygame.time.wait(100)
        self.after(0, lambda: self._flash(key, False))

    def _flash(self, key, playing):
        if key not in self.widgets:
            return
        w = self.widgets[key]
        if playing:
            w["indicator"].configure(fg_color="#ffffff")
            w["frame"].configure(border_color="#ffffff")
        else:
            has = bool(self.pads[key].sound_path)
            c = w["colors"]
            w["indicator"].configure(fg_color=c["accent"] if has else "#30363d")
            w["frame"].configure(border_color=c["accent"] if has else "#30363d")

    def _stop_all(self):
        pygame.mixer.stop()
        for k in self.widgets:
            self._flash(k, False)

    def _set_master_volume(self, val):
        self.master_volume = int(val)
        self.vol_label.configure(text=f"{self.master_volume}%")

    # ===================== Hotkeys =====================
    def _register_hotkeys(self):
        try:
            keyboard.unhook_all()
        except Exception:
            pass
        if not self.hotkeys_active:
            return
        for key, pad in self.pads.items():
            if pad.sound_path and key in HOTKEY_MAP:
                try:
                    hk = HOTKEY_MAP[key]
                    keyboard.on_press_key(
                        hk, lambda e, k=key: self.after(0, lambda: self._play_sound(k)),
                        suppress=False)
                except Exception:
                    pass

    def _unregister_hotkeys(self):
        try:
            keyboard.unhook_all()
        except Exception:
            pass

    def _toggle_hotkeys(self):
        self.hotkeys_active = self.hotkey_var.get()
        if self.hotkeys_active:
            self._register_hotkeys()
        else:
            self._unregister_hotkeys()

    def _open_updater(self):
        import subprocess
        updater_path = "TikTok_Updater.exe"
        if getattr(sys, "frozen", False):
            app_dir = os.path.dirname(sys.executable)
            updater_path = os.path.join(app_dir, updater_path)
        else:
            updater_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "version.py")
            if not os.path.exists(updater_path):
                print("Updater script not found.")
                return

        try:
            if updater_path.endswith(".py"):
                subprocess.Popen([sys.executable, updater_path])
            else:
                if os.path.exists(updater_path):
                    subprocess.Popen([updater_path])
                else:
                    from tkinter import messagebox
                    messagebox.showwarning("ไม่พบตัวอัปเดต", "ไม่พบไฟล์ TikTok_Updater.exe\nกรุณาดาวน์โหลดเวอร์ชันใหม่จาก GitHub ด้วยตนเอง")
        except Exception as e:
            print(f"Failed to launch updater: {e}")

    def _on_close(self):
        self._save_config()
        self._unregister_hotkeys()
        pygame.mixer.quit()
        self.destroy()


if __name__ == "__main__":
    if not os.path.exists(SOUNDS_DIR):
        os.makedirs(SOUNDS_DIR)
    app = SoundboardApp()
    app.mainloop()
