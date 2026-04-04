import React from "react";
import type { Lang } from "../lib/i18n";
import { t } from "../lib/i18n";
import { employeeLogin } from "../lib/api";
import { useNavigate } from "react-router-dom";

export function EmployeeLoginPage({ lang }: { lang: Lang }) {
  const nav = useNavigate();
  const [username, setUsername] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);

  async function login() {
    setError(null);
    setBusy(true);
    try {
      const token = await employeeLogin(username, password);
      localStorage.setItem("employeeToken", token);
      nav("/employee");
    } catch {
      setError(lang === "ar" ? "فشل تسجيل الدخول" : "Login failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="card" style={{ maxWidth: 560, margin: "0 auto" }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <h2 className="pageTitle">{t("login", lang)}</h2>
        <span className="badge">
          <span className="dot" aria-hidden="true" />
          <span>{lang === "ar" ? "بوابة الموظفين" : "Employee Portal"}</span>
        </span>
      </div>

      <div className="col" style={{ marginTop: 14 }}>
        <label className="col" style={{ gap: 8 }}>
          <div className="muted2">{t("username", lang)}</div>
          <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="admin" />
        </label>
        <label className="col" style={{ gap: 8 }}>
          <div className="muted2">{t("password", lang)}</div>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") login();
            }}
            placeholder="••••••••"
          />
        </label>

        {error ? (
          <div className="badge" style={{ borderColor: "rgba(251, 113, 133, 0.35)", color: "rgba(255,255,255,0.85)" }}>
            <span className="dot" aria-hidden="true" style={{ background: "var(--danger)", boxShadow: "0 0 0 4px rgba(251,113,133,0.18)" }} />
            <span>{error}</span>
          </div>
        ) : null}

        <button className="primary" onClick={login} disabled={busy} style={{ marginTop: 6 }}>
          {busy ? (lang === "ar" ? "جارٍ تسجيل الدخول…" : "Signing in…") : t("signIn", lang)}
        </button>
        <div className="muted2">
          {lang === "ar"
            ? "تأكد من أن الخلفية تعمل وأن كلمة المرور صحيحة."
            : "Make sure the backend is running and your credentials are correct."}
        </div>
      </div>
    </div>
  );
}

