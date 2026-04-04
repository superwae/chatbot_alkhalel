import React from "react";
import type { Lang } from "../lib/i18n";
import { t } from "../lib/i18n";
import { API_BASE_URL, publicChatStream } from "../lib/api";

type Msg = { role: "user" | "assistant"; text: string };

// Simple markdown renderer for chat messages
function renderMarkdown(text: string): React.ReactNode {
  if (!text) return null;

  const lines = text.split('\n');
  const elements: React.ReactNode[] = [];
  let listItems: string[] = [];
  let listType: 'ul' | 'ol' | null = null;

  const flushList = () => {
    if (listItems.length > 0 && listType) {
      const ListTag = listType;
      elements.push(
        <ListTag key={elements.length} style={{ margin: '8px 0', paddingInlineStart: '20px' }}>
          {listItems.map((item, i) => (
            <li key={i}>{formatInlineText(item)}</li>
          ))}
        </ListTag>
      );
      listItems = [];
      listType = null;
    }
  };

  const formatInlineText = (line: string): React.ReactNode => {
    // Handle bold **text** or __text__ and URLs
    const parts: React.ReactNode[] = [];
    let remaining = line;
    let key = 0;

    while (remaining) {
      // Check for bold or URL, whichever comes first
      const boldMatch = remaining.match(/(\*\*|__)(.+?)\1/);
      const urlMatch = remaining.match(/(https?:\/\/[^\s,،)}\]]+)/);

      const boldIdx = boldMatch?.index ?? Infinity;
      const urlIdx = urlMatch?.index ?? Infinity;

      if (boldIdx === Infinity && urlIdx === Infinity) {
        parts.push(remaining);
        break;
      }

      if (boldIdx <= urlIdx && boldMatch && boldMatch.index !== undefined) {
        if (boldMatch.index > 0) {
          parts.push(remaining.slice(0, boldMatch.index));
        }
        parts.push(<strong key={key++}>{boldMatch[2]}</strong>);
        remaining = remaining.slice(boldMatch.index + boldMatch[0].length);
      } else if (urlMatch && urlMatch.index !== undefined) {
        if (urlMatch.index > 0) {
          parts.push(remaining.slice(0, urlMatch.index));
        }
        const url = urlMatch[1];
        parts.push(
          <a key={key++} href={url} target="_blank" rel="noopener noreferrer" style={{ color: '#0066cc', textDecoration: 'underline' }}>
            {url}
          </a>
        );
        remaining = remaining.slice(urlMatch.index + url.length);
      }
    }
    return parts.length === 1 ? parts[0] : parts;
  };

  lines.forEach((line, idx) => {
    // Headers (### Header, ## Header, # Header)
    const h3Match = line.match(/^###\s+(.+)$/);
    if (h3Match) {
      flushList();
      elements.push(
        <h4 key={elements.length} style={{ margin: '12px 0 8px 0', fontSize: '1em', fontWeight: 600 }}>
          {formatInlineText(h3Match[1])}
        </h4>
      );
      return;
    }

    const h2Match = line.match(/^##\s+(.+)$/);
    if (h2Match) {
      flushList();
      elements.push(
        <h3 key={elements.length} style={{ margin: '14px 0 8px 0', fontSize: '1.1em', fontWeight: 600 }}>
          {formatInlineText(h2Match[1])}
        </h3>
      );
      return;
    }

    const h1Match = line.match(/^#\s+(.+)$/);
    if (h1Match) {
      flushList();
      elements.push(
        <h2 key={elements.length} style={{ margin: '16px 0 10px 0', fontSize: '1.2em', fontWeight: 600 }}>
          {formatInlineText(h1Match[1])}
        </h2>
      );
      return;
    }

    // Numbered list (1. item, 2. item) - also handle indented items with spaces
    const numberedMatch = line.match(/^\s*\d+\.\s+(.+)$/);
    if (numberedMatch) {
      if (listType !== 'ol') {
        flushList();
        listType = 'ol';
      }
      listItems.push(numberedMatch[1]);
      return;
    }

    // Bullet list (- item or * item) - also handle indented items with spaces
    const bulletMatch = line.match(/^\s*[-*]\s+(.+)$/);
    if (bulletMatch) {
      if (listType !== 'ul') {
        flushList();
        listType = 'ul';
      }
      listItems.push(bulletMatch[1]);
      return;
    }

    // Not a list item - flush any pending list
    flushList();

    // Empty line
    if (!line.trim()) {
      elements.push(<br key={elements.length} />);
      return;
    }

    // Regular paragraph
    elements.push(
      <p key={elements.length} style={{ margin: '4px 0' }}>
        {formatInlineText(line)}
      </p>
    );
  });

  flushList(); // Flush any remaining list

  return elements;
}

export function CitizenChatPage({ lang }: { lang: Lang }) {
  const [sessionId, setSessionId] = React.useState<string | null>(null);
  const [messages, setMessages] = React.useState<Msg[]>([]);
  const [text, setText] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [lastRoute, setLastRoute] = React.useState<string | null>(null);
  const [citations, setCitations] = React.useState<{ label: string; chunkId?: string | null }[]>([]);
  const [followUp, setFollowUp] = React.useState<string | null>(null);
  const [faqs, setFaqs] = React.useState<any[]>([]);
  const [currentStage, setCurrentStage] = React.useState<string | null>(null);
  const chatRef = React.useRef<HTMLDivElement | null>(null);

  async function send(overrideText?: string) {
    const raw = (overrideText ?? text).trim();
    if (!raw || busy) return;
    const msg = raw;
    setText("");
    setFollowUp(null);
    setCitations([]);
    setCurrentStage(null);
    setMessages((m) => [...m, { role: "user", text: msg }]);
    setBusy(true);

    // Add empty assistant message that will be streamed into
    setMessages((m) => [...m, { role: "assistant", text: "" }]);

    try {
      let currentText = "";

      for await (const evt of publicChatStream(msg, lang, sessionId)) {
        if (evt.type === "stage" && evt.stage) {
          setCurrentStage(evt.stage);
        } else if (evt.type === "meta") {
          setCurrentStage(null); // Clear stage when we get meta (answer is coming)
          if (evt.sessionId) setSessionId(evt.sessionId);
          if (evt.route) setLastRoute(evt.route);
          if (evt.followUpQuestion) {
            setFollowUp(evt.followUpQuestion);
            // If there's a followUpQuestion, show it as the assistant's message
            currentText = evt.followUpQuestion;
            setMessages((m) => {
              const updated = [...m];
              updated[updated.length - 1] = { role: "assistant", text: currentText };
              return updated;
            });
          }
          if (evt.citations) setCitations(evt.citations);
        } else if (evt.type === "chunk" && evt.content) {
          currentText += evt.content;
          // Update the last message (assistant's response)
          setMessages((m) => {
            const updated = [...m];
            updated[updated.length - 1] = { role: "assistant", text: currentText };
            return updated;
          });
        } else if (evt.type === "error") {
          setMessages((m) => {
            const updated = [...m];
            updated[updated.length - 1] = { role: "assistant", text: `Error: ${evt.error}` };
            return updated;
          });
        }
      }

      // If no content was received and no followUp, remove the empty assistant message
      if (!currentText) {
        setMessages((m) => m.slice(0, -1));
      }
    } catch (e: any) {
      setMessages((m) => {
        const updated = [...m];
        if (updated.length > 0 && updated[updated.length - 1].role === "assistant") {
          updated[updated.length - 1] = { role: "assistant", text: `Error: ${e?.message ?? e}` };
        }
        return updated;
      });
    } finally {
      setBusy(false);
      setCurrentStage(null);
    }
  }

  function resetChat() {
    setSessionId(null);
    setMessages([]);
    setText("");
    setBusy(false);
    setLastRoute(null);
    setCitations([]);
    setFollowUp(null);
  }

  React.useEffect(() => {
    const el = chatRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [messages]);

  React.useEffect(() => {
    const loadFaqs = async () => {
      try {
        const langCode = lang === "ar" ? "AR" : "EN";
        const res = await fetch(`${API_BASE_URL}/api/faqs/active?language=${langCode}`);
        if (res.ok) {
          const data = await res.json();
          setFaqs(data.slice(0, 4));
        }
      } catch (e) {
        console.error("Failed to load FAQs:", e);
      }
    };
    loadFaqs();
  }, [lang]);

  const hasFaqs = faqs.length > 0;

  return (
    <div className="col">
      <div className="card">
        <div className="row" style={{ justifyContent: "space-between" }}>
          <div className="row" style={{ gap: 10 }}>
            <h2 className="pageTitle">{t("citizenChat", lang)}</h2>
            {lastRoute ? (
              <span className="badge" title="Routing decision">
                <span className="dot" aria-hidden="true" />
                <span>{t("route", lang)}: {lastRoute}</span>
              </span>
            ) : (
              <span className="badge">
                <span className="dot" aria-hidden="true" />
                <span>{lang === "ar" ? "جاهز" : "Ready"}</span>
              </span>
            )}
          </div>
          <div className="row" style={{ gap: 10, alignItems: "center" }}>
            <button className="ghost" onClick={resetChat} disabled={busy && messages.length > 0}>
              {lang === "ar" ? "محادثة جديدة" : "New chat"}
            </button>
            <span className="muted2">
              {busy ? (currentStage || (lang === "ar" ? "يكتب..." : "Typing…")) : sessionId ? `${lang === "ar" ? "جلسة" : "Session"}: ${sessionId.slice(0, 8)}…` : ""}
            </span>
          </div>
        </div>

        {messages.length === 0 ? (
          <div className="hero" style={{ paddingTop: 10 }}>
            <div className="heroInner">
              <div>
                <h1 className="heroTitle">{t("welcomeTitle", lang)}</h1>
                <p className="heroSub">{t("welcomeMessage", lang)}</p>
              </div>

              <div className="promptBar" role="group" aria-label="Chat prompt">
                <div className="promptIcon" aria-hidden="true">G</div>
                <textarea
                  className="promptInput"
                  value={text}
                  onChange={(e) => setText(e.target.value)}
                  placeholder={t("messagePlaceholder", lang)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !e.shiftKey) {
                      e.preventDefault();
                      send();
                    }
                  }}
                  rows={1}
                />
                <div className="promptActions">
                  <button className="ghost" onClick={() => setText("")} disabled={busy || !text.trim()}>
                    {lang === "ar" ? "مسح" : "Clear"}
                  </button>
                  <button className="primary" onClick={() => send()} disabled={busy}>
                    {busy ? t("processing", lang) : t("send", lang)}
                  </button>
                </div>
              </div>

              {hasFaqs ? (
                <div style={{ marginTop: 20 }}>
                  <div className="muted2" style={{ marginBottom: 10, fontSize: 13 }}>
                    {t("popularQuestions", lang)}
                  </div>
                  <div className="suggestions" aria-label="Suggestions">
                    {faqs.map((faq) => (
                      <button
                        key={faq.faqId}
                        className="suggestion"
                        onClick={() => send(faq.question || faq.title)}
                        disabled={busy}
                      >
                        {faq.question || faq.title}
                      </button>
                    ))}
                  </div>
                </div>
              ) : (
                <div style={{ marginTop: 20, textAlign: "center" }}>
                  <div className="muted2" style={{ fontSize: 13 }}>
                    {t("noFaqsYet", lang)}
                  </div>
                </div>
              )}

              <div className="chips" style={{ marginTop: 20 }}>
                <span className="chip">PostgreSQL</span>
                <span className="chip">Qdrant</span>
                <span className="chip">OpenAI</span>
              </div>
            </div>
          </div>
        ) : (
          <div className="chatShell" style={{ marginTop: 14 }}>
            <div className="chatWindow" aria-label="chat" ref={chatRef}>
              <div className="chatStream">
                {messages.map((m, i) => (
                  <div key={i} className={`msg ${m.role === "user" ? "user" : "assistant"}`}>
                    {m.role === "assistant" ? (
                      m.text ? (
                        <div className="markdown-content">
                          {renderMarkdown(m.text)}
                          {busy && i === messages.length - 1 && (
                            <span className="cursor">|</span>
                          )}
                        </div>
                      ) : (
                        busy && i === messages.length - 1 && currentStage ? (
                          <span className="muted2">{currentStage}</span>
                        ) : null
                      )
                    ) : (
                      m.text
                    )}
                  </div>
                ))}
              </div>
            </div>

            <div className="composer">
              <textarea
                value={text}
                onChange={(e) => setText(e.target.value)}
                placeholder={t("messagePlaceholder", lang)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    send();
                  }
                }}
              />
              <button className="primary" onClick={() => send()} disabled={busy}>
                {busy ? t("processing", lang) : t("send", lang)}
              </button>
            </div>

            {citations.length ? (
              <div className="hintRow">
                <div className="muted">
                  <strong>{t("citations", lang)}:</strong>
                </div>
                <div className="chips">
                  {citations.map((c, idx) => (
                    <span key={idx} className="chip" title={c.chunkId ?? undefined}>
                      {c.label}
                    </span>
                  ))}
                </div>
              </div>
            ) : null}
          </div>
        )}
      </div>
    </div>
  );
}
