import React from "react";
import { Link, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { CitizenChatPage } from "./CitizenChatPage";
import { EmployeeLoginPage } from "./EmployeeLoginPage";
import { EmployeePortalPage } from "./EmployeePortalPage";
import type { Lang } from "../lib/i18n";
import { dir, t } from "../lib/i18n";
import { isTokenValid } from "../lib/api";

type ThemeMode = "system" | "dark" | "light";

export function App() {
  const [lang, setLang] = React.useState<Lang>(() => (localStorage.getItem("lang") as Lang) || "en");
  const [theme, setTheme] = React.useState<ThemeMode>(() => (localStorage.getItem("theme") as ThemeMode) || "system");
  const nav = useNavigate();
  const loc = useLocation();
  const token = localStorage.getItem("employeeToken");
  const tokenValid = isTokenValid(token);

  const isRtl = lang === "ar";

  React.useEffect(() => {
    localStorage.setItem("lang", lang);
  }, [lang]);

  React.useEffect(() => {
    localStorage.setItem("theme", theme);
    const el = document.documentElement;
    if (theme === "system") delete el.dataset.theme;
    else el.dataset.theme = theme;
  }, [theme]);

  return (
    <div className={isRtl ? "rtl" : ""} dir={dir(lang)}>
      <div className="nav">
        <div className="navInner">
          <Link to="/" className="brand" aria-label="Municipality Chatbot">
            <span className="brandMark" aria-hidden="true" />
            <span className="brandText">Municipality Chatbot</span>
          </Link>

          <div className="navLinks">
            <Link className="navLink" to="/">
              {t("citizenChat", lang)}
            </Link>
            <Link className="navLink" to="/employee">
              {t("employeePortal", lang)}
            </Link>
          </div>

          <div className="navRight">
            <span className="muted2">Theme</span>
            <select className="navSelect" value={theme} onChange={(e) => setTheme(e.target.value as ThemeMode)} aria-label="Theme">
              <option value="system">System</option>
              <option value="dark">Dark</option>
              <option value="light">Light</option>
            </select>
            <span className="muted2">{t("language", lang)}</span>
            <select className="navSelect" value={lang} onChange={(e) => setLang(e.target.value as Lang)}>
              <option value="en">EN</option>
              <option value="ar">AR</option>
            </select>
            {tokenValid ? (
              <button
                onClick={() => {
                  localStorage.removeItem("employeeToken");
                  if (loc.pathname.startsWith("/employee")) nav("/employee/login");
                }}
              >
                {t("signOut", lang)}
              </button>
            ) : null}
          </div>
        </div>
      </div>

      <div className="container">
        <Routes>
          <Route path="/" element={<CitizenChatPage lang={lang} />} />
          <Route path="/employee/login" element={<EmployeeLoginPage lang={lang} />} />
          <Route path="/employee/*" element={<EmployeePortalPage lang={lang} />} />
        </Routes>
      </div>
    </div>
  );
}

