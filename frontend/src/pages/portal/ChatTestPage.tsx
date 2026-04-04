import React from "react";
import type { Lang } from "../../lib/i18n";
import { authedPost } from "../../lib/api";

type TestResult = {
  question: string;
  category: string | null;
  lang: string | null;
  detectedLang: string | null;
  expectedRoute: string;
  actualRoute: string;
  routingCorrect: boolean;
  confidence: number;
  followUpQuestion: string | null;
  selectedFaqId: string | null;
  selectedChunkIds: string[] | null;
  requiresConfirmation: boolean;
  pendingSubmissionSummary: string | null;
  apiCallInfo: {
    apiId: string | null;
    apiName: string | null;
    params: any;
  } | null;
  apiRawResponse: string | null;
  finalAnswer: string | null;
  plannerRawJson: string | null;
  error: string | null;
  faqCandidates: {
    faqId: string | null;
    title: string | null;
    question: string | null;
    score: number;
    language: string | null;
  }[];
  docChunkCandidates: {
    chunkId: string | null;
    filename: string | null;
    textPreview: string | null;
    score: number;
    source: string | null;
  }[];
  embedTimeMs: number;
  searchTimeMs: number;
  plannerTimeMs: number;
  answerTimeMs: number;
  totalTimeMs: number;
};

type TestSummary = {
  totalQuestions: number;
  routingCorrect: number;
  routingWrong: number;
  routeDistribution: Record<string, number>;
  totalTimeMs: number;
  averageTimeMs: number;
  errors: { question: string; error: string }[];
};

type TestResponse = {
  summary: TestSummary;
  results: TestResult[];
};

function copyToClipboard(text: string) {
  if (navigator.clipboard?.writeText) {
    navigator.clipboard.writeText(text);
  } else {
    const textarea = document.createElement("textarea");
    textarea.value = text;
    textarea.style.position = "fixed";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand("copy");
    document.body.removeChild(textarea);
  }
}

