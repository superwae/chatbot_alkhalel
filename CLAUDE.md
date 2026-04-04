# Municipality Chatbot - Project Context

> **IMPORTANT**: This file MUST be updated whenever changes are made to the project.
> Last updated: 2026-02-22

## Project Overview

A bilingual (Arabic/English) AI chatbot for Hebron Municipality that helps citizens with:
- Answering FAQs
- Looking up real-time information via APIs (pharmacies, water schedule)
- Submitting complaints
- Document-based Q&A (RAG)

## Tech Stack

### Backend (.NET 10)
- **Framework**: ASP.NET Core Web API
- **Database**: PostgreSQL with Entity Framework Core
- **Vector DB**: Qdrant (semantic search)
- **LLM Providers**: OpenAI or Google Gemini (configurable)

### Frontend (React + TypeScript)
- **Build Tool**: Vite
- **Routing**: React Router
- **Styling**: Custom CSS (no framework)

## Project Structure

```
CHATBOT/
├── backend/
│   └── src/
│       ├── MunicipalityChatbot.Api/          # Controllers, orchestration
│       ├── MunicipalityChatbot.Application/  # Interfaces, models
│       ├── MunicipalityChatbot.Domain/       # Entities
│       └── MunicipalityChatbot.Infrastructure/ # Repositories, services
├── frontend/
│   └── src/
│       ├── pages/          # React components
│       └── lib/            # Utilities (api.ts, i18n.ts)
├── docs/                   # Documentation
│   ├── SERVER_COMMANDS.md  # Server management commands
│   ├── WIDGET.md           # Widget embedding guide
│   ├── DEPLOY.md           # Deployment guide
│   └── LOCAL_DEV.md        # Local development
├── prompts/                # LLM prompt templates
├── data/                   # Seed data files (docx, pdf)
└── CLAUDE.md               # THIS FILE
```

## Chat Flow Architecture

```
User Message → Embed → Vector Search (Qdrant)
                          ├─ FAQ candidates (top 5)
                          └─ Document chunks (top 8)
                                    ↓
                          Planner LLM Decision
                                    ↓
              ┌─────────────────────┼─────────────────────┐
              ↓                     ↓                     ↓
           FAQ Route            API Route            RAG/GENERAL
              ↓                     ↓                     ↓
        Return exact          Execute API           LLM generates
        FAQ.Answer            → Format result        answer from
        (no API call)         with LLM               chunks/context
```

### CRITICAL: Routes are Mutually Exclusive
- **FAQ**: Returns pre-written answer directly. NO API calls.
- **API**: Executes external API, then LLM formats result.
- **RAG**: LLM synthesizes answer from document chunks.
- **GENERAL**: Free-form LLM response.

## Available APIs (Seeded)

| API Name | Method | Endpoint | Auth | Purpose |
|----------|--------|----------|------|---------|
| Pharmacies On Duty | GET | `/ai/pharmacies_on_duty` | None | List open pharmacies |
| Water Schedule | GET | `/api/WaterAPIController/Water_s_plan` | None | Water distribution dates |
| Submit Complaint | POST | `/api/MobileAPIController/PostCMComplaint4` | None | File citizen complaints |
| Citizen Fees | GET | `/api/e-payments/fees-by-customer-id` | UserToken | Citizen fees/taxes lookup (f_type: 1=unpaid, 2=paid, 3=scheduled, 4=frozen) |

Base URL: `http://192.168.100.2:8282` (Pharmacies, Water, Complaint)
Citizen Fees Base URL: `https://192.168.100.2` (same server, HTTPS port 443, requires SSL bypass in Docker)

**Server Network Note:** Due to firewall restrictions, the production server must use internal IP `192.168.100.2` instead of the public domain. The other APIs use `http://...:8282` (HTTP), while Citizen Fees uses `https://...` (HTTPS port 443) — same server, different app/port. The domain `egate.hebron-city.ps` resolves to `192.168.100.2` but times out from Docker; use the IP directly.

## FAQs (Seeded)

