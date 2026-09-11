CREATE EXTENSION IF NOT EXISTS vector;

-- The app connects as the DB owner (claimpilot). Migrations run
-- CREATE EXTENSION IF NOT EXISTS vector, which requires superuser here;
-- pre-creating it in the init script lets the app migrate safely.
GRANT ALL ON SCHEMA public TO claimpilot;