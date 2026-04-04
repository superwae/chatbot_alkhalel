-- Create an initial admin employee (replace password hash with a real one).
-- Password hashing format: pbkdf2$ITERATIONS$BASE64SALT$BASE64KEY
-- Generate one with:
--   dotnet run --project backend/tools/PasswordHashTool -- "ChangeMeNow!"

-- This uses pgcrypto for UUID generation (see schema.sql).
INSERT INTO employees (employee_id, username, password_hash, role, is_active, created_at, updated_at)
VALUES (gen_random_uuid(), 'admin', 'pbkdf2$120000$REPLACE_WITH_BASE64_SALT$REPLACE_WITH_BASE64_KEY', 'EmployeeAdmin', true, now(), now());

