# Lilia Editor API - Data Structures

This document details the core data structures used in the Lilia Editor API.

---

## Document

A document is the top-level container for content.

### Database Schema

```sql
CREATE TABLE documents (
    id UUID PRIMARY KEY,
    owner_id VARCHAR(255) NOT NULL,      -- Clerk user ID
    team_id UUID,                         -- Optional team ownership
    title VARCHAR(500) NOT NULL,
    language VARCHAR(10) DEFAULT 'en',
    paper_size VARCHAR(20) DEFAULT 'a4',
    font_family VARCHAR(100) DEFAULT 'serif',
    font_size INTEGER DEFAULT 12,
    is_public BOOLEAN DEFAULT FALSE,
    share_link VARCHAR(100),              -- Public share token
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    last_opened_at TIMESTAMPTZ,
    deleted_at TIMESTAMPTZ                -- Soft delete
);
```

### API Response (DocumentDto)

```json
{
  "id": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "title": "My Research Paper",
  "ownerId": "user_2wh3MeURGR4xLFPfiWWlx9OoWQ8",
  "teamId": null,
  "language": "en",
  "paperSize": "a4",
  "fontFamily": "serif",
  "fontSize": 12,
  "isPublic": false,
  "shareLink": null,
  "createdAt": "2026-01-31T10:00:00Z",
  "updatedAt": "2026-01-31T11:30:00Z",
  "lastOpenedAt": "2026-01-31T11:30:00Z",
  "blocks": [...],
  "bibliography": [...],
  "labels": [...]
}
```

---

## Block

Blocks are the content units within a document. Each block has a type and JSON content.

### Database Schema

```sql
CREATE TABLE blocks (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL,
    content JSONB NOT NULL DEFAULT '{}',
    sort_order INTEGER NOT NULL DEFAULT 0,
    parent_id UUID REFERENCES blocks(id),  -- For nested blocks
    depth INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
```

### Supported Block Types

| Type | Description | Content Fields |
|------|-------------|----------------|
| `paragraph` | Rich text paragraph | `text`, `marks` |
| `heading` | Section heading | `text`, `level` (1-6) |
| `equation` | LaTeX math block | `latex`, `displayMode` |
| `figure` | Image with caption | `src`, `caption`, `alt`, `width` |
| `table` | Data table | `headers`, `rows` |
| `code` | Code snippet | `code`, `language` |
| `list` | Bullet/numbered list | `items`, `ordered` |
| `quote` | Block quote | `text` |
| `theorem` | Math theorem/proof | `theoremType`, `title`, `text`, `label` |
| `abstract` | Document abstract | `title`, `text` |
| `bibliography` | References section | `title`, `style`, `entries` |
| `divider` | Horizontal rule | (empty) |
| `tableOfContents` | Auto-generated TOC | `title` |

### API Response (BlockDto)

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "type": "paragraph",
  "content": {
    "text": "This is a paragraph with **bold** and *italic* text."
  },
  "sortOrder": 0,
  "parentId": null,
  "depth": 0,
  "createdAt": "2026-01-31T10:00:00Z",
  "updatedAt": "2026-01-31T11:30:00Z"
}
```

---

## Bibliography Entry

Citations and references for academic documents.

### Database Schema

```sql
CREATE TABLE bibliography_entries (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    cite_key VARCHAR(100) NOT NULL,       -- e.g., "einstein1905"
    entry_type VARCHAR(50) NOT NULL,      -- article, book, etc.
    data JSONB NOT NULL DEFAULT '{}',     -- BibTeX fields
    formatted_text TEXT,                  -- Pre-rendered citation
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    UNIQUE(document_id, cite_key)
);
```

### Entry Types

- `article` - Journal article
- `book` - Book
- `inproceedings` - Conference paper
- `phdthesis` - PhD thesis
- `mastersthesis` - Master's thesis
- `techreport` - Technical report
- `misc` - Miscellaneous
- `online` - Web resource

### API Response (BibliographyEntryDto)

```json
{
  "id": "bib-uuid-here",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "citeKey": "einstein1905",
  "entryType": "article",
  "data": {
    "title": "On the Electrodynamics of Moving Bodies",
    "author": "Albert Einstein",
    "journal": "Annalen der Physik",
    "year": "1905",
    "volume": "17",
    "pages": "891-921"
  },
  "formattedText": "Einstein, A. (1905). On the Electrodynamics of Moving Bodies. Annalen der Physik, 17, 891-921.",
  "createdAt": "2026-01-31T10:00:00Z",
  "updatedAt": "2026-01-31T10:00:00Z"
}
```

---

## User

Users are synced from Clerk authentication.

### Database Schema

```sql
CREATE TABLE users (
    id VARCHAR(255) PRIMARY KEY,          -- Clerk user ID
    email VARCHAR(255) UNIQUE NOT NULL,
    name VARCHAR(255),
    image_url TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
```

---

## Label

User-defined tags for organizing documents.

### Database Schema

```sql
CREATE TABLE labels (
    id UUID PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id),
    name VARCHAR(100) NOT NULL,
    color VARCHAR(7) DEFAULT '#3B82F6',   -- Hex color
    created_at TIMESTAMPTZ NOT NULL,
    UNIQUE(user_id, name)
);

