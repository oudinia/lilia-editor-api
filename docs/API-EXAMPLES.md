# Lilia Editor API - Request/Response Examples

Complete examples of API interactions with full payloads.

---

## Authentication

All requests require a Clerk JWT token:

```
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

## Document Operations

### Create Document

**Request:**
```http
POST /api/documents
Content-Type: application/json
Authorization: Bearer {token}

{
  "title": "My Research Paper",
  "language": "en",
  "paperSize": "a4",
  "fontFamily": "serif",
  "fontSize": 12
}
```

**Response (201 Created):**
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
  "updatedAt": "2026-01-31T10:00:00Z",
  "lastOpenedAt": null,
  "blocks": [],
  "bibliography": [],
  "labels": []
}
```

### Create Document from Template

**Request:**
```http
POST /api/documents
Content-Type: application/json

{
  "title": "Physics Assignment",
  "templateId": "template-uuid-for-physics"
}
```

**Response:** Document with pre-populated blocks from template.

### Get Document with All Content

**Request:**
```http
GET /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690
Authorization: Bearer {token}
```

**Response (200 OK):**
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
  "updatedAt": "2026-01-31T11:45:00Z",
  "lastOpenedAt": "2026-01-31T11:45:00Z",
  "blocks": [
    {
      "id": "block-1-uuid",
      "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
      "type": "heading",
      "content": {
        "text": "Introduction",
        "level": 1
      },
      "sortOrder": 0,
      "parentId": null,
      "depth": 0,
      "createdAt": "2026-01-31T10:05:00Z",
      "updatedAt": "2026-01-31T10:05:00Z"
    },
    {
      "id": "block-2-uuid",
      "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
      "type": "paragraph",
      "content": {
        "text": "This paper explores the relationship between quantum mechanics and general relativity."
      },
      "sortOrder": 1,
      "parentId": null,
      "depth": 0,
      "createdAt": "2026-01-31T10:06:00Z",
      "updatedAt": "2026-01-31T11:30:00Z"
    },
    {
      "id": "block-3-uuid",
      "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
      "type": "equation",
      "content": {
        "latex": "E = mc^2",
        "displayMode": true
      },
      "sortOrder": 2,
      "parentId": null,
      "depth": 0,
      "createdAt": "2026-01-31T10:10:00Z",
      "updatedAt": "2026-01-31T10:10:00Z"
    }
  ],
  "bibliography": [
    {
      "id": "bib-1-uuid",
      "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
      "citeKey": "einstein1905",
      "entryType": "article",
      "data": {
        "title": "On the Electrodynamics of Moving Bodies",
        "author": "Albert Einstein",
        "journal": "Annalen der Physik",
        "year": "1905"
      },
      "formattedText": "Einstein, A. (1905). On the Electrodynamics...",
      "createdAt": "2026-01-31T10:15:00Z",
      "updatedAt": "2026-01-31T10:15:00Z"
    }
  ],
  "labels": [
    {
      "id": "label-uuid",
      "name": "Physics",
      "color": "#3B82F6",
      "createdAt": "2026-01-30T09:00:00Z"
    }
  ]
}
```

### Update Document Metadata

**Request:**
```http
PUT /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690
Content-Type: application/json

{
  "title": "Updated Title",
  "language": "es",
  "paperSize": "letter",
  "fontFamily": "sans-serif",
  "fontSize": 11
}
```

**Response (200 OK):** Updated document object.

---

## Block Operations

### Create Block

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/blocks
Content-Type: application/json

{
  "type": "paragraph",
  "content": {
    "text": "This is a new paragraph."
  },
  "sortOrder": 5
}
```

**Response (201 Created):**
```json
{
  "id": "new-block-uuid",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "type": "paragraph",
  "content": {
    "text": "This is a new paragraph."
  },
  "sortOrder": 5,
  "parentId": null,
  "depth": 0,
  "createdAt": "2026-01-31T11:50:00Z",
  "updatedAt": "2026-01-31T11:50:00Z"
}
```

