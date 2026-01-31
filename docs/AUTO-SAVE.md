# Lilia Editor - Auto-Save Mechanism

This document explains how content is automatically saved in the Lilia Editor.

---

## Overview

The editor uses a **debounced auto-save** mechanism:
1. User makes changes to blocks
2. Changes are debounced (2 seconds default)
3. Modified blocks are sent to the API via batch update
4. Document metadata is updated separately if title changed

---

## Save Flow

```
User Types → Block onChange → setBlocks() → useEffect triggers
                                                    ↓
                                            save({ title, blocks })
                                                    ↓
                                            Debounce (2 seconds)
                                                    ↓
                                            POST /api/documents/{id}/blocks/batch
                                            PUT /api/documents/{id} (if title changed)
```

---

## API Endpoints Used

### 1. Batch Update Blocks

**Endpoint:** `POST /api/documents/{documentId}/blocks/batch`

**Purpose:** Update multiple blocks in a single request

**Request Body:**
```json
{
  "blocks": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "type": "paragraph",
      "content": {
        "text": "Updated paragraph content"
      },
      "sortOrder": 0
    },
    {
      "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "type": "heading",
      "content": {
        "text": "Chapter 1",
        "level": 1
      },
      "sortOrder": 1
    }
  ]
}
```

**Response:** Returns the updated blocks

```json
[
  {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "documentId": "48209712-4dbb-4d59-bc8c-6a964c5cf690",
    "type": "paragraph",
    "content": { "text": "Updated paragraph content" },
    "sortOrder": 0,
    "parentId": null,
    "depth": 0,
    "createdAt": "2026-01-31T10:00:00Z",
    "updatedAt": "2026-01-31T11:30:45Z"
  }
]
```

### 2. Update Document Metadata

**Endpoint:** `PUT /api/documents/{documentId}`

**Purpose:** Update document title and settings (not content)

**Request Body:**
```json
{
  "title": "New Document Title"
}
```

---

## Block Content Examples

### Paragraph Block

```json
{
  "id": "uuid",
  "type": "paragraph",
  "content": {
    "text": "This is plain text with **bold** and *italic* formatting."
  },
  "sortOrder": 0
}
```

With TipTap rich text (internal format):
```json
{
  "content": {
    "text": "Hello world",
    "tiptap": {
      "type": "doc",
      "content": [
        {
          "type": "paragraph",
          "content": [
            { "type": "text", "text": "Hello " },
            { "type": "text", "marks": [{ "type": "bold" }], "text": "world" }
          ]
        }
      ]
    }
  }
}
```

### Heading Block

```json
{
  "id": "uuid",
  "type": "heading",
  "content": {
    "text": "Introduction",
    "level": 1
  },
  "sortOrder": 0
}
```

Level values: 1 (H1) through 6 (H6)

### Equation Block

```json
{
  "id": "uuid",
  "type": "equation",
  "content": {
    "latex": "E = mc^2",
    "displayMode": true
  },
  "sortOrder": 2
}
```

- `displayMode: true` - Centered, display-style equation
- `displayMode: false` - Inline equation

### Figure Block

```json
{
  "id": "uuid",
  "type": "figure",
  "content": {
    "src": "https://assets.lilia.app/user123/documents/doc456/images/img789.png",
    "caption": "Figure 1: Experimental results",
    "alt": "Graph showing experimental data",
    "width": 0.8
  },
  "sortOrder": 3
}
```

- `width`: 0.25, 0.5, 0.75, or 1.0 (fraction of page width)

### Table Block

```json
{
  "id": "uuid",
  "type": "table",
  "content": {
    "headers": ["Name", "Value", "Unit"],
    "rows": [
      ["Temperature", "25.5", "°C"],
      ["Pressure", "101.3", "kPa"],
      ["Humidity", "45", "%"]
    ],
    "caption": "Table 1: Environmental conditions"
  },
  "sortOrder": 4
}
```

