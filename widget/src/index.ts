type Lang = "en" | "ar";
type Position = "bottom-right" | "bottom-left";

export type MunicipalityChatbotInitOptions = {
  apiBaseUrl: string;
  lang?: Lang;
  position?: Position;
  widgetApiKey?: string;
  title?: string;
};

type ChatResponse = {
  sessionId: string;
  route: "FAQ" | "RAG" | "GENERAL" | "API";
  answer: string;
  citations: { label: string; chunkId?: string | null }[];
  followUpQuestion?: string | null;
};

const i18n = {
  en: {
    title: "Municipality Chat",
    open: "Chat",
    close: "Close",
    placeholder: "Type your message…",
    send: "Send",
    language: "Language",
    route: "Route",
    citations: "Citations",
    followUp: "Follow-up",
  },
  ar: {
    title: "دردشة البلدية",
    open: "دردشة",
    close: "إغلاق",
    placeholder: "اكتب رسالتك…",
    send: "إرسال",
    language: "اللغة",
    route: "المسار",
    citations: "المراجع",
    followUp: "سؤال متابعة",
  },
} as const;

function css() {
  return `
  .mcb-root{all:initial;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;position:fixed;z-index:2147483647}
  .mcb-root *{box-sizing:border-box}
  .mcb-btn{all:unset;cursor:pointer;background:#2563eb;color:#fff;border-radius:999px;padding:12px 14px;font-size:14px;box-shadow:0 10px 25px rgba(0,0,0,.2)}
  .mcb-panel{width:340px;max-width:92vw;height:520px;max-height:80vh;background:#fff;border:1px solid #e2e8f0;border-radius:14px;box-shadow:0 16px 40px rgba(0,0,0,.18);overflow:hidden;display:flex;flex-direction:column}
  .mcb-header{display:flex;align-items:center;gap:8px;padding:10px 12px;border-bottom:1px solid #e2e8f0;background:#fff}
  .mcb-title{font-weight:700;font-size:14px;color:#0f172a;flex:1}
  .mcb-select{all:unset;border:1px solid #cbd5e1;border-radius:10px;padding:6px 8px;font-size:12px;color:#0f172a;background:#fff}
  .mcb-close{all:unset;cursor:pointer;border:1px solid #cbd5e1;border-radius:10px;padding:6px 8px;font-size:12px;color:#0f172a}
  .mcb-chat{flex:1;overflow:auto;padding:10px;background:#f8fafc;display:flex;flex-direction:column;gap:8px}
  .mcb-msg{max-width:85%;padding:10px 12px;border-radius:12px;border:1px solid #e2e8f0;background:#fff;color:#0f172a;white-space:pre-wrap;font-size:13px}
  .mcb-msg.user{margin-left:auto;background:#dbeafe;border-color:#bfdbfe}
  .mcb-meta{padding:8px 12px;border-top:1px solid #e2e8f0;background:#fff;font-size:11px;color:#64748b;display:flex;flex-direction:column;gap:6px}
  .mcb-inputRow{display:flex;gap:8px;padding:10px 12px;border-top:1px solid #e2e8f0;background:#fff}
  .mcb-input{all:unset;flex:1;border:1px solid #cbd5e1;border-radius:12px;padding:10px 12px;font-size:13px;color:#0f172a;background:#fff}
  .mcb-send{all:unset;cursor:pointer;background:#2563eb;color:#fff;border-radius:12px;padding:10px 12px;font-size:13px}
  .mcb-rtl{direction:rtl}
  `;
}

function makeEl<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string) {
  const el = document.createElement(tag);
  if (className) el.className = className;
  return el;
}

function setPos(root: HTMLElement, position: Position) {
  const inset = "18px";
  root.style.bottom = inset;
  if (position === "bottom-left") {
    root.style.left = inset;
    root.style.right = "auto";
  } else {
    root.style.right = inset;
    root.style.left = "auto";
  }
}

