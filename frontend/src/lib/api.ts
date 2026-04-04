import type { Lang } from "./i18n";

// In production, use empty string (nginx proxies /api to backend)
// In development, can set VITE_API_BASE_URL=http://localhost:8080
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "";

export type Citation = { label: string; chunkId?: string | null };
export type PublicChatResponse = {
  sessionId: string;
  route: "FAQ" | "RAG" | "GENERAL" | "API";
  answer: string;
  citations: Citation[];
  followUpQuestion?: string | null;
};

export async function publicChat(message: string, lang: Lang, sessionId?: string | null, userToken?: string | null): Promise<PublicChatResponse> {
  const body: Record<string, unknown> = { message, lang, sessionId };
  if (userToken) body.userToken = userToken;
  const res = await fetch(`${API_BASE_URL}/api/chat/public`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export type StreamEvent = {
  type: "stage" | "meta" | "chunk" | "error";
  sessionId?: string | null;
  route?: string | null;
  citations?: Citation[] | null;
  followUpQuestion?: string | null;
  content?: string | null;
  error?: string | null;
  stage?: string | null;  // Current processing stage for progress indicator
};

export async function* publicChatStream(
  message: string,
  lang: Lang,
  sessionId?: string | null,
  userToken?: string | null
): AsyncGenerator<StreamEvent> {
  const body: Record<string, unknown> = { message, lang, sessionId };
  if (userToken) body.userToken = userToken;
  const res = await fetch(`${API_BASE_URL}/api/chat/public/stream`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!res.ok) {
    yield { type: "error", error: await res.text() };
    return;
  }

  const reader = res.body?.getReader();
  if (!reader) {
    yield { type: "error", error: "No response body" };
    return;
  }

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() || "";

    for (const line of lines) {
      if (!line.startsWith("data: ")) continue;
      const data = line.slice(6).trim();
      if (data === "[DONE]") return;

      try {
        const evt = JSON.parse(data) as StreamEvent;
        yield evt;
      } catch {
        // Skip malformed events
      }
    }
  }
}

export async function employeeLogin(username: string, password: string): Promise<string> {
  const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) throw new Error("Login failed");
  const data = await res.json();
  return data.accessToken as string;
}

export async function authedGet(path: string, token: string) {
  // Add cache-busting to prevent stale data
  const separator = path.includes("?") ? "&" : "?";
  const url = `${API_BASE_URL}${path}${separator}_t=${Date.now()}`;
  const res = await fetch(url, {
    headers: {
      Authorization: `Bearer ${token}`,
      "Cache-Control": "no-cache",
    },
  });
  if (!res.ok) {
    const errorText = await res.text();
    throw new Error(errorText || `Request failed with status ${res.status}`);
  }
  return res.json();
}

export async function authedPost(path: string, token: string, body: any) {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const errorText = await res.text();
    throw new Error(errorText || `Request failed with status ${res.status}`);
  }
  return res.json();
}

export async function authedDelete(path: string, token: string) {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) throw new Error(await res.text());
  return res;
}

/** Decode JWT payload without verification (client-side only) */
function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const parts = token.split(".");
    if (parts.length !== 3) return null;
    const payload = parts[1];
    const decoded = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    return JSON.parse(decoded);
  } catch {
    return null;
  }
}

/** Check if token is valid (exists and not expired) */
export function isTokenValid(token: string | null): boolean {
  if (!token) return false;

  const payload = decodeJwtPayload(token);
  if (!payload) return false;

  // Check expiration
  const exp = payload.exp as number | undefined;
  if (!exp) return false;

  // exp is in seconds, Date.now() is in milliseconds
  const nowSeconds = Math.floor(Date.now() / 1000);
  return exp > nowSeconds;
}

/** Get token role from JWT payload */
export function getTokenRole(token: string | null): string | null {
  if (!token) return null;

  const payload = decodeJwtPayload(token);
  if (!payload) return null;

  // Role is typically stored in "role" claim or as an array in "roles"
  const role = payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] as string | undefined;
  return role || (payload.role as string | undefined) || null;
}