/** Build a human-readable debug report for pasting into conversations */
function buildDebugReport(data: TestResponse): string {
  const lines: string[] = [];
  const now = new Date().toISOString().slice(0, 16).replace("T", " ");

  lines.push("=== CHATBOT TEST RESULTS ===");
  lines.push(`Date: ${now}`);
  lines.push(`Total: ${data.summary.totalQuestions} | Correct: ${data.summary.routingCorrect} | Wrong: ${data.summary.routingWrong} | Time: ${(data.summary.totalTimeMs / 1000).toFixed(1)}s`);
  lines.push(`Routes: ${Object.entries(data.summary.routeDistribution).map(([k, v]) => `${k}=${v}`).join(", ")}`);
  lines.push("");

  // Wrong routing section first
  const wrong = data.results.filter(r => !r.routingCorrect);
  if (wrong.length > 0) {
    lines.push(`--- WRONG ROUTING (${wrong.length}) ---`);
    for (const r of wrong) {
      const idx = data.results.indexOf(r) + 1;
      lines.push(`#${idx}: "${r.question}"`);
      lines.push(`  Category: ${r.category}`);
      lines.push(`  Expected: ${r.expectedRoute} -> Actual: ${r.actualRoute} WRONG`);
      lines.push(`  Confidence: ${r.confidence}`);
      if (r.followUpQuestion) lines.push(`  Follow-up: ${r.followUpQuestion}`);
      if (r.apiCallInfo) lines.push(`  API: ${r.apiCallInfo.apiName} (${r.apiCallInfo.apiId})${r.apiCallInfo.params ? " params=" + JSON.stringify(r.apiCallInfo.params) : ""}`);
      lines.push(`  Top FAQ: ${r.faqCandidates?.[0] ? `${r.faqCandidates[0].title} (${r.faqCandidates[0].score.toFixed(3)})` : "-"}`);
      lines.push(`  Top Chunk: ${r.docChunkCandidates?.[0] ? `${r.docChunkCandidates[0].filename} (${r.docChunkCandidates[0].score.toFixed(3)})` : "-"}`);
      lines.push(`  Answer: ${(r.finalAnswer || "(empty)").substring(0, 300)}`);
      if (r.error) lines.push(`  ERROR: ${r.error}`);
      // Include planner JSON for wrong routes (critical for debugging)
      if (r.plannerRawJson) {
        try {
          const parsed = JSON.parse(r.plannerRawJson);
          lines.push(`  Planner: ${JSON.stringify(parsed)}`);
        } catch {
          lines.push(`  Planner: ${r.plannerRawJson}`);
        }
      }
      lines.push("");
    }
  }

  // Errors section
  const errors = data.results.filter(r => r.error);
  if (errors.length > 0) {
    lines.push(`--- ERRORS (${errors.length}) ---`);
    for (const r of errors) {
      const idx = data.results.indexOf(r) + 1;
      lines.push(`#${idx}: "${r.question}" -> ${r.error}`);
    }
    lines.push("");
  }

  // All results
  lines.push("--- ALL RESULTS ---");
  for (let i = 0; i < data.results.length; i++) {
    const r = data.results[i];
    const status = r.error ? "ERR" : r.routingCorrect ? "OK" : "WRONG";
    lines.push(`#${i + 1}: "${r.question}" [${r.category}]`);
    lines.push(`  ${r.expectedRoute} -> ${r.actualRoute} ${status} (conf=${r.confidence}, ${(r.totalTimeMs / 1000).toFixed(1)}s)`);
    if (r.followUpQuestion) lines.push(`  Follow-up: ${r.followUpQuestion}`);
    if (r.apiCallInfo) lines.push(`  API: ${r.apiCallInfo.apiName}${r.apiCallInfo.params ? " " + JSON.stringify(r.apiCallInfo.params) : ""}`);
    lines.push(`  FAQ[0]: ${r.faqCandidates?.[0] ? `${r.faqCandidates[0].title}(${r.faqCandidates[0].score.toFixed(3)})` : "-"} | Chunk[0]: ${r.docChunkCandidates?.[0] ? `${r.docChunkCandidates[0].filename}(${r.docChunkCandidates[0].score.toFixed(3)})` : "-"}`);
    lines.push(`  Answer: ${(r.finalAnswer || "(empty)").substring(0, 400)}`);
    if (r.error) lines.push(`  ERROR: ${r.error}`);
    lines.push("");
  }

  return lines.join("\n");
}

