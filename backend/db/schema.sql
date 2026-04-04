-- Municipality AI Chatbot - PostgreSQL schema (reference)
-- Notes:
-- - The real source of truth should be EF Core migrations.
-- - Keep secrets OUT of DB; API authConfig references env var names only.

-- For UUID generation in ad-hoc SQL (seed, etc.)
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TABLE employees (
  employee_id uuid PRIMARY KEY,
  username varchar(128) NOT NULL UNIQUE,
  password_hash varchar(512) NOT NULL,
  role varchar(64) NOT NULL,
  is_active boolean NOT NULL,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL
);

CREATE TABLE faqs (
  faq_id uuid PRIMARY KEY,
  title varchar(256) NOT NULL,
  question text NOT NULL,
  short_description text,
  answer text NOT NULL,
  language varchar(2) NOT NULL, -- EN|AR
  tags_csv varchar(512),
  department varchar(128),
  is_active boolean NOT NULL,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL
);

CREATE INDEX idx_faqs_lang_active ON faqs(language, is_active);

CREATE TABLE documents (
  doc_id uuid PRIMARY KEY,
  filename varchar(512) NOT NULL,
  filetype varchar(16) NOT NULL,
  file_size_bytes bigint NOT NULL,
  detected_language varchar(2),
  is_active boolean NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE TABLE document_chunks (
  chunk_id uuid PRIMARY KEY,
  doc_id uuid NOT NULL REFERENCES documents(doc_id),
  filename varchar(512) NOT NULL,
  filetype varchar(16) NOT NULL,
  language varchar(2),
  page_number int,
  sheet_name varchar(256),
  chunk_index int NOT NULL,
  text text NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE INDEX idx_chunks_doc ON document_chunks(doc_id);

CREATE TABLE api_definitions (
  api_id uuid PRIMARY KEY,
  api_name varchar(256) NOT NULL,
  description text,
  base_url varchar(512) NOT NULL,
  method varchar(16) NOT NULL,
  path_template varchar(512) NOT NULL,
  auth_type varchar(32) NOT NULL,
  auth_config_json text NOT NULL,
  headers_template_json text NOT NULL,
  query_params_schema_json text NOT NULL,
  body_schema_json text NOT NULL,
  body_template_json text,
  response_handling_notes text,
  allow_in_chat boolean NOT NULL,
  allowlisted_domain varchar(256) NOT NULL,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL
);

CREATE INDEX idx_api_allow_chat ON api_definitions(allow_in_chat);

-- Observability / audit
CREATE TABLE chat_sessions (
  session_id uuid PRIMARY KEY,
  channel varchar(32) NOT NULL,
  widget_origin varchar(512),
  user_lang varchar(2),
  created_at timestamptz NOT NULL
);

CREATE TABLE chat_messages (
  message_id uuid PRIMARY KEY,
  session_id uuid NOT NULL REFERENCES chat_sessions(session_id),
  role varchar(16) NOT NULL,
  text text NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE INDEX idx_msg_session ON chat_messages(session_id);

CREATE TABLE routing_decisions (
  decision_id uuid PRIMARY KEY,
  session_id uuid NOT NULL REFERENCES chat_sessions(session_id),
  message_id uuid NOT NULL REFERENCES chat_messages(message_id),
  route varchar(16) NOT NULL,
  confidence numeric NOT NULL,
  selected_faq_id uuid,
  selected_chunk_ids_csv text,
  selected_api_id uuid,
  planner_json text NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE INDEX idx_dec_route ON routing_decisions(route);

CREATE TABLE api_calls (
  api_call_id uuid PRIMARY KEY,
  session_id uuid NOT NULL REFERENCES chat_sessions(session_id),
  message_id uuid NOT NULL REFERENCES chat_messages(message_id),
  api_id uuid NOT NULL REFERENCES api_definitions(api_id),
  request_summary_json text NOT NULL,
  response_status_code int,
  response_summary_json text NOT NULL,
  error text,
  created_at timestamptz NOT NULL
);