### Code Block

```json
{
  "id": "uuid",
  "type": "code",
  "content": {
    "code": "def hello():\n    print('Hello, World!')",
    "language": "python"
  },
  "sortOrder": 5
}
```

Supported languages: javascript, typescript, python, java, c, cpp, csharp, go, rust, sql, html, css, json, yaml, markdown, latex, bash

### List Block

```json
{
  "id": "uuid",
  "type": "list",
  "content": {
    "items": [
      "First item",
      "Second item",
      "Third item"
    ],
    "ordered": false
  },
  "sortOrder": 6
}
```

- `ordered: true` - Numbered list (1, 2, 3)
- `ordered: false` - Bullet list

### Quote Block

```json
{
  "id": "uuid",
  "type": "quote",
  "content": {
    "text": "The only way to do great work is to love what you do.",
    "attribution": "Steve Jobs"
  },
  "sortOrder": 7
}
```

### Theorem Block

```json
{
  "id": "uuid",
  "type": "theorem",
  "content": {
    "theoremType": "theorem",
    "title": "Pythagorean Theorem",
    "text": "In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides: $a^2 + b^2 = c^2$",
    "label": "thm:pythagoras"
  },
  "sortOrder": 8
}
```

Theorem types: theorem, lemma, proposition, corollary, definition, proof, remark, example

### Abstract Block

```json
{
  "id": "uuid",
  "type": "abstract",
  "content": {
    "title": "Abstract",
    "text": "This paper presents a novel approach to..."
  },
  "sortOrder": 0
}
```

### Bibliography Block

```json
{
  "id": "uuid",
  "type": "bibliography",
  "content": {
    "title": "References",
    "style": "apa",
    "entries": []
  },
  "sortOrder": 99
}
```

Note: Bibliography entries are stored in the separate `bibliography_entries` table, not in the block content.

### Table of Contents Block

```json
{
  "id": "uuid",
  "type": "tableOfContents",
  "content": {
    "title": "Table of Contents"
  },
  "sortOrder": 1
}
```

The TOC is auto-generated from heading blocks.

### Divider Block

```json
{
  "id": "uuid",
  "type": "divider",
  "content": {},
  "sortOrder": 10
}
```

---

## What Gets Updated

When auto-save triggers:

| Field | Updated? | Notes |
|-------|----------|-------|
| `block.content` | Yes | Main content changes |
| `block.type` | Yes | If block type changes |
| `block.sortOrder` | Yes | If blocks reordered |
| `block.updatedAt` | Yes | Automatic timestamp |
| `document.updatedAt` | Yes | Automatic timestamp |
| `document.title` | If changed | Separate PUT request |

---

## Timestamps

All timestamps are stored in UTC and returned in ISO 8601 format:

```
2026-01-31T11:30:45.123Z
```

The API updates these automatically:
- `block.updatedAt` - When block content changes
- `document.updatedAt` - When any block changes or document metadata changes
- `document.lastOpenedAt` - When document is opened (GET request)

---

## Error Handling

### Successful Save
- HTTP 200 OK
- Returns updated blocks array

### Validation Errors
- HTTP 400 Bad Request
- Example: Invalid block ID format

### Permission Denied
- HTTP 403 Forbidden
- User doesn't have write access

### Document Not Found
- HTTP 404 Not Found
- Document ID doesn't exist

### Network/Server Errors
- HTTP 500 Internal Server Error
- Logged for debugging

---

## Frontend Auto-Save Configuration

Located in `useAutoSave.ts`:

```typescript
{
  documentId: string,
  debounceMs: 2000,      // 2 second debounce
  onSaveSuccess: () => {},
  onSaveError: (error) => {}
}
```

The debounce prevents excessive API calls while typing.

---

## Manual Save

Users can trigger immediate save via:
- **Ctrl+S** / **Cmd+S** keyboard shortcut
- **Save button** in toolbar

This bypasses the debounce and saves immediately.