### Update Single Block

**Request:**
```http
PUT /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/blocks/block-2-uuid
Content-Type: application/json

{
  "content": {
    "text": "Updated paragraph with new content and **bold** text."
  }
}
```

**Response (200 OK):** Updated block object.

### Batch Update Blocks (Auto-Save)

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/blocks/batch
Content-Type: application/json

{
  "blocks": [
    {
      "id": "block-1-uuid",
      "content": {
        "text": "Updated Introduction",
        "level": 1
      }
    },
    {
      "id": "block-2-uuid",
      "content": {
        "text": "This paragraph has been completely rewritten with new insights."
      }
    },
    {
      "id": "block-3-uuid",
      "content": {
        "latex": "E^2 = (pc)^2 + (m_0 c^2)^2",
        "displayMode": true
      }
    }
  ]
}
```

**Response (200 OK):**
```json
[
  {
    "id": "block-1-uuid",
    "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
    "type": "heading",
    "content": {
      "text": "Updated Introduction",
      "level": 1
    },
    "sortOrder": 0,
    "parentId": null,
    "depth": 0,
    "createdAt": "2026-01-31T10:05:00Z",
    "updatedAt": "2026-01-31T12:00:00Z"
  },
  {
    "id": "block-2-uuid",
    "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
    "type": "paragraph",
    "content": {
      "text": "This paragraph has been completely rewritten with new insights."
    },
    "sortOrder": 1,
    "parentId": null,
    "depth": 0,
    "createdAt": "2026-01-31T10:06:00Z",
    "updatedAt": "2026-01-31T12:00:00Z"
  },
  {
    "id": "block-3-uuid",
    "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
    "type": "equation",
    "content": {
      "latex": "E^2 = (pc)^2 + (m_0 c^2)^2",
      "displayMode": true
    },
    "sortOrder": 2,
    "parentId": null,
    "depth": 0,
    "createdAt": "2026-01-31T10:10:00Z",
    "updatedAt": "2026-01-31T12:00:00Z"
  }
]
```

### Reorder Blocks

**Request:**
```http
PUT /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/blocks/reorder
Content-Type: application/json

{
  "blockIds": [
    "block-3-uuid",
    "block-1-uuid",
    "block-2-uuid"
  ]
}
```

**Response:** Blocks with updated `sortOrder` values (0, 1, 2).

### Delete Block

**Request:**
```http
DELETE /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/blocks/block-2-uuid
```

**Response (204 No Content)**

---

## Bibliography Operations

### Add Citation

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/bibliography
Content-Type: application/json

{
  "citeKey": "hawking1988",
  "entryType": "book",
  "data": {
    "title": "A Brief History of Time",
    "author": "Stephen Hawking",
    "publisher": "Bantam Books",
    "year": "1988",
    "isbn": "978-0553380163"
  }
}
```

**Response (201 Created):**
```json
{
  "id": "new-bib-uuid",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "citeKey": "hawking1988",
  "entryType": "book",
  "data": {
    "title": "A Brief History of Time",
    "author": "Stephen Hawking",
    "publisher": "Bantam Books",
    "year": "1988",
    "isbn": "978-0553380163"
  },
  "formattedText": "Hawking, S. (1988). A Brief History of Time. Bantam Books.",
  "createdAt": "2026-01-31T12:00:00Z",
  "updatedAt": "2026-01-31T12:00:00Z"
}
```

### Import BibTeX

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/bibliography/import
Content-Type: application/json

{
  "bibtex": "@article{feynman1948,\n  author = {Richard Feynman},\n  title = {Space-Time Approach to Quantum Electrodynamics},\n  journal = {Physical Review},\n  year = {1948},\n  volume = {76},\n  pages = {769-789}\n}\n\n@book{dirac1930,\n  author = {Paul Dirac},\n  title = {Principles of Quantum Mechanics},\n  publisher = {Oxford University Press},\n  year = {1930}\n}"
}
```

**Response (200 OK):**
```json
{
  "imported": 2,
  "entries": [
    {
      "citeKey": "feynman1948",
      "entryType": "article",
      "data": {...}
    },
    {
      "citeKey": "dirac1930",
      "entryType": "book",
      "data": {...}
    }
  ]
}
```

### Export BibTeX

**Request:**
```http
GET /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/bibliography/export
```

**Response (200 OK):**
```
@article{einstein1905,
  author = {Albert Einstein},
  title = {On the Electrodynamics of Moving Bodies},
  journal = {Annalen der Physik},
  year = {1905}
}

