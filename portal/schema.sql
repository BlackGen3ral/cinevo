CREATE TABLE IF NOT EXISTS catalog_items (id TEXT PRIMARY KEY, owner_hash TEXT NOT NULL, source_url TEXT NOT NULL, title TEXT NOT NULL, data TEXT NOT NULL, created_at TEXT NOT NULL, UNIQUE(owner_hash,source_url,title));
CREATE INDEX IF NOT EXISTS catalog_owner_created ON catalog_items(owner_hash,created_at DESC);
