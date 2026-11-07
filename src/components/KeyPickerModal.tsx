import React from "react";
import { FKEY_ROW, KB_ROWS, NUMPAD_ROWS } from "../types";

interface KeyPickerModalProps {
  currentKey: string;
  onSelectKey: (key: string) => void;
  onClose: () => void;
}

export const KeyPickerModal: React.FC<KeyPickerModalProps> = ({
  currentKey,
  onSelectKey,
  onClose,
}) => {
  const renderBtn = (keyId: string, displayText?: string) => {
    const isCurrent = keyId === currentKey;
    return (
      <button
        key={keyId}
        onClick={() => onSelectKey(keyId)}
        className={`w-[34px] h-[34px] m-[2px] rounded text-[12px] font-semibold border transition-colors ${
          isCurrent
            ? "bg-[#e94560] border-[#ff6b81] text-white"
            : "bg-[#30363d] border-[#484f58] text-white hover:bg-[#3d444d] hover:border-[#58a6ff]"
        }`}
      >
        {displayText ?? keyId}
      </button>
    );
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-[2px]">
      <div className="w-[500px] max-h-[460px] rounded-[10px] bg-[#0d1117] border border-[#30363d] shadow-2xl flex flex-col overflow-hidden">
        {/* Title Bar */}
        <div className="bg-[#161b22] px-4 py-2.5 flex items-center justify-between border-b border-[#30363d]">
          <span className="text-[#c9d1d9] text-[14px] font-bold">
            📦 เลือกปุ่มที่จะย้ายเสียงไป
          </span>
          <button
            onClick={onClose}
            className="w-7 h-7 rounded text-[#8b949e] hover:text-[#f85149] hover:bg-white/10 flex items-center justify-center"
          >
            ✕
          </button>
        </div>

        {/* Keys Grid */}
        <div className="p-4 overflow-y-auto flex flex-col items-center space-y-3">
          <div className="w-full text-center">
            <div className="text-[#484f58] text-[11px] font-bold mb-1">
              FUNCTION KEYS
            </div>
            <div className="flex flex-wrap justify-center">
              {FKEY_ROW.map((k) => renderBtn(k))}
            </div>
          </div>

          <div className="w-full text-center">
            <div className="text-[#484f58] text-[11px] font-bold mb-1">
              MAIN KEYBOARD
            </div>
            <div className="flex flex-col items-center space-y-0.5">
              {KB_ROWS.map((row, idx) => (
                <div key={idx} className="flex flex-row justify-center">
                  {row.map((k) => renderBtn(k))}
                </div>
              ))}
            </div>
          </div>

          <div className="w-full text-center">
            <div className="text-[#484f58] text-[11px] font-bold mb-1">
              NUMPAD
            </div>
            <div className="flex flex-col items-center space-y-0.5">
              {NUMPAD_ROWS.map((row, idx) => (
                <div key={idx} className="flex flex-row justify-center">
                  {row.map((k) => renderBtn(k, k.replace("Num", "")))}
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