@book{hawking1988,
  author = {Stephen Hawking},
  title = {A Brief History of Time},
  publisher = {Bantam Books},
  year = {1988}
}
```

### DOI Lookup

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/bibliography/doi
Content-Type: application/json

{
  "doi": "10.1103/PhysRevLett.116.061102"
}
```

**Response (200 OK):**
```json
{
  "citeKey": "abbott2016",
  "entryType": "article",
  "data": {
    "title": "Observation of Gravitational Waves from a Binary Black Hole Merger",
    "author": "Abbott, B. P. and others",
    "journal": "Physical Review Letters",
    "year": "2016",
    "volume": "116",
    "doi": "10.1103/PhysRevLett.116.061102"
  }
}
```

---

## Version History

### Create Version Snapshot

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/versions
Content-Type: application/json

{
  "name": "Before major revisions"
}
```

**Response (201 Created):**
```json
{
  "id": "version-uuid",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "versionNumber": 3,
  "name": "Before major revisions",
  "createdBy": "user_2wh3MeURGR4xLFPfiWWlx9OoWQ8",
  "createdAt": "2026-01-31T12:30:00Z"
}
```

### Get Version Snapshot

**Request:**
```http
GET /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/versions/version-uuid
```

**Response (200 OK):**
```json
{
  "id": "version-uuid",
  "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
  "versionNumber": 3,
  "name": "Before major revisions",
  "snapshot": {
    "title": "My Research Paper",
    "language": "en",
    "paperSize": "a4",
    "blocks": [
      {"id": "...", "type": "heading", "content": {...}},
      {"id": "...", "type": "paragraph", "content": {...}}
    ],
    "bibliography": [...]
  },
  "createdBy": "user_2wh3MeURGR4xLFPfiWWlx9OoWQ8",
  "createdAt": "2026-01-31T12:30:00Z"
}
```

### Restore Version

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/versions/version-uuid/restore
```

**Response (200 OK):** Document restored to version state (creates new version as backup).

---

## Collaboration

### Share Document with User

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/collaborators/users
Content-Type: application/json

{
  "userId": "user_collaborator_id",
  "role": "editor"
}
```

### Generate Public Share Link

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/share
```

**Response (200 OK):**
```json
{
  "shareLink": "abc123xyz789",
  "url": "https://editor.lilia.app/shared/abc123xyz789"
}
```

### Access Shared Document (No Auth Required)

**Request:**
```http
GET /api/documents/shared/abc123xyz789
```

**Response:** Full document (read-only).

---

## Assets (File Upload)

### Request Upload URL

**Request:**
```http
POST /api/documents/48209712-4dbb-4d59-bc8c-6a964c5cf690/assets
Content-Type: application/json

{
  "fileName": "figure1.png",
  "fileType": "image/png",
  "fileSize": 245678
}
```

**Response (200 OK):**
```json
{
  "id": "asset-uuid",
  "uploadUrl": "https://storage.lilia.app/presigned-upload-url...",
  "publicUrl": "https://assets.lilia.app/user123/documents/doc456/images/asset-uuid.png"
}
```

### Upload File

```http
PUT {uploadUrl}
Content-Type: image/png

[binary file data]
```

---

## Error Responses

### Validation Error (400)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "errors": {
    "citeKey": ["The cite key 'einstein1905' already exists in this document."]
  }
}
```

### Unauthorized (401)

```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401
}
```

### Forbidden (403)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "You do not have write access to this document."
}
```

### Not Found (404)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Document not found."
}
```