Only **informational FAQs** are seeded (ones that don't need API calls):

| FAQ | Language | Type |
|-----|----------|------|
| Working Hours | AR/EN | Informational |
| App Registration | AR/EN | Informational |
| Talk to Employee | AR/EN | Informational |

**Why no pharmacy/water FAQs?** Because FAQ route doesn't call APIs. Users asking about pharmacies or water schedule should be routed to API route, not FAQ.

## Database Seeding

On startup, the system seeds (if tables are empty):
1. **Admin user** - `admin` / `ChangeMeNow!` (dev only)
2. **FAQs** - From `FaqSeedData.json`
3. **APIs** - From `ApiSeedData.json`

### Configuration
```json
{
  "Seed": {
    "Admin": { "Enabled": true, "Username": "admin", "Password": "..." },
    "Faq": { "Enabled": true, "ExternalFilePath": "" },
    "Api": { "Enabled": true, "ExternalFilePath": "" }
  }
}
```

## Employee Portal

### Authentication
- JWT-based with role claims
- Token stored in localStorage
- Frontend validates token expiration client-side

### Roles
- `EmployeeAdmin` - Full access
- `EmployeeEditor` - CRUD (no delete)
- `EmployeeViewer` - Read only

### Protected Routes
All `/employee/*` routes require valid token. Invalid/expired tokens redirect to login.

## Key Files

| Purpose | File |
|---------|------|
| Chat orchestration | `backend/.../Api/ChatOrchestrator.cs` |
| Planner prompt | `prompts/classify_and_plan.prompt.txt` |
| RAG answer prompt | `prompts/rag_answer.prompt.txt` |
| API answer prompt | `prompts/api_answer.prompt.txt` |
| FAQ seed data | `backend/.../Config/FaqSeedData.json` |
| API seed data | `backend/.../Config/ApiSeedData.json` |
| Database init | `backend/.../Services/DatabaseInitializer.cs` |
| Token validation | `frontend/src/lib/api.ts` (isTokenValid) |
| Document extraction | `backend/.../Services/IngestionService.cs` |
| Vision OCR service | `backend/.../Services/VisionOcrService.cs` |
| PDF OCR helper | `backend/.../Services/PdfOcrHelper.cs` |
| Server commands | `docs/SERVER_COMMANDS.md` |
| Widget docs | `docs/WIDGET.md` |
| Chat test controller | `backend/.../Controllers/ChatTestController.cs` |
| Chat test frontend | `frontend/src/pages/portal/ChatTestPage.tsx` |

## Document Upload: PDF vs DOCX

### Recommendation: **DOCX is better**

| Feature | DOCX | PDF |
|---------|------|-----|
| Table extraction | ✅ Excellent (OpenXML) | ⚠️ Variable (layout-based) |
| Text structure | ✅ Preserved | ⚠️ May lose structure |
| Arabic text | ✅ Full support | ✅ Full support |
| Library used | DocumentFormat.OpenXml | UglyToad.PdfPig |

**Why DOCX wins:**
- Tables are properly structured in XML (rows/cells)
- Paragraphs maintain order
- No OCR needed

**PDF issues (mitigated by Vision OCR):**
- Tables become positional text (may scramble) — Vision OCR handles this
- Multi-column layouts can interleave text — Vision OCR handles this
- Scanned PDFs — Vision OCR extracts text from images

### Extraction Code
See `backend/.../Services/IngestionService.cs`:
- `ExtractTextFromDocx()` - extracts paragraphs + tables
- `ExtractTextFromPdfAsync()` - **always uses GPT-5.2 Vision OCR** when available; PdfPig as fallback only

### PDF Vision OCR Pipeline
```
PDF Upload → Render each page to PNG (200 DPI) via PDFtoImage
           → Send to GPT-5.2 Vision API (base64 image)
           → Extract Arabic/English text
           → Chunk → Embed → Store in Qdrant + PostgreSQL
```
- Library: `PDFtoImage` (NuGet) + `VisionOcrService` (custom)
- Config: `OPENAI__VISION_MODEL` env var (default: `gpt-5.2`)
- Applies to both document uploads AND website crawler PDFs
- Re-uploading same filename auto-deletes old chunks first

## Embeddable Widget

The chatbot can be embedded on external websites using a JavaScript widget.

### Widget Files
- **Widget JS**: `backend/.../wwwroot/widget/chatbot-widget.js`
- **Example page**: `backend/.../wwwroot/widget/example.html`
- **Full documentation**: `docs/WIDGET.md`

### Embedding the Widget

Add to client's website before `</body>`:

```html
<script>
  window.MunicipalityChatbotConfig = {
    apiUrl: 'https://YOUR_SERVER_URL',
    // apiKey: 'optional-widget-api-key',
    // userToken: 'citizen-session-token',  // For APIs requiring auth (e.g. Citizen Fees)
    position: 'bottom-right',  // or 'bottom-left'
    themeColor: '#0066cc',
    defaultLang: 'ar',         // 'ar' or 'en'
    title: 'Municipality Assistant',
    titleAr: 'مساعد البلدية'
  };
</script>
<script src="https://YOUR_SERVER_URL/widget/chatbot-widget.js"></script>
```

### Server Configuration for Widgets

```bash
# Allow specific domains (comma-separated)
Cors__WidgetAllowedOrigins=https://client-site.com,https://another-site.com

# Or allow all domains (for testing)
Cors__WidgetAllowedOrigins=*

# Optional: Require API key for widget access
WIDGET__API_KEY=your-secret-key
```

### JavaScript API

```javascript
MunicipalityChatbot.open();                  // Open chat window
MunicipalityChatbot.close();                 // Close chat window
MunicipalityChatbot.toggle();                // Toggle chat window
MunicipalityChatbot.setLanguage('ar');       // Set language
MunicipalityChatbot.sendMessage('..');       // Send message programmatically
MunicipalityChatbot.setUserToken('token');   // Set/update citizen auth token (for APIs requiring auth)
```

## Test Questions

See `data/TEST_QUESTIONS.md` for comprehensive test scenarios including:
- FAQ routing tests
- API routing tests
- Unsupported query handling
- Expected answers for verification

## Recent Changes (Session Log)

### 2026-02-28
- [x] Added Vision OCR for scanned PDFs — uses GPT-5.2 Vision API to extract text from PDF page images. Handles scanned, mixed, and text PDFs. Uses `PDFtoImage` NuGet for page rendering, `VisionOcrService` for GPT-5.2 API calls
- [x] Always uses Vision OCR for ALL PDFs (not just scanned) — better Arabic text extraction than PdfPig. PdfPig kept as fallback only when OCR service unavailable
- [x] OCR applies to both document uploads (IngestionService) and website crawler (WebsiteCrawlerService)
- [x] Added `IOcrService` interface + `VisionOcrService` implementation + `PdfOcrHelper` shared helper
- [x] Added `VisionModel` to `OpenAiOptions` — configurable via `OPENAI__VISION_MODEL` env var, defaults to `gpt-5.2`
- [x] Added re-upload deduplication — when uploading a file with same filename, old document + chunks are deleted from both PostgreSQL and Qdrant before creating new ones
- [x] Added `FindByFilenameAsync` to `IDocumentRepository` for dedup lookup
- [x] Added ~50 test questions for new PDF laws/regulations — covers building tax, city planning, expropriation, electricity, local authorities, crafts, building regulations, waste fees, vehicle parking
- [x] Expanded test suite from ~58 to ~108 questions total

### 2026-02-22
- [x] Fixed Citizen Fees API — correct URL is `https://egate.hebron-city.ps` (not `192.168.100.2`), Bearer token only, no `customer_id` needed
- [x] Removed `customer_id` injection from ChatOrchestrator and ChatTestController — fees API identifies user by token alone
- [x] Updated ApiSeedData.json — new baseUrl, lowercase `f_type` query param, detailed responseHandlingNotes with field descriptions
- [x] Added all f_type scenarios to planner prompt — specific examples for paid (f_type=2), scheduled (f_type=3), frozen (f_type=4) queries
- [x] Added fee type mentioning for generic queries — when user asks "كم ذممي" (generic), bot shows unpaid fees AND mentions other available types (paid, scheduled, frozen). Updated both api_answer prompt and responseHandlingNotes
- [x] Added 5 new fees test cases — covers unpaid explicit, paid, scheduled, frozen, and general account inquiry
- [x] Expanded test suite to ~58 questions total
- [x] Added SSL bypass for ApiHttpClient — `DangerousAcceptAnyServerCertificateValidator` in Program.cs so Docker container can connect to HTTPS APIs with untrusted/self-signed certificates
- [x] Added `UserToken` to Auth Type dropdown in IntegrationsPage.tsx — was missing from the dropdown options (only had None, ApiKey, BearerToken, Basic)
- [x] Updated i18n authTypeHint to include UserToken in both EN and AR
- [x] Added AI answer preview in test results table — shows 2-line truncated answer below each question without needing to expand details; shows followUpQuestion in italic if no finalAnswer
- [x] Removed `customerId` from ChatTestController and ChatTestPage — fees API identifies user by token alone, no separate customer ID needed
- [x] **RESOLVED: Citizen Fees API connectivity** — `egate.hebron-city.ps` resolves to `192.168.100.2` (same server). Working URL from Docker: `https://192.168.100.2/api/e-payments/fees-by-customer-id`. PowerShell on host failed due to old TLS, but .NET HttpClient with SSL bypass works. Updated seed data baseUrl to `https://192.168.100.2`
- [x] Added Fees API URL scanner — "Test Fees API" button in test page tries all host/scheme/port/path combinations from inside Docker, shows results sorted by success. Found that HTTPS port 443 and port 8080 both work

### 2026-02-20
- [x] Fixed citizen fees keyword over-matching — "رسوم النفايات حسب النظام" and "براءة ذمة" were routed to Citizen Fees API instead of RAG. Added exclusion list (حسب النظام, براءة, شهادة, إجراءات, قانون) and made keywords more specific (require personal context like "كم ذم", "رصيدي", "بدي ادفع")
- [x] Added citizen fees personal vs regulatory distinction to planner prompt — explicit examples showing "رسوم حسب النظام" = RAG vs "كم ذممي" = API
- [x] Fixed complaint type "مرور" not recognized — added AVAILABLE CATEGORIES list to planner prompt with instruction to tell user when category doesn't exist and list alternatives
- [x] Added RAG follow-up recognition to planner prompt — "عدد الطوابق 6" after building law question now recognized as RAG continuation instead of new unrelated message

### 2026-02-21
- [x] Updated complaint follow-up to show available categories — when user says "أريد تقديم شكوى" without specifying a type, the response now lists all 6 categories (نفايات، صرف صحي، مياه، إنارة، طرق، كهرباء)
- [x] Expanded test suite from ~45 to ~52 questions — added citizen fees vs RAG distinction, protest messages, false FAQ matches, unknown complaint categories
- [x] Added UserToken support to test controller — `ChatTestRequest` accepts optional `userToken`, passes to API execution for Citizen Fees testing
- [x] Added UserToken input to test page UI — employee can paste JWT token before running tests, fees tests execute with real API data
- [x] Replaced copy output with debug report — "Copy Debug Report" generates human-readable text with wrong routing section first, all results with FAQ/chunk scores, planner JSON for wrong routes
- [x] Added copy confirmation feedback in test UI
- [x] Fixed Citizen Fees API base URL — changed from `https://192.168.100.2` (SSL error) to `http://192.168.100.2` (HTTP)
- [x] Added `CustomerId` support through full pipeline — `PublicChatRequest` → `ChatController` → `ChatOrchestrator` → injected as `customer_id` query param for UserToken APIs. Widget also supports `customerId` config and `setCustomerId()` API
- [x] Fixed website chunk/DB mismatch (band-aid) — website chunks were stored only in Qdrant (not PostgreSQL), causing `[No chunks found in DB]` errors when planner selected them for RAG. Added Qdrant payload text fallback as interim fix
- [x] Added Customer ID input to test page UI alongside User Token — for testing Citizen Fees API with specific customer
- [x] **Fixed root cause of website chunk/DB mismatch** — `WebsiteCrawlerService.ProcessPageAsync()` now stores chunks in BOTH Qdrant and PostgreSQL (as `DocumentChunk` entities with `DocId=PageId`, `FileType="website"`). When re-crawling, also deletes old chunks from PostgreSQL. The Qdrant payload fallback remains as a safety net but should no longer trigger after re-crawling
- [x] Fixed streaming path missing website chunk search — `HandlePublicStreamAsync` only searched `doc_chunk` in Qdrant, never searched `website` chunks. Added `websiteHits` search and combined with doc chunks (matching non-streaming path behavior). This means website content (like mayor info) was invisible to the streaming chat path

### 2026-02-17
- [x] Fixed FAQ safety net false matches — raised threshold from 0.5 to 0.72 cosine similarity. "ما هو موقع البلدية" no longer matches "ساعات العمل" FAQ
- [x] Added `IsGreeting()` safety net — greetings like "صباح الخير", "كيف حالك", "hi" ALWAYS route to GENERAL regardless of conversation history. Fixes "صباح الخير" → Pharmacies API bug
- [x] Added `IsProtestMessage()` safety net — "شو دخل الصيدليات", "لم اسأل عن X" route to GENERAL. Fixes users being stuck in wrong API context
- [x] Added negation guard to `MatchGetApiKeywords()` — "لم اسأل عن الصيدليات" no longer matches pharmacy keywords (negation prefixes checked first)
- [x] Added citizen fees keyword detection — "ذمم", "ذمة", "رصيد", "رسوم" now route to Citizen Fees API via safety net
- [x] Improved planner prompt: greeting rules strengthened (explicit greeting word list, "even mid-conversation" rule), protest handling added, citizen fees API examples added
- [x] Improved planner prompt FAQ matching: changed from "trust the retrieval system" to "TOPIC MUST MATCH" with explicit correct/wrong match examples. High vector score ≠ topic match
- [x] Added greeting gate to FAQ safety net — greetings can no longer be overridden to FAQ even with high vector scores
- [x] Fixed appsettings.json: `chatgpt-4o-mini` → `gpt-4o-mini` (correct OpenAI model ID)
- [x] Added GPT-5 model compatibility — GPT-5+ models don't support `temperature` parameter, so `ChatCompletionAsync` and `StreamChatCompletionAsync` now conditionally omit it via `IsGpt5Model()` check. Also covers o3/o4 reasoning models.

### 2026-02-16
- [x] Added dual-model support — planner uses `gpt-4o` (better reasoning), answers use `gpt-4o-mini` (cheaper). Configurable via `OPENAI__PLANNER_MODEL` env var. Default: `gpt-4o` for planner, `gpt-4o-mini` for everything else
- [x] Also fixed default Model from `chatgpt-4o-mini` to `gpt-4o-mini` (correct model ID)
- [x] Fixed FAQ safety net misrouting personal statements — "انا اسمي اسامة" was routed to FAQ (90% score) because embeddings matched "registration". Added `IsQuestionOrRequest()` gate so FAQ safety net only overrides GENERAL→FAQ when the message contains question words, request words, or a question mark
- [x] Fixed complaint corrections showing old data — planner often copies old body params instead of updating corrected fields. Added `ApplyCorrectionToBodyParams()` server-side enforcement that detects correction messages and updates the specific field (type, phone, location) in body params before regenerating the confirmation card
- [x] Changed confirmation card summary to always use `GenerateComplaintSummary()` from body params instead of trusting the planner's potentially stale `PendingSubmissionSummary`

### 2026-02-15
- [x] Added server-side POST API confirmation enforcement - complaints were submitted without user confirmation when planner skipped `requiresConfirmation=true` or set `userConfirmed=true` directly. Now the backend checks if a confirmation card was actually shown before allowing POST execution
- [x] Added `GenerateComplaintSummary()` helper - generates confirmation card from body params when planner doesn't provide `PendingSubmissionSummary`
- [x] Completed single-port deployment setup - removed backend `ports: 8080:8080` from docker-compose.prod.yml, added `/widget/` proxy to nginx.conf. Only port 8123 exposed publicly.

### 2026-02-14 (session 2)
- [x] Fixed context stickiness - after water schedule conversation, "بدي ابعت شكوى" and "مشكلة صرف صحي" were misrouted to water API. Added complaint keyword safety net (`IsComplaintInitiation`, `IsInComplaintFlow`, `GenerateComplaintFollowUp`) that overrides GET API misroutes to complaint POST API
- [x] Fixed complaint detail editing - corrections like "نوع الشكوى غلط" were cancelled by Safety net 0 because "غلط" matched rejection patterns. Added `IsCorrectionMessage()` that detects field corrections (غلط + field reference, قصدي, بدي اغير) and bypasses rejection
- [x] Fixed language switching to English for "no cancel" - expanded language override to detect rejection/confirmation words (`IsRejectionMessage` / `ConfirmationWords`) in addition to `IsNonTextMessage`, preserving Arabic from conversation history
- [x] Strengthened planner prompt NEW TOPIC DETECTION - added explicit complaint-after-water examples, "مشكلة صرف صحي ≠ water distribution" rule, complaint keyword rules
- [x] Strengthened planner prompt PATTERN 3 (corrections) - added CORRECTION vs REJECTION distinction, after-confirmation-card examples, "re-show UPDATED confirmation card" instruction
- [x] Expanded `IsCorrectionMessage` to detect "لا + هو/هي" replacement patterns, field word variants/typos (رقم/رفم), and phone number patterns after "لا"
- [x] Fixed planner missing phone numbers in combined messages - added "COMBINED MESSAGES" extraction guidance (location + number in one message)
- [x] Added LATEST VALUE RULE to planner - always use the most recent value when a field is provided multiple times
- [x] Added CONTEXT-AWARE EXTRACTION rule - when bot asks for specific field, user's response IS that field
- [x] Fixed planner auto-filling NOTES with just complaint type - added "NOTES MUST BE REAL DETAILS" rule, bot must ask for actual description

### 2026-02-14 (session 1)
- [x] Added routing safety net in ChatOrchestrator - overrides GENERAL→FAQ when top FAQ score >0.5, GENERAL→RAG when top chunk score >0.35
- [x] Lowered RAG safety net threshold from 0.4 to 0.35 to catch borderline cases (e.g., water subscription RAG)
- [x] Fixed planner ignoring high-scoring FAQ candidates (score 0.56+) by adding trust-the-retrieval-system guidance
- [x] Fixed planner ignoring relevant document chunks (score 0.44+) by adding anti-GENERAL bias rules
- [x] Added NEW TOPIC DETECTION to planner prompt - prevents conversation history from overriding clear API keyword matches (e.g., "متى موعد المياه" after water subscription discussion)
- [x] Fixed false confirmation in complaint flow - "ليش اقدم شكوى" was treated as confirmation. Added explicit rejection/confusion patterns (ليش, ما بدي, غلط, etc.)
- [x] Added server-side rejection detection - RejectionPatterns checked BEFORE ConfirmationWords in IsConfirmationMessage
- [x] Fixed streaming docCandidates missing score field (inconsistent with non-streaming flow)
- [x] Fixed clipboard copy button for HTTP sites - added fallback using document.execCommand('copy')
- [x] Added hardcoded API keyword detection (`MatchGetApiKeywords`) in ChatOrchestrator - catches water schedule and pharmacy keywords regardless of planner decision
- [x] Fixed RAG answer prompt rejecting valid document chunks - `rag_answer.prompt.txt` CHECK RELEVANCE was too strict, rejecting employee-format docs as irrelevant. Changed to "be GENEROUS, not strict" and added guidance for extracting citizen-useful info from internal-format documents
- [x] Fixed complaint cancellation not working - added server-side rejection detection (Safety net 0) that overrides route to GENERAL when user rejects confirmation card ("لا", "بطلت", "cancel", etc.)
- [x] Added more rejection patterns: "بطلت", "خلص", "خلاص", "كنسل", "الغي", "بلاش", "مش عايز", "nevermind", "forget it"
- [x] Fixed language switching to English for phone numbers - `IsNonTextMessage()` detects numeric-only messages and preserves Arabic from conversation history
- [x] Changed frontend port from 80 to 8123 in docker-compose.prod.yml
- [x] Expanded test suite from 25 to 45 questions - added water schedule variants, complaint cancellation, citizen fees (كم ذممي), RAG procedures, chitchat, out-of-scope, municipality info, real user questions from history
- [x] Added 1.5s delay between test questions to prevent 429 rate limiting from LLM provider
- [x] Added "جدول توزيع" to water schedule keyword detection (catches "ما هو جدول توزيع المايه" with typo)
- [x] Strengthened planner prompt rejection handling - added "بطلت/خلص/بلاش" to rejection lists, added rule "NEVER re-show confirmation card when user rejects"

### 2026-02-13
- [x] Fixed Water Schedule API area matching - LLM was failing to find areas in PLAN_TEXT substring (e.g., "وادي الجوز" inside long comma-separated text). Added explicit PLAN_TEXT format explanation, double-check instruction, and general query handling to `api_answer.prompt.txt`
- [x] Fixed misrouting of service requests to complaint API - Added "2b. SERVICE REQUESTS vs COMPLAINTS" section to planner prompt. "طلب اشتراك مياه/كهرباء" (subscription requests) now route to RAG/GENERAL instead of complaint API
- [x] Improved complaint flow to be more conversational - Follow-up questions now ask 1-2 things at a time instead of dumping all 4 fields in a numbered list
- [x] Added intelligent field extraction for complaints - Planner now correctly maps location text to LOCATION, description to NOTES, etc. instead of dumping everything into NOTES
- [x] Fixed complaint correction handling - When user says "لا قصدي..." (correction), planner now updates the specific field instead of treating correction as new NOTES
- [x] Added multi-turn complaint flow recognition - Planner now stays in API route during complaint data collection even if user's short response could be misrouted
- [x] Fixed routing priority numbering in planner prompt (FAQ was "3", now correctly "4")
- [x] Created Chat Test Suite - employee portal test page (`/employee/test`) runs 25 questions through full pipeline with detailed logging
- [x] Created `ChatTestController.cs` backend endpoint (`POST /api/chat-test/run`) - runs test questions through embed → vector search → planner → answer with timing and intermediate data capture

### 2026-02-07
- [x] Added Citizen Fees API to seed data (`/api/e-payments/fees-by-customer-id`, F_TYPE param)
- [x] Changed all API base URLs from `egate.hebron-city.ps:8282` to `192.168.100.2:8282`
- [x] Added `UserToken` auth type - forwards citizen's session token to external APIs that require it
- [x] Updated widget to accept `userToken` config and `setUserToken()` dynamic API
- [x] Updated `PublicChatRequest` DTO with optional `UserToken` field
- [x] Token forwarding: widget/frontend -> ChatController -> ChatOrchestrator -> ApiExecutionService -> external API

### 2026-01-31
- [x] Fixed API retry bug - HttpRequestMessage cannot be reused across retries
- [x] Fixed empty API response handling - graceful error messages
- [x] Added non-municipality service filtering (driving license, passport, etc.)
- [x] Fixed LLM hallucination in RAG responses - updated prompts to verify chunk relevance
- [x] Improved Water API response handling for areas not in schedule
- [x] Fixed API integrations page - better error display, auto-sync allowlistedDomain
- [x] Created comprehensive `docs/SERVER_COMMANDS.md` with all server management commands
- [x] Created `docs/WIDGET.md` widget embedding documentation
- [x] Organized docs/ folder structure
- [x] Deleted redundant files (CLEANUP.md, widget-test.html, migration.sql)
- [x] Moved DEPLOY.md and START.md to docs/
- [x] Added PDF support to website crawler (using PdfPig library)
- [x] Fixed ASP.NET WebForms content extraction (removed `//form` from nodesToRemove)
- [x] Added detailed skip logging to website crawler (shows titles and reasons)
- [x] Documented Qdrant "too many open files" fix in SERVER_COMMANDS.md

### 2026-01-24
- [x] Created FAQ seeding system with `FaqSeedData.json`
- [x] Created API seeding system with `ApiSeedData.json`
- [x] Fixed DOCX table extraction (was only extracting paragraphs)
- [x] Fixed employee portal - added JWT expiration validation
- [x] Removed FAQ entries that promised API calls (FAQ route can't call APIs)
- [x] Created this CLAUDE.md file
- [x] Created `data/TEST_QUESTIONS.md` with test scenarios
- [x] Deleted accidental `nul` file (see Known Issues)
- [x] Added conversation history support for multi-turn chat context
- [x] Created embeddable widget for external sites (`/widget/chatbot-widget.js`)
- [x] Added static file serving for widget assets
- [x] Fixed CORS/Widget env var binding for `Cors__WidgetAllowedOrigins`

## TODO / Known Issues

### Missing APIs (from original FAQ document)
The customer's FAQ document expects these APIs that don't exist:
- [ ] Request status lookup by number (االستفسار عن طلب معين)
- [ ] Water bill inquiry by subscription (فاتورة المياه)
- [ ] Water tank request status (طلب تنك)
- [ ] Remaining installments lookup (األقساط المتبقية)
- [ ] Power outage information (انقطاع كهربائي)

### Pending Features
- [ ] Complaint submission flow needs testing with real API
- [ ] Document upload for knowledge base (UI exists, needs testing)
- [ ] Analytics dashboard data

### Technical Debt
- [ ] Add rate limiting configuration to docs
- [ ] Add health check endpoint
- [ ] Consider caching FAQ/API definitions

### Known Bug: `nul` File Creation (Windows)
**Problem:** A file named `nul` was created in the project root.

**Cause:** On Windows, `nul` is a reserved device name (like `/dev/null` on Linux). If a shell command uses `> nul` to discard output but runs in a Unix-like shell (Git Bash, WSL), it creates an actual file instead of discarding.

**Prevention:**
- Use `/dev/null` in bash scripts, not `nul`
- Or use `2>&1 | Out-Null` in PowerShell
- The file was deleted and is harmless if it reappears

### Known Issue: Qdrant "Too Many Open Files"
**Problem:** Website crawler fails with 500/502 errors. Qdrant logs show `Too many open files (os error 24)`.

**Quick Fix:** Full restart - `docker compose -f docker-compose.prod.yml down && docker compose -f docker-compose.prod.yml up -d`

**Permanent Fix:** Add ulimits to Qdrant service in docker-compose.prod.yml. See `docs/SERVER_COMMANDS.md` for details.

## Running the Project

### Backend
```bash
cd backend/src/MunicipalityChatbot.Api
dotnet run
```

### Frontend
```bash
cd frontend
npm install
npm run dev
```

### Required Environment Variables
```
# Database
Postgres__ConnectionString=Host=localhost;Database=chatbot;Username=...

# LLM (choose one)
Llm__Provider=OpenAI  # or Gemini
OpenAi__ApiKey=sk-...
# OR
Gemini__ApiKey=...

# Vector DB
Qdrant__Url=http://localhost:6333

# JWT
Jwt__SigningKey=your-secret-key-here
```

## Data Files Location

Customer-provided files in `/data/`:
- `__ملحق طلبات للردا لالي_.docx` - Original FAQ questions (Arabic)
- `chat bot answer.docx` - Additional content
- `Public_APIs_Documentation.pdf` - API documentation

---

**Remember: Update this file when making changes!**
