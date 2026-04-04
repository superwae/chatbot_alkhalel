import React from "react";
import type { Lang } from "../../lib/i18n";
import { API_BASE_URL, authedGet, authedPost } from "../../lib/api";
import { t } from "../../lib/i18n";
import { Modal, useModal } from "../../components/Modal";

export function FaqsPage({ lang, token }: { lang: Lang; token: string }) {
  const [language, setLanguage] = React.useState<"EN" | "AR">(lang === "ar" ? "AR" : "EN");
  const [rows, setRows] = React.useState<any[]>([]);
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const { state: modalState, showModal, hideModal } = useModal();

  const [editingId, setEditingId] = React.useState<string | null>(null);
  const [form, setForm] = React.useState({
    title: "",
    question: "",
    shortDescription: "",
    answer: "",
    tags: "",
    department: "",
    isActive: true,
  });

  function editFaq(faq: any) {
    setEditingId(faq.faqId);
    setForm({
      title: faq.title || "",
      question: faq.question || "",
      shortDescription: faq.shortDescription || "",
      answer: faq.answer || "",
      tags: faq.tagsCsv || "",
      department: faq.department || "",
      isActive: faq.isActive ?? true,
    });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm({ title: "", question: "", shortDescription: "", answer: "", tags: "", department: "", isActive: true });
  }

  async function load() {
    setError(null);
    try {
      const data = await authedGet(`/api/faqs/active?language=${language}`, token);
      setRows(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    }
  }

  React.useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [language]);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await authedPost("/api/faqs", token, { ...form, language, faqId: editingId });
      cancelEdit();
      await load();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function deleteFaq(faqId: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/faqs/${faqId}`, {
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

  function confirmDelete(faqId: string, title: string) {
    showModal({
      title: t("confirmDelete", lang),
      message: t("deleteFaqMessage", lang) + `\n\n"${title}"`,
      confirmText: t("delete", lang),
      confirmVariant: "danger",
      onConfirm: () => deleteFaq(faqId),
    });
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

      {/* Header */}
      <div className="card">
        <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
          <h3 style={{ margin: 0, fontSize: 18 }}>{t("faqs", lang)}</h3>
          <div className="row" style={{ gap: 10 }}>
            <select value={language} onChange={(e) => setLanguage(e.target.value as any)}>
              <option value="EN">EN</option>
              <option value="AR">AR</option>
            </select>
            <button className="ghost" onClick={load} style={{ padding: "6px 12px", fontSize: 12 }}>
              {t("refresh", lang)}
            </button>
          </div>
        </div>
        {error && (
          <div className="badge" style={{ borderColor: "rgba(251, 113, 133, 0.35)", marginTop: 12 }}>
            {error}
          </div>
        )}
        <div className="muted2" style={{ marginTop: 8 }}>
          {rows.length} {lang === "ar" ? "عناصر" : "items"}
        </div>
      </div>

      {/* Create/Edit Form */}
      <div className="card">
        <div className="row" style={{ alignItems: "center", gap: 10, marginBottom: 16 }}>
          <h4 style={{ margin: 0 }}>{editingId ? t("editFaq", lang) : t("createFaq", lang)}</h4>
          {editingId && (
            <button className="ghost" onClick={cancelEdit} style={{ padding: "4px 12px", fontSize: 12 }}>
              {t("cancel", lang)}
            </button>
          )}
        </div>

        <div className="col" style={{ gap: 16 }}>
          {/* Title */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("title", lang)}</label>
            <input
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              placeholder={lang === "ar" ? "عنوان السؤال الشائع" : "FAQ title"}
            />
          </div>

          {/* Question */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("question", lang)}</label>
            <textarea
              value={form.question}
              onChange={(e) => setForm({ ...form, question: e.target.value })}
              placeholder={lang === "ar" ? "السؤال الكامل" : "The full question"}
              rows={2}
            />
          </div>

          {/* Short Description */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("shortDescription", lang)}</label>
            <textarea
              value={form.shortDescription}
              onChange={(e) => setForm({ ...form, shortDescription: e.target.value })}
              placeholder={lang === "ar" ? "وصف مختصر للعرض" : "Brief description for display"}
              rows={2}
            />
          </div>

          {/* Answer */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("answer", lang)}</label>
            <textarea
              value={form.answer}
              onChange={(e) => setForm({ ...form, answer: e.target.value })}
              placeholder={lang === "ar" ? "الإجابة الكاملة" : "The complete answer"}
              rows={4}
            />
          </div>

          {/* Tags & Department Row */}
          <div className="row" style={{ gap: 16 }}>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>
                {t("tags", lang)} <span className="muted2" style={{ fontWeight: 400 }}>({t("tagsHint", lang)})</span>
              </label>
              <input
                value={form.tags}
                onChange={(e) => setForm({ ...form, tags: e.target.value })}
                placeholder={lang === "ar" ? "رخصة، بناء، تجديد" : "license, building, renewal"}
              />
            </div>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("department", lang)}</label>
              <input
                value={form.department}
                onChange={(e) => setForm({ ...form, department: e.target.value })}
                placeholder={lang === "ar" ? "مثال: الخدمات العامة" : "e.g., Public Services"}
              />
            </div>
          </div>

          {/* Is Active */}
          <label className="row" style={{ gap: 8, cursor: "pointer" }}>
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              style={{ width: 16, height: 16 }}
            />
            <span>{t("isActive", lang)}</span>
          </label>

          {/* Submit Button */}
          <button className="primary" disabled={busy} onClick={save} style={{ padding: "12px 24px" }}>
            {busy ? t("saving", lang) : editingId ? t("updateFaq", lang) : t("createFaq", lang)}
          </button>
        </div>
      </div>

      {/* FAQ List */}
      <div className="card">
        <h4 style={{ margin: "0 0 16px 0" }}>{t("activeFaqs", lang)} ({rows.length})</h4>
        <div className="col" style={{ gap: 8 }}>
          {rows.length === 0 ? (
            <div className="muted2" style={{ padding: 24, textAlign: "center" }}>{t("noFaqs", lang)}</div>
          ) : null}
          {rows.map((r) => (
            <div
              key={r.faqId}
              className="row"
              style={{
                gap: 12,
                padding: "12px 16px",
                background: "var(--surface)",
                borderRadius: 8,
                alignItems: "center",
              }}
            >
              <div className="col" style={{ flex: 1, gap: 4 }}>
                <strong>{r.title}</strong>
                <div className="muted" style={{ fontSize: 13 }}>{r.question}</div>
                {r.tagsCsv && (
                  <div className="row" style={{ gap: 4, marginTop: 4 }}>
                    {r.tagsCsv.split(",").map((tag: string, idx: number) => (
                      <span key={idx} className="badge" style={{ fontSize: 10, padding: "2px 6px" }}>
                        {tag.trim()}
                      </span>
                    ))}
                  </div>
                )}
              </div>
              <span className="badge">{r.language}</span>
              <button
                className="ghost"
                disabled={busy}
                onClick={() => editFaq(r)}
                style={{ padding: "6px 12px", fontSize: 12 }}
              >
                {t("edit", lang)}
              </button>
              <button
                className="ghost"
                disabled={busy}
                onClick={() => confirmDelete(r.faqId, r.title)}
                style={{ padding: "6px 12px", fontSize: 12, color: "var(--warn)", borderColor: "var(--warn)" }}
              >
                {t("delete", lang)}
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
