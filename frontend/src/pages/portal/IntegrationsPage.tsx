import React from "react";
import type { Lang } from "../../lib/i18n";
import { API_BASE_URL, authedGet, authedPost } from "../../lib/api";
import { t } from "../../lib/i18n";
import { Modal, useModal } from "../../components/Modal";

const defaultForm = {
  apiName: "",
  description: "",
  baseUrl: "https://api.example.gov",
  method: "GET",
  pathTemplate: "/v1/example",
  authType: "None",
  authConfigJson: "{}",
  headersTemplateJson: "{}",
  queryParamsSchemaJson: "{}",
  bodySchemaJson: "{}",
  bodyTemplateJson: "",
  responseHandlingNotes: "",
  allowInChat: false,
  allowlistedDomain: "api.example.gov",
};

export function IntegrationsPage({ lang, token }: { lang: Lang; token: string }) {
  const [rows, setRows] = React.useState<any[]>([]);
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [editingId, setEditingId] = React.useState<string | null>(null);
  const [form, setForm] = React.useState(defaultForm);
  const { state: modalState, showModal, hideModal } = useModal();

  async function load() {
    setError(null);
    try {
      const data = await authedGet("/api/integrations", token);
      setRows(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    }
  }

  React.useEffect(() => {
    load();
  }, []);

  function editApi(api: any) {
    setEditingId(api.apiId);
    setForm({
      apiName: api.apiName || "",
      description: api.description || "",
      baseUrl: api.baseUrl || "",
      method: api.method || "GET",
      pathTemplate: api.pathTemplate || "",
      authType: api.authType || "None",
      authConfigJson: api.authConfigJson || "{}",
      headersTemplateJson: api.headersTemplateJson || "{}",
      queryParamsSchemaJson: api.queryParamsSchemaJson || "{}",
      bodySchemaJson: api.bodySchemaJson || "{}",
      bodyTemplateJson: api.bodyTemplateJson || "",
      responseHandlingNotes: api.responseHandlingNotes || "",
      allowInChat: api.allowInChat ?? false,
      allowlistedDomain: api.allowlistedDomain || "",
    });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(defaultForm);
  }

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await authedPost("/api/integrations", token, {
        ...form,
        apiId: editingId,
        bodyTemplateJson: form.bodyTemplateJson || null,
      });
      cancelEdit();
      await load();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function deleteApi(apiId: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/integrations/${apiId}`, {
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

  function confirmDelete(apiId: string, apiName: string) {
    showModal({
      title: t("confirmDelete", lang),
      message: t("deleteIntegrationMessage", lang) + `\n\n"${apiName}"`,
      confirmText: t("delete", lang),
      confirmVariant: "danger",
      onConfirm: () => deleteApi(apiId),
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
          <h3 style={{ margin: 0, fontSize: 18 }}>{t("integrations", lang)}</h3>
          <button className="ghost" onClick={load} style={{ padding: "6px 12px", fontSize: 12 }}>
            {t("refresh", lang)}
          </button>
        </div>
        {error && (
          <div
            style={{
              marginTop: 12,
              padding: "12px 16px",
              background: "rgba(239, 68, 68, 0.1)",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              borderRadius: 8,
              color: "#dc2626",
              fontSize: 14,
            }}
          >
            <strong>{lang === "ar" ? "خطأ:" : "Error:"}</strong> {error}
          </div>
        )}
        {busy && (
          <div className="badge" style={{ marginTop: 12 }}>
            {t("processing", lang)}
          </div>
        )}
      </div>

      {/* Create/Edit Form */}
      <div className="card">
        <div className="row" style={{ alignItems: "center", gap: 10, marginBottom: 16 }}>
          <h4 style={{ margin: 0 }}>{editingId ? t("editApi", lang) : t("createApi", lang)}</h4>
          {editingId && (
            <button className="ghost" onClick={cancelEdit} style={{ padding: "4px 12px", fontSize: 12 }}>
              {t("cancel", lang)}
            </button>
          )}
        </div>

        <div className="col" style={{ gap: 16 }}>
          {/* API Name */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("apiName", lang)}</label>
            <input
              value={form.apiName}
              onChange={(e) => setForm({ ...form, apiName: e.target.value })}
              placeholder={lang === "ar" ? "مثال: WeatherAPI" : "e.g., WeatherAPI"}
            />
          </div>

          {/* Description */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("description", lang)}</label>
            <textarea
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              placeholder={lang === "ar" ? "وصف ما يفعله هذا API" : "Describe what this API does"}
              rows={2}
            />
          </div>

          {/* Base URL & Allowlisted Domain */}
          <div className="row" style={{ gap: 16 }}>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("baseUrl", lang)}</label>
              <input
                value={form.baseUrl}
                onChange={(e) => {
                  const newBaseUrl = e.target.value;
                  // Auto-extract host from URL and update allowlistedDomain
                  let newDomain = form.allowlistedDomain;
                  try {
                    const url = new URL(newBaseUrl);
                    newDomain = url.hostname;
                  } catch {
                    // Invalid URL, don't update domain yet
                  }
                  setForm({ ...form, baseUrl: newBaseUrl, allowlistedDomain: newDomain });
                }}
                placeholder="https://api.example.gov"
              />
            </div>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>
                {t("allowlistedDomain", lang)}
                <span className="muted2" style={{ fontWeight: 400, marginInlineStart: 4 }}>
                  ({lang === "ar" ? "يتم تحديثه تلقائياً" : "auto-filled"})
                </span>
              </label>
              <input
                value={form.allowlistedDomain}
                onChange={(e) => setForm({ ...form, allowlistedDomain: e.target.value })}
                placeholder="api.example.gov"
              />
            </div>
          </div>

          {/* Method & Path Template */}
          <div className="row" style={{ gap: 16 }}>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("method", lang)}</label>
              <select
                value={form.method}
                onChange={(e) => setForm({ ...form, method: e.target.value })}
                style={{ padding: "8px 12px" }}
              >
                <option value="GET">GET</option>
                <option value="POST">POST</option>
                <option value="PUT">PUT</option>
                <option value="DELETE">DELETE</option>
                <option value="PATCH">PATCH</option>
              </select>
            </div>
            <div className="col" style={{ gap: 4, flex: 2 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("pathTemplate", lang)}</label>
              <input
                value={form.pathTemplate}
                onChange={(e) => setForm({ ...form, pathTemplate: e.target.value })}
                placeholder="/v1/resource/{id}"
              />
            </div>
          </div>

          {/* Auth Type & Allow In Chat */}
          <div className="row" style={{ gap: 16, alignItems: "flex-end" }}>
            <div className="col" style={{ gap: 4, flex: 1 }}>
              <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>
                {t("authType", lang)} <span className="muted2" style={{ fontWeight: 400 }}>({t("authTypeHint", lang)})</span>
              </label>
              <select
                value={form.authType}
                onChange={(e) => setForm({ ...form, authType: e.target.value })}
                style={{ padding: "8px 12px" }}
              >
                <option value="None">None</option>
                <option value="UserToken">UserToken (citizen's token)</option>
                <option value="ApiKey">ApiKey</option>
                <option value="BearerToken">BearerToken</option>
                <option value="Basic">Basic</option>
              </select>
            </div>
            <label className="row" style={{ gap: 8, cursor: "pointer", paddingBottom: 8 }}>
              <input
                type="checkbox"
                checked={form.allowInChat}
                onChange={(e) => setForm({ ...form, allowInChat: e.target.checked })}
                style={{ width: 16, height: 16 }}
              />
              <span>{t("allowInChat", lang)}</span>
            </label>
          </div>

          {/* Auth Config JSON */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("authConfig", lang)}</label>
            <textarea
              value={form.authConfigJson}
              onChange={(e) => setForm({ ...form, authConfigJson: e.target.value })}
              placeholder='{"apiKeyEnvVar": "MY_API_KEY"}'
              rows={2}
              style={{ fontFamily: "monospace", fontSize: 12 }}
            />
          </div>

          {/* Headers Template JSON */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("headersTemplate", lang)}</label>
            <textarea
              value={form.headersTemplateJson}
              onChange={(e) => setForm({ ...form, headersTemplateJson: e.target.value })}
              placeholder='{"Content-Type": "application/json"}'
              rows={2}
              style={{ fontFamily: "monospace", fontSize: 12 }}
            />
          </div>

          {/* Query Params Schema JSON */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("queryParamsSchema", lang)}</label>
            <textarea
              value={form.queryParamsSchemaJson}
              onChange={(e) => setForm({ ...form, queryParamsSchemaJson: e.target.value })}
              placeholder='{"city": "string", "units": "string"}'
              rows={2}
              style={{ fontFamily: "monospace", fontSize: 12 }}
            />
          </div>

          {/* Body Schema JSON */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("bodySchema", lang)}</label>
            <textarea
              value={form.bodySchemaJson}
              onChange={(e) => setForm({ ...form, bodySchemaJson: e.target.value })}
              placeholder='{"name": "string", "email": "string"}'
              rows={2}
              style={{ fontFamily: "monospace", fontSize: 12 }}
            />
          </div>

          {/* Body Template JSON */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("bodyTemplate", lang)}</label>
            <textarea
              value={form.bodyTemplateJson}
              onChange={(e) => setForm({ ...form, bodyTemplateJson: e.target.value })}
              placeholder='{"data": {"name": "{{name}}"}}'
              rows={2}
              style={{ fontFamily: "monospace", fontSize: 12 }}
            />
          </div>

          {/* Response Handling Notes */}
          <div className="col" style={{ gap: 4 }}>
            <label className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{t("responseNotes", lang)}</label>
            <textarea
              value={form.responseHandlingNotes}
              onChange={(e) => setForm({ ...form, responseHandlingNotes: e.target.value })}
              placeholder={lang === "ar" ? "ملاحظات حول كيفية معالجة الاستجابة" : "Notes on how to parse/handle the response"}
              rows={2}
            />
          </div>

          {/* Submit Button */}
          <button className="primary" onClick={save} disabled={busy} style={{ padding: "12px 24px" }}>
            {busy ? t("saving", lang) : editingId ? t("updateApi", lang) : t("createApi", lang)}
          </button>
        </div>
      </div>

      {/* API Integrations List */}
      <div className="card">
        <h4 style={{ margin: "0 0 16px 0" }}>{t("apiIntegrations", lang)} ({rows.length})</h4>
        <div className="col" style={{ gap: 8 }}>
          {rows.length === 0 ? (
            <div className="muted2" style={{ padding: 24, textAlign: "center" }}>{t("noIntegrations", lang)}</div>
          ) : null}
          {rows.map((r) => (
            <div
              key={r.apiId}
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
                <strong>{r.apiName}</strong>
                <div className="muted" style={{ fontSize: 13 }}>{r.description}</div>
                <div className="muted2" style={{ fontSize: 11, fontFamily: "monospace" }}>
                  {r.method} {r.baseUrl}{r.pathTemplate}
                </div>
              </div>
              <span className="badge">{r.method}</span>
              {r.allowInChat && (
                <span className="badge" style={{ background: "var(--ok)", color: "white" }}>
                  {lang === "ar" ? "محادثة" : "Chat"}
                </span>
              )}
              <button
                className="ghost"
                disabled={busy}
                onClick={() => editApi(r)}
                style={{ padding: "6px 12px", fontSize: 12 }}
              >
                {t("edit", lang)}
              </button>
              <button
                className="ghost"
                disabled={busy}
                onClick={() => confirmDelete(r.apiId, r.apiName)}
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
