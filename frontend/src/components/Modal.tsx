import React from "react";
import type { Lang } from "../lib/i18n";
import { t, dir } from "../lib/i18n";

interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm?: () => void;
  title: string;
  message: string;
  lang: Lang;
  confirmText?: string;
  cancelText?: string;
  confirmVariant?: "danger" | "primary";
  showCancel?: boolean;
}

export function Modal({
  isOpen,
  onClose,
  onConfirm,
  title,
  message,
  lang,
  confirmText,
  cancelText,
  confirmVariant = "danger",
  showCancel = true,
}: ModalProps) {
  if (!isOpen) return null;

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        background: "rgba(0, 0, 0, 0.5)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={onClose}
    >
      <div
        className="card"
        dir={dir(lang)}
        style={{
          minWidth: 320,
          maxWidth: 480,
          animation: "fadeIn 0.2s ease",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 style={{ margin: "0 0 12px 0", fontSize: 18 }}>{title}</h3>
        <p style={{ margin: "0 0 20px 0", color: "var(--muted)", lineHeight: 1.5 }}>
          {message}
        </p>
        <div className="row" style={{ gap: 10, justifyContent: "flex-end" }}>
          {showCancel && (
            <button className="ghost" onClick={onClose}>
              {cancelText || t("cancel", lang)}
            </button>
          )}
          {onConfirm && (
            <button
              className={confirmVariant === "danger" ? "ghost" : "primary"}
              onClick={() => {
                onConfirm();
                onClose();
              }}
              style={
                confirmVariant === "danger"
                  ? { color: "var(--warn)", borderColor: "var(--warn)" }
                  : {}
              }
            >
              {confirmText || t("confirm", lang)}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

// Hook for easy modal usage
export function useModal() {
  const [state, setState] = React.useState<{
    isOpen: boolean;
    title: string;
    message: string;
    onConfirm?: () => void;
    confirmText?: string;
    confirmVariant?: "danger" | "primary";
  }>({
    isOpen: false,
    title: "",
    message: "",
  });

  const showModal = (opts: {
    title: string;
    message: string;
    onConfirm?: () => void;
    confirmText?: string;
    confirmVariant?: "danger" | "primary";
  }) => {
    setState({ isOpen: true, ...opts });
  };

  const hideModal = () => {
    setState((prev) => ({ ...prev, isOpen: false }));
  };

  return { state, showModal, hideModal };
}
