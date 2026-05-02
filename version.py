"""
TikTok Soundboard - Version & Auto-Updater
ตรวจสอบเวอร์ชันและอัปเดตโปรแกรมอัตโนมัติผ่าน GitHub Releases
"""

import os
import sys
import json
import shutil
import tempfile
import zipfile
import threading
import webbrowser
from urllib.request import urlopen, Request
from urllib.error import URLError, HTTPError

import customtkinter as ctk
from tkinter import messagebox

# ===================== Configuration =====================
CURRENT_VERSION = "1.1.1"
GITHUB_OWNER = "BlackBodyX64"
GITHUB_REPO = "soundboard"
GITHUB_API_URL = f"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest"
GITHUB_RELEASES_URL = f"https://github.com/{GITHUB_OWNER}/{GITHUB_REPO}/releases"
APP_EXE_NAME = "TikTok_Soundboard.exe"
UPDATER_EXE_NAME = "TikTok_Updater.exe"
USER_AGENT = "TikTok-Soundboard-Updater/1.0"


def parse_version(v: str) -> tuple:
    """Parse version string (e.g. 'v1.2.3' or '1.2.3') into a comparable tuple."""
    v = v.strip().lstrip("v")
    parts = []
    for p in v.split("."):
        try:
            parts.append(int(p))
        except ValueError:
            parts.append(0)
    while len(parts) < 3:
        parts.append(0)
    return tuple(parts)


def get_latest_release() -> dict | None:
    """Fetch latest release info from GitHub API.
    Returns dict with keys: tag_name, name, body, assets (list), html_url
    """
    try:
        req = Request(GITHUB_API_URL, headers={"User-Agent": USER_AGENT})
        with urlopen(req, timeout=15) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except (URLError, HTTPError, json.JSONDecodeError, Exception):
        return None


def find_zip_asset(release: dict) -> dict | None:
    """Find the .zip asset from the release."""
    for asset in release.get("assets", []):
        if asset["name"].lower().endswith(".zip"):
            return asset
    return None


def download_file(url: str, dest_path: str, progress_callback=None) -> bool:
    """Download a file with optional progress callback(downloaded, total)."""
    try:
        req = Request(url, headers={"User-Agent": USER_AGENT})
        with urlopen(req, timeout=120) as resp:
            total = int(resp.headers.get("Content-Length", 0))
            downloaded = 0
            block_size = 8192
            with open(dest_path, "wb") as f:
                while True:
                    chunk = resp.read(block_size)
                    if not chunk:
                        break
                    f.write(chunk)
                    downloaded += len(chunk)
                    if progress_callback:
                        progress_callback(downloaded, total)
        return True
    except Exception:
        return False


