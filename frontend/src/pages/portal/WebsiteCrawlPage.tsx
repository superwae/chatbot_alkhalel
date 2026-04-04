import React from "react";
import type { Lang } from "../../lib/i18n";
import { API_BASE_URL, authedGet } from "../../lib/api";

interface CrawlStatus {
  totalPages: number;
  totalChunks: number;
  lastCrawledAt: string | null;
  recentPages: {
    url: string;
    title: string;
    chunkCount: number;
    lastCrawledAt: string;
  }[];
}

interface CrawlResult {
  pagesProcessed: number;
  pagesSkipped: number;
  chunksCreated: number;
  errors: string[];
}

export function WebsiteCrawlPage({ lang, token }: { lang: Lang; token: string }) {
  const [status, setStatus] = React.useState<CrawlStatus | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [crawling, setCrawling] = React.useState(false);
  const [lastResult, setLastResult] = React.useState<CrawlResult | null>(null);

  async function loadStatus() {
    setError(null);
    setBusy(true);
    try {
      const data = await authedGet("/api/website-crawl/status", token);
      setStatus(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  React.useEffect(() => {
    loadStatus();
  }, []);

  async function triggerCrawl() {
    setCrawling(true);
    setError(null);
    setLastResult(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/website-crawl/trigger`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(await res.text());
      const result: CrawlResult = await res.json();
      setLastResult(result);
      await loadStatus();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setCrawling(false);
    }
  }

  async function clearAll() {
    if (!confirm(lang === "ar"
      ? "هل أنت متأكد؟ سيتم حذف جميع البيانات المستخرجة من الموقع."
      : "Are you sure? This will delete all website crawl data.")) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/website-crawl/clear`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(await res.text());
      await loadStatus();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleString(lang === "ar" ? "ar-PS" : "en-US");
  };

  return (
    <div className="col" style={{ gap: 16 }}>
      {error && (
        <div className="errorBanner" style={{ background: "#fee", color: "#c00", padding: 12, borderRadius: 8 }}>
          {error}
        </div>
      )}

      <div className="card">
        <div className="row" style={{ justifyContent: "space-between", alignItems: "center", marginBottom: 16 }}>
          <h3>{lang === "ar" ? "استخراج بيانات الموقع" : "Website Crawler"}</h3>
          <div className="row" style={{ gap: 8 }}>
            <button
              onClick={triggerCrawl}
              disabled={crawling || busy}
              style={{ background: "#4CAF50", color: "#fff", padding: "8px 16px", borderRadius: 6, border: "none", cursor: crawling ? "wait" : "pointer" }}
            >
              {crawling
                ? (lang === "ar" ? "جاري الاستخراج..." : "Crawling...")
                : (lang === "ar" ? "استخراج الآن" : "Crawl Now")}
            </button>
            <button
              onClick={clearAll}
              disabled={busy || crawling}
              style={{ background: "#f44336", color: "#fff", padding: "8px 16px", borderRadius: 6, border: "none", cursor: "pointer" }}
            >
              {lang === "ar" ? "حذف الكل" : "Clear All"}
            </button>
          </div>
        </div>

        {status && (
          <div className="row" style={{ gap: 24, marginBottom: 16 }}>
            <div style={{ textAlign: "center", padding: 12, background: "#f5f5f5", borderRadius: 8, flex: 1 }}>
              <div style={{ fontSize: 24, fontWeight: "bold" }}>{status.totalPages}</div>
              <div style={{ color: "#666" }}>{lang === "ar" ? "صفحات" : "Pages"}</div>
            </div>
            <div style={{ textAlign: "center", padding: 12, background: "#f5f5f5", borderRadius: 8, flex: 1 }}>
              <div style={{ fontSize: 24, fontWeight: "bold" }}>{status.totalChunks}</div>
              <div style={{ color: "#666" }}>{lang === "ar" ? "أجزاء" : "Chunks"}</div>
            </div>
            <div style={{ textAlign: "center", padding: 12, background: "#f5f5f5", borderRadius: 8, flex: 1 }}>
              <div style={{ fontSize: 14 }}>
                {status.lastCrawledAt
                  ? formatDate(status.lastCrawledAt)
                  : (lang === "ar" ? "لم يتم الاستخراج بعد" : "Never")}
              </div>
              <div style={{ color: "#666" }}>{lang === "ar" ? "آخر استخراج" : "Last Crawl"}</div>
            </div>
          </div>
        )}

        {lastResult && (
          <div style={{
            padding: 12,
            background: lastResult.errors.length > 0 ? "#fff3cd" : "#d4edda",
            borderRadius: 8,
            marginBottom: 16
          }}>
            <strong>{lang === "ar" ? "نتيجة الاستخراج:" : "Crawl Result:"}</strong>
            <ul style={{ margin: "8px 0 0 20px", padding: 0 }}>
              <li>{lang === "ar" ? `صفحات تمت معالجتها: ${lastResult.pagesProcessed}` : `Pages processed: ${lastResult.pagesProcessed}`}</li>
              <li>{lang === "ar" ? `صفحات تم تخطيها: ${lastResult.pagesSkipped}` : `Pages skipped: ${lastResult.pagesSkipped}`}</li>
              <li>{lang === "ar" ? `أجزاء تم إنشاؤها: ${lastResult.chunksCreated}` : `Chunks created: ${lastResult.chunksCreated}`}</li>
              {lastResult.errors.length > 0 && (
                <li style={{ color: "#856404" }}>
                  {lang === "ar" ? `أخطاء: ${lastResult.errors.length}` : `Errors: ${lastResult.errors.length}`}
                </li>
              )}
            </ul>
          </div>
        )}
      </div>

      {status && status.recentPages.length > 0 && (
        <div className="card">
          <h4>{lang === "ar" ? "الصفحات المستخرجة" : "Crawled Pages"}</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", marginTop: 12 }}>
            <thead>
              <tr style={{ borderBottom: "2px solid #ddd" }}>
                <th style={{ textAlign: lang === "ar" ? "right" : "left", padding: 8 }}>
                  {lang === "ar" ? "العنوان" : "Title"}
                </th>
                <th style={{ textAlign: "center", padding: 8 }}>
                  {lang === "ar" ? "أجزاء" : "Chunks"}
                </th>
                <th style={{ textAlign: lang === "ar" ? "right" : "left", padding: 8 }}>
                  {lang === "ar" ? "آخر تحديث" : "Last Updated"}
                </th>
              </tr>
            </thead>
            <tbody>
              {status.recentPages.map((page, i) => (
                <tr key={i} style={{ borderBottom: "1px solid #eee" }}>
                  <td style={{ padding: 8 }}>
                    <a href={page.url} target="_blank" rel="noopener noreferrer" style={{ color: "#0066cc" }}>
                      {page.title || page.url}
                    </a>
                  </td>
                  <td style={{ textAlign: "center", padding: 8 }}>{page.chunkCount}</td>
                  <td style={{ padding: 8 }}>{formatDate(page.lastCrawledAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="card" style={{ background: "#f9f9f9" }}>
        <h4>{lang === "ar" ? "معلومات" : "Info"}</h4>
        <p style={{ color: "#666", margin: 0 }}>
          {lang === "ar"
            ? "يقوم هذا النظام باستخراج المحتوى من موقع البلدية (hebron-city.ps) وإضافته لقاعدة المعرفة حتى يتمكن الشات بوت من الإجابة على الأسئلة المتعلقة بالموقع."
            : "This system crawls the municipality website (hebron-city.ps) and adds its content to the knowledge base so the chatbot can answer questions about website content."}
        </p>
      </div>
    </div>
  );
}
