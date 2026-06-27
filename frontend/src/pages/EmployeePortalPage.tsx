import React from "react";
import { Link, Navigate, Route, Routes, useNavigate } from "react-router-dom";
import type { Lang } from "../lib/i18n";
import { t } from "../lib/i18n";
import { isTokenValid, getTokenRole } from "../lib/api";
import { AnalyticsPage } from "./portal/AnalyticsPage";
import { FaqsPage } from "./portal/FaqsPage";
import { DocumentsPage } from "./portal/DocumentsPage";
import { IntegrationsPage } from "./portal/IntegrationsPage";
import { WebsiteCrawlPage } from "./portal/WebsiteCrawlPage";
import { ChatTestPage } from "./portal/ChatTestPage";

export function EmployeePortalPage({ lang }: { lang: Lang }) {
  const nav = useNavigate();
  const token = localStorage.getItem("employeeToken");
  const tokenValid = isTokenValid(token);
  const role = getTokenRole(token);
  const isViewer = role === "EmployeeViewer";

  React.useEffect(() => {
    if (!tokenValid) {
      // Clear invalid token
      if (token) localStorage.removeItem("employeeToken");
      nav("/employee/login", { replace: true });
    }
  }, [token, tokenValid, nav]);

  if (!tokenValid) return null;

  return (
    <div className="col" style={{ gap: 12 }}>
      <div className="card">
        <div className="row" style={{ justifyContent: "space-between" }}>
          <div className="row" style={{ gap: 10 }}>
            <h2 className="pageTitle">{t("employeePortal", lang)}</h2>
            <span className="badge">
              <span className="dot" aria-hidden="true" />
              <span>{lang === "ar" ? "إدارة المحتوى" : "Manage content"}</span>
            </span>
          </div>
          <div className="navLinks">
            {!isViewer && (
              <>
                <Link className="navLink" to="/employee/faqs">
                  {t("faqs", lang)}
                </Link>
                <Link className="navLink" to="/employee/documents">
                  {t("documents", lang)}
                </Link>
                <Link className="navLink" to="/employee/integrations">
                  {t("integrations", lang)}
                </Link>
                <Link className="navLink" to="/employee/website">
                  {lang === "ar" ? "الموقع" : "Website"}
                </Link>
              </>
            )}
            <Link className="navLink" to="/employee/analytics">
              {t("analytics", lang)}
            </Link>
            {!isViewer && (
              <Link className="navLink" to="/employee/test">
                {lang === "ar" ? "اختبار" : "Test"}
              </Link>
            )}
          </div>
        </div>
      </div>

      <Routes>
        <Route path="/" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <div className="card">Welcome.</div>} />
        <Route path="faqs" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <FaqsPage lang={lang} token={token!} />} />
        <Route path="documents" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <DocumentsPage lang={lang} token={token!} />} />
        <Route path="integrations" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <IntegrationsPage lang={lang} token={token!} />} />
        <Route path="website" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <WebsiteCrawlPage lang={lang} token={token!} />} />
        <Route path="analytics" element={<AnalyticsPage lang={lang} token={token!} />} />
        <Route path="test" element={isViewer ? <Navigate to="/employee/analytics" replace /> : <ChatTestPage lang={lang} token={token!} />} />
      </Routes>
    </div>
  );
}