export function init(options: MunicipalityChatbotInitOptions) {
  if (!options?.apiBaseUrl) throw new Error("MunicipalityChatbot.init: apiBaseUrl is required");

  const position: Position = options.position ?? "bottom-right";
  let lang: Lang = options.lang ?? "en";
  let sessionId: string | null = null;

  const root = makeEl("div", "mcb-root");
  setPos(root, position);
  document.body.appendChild(root);

  const style = makeEl("style");
  style.textContent = css();
  document.head.appendChild(style);

  const btn = makeEl("button", "mcb-btn");
  const panel = makeEl("div", "mcb-panel");
  panel.style.display = "none";

  const header = makeEl("div", "mcb-header");
  const title = makeEl("div", "mcb-title");
  const langSelect = makeEl("select", "mcb-select") as HTMLSelectElement;
  const closeBtn = makeEl("button", "mcb-close");

  langSelect.innerHTML = `<option value="en">EN</option><option value="ar">AR</option>`;
  langSelect.value = lang;

  header.appendChild(title);
  header.appendChild(langSelect);
  header.appendChild(closeBtn);

  const chat = makeEl("div", "mcb-chat");
  const inputRow = makeEl("div", "mcb-inputRow");
  const input = makeEl("input", "mcb-input") as HTMLInputElement;
  const send = makeEl("button", "mcb-send");
  inputRow.appendChild(input);
  inputRow.appendChild(send);

  const meta = makeEl("div", "mcb-meta");

  panel.appendChild(header);
  panel.appendChild(chat);
  panel.appendChild(meta);
  panel.appendChild(inputRow);

  root.appendChild(btn);
  root.appendChild(panel);

  function applyLang() {
    const d = i18n[lang];
    title.textContent = options.title ?? d.title;
    btn.textContent = d.open;
    closeBtn.textContent = d.close;
    input.placeholder = d.placeholder;
    send.textContent = d.send;
    panel.classList.toggle("mcb-rtl", lang === "ar");
  }

  function addMsg(role: "user" | "assistant", text: string) {
    const msg = makeEl("div", `mcb-msg ${role === "user" ? "user" : "assistant"}`);
    msg.textContent = text;
    chat.appendChild(msg);
    chat.scrollTop = chat.scrollHeight;
  }

  function setMeta(route?: string, citations?: string[], followUp?: string | null) {
    const d = i18n[lang];
    const parts: string[] = [];
    if (route) parts.push(`${d.route}: ${route}`);
    if (followUp) parts.push(`${d.followUp}: ${followUp}`);
    if (citations && citations.length) parts.push(`${d.citations}: ${citations.join(" | ")}`);
    meta.textContent = parts.join("\n");
  }

  async function sendMsg() {
    const message = input.value.trim();
    if (!message) return;
    input.value = "";
    addMsg("user", message);
    setMeta();

    const res = await fetch(`${options.apiBaseUrl.replace(/\/+$/, "")}/api/chat/public`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(options.widgetApiKey ? { "X-Widget-Api-Key": options.widgetApiKey } : {}),
      },
      body: JSON.stringify({ message, lang, sessionId }),
    });

    if (!res.ok) {
      const txt = await res.text();
      addMsg("assistant", `Error: ${txt}`);
      return;
    }

    const data = (await res.json()) as ChatResponse;
    sessionId = data.sessionId;
    if (data.answer) addMsg("assistant", data.answer);
    setMeta(
      data.route,
      (data.citations ?? []).map((c) => c.label),
      data.followUpQuestion ?? null
    );
  }

  btn.addEventListener("click", () => {
    const open = panel.style.display === "none";
    panel.style.display = open ? "flex" : "none";
    btn.style.display = open ? "none" : "inline-block";
    if (open) input.focus();
  });

  closeBtn.addEventListener("click", () => {
    panel.style.display = "none";
    btn.style.display = "inline-block";
  });

  langSelect.addEventListener("change", () => {
    lang = (langSelect.value as Lang) ?? "en";
    applyLang();
  });

  send.addEventListener("click", sendMsg);
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") sendMsg();
  });

  applyLang();

  return {
    destroy() {
      root.remove();
      style.remove();
    },
  };
}

// UMD global
declare global {
  interface Window {
    MunicipalityChatbot?: { init: typeof init };
  }
}
if (typeof window !== "undefined") {
  window.MunicipalityChatbot = { init };
}

