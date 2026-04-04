import React from "react";
import type { Lang } from "../../lib/i18n";
import { API_BASE_URL, authedGet } from "../../lib/api";
import { t } from "../../lib/i18n";
import { Modal, useModal } from "../../components/Modal";

export function DocumentsPage({ lang, token }: { lang: Lang; token: string }) {
  const [rows, setRows] = React.useState<any[]>([]);
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [stagedFiles, setStagedFiles] = React.useState<File[]>([]);
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  const { state: modalState, showModal, hideModal } = useModal();

  async function load() {
    setError(null);
    try {
      const data = await authedGet("/api/documents", token);
      setRows(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    }
  }

  React.useEffect(() => {
    load();
  }, []);

  function stageFiles(files: FileList | null) {
    if (!files) return;
    const newFiles = Array.from(files);
    setStagedFiles((prev) => [...prev, ...newFiles]);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  function removeStaged(idx: number) {
    setStagedFiles((prev) => prev.filter((_, i) => i !== idx));
  }

  async function uploadAll() {
    if (stagedFiles.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      for (const file of stagedFiles) {
        const fd = new FormData();
        fd.append("file", file);
        const res = await fetch(`${API_BASE_URL}/api/documents/upload`, {
          method: "POST",
          headers: { Authorization: `Bearer ${token}` },
          body: fd,
        });
        if (!res.ok) throw new Error(`${file.name}: ${await res.text()}`);
      }
      setStagedFiles([]);
      await load();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function deleteDoc(docId: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/documents/${docId}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(await res.text());
      await load();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  function confirmDelete(docId: string, filename: string) {
    showModal({
      title: t("confirmDelete", lang),
      message: t("deleteDocumentMessage", lang) + `\n\n"${filename}"`,
      confirmText: t("delete", lang),
      confirmVariant: "danger",
      onConfirm: () => deleteDoc(docId),
    });
  }

  function formatSize(bytes: number) {
    if (bytes < 1024) return bytes + " B";
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  return (
    <div className="col" style={{ gap: 16 }}>
      <Modal
        isOpen={modalState.isOpen}
        onClose={hideModal}
        onConfirm={modalState.onConfirm}
        title={modalState.title}
        message={modalState.message}
        lang={lang}
        confirmText={modalState.confirmText}
        confirmVariant={modalState.confirmVariant}
      />

      {/* Upload Section */}
      <div className="card">
        <h3 style={{ margin: "0 0 16px 0", fontSize: 18 }}>{t("documents", lang)}</h3>

        {error && (
          <div className="badge" style={{ borderColor: "rgba(251, 113, 133, 0.35)", marginBottom: 12 }}>
            {error}
          </div>
        )}

        <div style={{
          border: "2px dashed var(--border)",
          borderRadius: 8,
          padding: 24,
          textAlign: "center",
          background: "var(--surface)"
        }}>
          <input
            ref={fileInputRef}
            type="file"
            multiple
            disabled={busy}
            onChange={(e) => stageFiles(e.target.files)}
            style={{ display: "none" }}
            accept=".pdf,.docx,.doc,.txt,.md"
          />
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={busy}
            style={{ padding: "12px 24px", fontSize: 14 }}
          >
            {t("chooseFiles", lang)}
          </button>
          <div className="muted2" style={{ marginTop: 8, fontSize: 12 }}>
            {t("supportedFormats", lang)}
          </div>
        </div>

        {/* Staged Files */}
        {stagedFiles.length > 0 && (
          <div style={{ marginTop: 16 }}>
            <div className="row" style={{ marginBottom: 8 }}>
              <strong>{t("readyToUpload", lang)} ({stagedFiles.length})</strong>
            </div>
            <div className="col" style={{ gap: 8 }}>
              {stagedFiles.map((file, idx) => (
                <div
                  key={idx}
                  className="row"
                  style={{
                    gap: 12,
                    padding: "8px 12px",
                    background: "var(--surface)",
                    borderRadius: 6,
                    alignItems: "center"
                  }}
                >
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 500 }}>{file.name}</div>
                    <div className="muted2" style={{ fontSize: 11 }}>{formatSize(file.size)}</div>
                  </div>
                  <button
                    className="ghost"
                    onClick={() => removeStaged(idx)}
                    disabled={busy}
                    style={{ padding: "4px 8px", fontSize: 12 }}
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>
            <div className="row" style={{ marginTop: 12, gap: 8 }}>
              <button
                className="primary"
                onClick={uploadAll}
                disabled={busy}
                style={{ padding: "10px 20px" }}
              >
                {busy ? t("processing", lang) : `${t("uploadFiles", lang)} (${stagedFiles.length})`}
              </button>
              <button
                className="ghost"
                onClick={() => setStagedFiles([])}
                disabled={busy}
              >
                {t("clearAll", lang)}
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Documents List */}
      <div className="card">
        <div className="row" style={{ marginBottom: 12 }}>
          <h4 style={{ margin: 0 }}>{t("uploadedDocuments", lang)} ({rows.length})</h4>
          <div style={{ marginLeft: "auto" }} />
          <button className="ghost" onClick={load} disabled={busy} style={{ padding: "6px 12px", fontSize: 12 }}>
            {t("refresh", lang)}
          </button>
        </div>

        {rows.length === 0 ? (
          <div className="muted2" style={{ padding: 24, textAlign: "center" }}>
            {t("noDocuments", lang)}
          </div>
        ) : (
          <div className="col" style={{ gap: 8 }}>
            {rows.map((r) => (
              <div
                key={r.docId}
                className="row"
                style={{
                  gap: 12,
                  padding: "12px 16px",
                  background: "var(--surface)",
                  borderRadius: 8,
                  alignItems: "center"
                }}
              >
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 500 }}>{r.filename}</div>
                  <div className="row" style={{ gap: 8, marginTop: 4 }}>
                    <span className="badge" style={{ fontSize: 10, padding: "2px 6px" }}>{r.fileType}</span>
                    {r.detectedLanguage && (
                      <span className="badge" style={{ fontSize: 10, padding: "2px 6px" }}>{r.detectedLanguage}</span>
                    )}
                    {r.chunkCount && (
                      <span className="muted2" style={{ fontSize: 11 }}>{r.chunkCount} chunks</span>
                    )}
                  </div>
                </div>
                <button
                  className="ghost"
                  disabled={busy}
                  onClick={() => confirmDelete(r.docId, r.filename)}
                  style={{
                    padding: "6px 12px",
                    fontSize: 12,
                    color: "var(--warn)",
                    borderColor: "var(--warn)"
                  }}
                >
                  {t("delete", lang)}
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