export function ChatTestPage({ lang, token }: { lang: Lang; token: string }) {
  const [running, setRunning] = React.useState(false);
  const [progress, setProgress] = React.useState("");
  const [data, setData] = React.useState<TestResponse | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [expandedIdx, setExpandedIdx] = React.useState<number | null>(null);
  const [showRawJson, setShowRawJson] = React.useState(false);
  const [userToken, setUserToken] = React.useState("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6Ijg1NDY2OTE1NyIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiLYp9mK2YXYp9mGINmG2KfZgdiwINmG2KfYtdixINi02LHYqNin2KrZiiIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkhNVXNlciIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvc2VyaWFsbnVtYmVyIjoiMTkyNyIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL3N0cmVldGFkZHJlc3MiOiIiLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9zdGF0ZW9ycHJvdmluY2UiOiIiLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9jb3VudHJ5IjoiIiwiaHR0cDovL3NjaGVtYXMueG1sc29hcC5vcmcvd3MvMjAwNS8wNS9pZGVudGl0eS9jbGFpbXMvbW9iaWxlcGhvbmUiOiIwNTkyMTM2MDAyIiwiaHR0cDovL3NjaGVtYXMueG1sc29hcC5vcmcvd3MvMjAwNS8wNS9pZGVudGl0eS9jbGFpbXMvaG9tZXBob25lIjoiMCIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6IiIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvZ3JvdXBzaWQiOiIyMTkiLCJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL3dzLzIwMDgvMDYvaWRlbnRpdHkvY2xhaW1zL3ByaW1hcnlncm91cHNpZCI6Itij2KjZiCDYr9i52KzYp9mGINin2YTZhdmG2K7Zgdi2IiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9kc2EiOiIxMDEiLCJleHAiOjIwMjk5OTIzMjEsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3QiLCJhdWQiOiJodHRwOi8vbG9jYWxob3N0In0.cpIQh63e6PMNPPegQ3gufQdXRWMB29x20fh3enHpYKo");
  const [showTokenInput, setShowTokenInput] = React.useState(false);
  const [copyMsg, setCopyMsg] = React.useState("");
  const [feesRunning, setFeesRunning] = React.useState(false);
  const [feesResult, setFeesResult] = React.useState<any>(null);

  async function testFees() {
    setFeesRunning(true);
    setFeesResult(null);
    try {
      const body: any = { fType: 1 };
      if (userToken.trim()) body.userToken = userToken.trim();
      const res = await authedPost("/api/chat-test/test-fees", token, body);
      setFeesResult(res);
    } catch (e: any) {
      setFeesResult({ success: false, error: e?.message ?? String(e) });
    } finally {
      setFeesRunning(false);
    }
  }

  async function runTests() {
    setRunning(true);
    setError(null);
    setData(null);
    setProgress(lang === "ar" ? "جارٍ تشغيل الاختبارات... (قد يستغرق 2-5 دقائق)" : "Running tests... (may take 2-5 minutes)");

    try {
      const body: any = {};
      if (userToken.trim()) body.userToken = userToken.trim();
      const res = await authedPost("/api/chat-test/run", token, body);
      setData(res as TestResponse);
      setProgress("");
    } catch (e: any) {
      setError(e?.message ?? String(e));
      setProgress("");
    } finally {
      setRunning(false);
    }
  }

  function copyDebugReport() {
    if (!data) return;
    copyToClipboard(buildDebugReport(data));
    setCopyMsg("Debug report copied!");
    setTimeout(() => setCopyMsg(""), 2000);
  }

  function copyFullJson() {
    if (!data) return;
    copyToClipboard(JSON.stringify(data, null, 2));
    setCopyMsg("Full JSON copied!");
    setTimeout(() => setCopyMsg(""), 2000);
  }

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="row" style={{ justifyContent: "space-between", marginBottom: 16 }}>
        <div>
          <h3 style={{ margin: 0 }}>{lang === "ar" ? "اختبار الشات بوت" : "Chatbot Test Suite"}</h3>
          <p className="muted2" style={{ margin: "4px 0 0" }}>
            {lang === "ar"
              ? "يشغل أسئلة اختبارية عبر النظام الكامل (تضمين → بحث متجهي → تخطيط → إجابة)"
              : "Runs test questions through the full pipeline (embed → vector search → planner → answer)"}
          </p>
        </div>
        <div className="row" style={{ gap: 8 }}>
          {data && (
            <>
              <button className="ghost" onClick={copyDebugReport} title="Copy readable debug report">
                {lang === "ar" ? "نسخ تقرير" : "Copy Debug Report"}
              </button>
              <button className="ghost" onClick={copyFullJson} title="Copy full JSON">
                {lang === "ar" ? "نسخ JSON" : "Copy Full JSON"}
              </button>
            </>
          )}
          <button className="ghost" onClick={() => setShowTokenInput(!showTokenInput)} title="Set citizen token for fees API testing">
            {lang === "ar" ? "رمز المستخدم" : "User Token"}
          </button>
          <button className="ghost" onClick={testFees} disabled={feesRunning} style={{ borderColor: "var(--warning, orange)" }}>
            {feesRunning
              ? (lang === "ar" ? "جارٍ الاختبار..." : "Testing...")
              : (lang === "ar" ? "اختبار API الرسوم" : "Test Fees API")}
          </button>
          <button className="primary" onClick={runTests} disabled={running}>
            {running
              ? (lang === "ar" ? "جارٍ التشغيل..." : "Running...")
              : (lang === "ar" ? "تشغيل الاختبارات" : "Run Tests")}
          </button>
        </div>
      </div>

      {/* Copy confirmation */}
      {copyMsg && (
        <div style={{ background: "var(--success-bg, #22c55e20)", borderRadius: 6, padding: "6px 12px", marginBottom: 8, fontSize: 13 }}>
          {copyMsg}
        </div>
      )}

      {/* User Token Input */}
      {showTokenInput && (
        <div style={{ marginBottom: 12, padding: 12, background: "var(--bg-secondary, #1a1a2e)", borderRadius: 8 }}>
          <label style={{ fontSize: 12, display: "block", marginBottom: 4 }}>
            {lang === "ar" ? "رمز المستخدم (JWT) لاختبار API الرسوم:" : "User Token (JWT) for Citizen Fees API testing:"}
          </label>
          <div className="row" style={{ gap: 8 }}>
            <input
              type="text"
              value={userToken}
              onChange={e => setUserToken(e.target.value)}
              placeholder="eyJhbGci..."
              style={{
                flex: 1, padding: "6px 10px", borderRadius: 6, fontSize: 12,
                background: "var(--bg-tertiary, #000)", border: "1px solid var(--border)",
                color: "inherit", fontFamily: "monospace"
              }}
            />
            {userToken && (
              <button className="ghost" onClick={() => setUserToken("")} style={{ fontSize: 12 }}>Clear</button>
            )}
          </div>
          <p className="muted2" style={{ fontSize: 11, margin: "4px 0 0" }}>
            {userToken
              ? (lang === "ar" ? "سيتم استخدام الرمز لاختبار API الرسوم" : "Token will be used for Citizen Fees API tests")
              : (lang === "ar" ? "بدون رمز، اختبارات الرسوم ستتحقق من التوجيه فقط" : "Without token, fees tests will verify routing only")}
          </p>
        </div>
      )}

      {/* Fees API Scan Results */}
      {feesResult && (
        <div style={{ marginBottom: 12, padding: 12, borderRadius: 8, background: "var(--bg-secondary, #1a1a2e)", border: "1px solid var(--border)" }}>
          <div className="row" style={{ justifyContent: "space-between", marginBottom: 8 }}>
            <strong>
              {lang === "ar" ? "نتائج فحص API الرسوم" : "Fees API Scan Results"}
              {feesResult.totalTimeMs && <span className="muted2" style={{ fontWeight: 400, fontSize: 12 }}> ({(feesResult.totalTimeMs / 1000).toFixed(1)}s)</span>}
            </strong>
            <button className="ghost" onClick={() => setFeesResult(null)} style={{ fontSize: 11, padding: "2px 8px" }}>X</button>
          </div>
          {feesResult.error && <div style={{ fontSize: 12, color: "var(--danger, #ef4444)", marginBottom: 8 }}>{feesResult.error}</div>}
          {feesResult.results?.length > 0 && (
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <thead>
                  <tr style={{ borderBottom: "2px solid var(--border)" }}>
                    <th style={{ ...thSmall, width: 50 }}>Status</th>
                    <th style={thSmall}>URL</th>
                    <th style={{ ...thSmall, width: 50 }}>Time</th>
                    <th style={thSmall}>Response</th>
                  </tr>
                </thead>
                <tbody>
                  {feesResult.results.map((r: any, i: number) => (
                    <tr key={i} style={{
                      borderBottom: "1px solid var(--border)",
                      background: r.success ? "var(--success-bg, #22c55e15)" : r.statusCode ? "var(--warning-bg, #ff990010)" : undefined
                    }}>
                      <td style={tdSmall}>
                        <span style={{
                          color: r.success ? "var(--success, #22c55e)" : r.statusCode ? "var(--warning, orange)" : "var(--danger, #ef4444)",
                          fontWeight: 700
                        }}>
                          {r.success ? "OK" : r.statusCode ?? "ERR"}
                        </span>
                      </td>
                      <td style={{ ...tdSmall, fontFamily: "monospace", fontSize: 11, wordBreak: "break-all" }}>{r.url}</td>
                      <td style={tdSmall}>{r.timeMs}ms</td>
                      <td style={{ ...tdSmall, maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis", fontSize: 11 }} title={r.body}>
                        {r.body?.substring(0, 120)}{r.body?.length > 120 ? "..." : ""}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {progress && (
        <div className="badge" style={{ marginBottom: 12, padding: "8px 16px" }}>
          <span className="dot" aria-hidden="true" style={{ animation: "pulse 1.5s infinite" }} />
          <span>{progress}</span>
        </div>
      )}

      {error && (
        <div style={{ background: "var(--danger-bg, #ff000020)", border: "1px solid var(--danger, red)", borderRadius: 8, padding: 12, marginBottom: 12 }}>
          <strong>Error:</strong> {error}
        </div>
      )}

      {data && (
        <>
          {/* Summary Card */}
          <div style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
            gap: 12,
            marginBottom: 16
          }}>
            <SummaryBox label={lang === "ar" ? "إجمالي الأسئلة" : "Total Questions"} value={data.summary.totalQuestions} />
            <SummaryBox
              label={lang === "ar" ? "توجيه صحيح" : "Routing Correct"}
              value={data.summary.routingCorrect}
              color="var(--success, #22c55e)"
            />
            <SummaryBox
              label={lang === "ar" ? "توجيه خاطئ" : "Routing Wrong"}
              value={data.summary.routingWrong}
              color={data.summary.routingWrong > 0 ? "var(--danger, #ef4444)" : undefined}
            />
            <SummaryBox label={lang === "ar" ? "الوقت الكلي" : "Total Time"} value={`${(data.summary.totalTimeMs / 1000).toFixed(1)}s`} />
            <SummaryBox label={lang === "ar" ? "متوسط/سؤال" : "Avg/Question"} value={`${(data.summary.averageTimeMs / 1000).toFixed(1)}s`} />
          </div>

          {/* Route Distribution */}
          <div style={{ marginBottom: 16, display: "flex", gap: 8, flexWrap: "wrap" }}>
            {Object.entries(data.summary.routeDistribution).map(([route, count]) => (
              <span key={route} className="badge" style={{ padding: "4px 12px" }}>
                {route}: {count}
              </span>
            ))}
          </div>

          {/* Results Table */}
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
              <thead>
                <tr style={{ borderBottom: "2px solid var(--border)" }}>
                  <th style={thStyle}>#</th>
                  <th style={thStyle}>{lang === "ar" ? "السؤال" : "Question"}</th>
                  <th style={thStyle}>{lang === "ar" ? "الفئة" : "Category"}</th>
                  <th style={thStyle}>{lang === "ar" ? "المتوقع" : "Expected"}</th>
                  <th style={thStyle}>{lang === "ar" ? "الفعلي" : "Actual"}</th>
                  <th style={thStyle}>{lang === "ar" ? "ثقة" : "Conf."}</th>
                  <th style={thStyle}>{lang === "ar" ? "الوقت" : "Time"}</th>
                  <th style={thStyle}>{lang === "ar" ? "الحالة" : "Status"}</th>
                </tr>
              </thead>
              <tbody>
                {data.results.map((r, i) => (
                  <React.Fragment key={i}>
                    <tr
                      onClick={() => setExpandedIdx(expandedIdx === i ? null : i)}
                      style={{
                        borderBottom: "1px solid var(--border)",
                        cursor: "pointer",
                        background: r.error ? "var(--danger-bg, #ff000010)" : r.routingCorrect ? undefined : "var(--warning-bg, #ff990010)"
                      }}
                    >
                      <td style={tdStyle}>{i + 1}</td>
                      <td style={{ ...tdStyle, maxWidth: 400 }} title={r.question}>
                        <div style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.question}</div>
                        {r.finalAnswer && (
                          <div
                            className="muted2"
                            style={{
                              fontSize: 11,
                              lineHeight: 1.4,
                              marginTop: 3,
                              maxHeight: 40,
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              display: "-webkit-box",
                              WebkitLineClamp: 2,
                              WebkitBoxOrient: "vertical" as const,
                              whiteSpace: "normal",
                              wordBreak: "break-word"
                            }}
                            title={r.finalAnswer}
                          >
                            {r.finalAnswer}
                          </div>
                        )}
                        {r.followUpQuestion && !r.finalAnswer && (
                          <div
                            className="muted2"
                            style={{
                              fontSize: 11,
                              lineHeight: 1.4,
                              marginTop: 3,
                              maxHeight: 40,
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              display: "-webkit-box",
                              WebkitLineClamp: 2,
                              WebkitBoxOrient: "vertical" as const,
                              whiteSpace: "normal",
                              wordBreak: "break-word",
                              fontStyle: "italic"
                            }}
                            title={r.followUpQuestion}
                          >
                            {r.followUpQuestion}
                          </div>
                        )}
                      </td>
                      <td style={tdStyle}><span className="muted2">{r.category}</span></td>
                      <td style={tdStyle}>{r.expectedRoute}</td>
                      <td style={tdStyle}>
                        <span className="badge" style={{ padding: "2px 8px", fontSize: 11 }}>
                          {r.actualRoute}
                        </span>
                      </td>
                      <td style={tdStyle}>{r.confidence.toFixed(2)}</td>
                      <td style={tdStyle}>{(r.totalTimeMs / 1000).toFixed(1)}s</td>
                      <td style={tdStyle}>
                        {r.error ? "ERR" : r.routingCorrect ? "OK" : "WRONG"}
                      </td>
                    </tr>
                    {expandedIdx === i && (
                      <tr>
                        <td colSpan={8} style={{ padding: 0 }}>
                          <ExpandedResult r={r} lang={lang} showRawJson={showRawJson} setShowRawJson={setShowRawJson} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

function SummaryBox({ label, value, color }: { label: string; value: string | number; color?: string }) {
  return (
    <div style={{
      background: "var(--bg-secondary, #1a1a2e)",
      borderRadius: 8,
      padding: "12px 16px",
      textAlign: "center"
    }}>
      <div className="muted2" style={{ fontSize: 11, marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 22, fontWeight: 700, color }}>{value}</div>
    </div>
  );
}

function ExpandedResult({ r, lang, showRawJson, setShowRawJson }: {
  r: TestResult; lang: Lang; showRawJson: boolean; setShowRawJson: (v: boolean) => void
}) {
  return (
    <div style={{
      padding: 16,
      background: "var(--bg-secondary, #0a0a1e)",
      borderBottom: "2px solid var(--border)",
      fontSize: 13
    }}>
      {/* Timing */}
      <Section title={lang === "ar" ? "التوقيت" : "Timing"}>
        <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
          <span>Embed: {r.embedTimeMs}ms</span>
          <span>Search: {r.searchTimeMs}ms</span>
          <span>Planner: {r.plannerTimeMs}ms</span>
          <span>Answer: {r.answerTimeMs}ms</span>
          <span><strong>Total: {r.totalTimeMs}ms</strong></span>
        </div>
      </Section>

      {/* Error */}
      {r.error && (
        <Section title="Error">
          <pre style={preStyle}>{r.error}</pre>
        </Section>
      )}

      {/* Final Answer */}
      <Section title={lang === "ar" ? "الإجابة النهائية" : "Final Answer"}>
        <div style={{ whiteSpace: "pre-wrap", lineHeight: 1.6 }}>{r.finalAnswer || "(empty)"}</div>
      </Section>

      {/* Follow-up */}
      {r.followUpQuestion && (
        <Section title={lang === "ar" ? "سؤال متابعة" : "Follow-up Question"}>
          <div style={{ whiteSpace: "pre-wrap" }}>{r.followUpQuestion}</div>
        </Section>
      )}

      {/* API Info */}
      {r.apiCallInfo && (
        <Section title={lang === "ar" ? "معلومات API" : "API Call Info"}>
          <div>API: <strong>{r.apiCallInfo.apiName}</strong> ({r.apiCallInfo.apiId})</div>
          {r.apiCallInfo.params && (
            <pre style={preStyle}>{JSON.stringify(r.apiCallInfo.params, null, 2)}</pre>
          )}
        </Section>
      )}

      {/* API Raw Response */}
      {r.apiRawResponse && (
        <Section title={lang === "ar" ? "استجابة API الخام" : "API Raw Response"}>
          <pre style={preStyle}>{r.apiRawResponse}</pre>
        </Section>
      )}

      {/* Confirmation */}
      {r.requiresConfirmation && (
        <Section title={lang === "ar" ? "ملخص التأكيد" : "Confirmation Summary"}>
          <div>{r.pendingSubmissionSummary || "(none)"}</div>
        </Section>
      )}

      {/* FAQ Candidates */}
      {r.faqCandidates?.length > 0 && (
        <Section title={`FAQ Candidates (${r.faqCandidates.length})`}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr>
                <th style={thSmall}>Score</th>
                <th style={thSmall}>Title</th>
                <th style={thSmall}>Question</th>
                <th style={thSmall}>Lang</th>
                {r.selectedFaqId && <th style={thSmall}>Selected</th>}
              </tr>
            </thead>
            <tbody>
              {r.faqCandidates.map((f, i) => (
                <tr key={i} style={{
                  background: f.faqId === r.selectedFaqId ? "var(--success-bg, #22c55e20)" : undefined
                }}>
                  <td style={tdSmall}>{f.score.toFixed(4)}</td>
                  <td style={tdSmall}>{f.title}</td>
                  <td style={tdSmall}>{f.question}</td>
                  <td style={tdSmall}>{f.language}</td>
                  {r.selectedFaqId && <td style={tdSmall}>{f.faqId === r.selectedFaqId ? "YES" : ""}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}

      {/* Doc Chunk Candidates */}
      {r.docChunkCandidates?.length > 0 && (
        <Section title={`Document Chunks (${r.docChunkCandidates.length})`}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr>
                <th style={thSmall}>Score</th>
                <th style={thSmall}>Source</th>
                <th style={thSmall}>File</th>
                <th style={thSmall}>Text Preview</th>
                {r.selectedChunkIds && <th style={thSmall}>Selected</th>}
              </tr>
            </thead>
            <tbody>
              {r.docChunkCandidates.map((c, i) => (
                <tr key={i} style={{
                  background: r.selectedChunkIds?.includes(c.chunkId ?? "") ? "var(--success-bg, #22c55e20)" : undefined
                }}>
                  <td style={tdSmall}>{c.score.toFixed(4)}</td>
                  <td style={tdSmall}>{c.source}</td>
                  <td style={{ ...tdSmall, maxWidth: 150, overflow: "hidden", textOverflow: "ellipsis" }}>{c.filename}</td>
                  <td style={{ ...tdSmall, maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis" }}>{c.textPreview}</td>
                  {r.selectedChunkIds && <td style={tdSmall}>{r.selectedChunkIds.includes(c.chunkId ?? "") ? "YES" : ""}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}

      {/* Planner Raw JSON */}
      <Section title={lang === "ar" ? "JSON المخطط" : "Planner Raw JSON"}>
        <button className="ghost" onClick={() => setShowRawJson(!showRawJson)} style={{ fontSize: 12, marginBottom: 4 }}>
          {showRawJson ? "Hide" : "Show"} Raw JSON
        </button>
        {showRawJson && (
          <pre style={preStyle}>{tryFormatJson(r.plannerRawJson)}</pre>
        )}
      </Section>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 12 }}>
      <div style={{ fontWeight: 600, marginBottom: 4, color: "var(--text-muted)" }}>{title}</div>
      {children}
    </div>
  );
}

function tryFormatJson(json: string | null | undefined): string {
  if (!json) return "(empty)";
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

const thStyle: React.CSSProperties = { padding: "8px 6px", textAlign: "start", whiteSpace: "nowrap" };
const tdStyle: React.CSSProperties = { padding: "8px 6px" };
const thSmall: React.CSSProperties = { padding: "4px 6px", textAlign: "start", fontSize: 11 };
const tdSmall: React.CSSProperties = { padding: "4px 6px", fontSize: 12 };
const preStyle: React.CSSProperties = {
  background: "var(--bg-tertiary, #000)",
  padding: 10,
  borderRadius: 6,
  overflow: "auto",
  maxHeight: 300,
  fontSize: 12,
  whiteSpace: "pre-wrap",
  wordBreak: "break-all"
};
