import React from "react";
import type { Lang } from "../../lib/i18n";
import { authedGet } from "../../lib/api";
import { t } from "../../lib/i18n";

type Tab = "overview" | "chat-logs" | "faqs" | "routes";

export function AnalyticsPage({ lang, token }: { lang: Lang; token: string }) {
  const [tab, setTab] = React.useState<Tab>("overview");
  const [summary, setSummary] = React.useState<any>(null);
  const [chatLogs, setChatLogs] = React.useState<any>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [page, setPage] = React.useState(0);
  const [expandedIds, setExpandedIds] = React.useState<Set<string>>(new Set());
  const [fromDate, setFromDate] = React.useState("");
  const [toDate, setToDate] = React.useState("");
  const [routeFilter, setRouteFilter] = React.useState<string>("");
  const [exporting, setExporting] = React.useState(false);
  const limit = 25;

  async function loadSummary() {
    setLoading(true);
    setError(null);
    try {
      let url = "/api/analytics/summary";
      const params = new URLSearchParams();
      if (fromDate) params.append("from", new Date(fromDate).toISOString());
      if (toDate) params.append("to", new Date(toDate).toISOString());
      if (params.toString()) url += "?" + params.toString();
      const data = await authedGet(url, token);
      setSummary(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setLoading(false);
    }
  }

  async function loadChatLogs(offset: number) {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      params.append("limit", String(limit));
      params.append("offset", String(offset));
      if (fromDate) params.append("from", new Date(fromDate).toISOString());
      if (toDate) params.append("to", new Date(toDate).toISOString());
      if (routeFilter) params.append("route", routeFilter);
      const data = await authedGet(`/api/analytics/chat-history?${params.toString()}`, token);
      setChatLogs(data);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setLoading(false);
    }
  }

  async function exportLogs() {
    setExporting(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (fromDate) params.append("from", new Date(fromDate).toISOString());
      if (toDate) params.append("to", new Date(toDate).toISOString());
      const data = await authedGet(`/api/analytics/export?${params.toString()}`, token);

      // Download as JSON file
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `chat-logs-${new Date().toISOString().split("T")[0]}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setExporting(false);
    }
  }

  const totalPages = chatLogs ? Math.ceil(chatLogs.totalCount / limit) : 0;

  // Reset to page 0 when switching to chat-logs (avoids showing an invalid page)
  const prevTabRef = React.useRef(tab);
  React.useEffect(() => {
    if (tab === "chat-logs" && prevTabRef.current !== "chat-logs") {
      setPage(0);
    }
    prevTabRef.current = tab;
  }, [tab]);

  React.useEffect(() => {
    if (tab === "overview" || tab === "faqs" || tab === "routes") {
      loadSummary();
    } else if (tab === "chat-logs") {
      loadChatLogs(page * limit);
    }
  }, [tab, page, fromDate, toDate, routeFilter]);

  // If page is beyond available data (e.g. after filter change), snap back to last valid page
  React.useEffect(() => {
    if (chatLogs && totalPages > 0 && page >= totalPages) {
      setPage(totalPages - 1);
    }
  }, [chatLogs, totalPages, page]);

  function toggleExpand(id: string) {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  // Calculate route-specific counts
  const routeCounts = React.useMemo(() => {
    if (!summary?.routeDistribution) return { faq: 0, rag: 0, api: 0, general: 0, total: 0 };
    const counts = { faq: 0, rag: 0, api: 0, general: 0, total: 0 };
    for (const r of summary.routeDistribution) {
      counts.total += r.cnt;
      if (r.route === "FAQ") counts.faq = r.cnt;
      else if (r.route === "RAG") counts.rag = r.cnt;
      else if (r.route === "API") counts.api = r.cnt;
      else if (r.route === "GENERAL") counts.general = r.cnt;
    }
    return counts;
  }, [summary]);

  return (
    <div className="col">
      <div className="card">
        <div className="row" style={{ gap: 10, flexWrap: "wrap" }}>
          <h3 className="pageTitle" style={{ fontSize: 18, margin: 0 }}>{t("analytics", lang)}</h3>
          <div style={{ marginLeft: "auto" }} />
          <button
            className={tab === "overview" ? "primary" : "ghost"}
            onClick={() => setTab("overview")}
            style={{ padding: "6px 16px", fontSize: 13 }}
          >
            {t("overview", lang)}
          </button>
          <button
            className={tab === "routes" ? "primary" : "ghost"}
            onClick={() => setTab("routes")}
            style={{ padding: "6px 16px", fontSize: 13 }}
          >
            {t("routes", lang)}
          </button>
          <button
            className={tab === "faqs" ? "primary" : "ghost"}
            onClick={() => setTab("faqs")}
            style={{ padding: "6px 16px", fontSize: 13 }}
          >
            {t("topFaqs", lang)}
          </button>
          <button
            className={tab === "chat-logs" ? "primary" : "ghost"}
            onClick={() => setTab("chat-logs")}
            style={{ padding: "6px 16px", fontSize: 13 }}
          >
            {lang === "ar" ? "سجل المحادثات" : "Chat Logs"}
          </button>
        </div>

        {/* Date Filters */}
        <div className="row" style={{ gap: 10, marginTop: 12, flexWrap: "wrap" }}>
          <div className="col" style={{ gap: 4 }}>
            <label className="muted2" style={{ fontSize: 11 }}>{lang === "ar" ? "من تاريخ" : "From Date"}</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => { setFromDate(e.target.value); setPage(0); }}
              style={{ padding: "6px 10px", borderRadius: 6, border: "1px solid var(--border)", background: "var(--surface)" }}
            />
          </div>
          <div className="col" style={{ gap: 4 }}>
            <label className="muted2" style={{ fontSize: 11 }}>{lang === "ar" ? "إلى تاريخ" : "To Date"}</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => { setToDate(e.target.value); setPage(0); }}
              style={{ padding: "6px 10px", borderRadius: 6, border: "1px solid var(--border)", background: "var(--surface)" }}
            />
          </div>
          {(fromDate || toDate) && (
            <button
              className="ghost"
              onClick={() => { setFromDate(""); setToDate(""); setPage(0); }}
              style={{ alignSelf: "flex-end", padding: "6px 12px", fontSize: 12 }}
            >
              {lang === "ar" ? "مسح" : "Clear"}
            </button>
          )}
        </div>

        {error && <div className="badge" style={{ borderColor: "rgba(251, 113, 133, 0.35)", marginTop: 10 }}>{error}</div>}
        {loading && <div className="badge" style={{ background: "var(--brand)", marginTop: 10 }}>{t("loading", lang)}</div>}
      </div>

      {tab === "overview" && summary && <OverviewTab summary={summary} routeCounts={routeCounts} lang={lang} />}
      {tab === "routes" && summary && <RoutesTab summary={summary} lang={lang} />}
      {tab === "faqs" && summary && <FaqsTab summary={summary} lang={lang} />}
      {tab === "chat-logs" && (
        <ChatLogsTab
          chatLogs={chatLogs}
          page={page}
          totalPages={totalPages}
          onPageChange={setPage}
          expandedIds={expandedIds}
          toggleExpand={toggleExpand}
          routeFilter={routeFilter}
          setRouteFilter={(r) => { setRouteFilter(r); setPage(0); }}
          onExport={exportLogs}
          exporting={exporting}
          lang={lang}
        />
      )}
    </div>
  );
}

function OverviewTab({ summary, routeCounts, lang }: { summary: any; routeCounts: any; lang: Lang }) {
  const failedCount = summary.failedApis?.reduce((sum: number, a: any) => sum + a.cnt, 0) || 0;

  return (
    <div className="col">
      <div className="card">
        <h4 style={{ marginTop: 0 }}>{t("systemMetrics", lang)}</h4>
        <div className="row" style={{ gap: 16, flexWrap: "wrap" }}>
          <MetricCard label={t("totalQueries", lang)} value={routeCounts.total} color="var(--brand)" />
          <MetricCard label={lang === "ar" ? "الجلسات" : "Sessions"} value={summary.totalSessions || 0} color="var(--brand2)" />
          <MetricCard label={t("faqQueries", lang)} value={routeCounts.faq} color="var(--ok)" />
          <MetricCard label={t("ragQueries", lang)} value={routeCounts.rag} color="var(--brand2)" />
          <MetricCard label={t("apiQueries", lang)} value={routeCounts.api} color="var(--info)" />
          <MetricCard label={t("generalQueries", lang)} value={routeCounts.general} color="var(--muted)" />
          <MetricCard label={t("failedApiCalls", lang)} value={failedCount} color="var(--warn)" />
        </div>
      </div>

      <div className="card">
        <h4 style={{ marginTop: 0 }}>{t("routeDistribution", lang)}</h4>
        <div className="col">
          {summary.routeDistribution?.length === 0 && <div className="muted2">{t("noDataYet", lang)}</div>}
          {summary.routeDistribution?.map((r: any) => (
            <div key={r.route} className="card compact">
              <div className="row" style={{ gap: 10, alignItems: "center" }}>
                <span className="badge">{r.route}</span>
                <div className="muted" style={{ flex: 1 }}>{r.cnt} {lang === "ar" ? "استعلام" : "queries"}</div>
                <div
                  style={{
                    width: `${(r.cnt / routeCounts.total) * 100}%`,
                    maxWidth: "50%",
                    height: 8,
                    background: r.route === "FAQ" ? "var(--ok)" : r.route === "RAG" ? "var(--brand2)" : r.route === "API" ? "var(--info)" : "var(--brand)",
                    borderRadius: 4,
                  }}
                />
                <strong style={{ minWidth: 50, textAlign: "right" }}>{((r.cnt / routeCounts.total) * 100).toFixed(1)}%</strong>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function MetricCard({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div className="card compact" style={{ flex: 1, minWidth: 140 }}>
      <div className="muted2" style={{ fontSize: 11 }}>{label}</div>
      <div style={{ fontSize: 28, fontWeight: 700, color }}>{value}</div>
    </div>
  );
}

function RoutesTab({ summary, lang }: { summary: any; lang: Lang }) {
  const totalQueries = summary.routeDistribution?.reduce((sum: number, r: any) => sum + r.cnt, 0) || 0;

  const routeDescriptions: Record<string, { en: string; ar: string }> = {
    FAQ: { en: "Queries answered using pre-defined FAQ knowledge base", ar: "استعلامات أُجيبت باستخدام قاعدة الأسئلة الشائعة" },
    RAG: { en: "Queries answered using document retrieval (RAG)", ar: "استعلامات أُجيبت باستخدام استرجاع المستندات (RAG)" },
    GENERAL: { en: "General conversational queries", ar: "استعلامات محادثة عامة" },
    API: { en: "Queries requiring external API integration", ar: "استعلامات تتطلب تكامل API خارجي" },
  };

  return (
    <div className="card">
      <h4 style={{ marginTop: 0 }}>{t("routeAnalysis", lang)}</h4>
      <div className="col">
        {summary.routeDistribution?.length === 0 && <div className="muted2">{t("noDataYet", lang)}</div>}
        {summary.routeDistribution?.map((r: any) => (
          <div key={r.route} className="card compact">
            <div className="col">
              <div className="row" style={{ gap: 10 }}>
                <span className="badge">{r.route}</span>
                <strong style={{ flex: 1 }}>{r.cnt} {lang === "ar" ? "استعلام" : "queries"}</strong>
                <strong>{((r.cnt / totalQueries) * 100).toFixed(1)}%</strong>
              </div>
              <div
                style={{
                  width: "100%",
                  height: 12,
                  background: "var(--surface)",
                  borderRadius: 6,
                  overflow: "hidden",
                  marginTop: 8,
                }}
              >
                <div
                  style={{
                    width: `${(r.cnt / totalQueries) * 100}%`,
                    height: "100%",
                    background: r.route === "FAQ" ? "var(--ok)" : r.route === "RAG" ? "var(--brand2)" : r.route === "API" ? "var(--info)" : "var(--brand)",
                    transition: "width 0.3s ease",
                  }}
                />
              </div>
              <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                {routeDescriptions[r.route]?.[lang] || ""}
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function FaqsTab({ summary, lang }: { summary: any; lang: Lang }) {
  console.log("FAQ:", summary.topFaqs);
  return (
    <div className="card">
      <h4 style={{ marginTop: 0 }}>{t("mostAskedFaqs", lang)}</h4>
      <div className="col">
        {summary.topFaqs?.length === 0 && <div className="muted2">{t("noDataYet", lang)}</div>}
        {summary.topFaqs?.map((faq: any, idx: number) => (
          <div key={faq.faqId} className="card compact">
            <span>{faq.title}</span>
            <div className="row" style={{ gap: 10, alignItems: "center" }}>
              <div
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: "50%",
                  background: idx < 3 ? "var(--brand)" : "var(--surface2)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  fontWeight: 700,
                  fontSize: 14,
                }}
              >
                #{idx + 1}
              </div>
              <div className="col" style={{ flex: 1 }}>
                <div className="muted2" style={{ fontSize: 11 }}>FAQ ID: {faq.faqId}</div>
                <strong>{faq.cnt} {lang === "ar" ? "مرة" : "times asked"}</strong>
              </div>
              <span className="badge">{faq.cnt}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function ChatLogsTab({
  chatLogs,
  page,
  totalPages,
  onPageChange,
  expandedIds,
  toggleExpand,
  routeFilter,
  setRouteFilter,
  onExport,
  exporting,
  lang,
}: {
  chatLogs: any;
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  expandedIds: Set<string>;
  toggleExpand: (id: string) => void;
  routeFilter: string;
  setRouteFilter: (route: string) => void;
  onExport: () => void;
  exporting: boolean;
  lang: Lang;
}) {
  return (
    <div className="col">
      <div className="card">
        <div className="row" style={{ gap: 10, flexWrap: "wrap", alignItems: "center" }}>
          <h4 style={{ margin: 0 }}>{lang === "ar" ? "سجل المحادثات" : "Chat Logs"}</h4>
          <span className="badge">{chatLogs?.totalCount || 0} {lang === "ar" ? "سجل" : "records"}</span>

          {/* Route filter */}
          <select
            value={routeFilter}
            onChange={(e) => setRouteFilter(e.target.value)}
            style={{ padding: "6px 10px", borderRadius: 6, border: "1px solid var(--border)", background: "var(--surface)" }}
          >
            <option value="">{lang === "ar" ? "كل المسارات" : "All Routes"}</option>
            <option value="FAQ">FAQ</option>
            <option value="RAG">RAG</option>
            <option value="API">API</option>
            <option value="GENERAL">GENERAL</option>
          </select>

          <div style={{ marginLeft: "auto" }} />

          <button
            className="ghost"
            onClick={onExport}
            disabled={exporting}
            style={{ padding: "6px 12px", fontSize: 12 }}
          >
            {exporting ? (lang === "ar" ? "جاري التصدير..." : "Exporting...") : (lang === "ar" ? "تصدير JSON" : "Export JSON")}
          </button>

          <button className="ghost" disabled={page === 0} onClick={() => onPageChange(page - 1)}>
            {t("prev", lang)}
          </button>
          <span className="muted">
            {t("page", lang)} {page + 1} {t("of", lang)} {totalPages || 1}
          </span>
          <button className="ghost" disabled={page >= totalPages - 1} onClick={() => onPageChange(page + 1)}>
            {t("next", lang)}
          </button>
        </div>
      </div>

      <div className="col">
        {(!chatLogs?.logs || chatLogs.logs.length === 0) && (
          <div className="card">
            <div className="muted2">{t("noDataYet", lang)}</div>
          </div>
        )}
        {chatLogs?.logs?.map((log: any) => {
          const isExpanded = expandedIds.has(log.decisionId);
          const routeColor = log.route === "FAQ" ? "var(--ok)" : log.route === "RAG" ? "var(--brand2)" : log.route === "API" ? "var(--info)" : "var(--brand)";

          return (
            <div key={log.decisionId} className="card" style={{ borderLeft: `4px solid ${routeColor}` }}>
              <div className="row" style={{ gap: 10, marginBottom: 8, flexWrap: "wrap" }}>
                <span className="badge" style={{ background: routeColor }}>{log.route}</span>
                {log.userLanguage && <span className="badge">{log.userLanguage.toUpperCase()}</span>}
                {log.channel && <span className="badge">{log.channel}</span>}
                {log.confidence && (
                  <span className="badge" style={{ background: "var(--surface2)" }}>
                    {(log.confidence * 100).toFixed(0)}%
                  </span>
                )}
                <span className="muted2" style={{ fontSize: 11, marginLeft: "auto" }}>
                  {new Date(log.createdAt).toLocaleString()}
                </span>
              </div>

              {/* Question */}
              <div style={{ marginBottom: 8 }}>
                <div className="muted2" style={{ fontSize: 11, marginBottom: 4 }}>{lang === "ar" ? "السؤال" : "Question"}</div>
                <div style={{
                  padding: "8px 12px",
                  background: "var(--surface)",
                  borderRadius: 8,
                  direction: log.userLanguage === "ar" ? "rtl" : "ltr"
                }}>
                  {log.question || <span className="muted">-</span>}
                </div>
              </div>

              {/* Answer */}
              <div style={{ marginBottom: 8 }}>
                <div className="muted2" style={{ fontSize: 11, marginBottom: 4 }}>{lang === "ar" ? "الإجابة" : "Answer"}</div>
                <div style={{
                  padding: "8px 12px",
                  background: "var(--surface2)",
                  borderRadius: 8,
                  direction: log.userLanguage === "ar" ? "rtl" : "ltr",
                  maxHeight: isExpanded ? "none" : 100,
                  overflow: "hidden"
                }}>
                  {log.answer || <span className="muted">{lang === "ar" ? "لا توجد إجابة مسجلة" : "No answer recorded"}</span>}
                </div>
              </div>

              {/* Route-specific info */}
              <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                {log.faqTitle && (
                  <div className="badge" style={{ background: "var(--surface2)" }}>
                    FAQ: {log.faqTitle}
                  </div>
                )}
                {log.apiName && (
                  <div className="badge" style={{ background: "var(--surface2)" }}>
                    API: {log.apiName}
                  </div>
                )}
                {log.ragChunks && log.ragChunks.length > 0 && (
                  <div className="badge" style={{ background: "var(--surface2)" }}>
                    {lang === "ar" ? "مقاطع RAG" : "RAG Chunks"}: {log.ragChunks.length}
                  </div>
                )}
              </div>

              <button
                className="ghost"
                onClick={() => toggleExpand(log.decisionId)}
                style={{ marginTop: 8, padding: "4px 12px", fontSize: 12 }}
              >
                {isExpanded ? t("hideDetails", lang) : t("viewDetails", lang)}
              </button>

              {isExpanded && (
                <div style={{ marginTop: 12, padding: 12, background: "var(--surface)", borderRadius: 8 }}>
                  <h5 style={{ margin: "0 0 8px 0" }}>{t("messageDetails", lang)}</h5>
                  <div className="col" style={{ gap: 6, fontSize: 13 }}>
                    <div><strong>{t("route", lang)}:</strong> {log.route}</div>
                    <div><strong>{t("confidence", lang)}:</strong> {log.confidence ? `${(log.confidence * 100).toFixed(1)}%` : "-"}</div>
                    <div><strong>Decision ID:</strong> <span className="muted">{log.decisionId}</span></div>
                    <div><strong>Session ID:</strong> <span className="muted">{log.sessionId}</span></div>
                    {log.faqTitle && <div><strong>FAQ:</strong> {log.faqTitle}</div>}
                    {log.faqQuestion && <div><strong>{t("question", lang)}:</strong> {log.faqQuestion}</div>}
                    {log.apiName && <div><strong>API:</strong> {log.apiName}</div>}

                    {/* RAG Chunks details */}
                    {log.ragChunks && log.ragChunks.length > 0 && (
                      <div style={{ marginTop: 8 }}>
                        <strong>{lang === "ar" ? "مقاطع المستندات المستخدمة" : "Document Chunks Used"}:</strong>
                        <div className="col" style={{ gap: 4, marginTop: 4 }}>
                          {log.ragChunks.map((chunk: any, idx: number) => (
                            <div key={chunk.chunkId || idx} className="badge" style={{ background: "var(--surface2)", display: "inline-block" }}>
                              {chunk.filename} (#{chunk.chunkIndex})
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Full answer */}
                    {log.answer && (
                      <div style={{ marginTop: 8 }}>
                        <strong>{lang === "ar" ? "الإجابة الكاملة" : "Full Answer"}:</strong>
                        <div style={{
                          marginTop: 4,
                          padding: 8,
                          background: "var(--surface2)",
                          borderRadius: 6,
                          whiteSpace: "pre-wrap",
                          direction: log.userLanguage === "ar" ? "rtl" : "ltr"
                        }}>
                          {log.answer}
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