CREATE TABLE document_labels (
    document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
    label_id UUID REFERENCES labels(id) ON DELETE CASCADE,
    PRIMARY KEY (document_id, label_id)
);
```

---

## Team & Collaboration

### Teams

```sql
CREATE TABLE teams (
    id UUID PRIMARY KEY,
    owner_id VARCHAR(255) NOT NULL REFERENCES users(id),
    name VARCHAR(255) NOT NULL,
    slug VARCHAR(100) UNIQUE,
    image_url TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
```

### Groups (within Teams)

```sql
CREATE TABLE groups (
    id UUID PRIMARY KEY,
    team_id UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);
```

### Document Collaborators

```sql
-- Individual user access
CREATE TABLE document_collaborators (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id),
    role VARCHAR(50) NOT NULL DEFAULT 'viewer',  -- owner, editor, viewer
    invited_by VARCHAR(255) REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL,
    UNIQUE(document_id, user_id)
);

-- Group access
CREATE TABLE document_groups (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    role VARCHAR(50) NOT NULL DEFAULT 'viewer',
    created_at TIMESTAMPTZ NOT NULL,
    UNIQUE(document_id, group_id)
);
```

### Roles & Permissions

| Role | Permissions |
|------|-------------|
| `owner` | read, write, delete, manage, transfer |
| `editor` | read, write |
| `viewer` | read |

---

## Document Version

Snapshots for version history.

### Database Schema

```sql
CREATE TABLE document_versions (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    version_number INTEGER NOT NULL,
    name VARCHAR(255),                    -- Optional version name
    snapshot JSONB NOT NULL,              -- Full document state
    created_by VARCHAR(255) REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL
);
```

### Snapshot Content

The `snapshot` field contains the complete document state:

```json
{
  "title": "My Document",
  "language": "en",
  "paperSize": "a4",
  "fontFamily": "serif",
  "fontSize": 12,
  "blocks": [
    { "id": "...", "type": "heading", "content": {...}, "sortOrder": 0 },
    { "id": "...", "type": "paragraph", "content": {...}, "sortOrder": 1 }
  ],
  "bibliography": [
    { "citeKey": "einstein1905", "entryType": "article", "data": {...} }
  ]
}
```

---

## Asset

File attachments (images, etc.).

### Database Schema

```sql
CREATE TABLE assets (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    file_name VARCHAR(255) NOT NULL,
    file_type VARCHAR(100) NOT NULL,
    file_size BIGINT NOT NULL,
    storage_key VARCHAR(500) NOT NULL,    -- S3/R2 key or local path
    width INTEGER,                        -- For images
    height INTEGER,
    url TEXT,                             -- Public URL
    created_at TIMESTAMPTZ NOT NULL
);
```

Storage key format: `{userId}/documents/{documentId}/images/{assetId}`

---

## User Preferences

### Database Schema

```sql
CREATE TABLE user_preferences (
    user_id VARCHAR(255) PRIMARY KEY REFERENCES users(id),
    theme VARCHAR(20) DEFAULT 'system',   -- system, light, dark
    auto_save_enabled BOOLEAN DEFAULT TRUE,
    auto_save_interval INTEGER DEFAULT 2000,  -- milliseconds
    default_language VARCHAR(10) DEFAULT 'en',
    default_paper_size VARCHAR(20) DEFAULT 'a4',
    default_font_family VARCHAR(100) DEFAULT 'serif',
    default_font_size INTEGER DEFAULT 12,
    keyboard_shortcuts JSONB DEFAULT '{}',
    updated_at TIMESTAMPTZ NOT NULL
);
```