class UpdaterApp(ctk.CTk):
    """GUI Application for version checking and auto-updating."""

    def __init__(self):
        super().__init__()
        self.title("🔄 TikTok Soundboard - Updater")
        self.geometry("520x420")
        self.resizable(False, False)
        self.configure(fg_color="#0d1117")
        ctk.set_appearance_mode("dark")

        self._latest_release = None
        self._is_updating = False

        self._build_ui()
        self.protocol("WM_DELETE_WINDOW", self._on_close)

        # Auto-check on startup
        self.after(500, self._check_version)

    def _build_ui(self):
        # ---- Header ----
        header = ctk.CTkFrame(self, fg_color="#161b22", corner_radius=0, height=56)
        header.pack(fill="x")
        header.pack_propagate(False)

        ctk.CTkLabel(
            header,
            text="🔄 TikTok Soundboard Updater",
            font=("Segoe UI", 18, "bold"),
            text_color="#58a6ff",
        ).pack(side="left", padx=20, pady=10)

        # ---- Current Version Card ----
        card = ctk.CTkFrame(self, fg_color="#161b22", corner_radius=12)
        card.pack(fill="x", padx=20, pady=(16, 8))

        ver_frame = ctk.CTkFrame(card, fg_color="transparent")
        ver_frame.pack(fill="x", padx=20, pady=14)

        ctk.CTkLabel(
            ver_frame,
            text="เวอร์ชันปัจจุบัน",
            font=("Segoe UI", 12),
            text_color="#8b949e",
        ).pack(anchor="w")

        self.current_ver_label = ctk.CTkLabel(
            ver_frame,
            text=f"v{CURRENT_VERSION}",
            font=("Consolas", 28, "bold"),
            text_color="#e6edf3",
        )
        self.current_ver_label.pack(anchor="w", pady=(2, 0))

        # ---- Status Area ----
        status_card = ctk.CTkFrame(self, fg_color="#161b22", corner_radius=12)
        status_card.pack(fill="x", padx=20, pady=8)

        self.status_icon = ctk.CTkLabel(
            status_card,
            text="⏳",
            font=("Segoe UI", 32),
        )
        self.status_icon.pack(pady=(16, 4))

        self.status_label = ctk.CTkLabel(
            status_card,
            text="กำลังตรวจสอบ...",
            font=("Segoe UI", 13, "bold"),
            text_color="#c9d1d9",
        )
        self.status_label.pack()

        self.status_detail = ctk.CTkLabel(
            status_card,
            text="เชื่อมต่อ GitHub Releases...",
            font=("Segoe UI", 10),
            text_color="#8b949e",
        )
        self.status_detail.pack(pady=(2, 4))

        # Progress bar (hidden by default)
        self.progress_bar = ctk.CTkProgressBar(
            status_card,
            width=400,
            height=8,
            progress_color="#58a6ff",
            fg_color="#21262d",
            corner_radius=4,
        )
        self.progress_bar.set(0)
        self.progress_label = ctk.CTkLabel(
            status_card,
            text="",
            font=("Segoe UI", 9),
            text_color="#8b949e",
        )

        # Spacer
        ctk.CTkFrame(status_card, height=12, fg_color="transparent").pack()

        # ---- Action Buttons ----
        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=20, pady=(8, 16))

        self.update_btn = ctk.CTkButton(
            btn_frame,
            text="⬇ อัปเดตเลย",
            font=("Segoe UI", 13, "bold"),
            fg_color="#238636",
            hover_color="#2ea043",
            height=40,
            corner_radius=8,
            command=self._start_update,
            state="disabled",
        )
        self.update_btn.pack(side="left", expand=True, fill="x", padx=(0, 5))

        self.check_btn = ctk.CTkButton(
            btn_frame,
            text="🔍 ตรวจสอบอีกครั้ง",
            font=("Segoe UI", 13, "bold"),
            fg_color="#30363d",
            hover_color="#484f58",
            height=40,
            corner_radius=8,
            command=self._check_version,
        )
        self.check_btn.pack(side="left", expand=True, fill="x", padx=(5, 5))

        self.release_btn = ctk.CTkButton(
            btn_frame,
            text="🌐 GitHub",
            font=("Segoe UI", 13, "bold"),
            fg_color="#30363d",
            hover_color="#484f58",
            height=40,
            corner_radius=8,
            width=90,
            command=lambda: webbrowser.open(GITHUB_RELEASES_URL),
        )
        self.release_btn.pack(side="right", padx=(5, 0))

    # ===================== Version Check =====================
    def _check_version(self):
        """Check for updates in a background thread."""
        self.check_btn.configure(state="disabled")
        self.update_btn.configure(state="disabled")
        self._set_status("⏳", "กำลังตรวจสอบ...", "เชื่อมต่อ GitHub Releases...")
        self._hide_progress()

        threading.Thread(target=self._do_check, daemon=True).start()

    def _do_check(self):
        release = get_latest_release()
        self.after(0, lambda: self._on_check_done(release))

    def _on_check_done(self, release):
        self.check_btn.configure(state="normal")

        if release is None:
            self._set_status(
                "❌",
                "ไม่สามารถตรวจสอบได้",
                "เชื่อมต่อ GitHub ไม่สำเร็จ — ตรวจสอบอินเทอร์เน็ต",
                color="#f85149",
            )
            return

        self._latest_release = release
        latest_tag = release.get("tag_name", "")
        latest_ver = parse_version(latest_tag)
        current_ver = parse_version(CURRENT_VERSION)

        if latest_ver > current_ver:
            asset = find_zip_asset(release)
            size_text = ""
            if asset:
                size_mb = asset.get("size", 0) / (1024 * 1024)
                size_text = f" ({size_mb:.1f} MB)"

            self._set_status(
                "🆕",
                f"มีเวอร์ชันใหม่! {latest_tag}",
                f"พร้อมอัปเดตจาก v{CURRENT_VERSION} → {latest_tag}{size_text}",
                color="#3fb950",
            )
            self.update_btn.configure(state="normal")
        elif latest_ver == current_ver:
            self._set_status(
                "✅",
                "เวอร์ชันล่าสุดแล้ว!",
                f"v{CURRENT_VERSION} เป็นเวอร์ชันล่าสุด",
                color="#58a6ff",
            )
        else:
            self._set_status(
                "🔮",
                "เวอร์ชัน Dev",
                f"v{CURRENT_VERSION} ใหม่กว่า release ล่าสุด ({latest_tag})",
                color="#d2a8ff",
            )

    # ===================== Update Process =====================
    def _start_update(self):
        """Start the update download and install process."""
        if self._is_updating or not self._latest_release:
            return

        release = self._latest_release
        asset = find_zip_asset(release)

        if not asset:
            self._set_status(
                "⚠️",
                "ไม่พบไฟล์ดาวน์โหลด",
                "ไม่พบไฟล์ .zip ใน release — กรุณาดาวน์โหลดจาก GitHub",
                color="#d29922",
            )
            return

        self._is_updating = True
        self.update_btn.configure(state="disabled")
        self.check_btn.configure(state="disabled")
        self._show_progress()
        self._set_status(
            "⬇",
            "กำลังดาวน์โหลด...",
            f"ดาวน์โหลด {asset['name']}...",
        )

        threading.Thread(
            target=self._do_update, args=(asset,), daemon=True
        ).start()

    def _do_update(self, asset):
        """Background thread: download, extract, replace."""
        download_url = asset["browser_download_url"]
        tmp_dir = tempfile.mkdtemp(prefix="soundboard_update_")
        zip_path = os.path.join(tmp_dir, asset["name"])

        try:
            # 1. Download
            success = download_file(
                download_url, zip_path,
                progress_callback=lambda d, t: self.after(
                    0, lambda d=d, t=t: self._update_progress(d, t)
                ),
            )

            if not success:
                self.after(0, lambda: self._update_failed("ดาวน์โหลดไม่สำเร็จ"))
                return

            # 2. Extract
            self.after(0, lambda: self._set_status("📦", "กำลังแตกไฟล์...", "แตกไฟล์อัปเดต..."))

            extract_dir = os.path.join(tmp_dir, "extracted")
            with zipfile.ZipFile(zip_path, "r") as zf:
                zf.extractall(extract_dir)

            # 3. Find the new exe
            new_exe_path = None
            for root, dirs, files in os.walk(extract_dir):
                for f in files:
                    if f.lower() == APP_EXE_NAME.lower():
                        new_exe_path = os.path.join(root, f)
                        break
                if new_exe_path:
                    break

            if not new_exe_path:
                self.after(0, lambda: self._update_failed(
                    f"ไม่พบ {APP_EXE_NAME} ในไฟล์ที่ดาวน์โหลด"
                ))
                return

            # 4. Replace the exe
            self.after(0, lambda: self._set_status("🔄", "กำลังอัปเดต...", "แทนที่ไฟล์เก่า..."))

            # Determine target path (same directory as this updater)
            if getattr(sys, "frozen", False):
                app_dir = os.path.dirname(sys.executable)
            else:
                app_dir = os.path.dirname(os.path.abspath(__file__))

            target_exe = os.path.join(app_dir, APP_EXE_NAME)
            backup_exe = os.path.join(app_dir, APP_EXE_NAME + ".bak")

            # Backup old exe
            if os.path.exists(target_exe):
                if os.path.exists(backup_exe):
                    os.remove(backup_exe)
                shutil.move(target_exe, backup_exe)

            # Copy new exe
            shutil.copy2(new_exe_path, target_exe)

            # Also copy any extra files (sounds folder, etc.) if present
            for item in os.listdir(extract_dir):
                src = os.path.join(extract_dir, item)
                dst = os.path.join(app_dir, item)
                if item.lower() == APP_EXE_NAME.lower():
                    continue  # Already copied
                if item.lower() == UPDATER_EXE_NAME.lower():
                    continue  # Don't overwrite the updater itself
                if item.lower() == "soundboard_config.json":
                    continue  # Don't overwrite user config
                try:
                    if os.path.isdir(src):
                        if os.path.exists(dst):
                            shutil.rmtree(dst)
                        shutil.copytree(src, dst)
                    else:
                        shutil.copy2(src, dst)
                except Exception:
                    pass

            # 5. Cleanup
            try:
                shutil.rmtree(tmp_dir)
            except Exception:
                pass

            latest_tag = self._latest_release.get("tag_name", "?")
            self.after(0, lambda: self._update_success(latest_tag))

        except Exception as e:
            self.after(0, lambda: self._update_failed(str(e)))

    def _update_success(self, new_version):
        self._is_updating = False
        self._hide_progress()
        self.check_btn.configure(state="normal")
        self._set_status(
            "🎉",
            f"อัปเดตสำเร็จ! → {new_version}",
            "เปิดโปรแกรม TikTok Soundboard ใหม่เพื่อใช้เวอร์ชันล่าสุด",
            color="#3fb950",
        )

        # Ask to launch the updated app
        if messagebox.askyesno(
            "อัปเดตสำเร็จ",
            f"อัปเดตเป็น {new_version} เรียบร้อย!\n\nต้องการเปิด TikTok Soundboard เลยไหม?",
        ):
            self._launch_app()

    def _update_failed(self, error_msg):
        self._is_updating = False
        self._hide_progress()
        self.check_btn.configure(state="normal")
        self.update_btn.configure(state="normal")
        self._set_status("❌", "อัปเดตไม่สำเร็จ", error_msg, color="#f85149")

    def _launch_app(self):
        """Launch the main soundboard application."""
        if getattr(sys, "frozen", False):
            app_dir = os.path.dirname(sys.executable)
        else:
            app_dir = os.path.dirname(os.path.abspath(__file__))

        exe_path = os.path.join(app_dir, APP_EXE_NAME)
        if os.path.exists(exe_path):
            os.startfile(exe_path)
        self._on_close()

    # ===================== UI Helpers =====================
    def _set_status(self, icon, text, detail, color="#c9d1d9"):
        self.status_icon.configure(text=icon)
        self.status_label.configure(text=text, text_color=color)
        self.status_detail.configure(text=detail)

    def _show_progress(self):
        self.progress_bar.pack(fill="x", padx=20, pady=(4, 0))
        self.progress_label.pack(pady=(2, 4))
        self.progress_bar.set(0)
        self.progress_label.configure(text="0%")

    def _hide_progress(self):
        self.progress_bar.pack_forget()
        self.progress_label.pack_forget()

    def _update_progress(self, downloaded, total):
        if total > 0:
            pct = downloaded / total
            self.progress_bar.set(pct)
            mb_down = downloaded / (1024 * 1024)
            mb_total = total / (1024 * 1024)
            self.progress_label.configure(
                text=f"{mb_down:.1f} / {mb_total:.1f} MB ({pct * 100:.0f}%)"
            )
        else:
            mb_down = downloaded / (1024 * 1024)
            self.progress_label.configure(text=f"{mb_down:.1f} MB ดาวน์โหลดแล้ว")

    def _on_close(self):
        if self._is_updating:
            if not messagebox.askyesno(
                "กำลังอัปเดต",
                "กำลังดาวน์โหลดอัปเดตอยู่ ต้องการยกเลิกไหม?",
            ):
                return
        self.destroy()


if __name__ == "__main__":
    app = UpdaterApp()
    app.mainloop()
