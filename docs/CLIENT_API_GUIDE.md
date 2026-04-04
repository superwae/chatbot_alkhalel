# Municipality Chatbot API - Integration Guide

## Endpoint

```
POST http://egate.hebron-city.ps:8123/api/chat/public
```

## Headers

```
Content-Type: application/json
```

## Request Body

```json
{
  "Message": "بدي اقدم شكوى",
  "Lang": "ar",
  "SessionId": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Message` | string | **Yes** | The user's message |
| `Lang` | string | No | `"ar"` or `"en"` (auto-detected if omitted) |
| `SessionId` | string (GUID) | No | `null` for first message. **Must send back the `sessionId` from the response in all follow-up messages** |
| `UserToken` | string | No | Citizen auth token (for services that require login, e.g. fees lookup) |

> **⚠️ IMPORTANT:** Property names are **PascalCase** (`Message`, not `message`).

## Response

```json
{
  "sessionId": "db380920-290b-46b6-96aa-2fc1ef66d69c",
  "route": "API",
  "answer": "الإجابة هنا",
  "citations": [],
  "followUpQuestion": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `sessionId` | string (GUID) | Session ID — **send this back in the next request** |
| `route` | string | Which route was used (`FAQ`, `API`, `RAG`, `GENERAL`) |
| `answer` | string | The bot's answer (may contain URLs as plain text — render them as links in your app) |
| `citations` | array | Source references (if any) |
| `followUpQuestion` | string or null | If not null, show this instead of `answer` — the bot is asking the user for more info |

## Multi-Turn Flow (Critical)

The chatbot supports multi-turn conversations (e.g. complaints, follow-up questions). For this to work:

1. First request → send `SessionId: null`
2. Get response → save the `sessionId` from the response
3. All next requests → send that same `sessionId` back

**If you don't send `SessionId` back, every message is treated as a new conversation and multi-turn flows (complaints, follow-ups) will not work.**

## Example: Full Complaint Flow

### Step 1 — User starts complaint
```json
// Request
{ "Message": "بدي اقدم شكوى", "Lang": "ar", "SessionId": null }

// Response
{
  "sessionId": "db380920-290b-46b6-96aa-2fc1ef66d69c",
  "answer": "",
  "followUpQuestion": "بالتأكيد 👍 ما هي المشكلة التي تواجهها وأين موقعها؟"
}
```

### Step 2 — User provides details (send sessionId back!)
```json
// Request
{ "Message": "مياه في شارع السلام", "Lang": "ar", "SessionId": "db380920-290b-46b6-96aa-2fc1ef66d69c" }

// Response — bot asks for more details or shows confirmation
```

### Step 3, 4... — Continue until complaint is submitted
Always send the same `SessionId` in every request.

## Display Logic

In your app, check the response and display:

```
if (followUpQuestion != null)
    show followUpQuestion
else
    show answer
```

If `answer` contains URLs (e.g. `https://www.hebron-city.ps`), render them as clickable links.

## Error Handling

| Status | Meaning |
|--------|---------|
| 200 | Success |
| 400 | Bad request — check property names (PascalCase) and that `Message` is not empty |
| 429 | Rate limited — wait and retry |
| 500 | Server error — retry later |
