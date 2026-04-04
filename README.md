## Municipality AI Chatbot (Monorepo)

Production-style scaffold for a bilingual (Arabic/English) municipality chatbot with:
- **Backend**: ASP.NET Core (.NET 10 LTS, compatible patterns)
- **Frontend**: React + TypeScript
- **Vector DB**: Qdrant
- **Relational DB**: PostgreSQL (local via compose or external)
- **Embeddable widget**: single `widget.js` you can inject into any website

### Repo layout

- **`backend/`**: Clean Architecture solution (`Api`, `Application`, `Domain`, `Infrastructure`)
- **`frontend/`**: React web app (Citizen Chat + Employee Portal)
- **`widget/`**: embeddable widget (vanilla TS → UMD `widget.js`)
- **`prompts/`**: OpenAI prompts (planner + RAG + API answer)
- **`env/env.example`**: environment variable template

### Configuration

Everything is configurable by **env vars** and **`appsettings.json`**.

- **Env var template**: `env/env.example`
- Key env vars:
  - **`LLM__PROVIDER`** (`OpenAI` | `Gemini`)
  - **`OPENAI__API_KEY`**
  - **`GEMINI__API_KEY`**
  - **`QDRANT__URL`** (compose uses `http://qdrant:6333`)
  - **`POSTGRES__CONNECTION_STRING`**
  - **`AUTH__JWT__SIGNING_KEY`**
  - **`CORS__ALLOWED_ORIGINS`**, **`CORS__WIDGET_ALLOWED_ORIGINS`**
  - **`WIDGET__API_KEY`** (optional)
  - **`RATELIMIT__PUBLIC_CHAT__PERMIT_LIMIT`**, **`RATELIMIT__PUBLIC_CHAT__WINDOW_SECONDS`**

### Local development (Docker)

This brings up:
- `qdrant` on `http://localhost:6333`
- `backend` on `http://localhost:8080` (Swagger at `/swagger`)
- `frontend` on `http://localhost:5173`
- `postgres` on `localhost:5432`

1) Set your env vars (copy from `env/env.example` into your shell or a local `.env` file)
2) Run:

```bash
docker compose up --build
```

### PostgreSQL setup (EF Core migrations)

Local dev (docker-compose) already starts Postgres. To create tables, run EF migrations:

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/MunicipalityChatbot.Infrastructure --startup-project src/MunicipalityChatbot.Api
```

Create an admin employee:

1) Generate a password hash:

```bash
dotnet run --project backend/tools/PasswordHashTool -- "ChangeMeNow!"
```

2) Insert a row into `employees` (example in **`backend/db/seed.sql`**).

### LLM provider switch (OpenAI vs Gemini)

Set:
- `LLM__PROVIDER=OpenAI` (default) and `OPENAI__API_KEY=...`, or
- `LLM__PROVIDER=Gemini` and `GEMINI__API_KEY=...`

### Backend endpoints

- **Public chat (anonymous)**: `POST /api/chat/public` (rate limited)
- **Employee login (JWT)**: `POST /api/auth/login`
- **FAQs CRUD**: `GET/POST/DELETE /api/faqs/*`
- **Documents upload/list**: `POST /api/documents/upload`, `GET /api/documents`
- **API Integrations CRUD**: `GET/POST/DELETE /api/integrations/*`
- **Analytics**: `GET /api/analytics/summary`

### Chat runtime flow (implemented)

Backend enforces:
- **Retrieval before classification** (Qdrant search for `type=faq` and `type=doc_chunk`)
- **Planner prompt outputs strict JSON**
- **Routing enforcement**
  - FAQ route returns **exact stored FAQ answer** (no rewriting in this scaffold)
  - RAG route sends only selected chunks to the RAG prompt (with citations)
  - GENERAL route returns a clearly-labeled general answer
  - API route executes only allowlisted, stored API definitions

### Embeddable widget

Build it:

```bash
cd widget
npm ci
npm run build
```

This produces **`widget/dist/widget.js`**.

Embed snippet:

```html
<script src="https://YOUR-CDN-OR-STATIC-HOST/widget.js"></script>
<script>
  MunicipalityChatbot.init({
    apiBaseUrl: "http://localhost:8080",
    lang: "ar",
    position: "bottom-right",
    // widgetApiKey: "optional"
  });
</script>
```

Widget features:
- Floating button + panel UI
- **Arabic/English toggle**
- **RTL layout for Arabic**
- Calls backend `POST /api/chat/public`

### Frontend (React)

Local dev:

```bash
cd frontend
npm ci
npm run dev
```

Set `VITE_API_BASE_URL` to point to the backend (default `http://localhost:8080`).

### Security notes

- Do **NOT** commit real API keys to git. Use env vars (`env/env.example` is a template only).
- If you ever pasted a real key in chat/logs, **rotate it** in the provider console.
- **Citizens**: no login required (anonymous chat)
- **Employees**: JWT auth + role claims (Admin/Editor/Viewer)
- Passwords are stored hashed using PBKDF2 (`PasswordHasher`)
- Widget can optionally require **`WIDGET__API_KEY`** + origin allowlist
- Public chat is **rate limited**
- API Integrations enforce:
  - allowlisted domain matching
  - only stored `apiId` with `allowInChat=true`
  - timeouts/retries
  - audit logging without secrets










 docker build --no-cache -t superwae/municipality-chatbot-backend:latest -f backend/Dockerfile .
 docker push superwae/municipality-chatbot-backend:latest
 
 docker build --no-cache -t superwae/municipality-chatbot-frontend:latest -f frontend/Dockerfile .
 docker push superwae/municipality-chatbot-frontend:latest




docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --force-recreate
