use std::collections::HashSet;
use std::sync::{Arc, OnceLock, RwLock};
use tauri::{AppHandle, Emitter};

pub struct HotkeySharedState {
    pub active: bool,
    pub registered_keys: HashSet<String>,
}

struct GlobalHookContext {
    app_handle: AppHandle,
    state: Arc<RwLock<HotkeySharedState>>,
}

static HOOK_CTX: OnceLock<GlobalHookContext> = OnceLock::new();

#[derive(Clone)]
pub struct GlobalHotkeyService {
    state: Arc<RwLock<HotkeySharedState>>,
}

impl GlobalHotkeyService {
    pub fn new(app_handle: AppHandle) -> Self {
        let state = Arc::new(RwLock::new(HotkeySharedState {
            active: true,
            registered_keys: HashSet::new(),
        }));

        let _ = HOOK_CTX.set(GlobalHookContext {
            app_handle,
            state: Arc::clone(&state),
        });

        #[cfg(windows)]
        std::thread::spawn(|| {
            install_windows_hook_loop();
        });

        Self { state }
    }

    pub fn sync(&self, registered_keys: Vec<String>, active: bool) {
        if let Ok(mut guard) = self.state.write() {
            guard.active = active;
            guard.registered_keys = registered_keys.into_iter().collect();
        }
    }
}

fn vk_to_key_name(vk: u32) -> Option<String> {
    match vk {
        0x70..=0x7B => Some(format!("F{}", vk - 0x6F)),
        0x30..=0x39 => Some(format!("{}", vk - 0x30)),
        0x41..=0x5A => {
            let ch = (b'A' + (vk - 0x41) as u8) as char;
            Some(ch.to_string())
        }
        0x60..=0x69 => Some(format!("Num{}", vk - 0x60)),
        0x6E => Some("Num.".to_string()),
        0x6B => Some("Num+".to_string()),
        _ => None,
    }
}

#[cfg(windows)]
fn install_windows_hook_loop() {
    use windows_sys::Win32::Foundation::{LPARAM, LRESULT, WPARAM};
    use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        CallNextHookEx, DispatchMessageW, GetMessageW, SetWindowsHookExW, TranslateMessage,
        UnhookWindowsHookEx, KBDLLHOOKSTRUCT, MSG, WH_KEYBOARD_LL, WM_KEYDOWN,
    };

    unsafe extern "system" fn hook_callback(
        n_code: i32,
        w_param: WPARAM,
        l_param: LPARAM,
    ) -> LRESULT {
        if n_code >= 0 && (w_param as u32) == WM_KEYDOWN {
            if let Some(ctx) = HOOK_CTX.get() {
                let kb = &*(l_param as *const KBDLLHOOKSTRUCT);
                let vk = kb.vkCode;

                if let Ok(guard) = ctx.state.read() {
                    if guard.active {
                        if vk == 0x1B || vk == 0x08 {
                            let _ = ctx.app_handle.emit("global-stop", ());
                        } else if let Some(key_name) = vk_to_key_name(vk) {
                            if guard.registered_keys.contains(&key_name) {
                                let _ = ctx.app_handle.emit("hotkey-triggered", key_name);
                            }
                        }
                    }
                }
            }
        }
        CallNextHookEx(std::ptr::null_mut(), n_code, w_param, l_param)
    }

    unsafe {
        let h_mod = GetModuleHandleW(std::ptr::null());
        let hook_id = SetWindowsHookExW(WH_KEYBOARD_LL, Some(hook_callback), h_mod, 0);
        if hook_id.is_null() {
            eprintln!("Failed to install WH_KEYBOARD_LL hook");
            return;
        }

        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        UnhookWindowsHookEx(hook_id);
    }
}

