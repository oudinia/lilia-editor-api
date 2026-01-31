# Lilia Editor API - Manual Test Plan

**Target Launch:** February 2026
**API Base URL:** `http://localhost:5000`
**Swagger:** `http://localhost:5000/swagger`

---

## Test Environment Setup

### Prerequisites
```powershell
# 1. Start PostgreSQL
# 2. Run migrations
psql -d lilia_core -f database/001_create_lilia_core.sql
psql -d lilia_core -f database/003_seed_templates.sql

# 3. Start API
cd src/Lilia.Api
dotnet run
```

### Test Tools
- **Swagger UI** at root URL for interactive testing
- **curl** or **Postman** for scripted tests
- **Logs** at `./logs/lilia-api-{date}.log`

---

## Authentication & Authorization

### Clerk Integration
| # | Test | Expected | Status |
|---|------|----------|--------|
| 1.1 | Request without token | 401 Unauthorized | [x] DONE |
| 1.2 | Request with invalid token | 401 Unauthorized | [x] DONE |
| 1.3 | Request with valid Clerk JWT | 200 OK, user synced | [x] DONE |
| 1.4 | User sync creates DB record | User appears in database | [x] DONE |
| 1.5 | User sync updates existing | Name/email updated | [x] DONE |

### Development Auth (No Clerk configured)
| # | Test | Expected | Status |
|---|------|----------|--------|
| 1.6 | API runs without Clerk:Authority | Dev user auto-created | [ ] |
| 1.7 | Dev user has full access | All endpoints work | [ ] |

---

## Documents API

### Document CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 2.1 | Create document | `POST /api/documents` with title | 201, document returned with ID | [ ] |
| 2.2 | Create with template | `POST /api/documents` with templateId | Document has template blocks | [ ] |
| 2.3 | List documents | `GET /api/documents` | Returns user's documents | [ ] |
| 2.4 | List with search | `GET /api/documents?search=test` | Filters by title | [ ] |
| 2.5 | List with label filter | `GET /api/documents?labelId={id}` | Filters by label | [ ] |
| 2.6 | Get document | `GET /api/documents/{id}` | Returns doc with blocks | [ ] |
| 2.7 | Get updates lastOpenedAt | Check timestamp after GET | Timestamp updated | [ ] |
| 2.8 | Update document | `PUT /api/documents/{id}` | Metadata updated | [ ] |
| 2.9 | Update title | Change title field | Title persists | [ ] |
| 2.10 | Update language | Change to "es" or "fr" | Language persists | [ ] |
| 2.11 | Update paper size | Change to "letter" | Paper size persists | [ ] |
| 2.12 | Update font | Change font family/size | Font settings persist | [ ] |
| 2.13 | Delete document | `DELETE /api/documents/{id}` | Soft delete (deleted_at set) | [ ] |
| 2.14 | Deleted doc not in list | `GET /api/documents` | Deleted doc excluded | [ ] |

### Document Sharing
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 2.15 | Generate share link | `POST /api/documents/{id}/share` | Returns shareLink token | [ ] |
| 2.16 | Access shared doc | `GET /api/documents/shared/{link}` | Returns doc (no auth) | [ ] |
| 2.17 | Shared doc is read-only | Try to modify via share link | Should not allow writes | [ ] |
| 2.18 | Revoke share link | `DELETE /api/documents/{id}/share` | 200 OK | [ ] |
| 2.19 | Revoked link fails | Access old share link | 404 Not Found | [ ] |

### Document Duplication
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 2.20 | Duplicate document | `POST /api/documents/{id}/duplicate` | New doc created | [ ] |
| 2.21 | Duplicate has all blocks | Check new doc | All blocks copied | [ ] |
| 2.22 | Duplicate has bibliography | Check new doc | Bibliography copied | [ ] |
| 2.23 | Duplicate has new owner | Check owner_id | Current user owns copy | [ ] |

---

## Blocks API

### Block CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 3.1 | List blocks | `GET /api/documents/{docId}/blocks` | Returns ordered blocks | [ ] |
| 3.2 | Create paragraph | POST with type "paragraph" | Block created | [ ] |
| 3.3 | Create heading | POST with type "heading" | Block created | [ ] |
| 3.4 | Create equation | POST with type "equation" | Block created | [ ] |
| 3.5 | Create list | POST with type "list" | Block created | [ ] |
| 3.6 | Create code | POST with type "code" | Block created | [ ] |
| 3.7 | Create quote | POST with type "quote" | Block created | [ ] |
| 3.8 | Create image | POST with type "image" | Block created | [ ] |
| 3.9 | Create table | POST with type "table" | Block created | [ ] |
| 3.10 | Create theorem | POST with type "theorem" | Block created | [ ] |
| 3.11 | Create abstract | POST with type "abstract" | Block created | [ ] |
| 3.12 | Create bibliography | POST with type "bibliography" | Block created | [ ] |
| 3.13 | Create divider | POST with type "divider" | Block created | [ ] |
| 3.14 | Get single block | `GET /api/documents/{docId}/blocks/{id}` | Returns block | [ ] |
| 3.15 | Update block content | `PUT /api/documents/{docId}/blocks/{id}` | Content updated | [ ] |
| 3.16 | Delete block | `DELETE /api/documents/{docId}/blocks/{id}` | Block removed | [ ] |

### Block Ordering & Nesting
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 3.17 | Blocks return ordered | Create 3 blocks, list | Ordered by sort_order | [ ] |
| 3.18 | Reorder blocks | `PUT .../blocks/reorder` with IDs | Order updated | [ ] |
| 3.19 | Create nested block | POST with parent_id | Block has parent | [ ] |
| 3.20 | Nested block depth | Check depth field | Depth is parent+1 | [ ] |

### Batch Operations
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 3.21 | Batch update | `POST .../blocks/batch` | Multiple blocks updated | [ ] |
| 3.22 | Batch partial failure | One invalid block in batch | Returns errors for invalid | [ ] |

---

## Bibliography API

### Entry CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 4.1 | List entries | `GET /api/documents/{docId}/bibliography` | Returns entries | [ ] |
| 4.2 | Create article entry | POST with type "article" | Entry created | [ ] |
| 4.3 | Create book entry | POST with type "book" | Entry created | [ ] |
| 4.4 | Create inproceedings | POST with type "inproceedings" | Entry created | [ ] |
| 4.5 | Create thesis entry | POST with type "phdthesis" | Entry created | [ ] |
| 4.6 | Get single entry | `GET .../bibliography/{id}` | Returns entry | [ ] |
| 4.7 | Update entry | `PUT .../bibliography/{id}` | Entry updated | [ ] |
| 4.8 | Delete entry | `DELETE .../bibliography/{id}` | Entry removed | [ ] |
| 4.9 | Unique cite_key | Create duplicate cite_key | 400 error | [ ] |

### BibTeX Import/Export
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 4.10 | Import BibTeX | `POST .../bibliography/import` | Entries created | [ ] |
| 4.11 | Import multiple | BibTeX with 3 entries | All 3 created | [ ] |
| 4.12 | Import invalid | Malformed BibTeX | Error with details | [ ] |
| 4.13 | Export BibTeX | `GET .../bibliography/export` | Valid BibTeX string | [ ] |
| 4.14 | Export empty | No entries | Empty or header only | [ ] |

### DOI Lookup
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 4.15 | DOI lookup | `POST .../bibliography/doi` with DOI | Entry data returned | [ ] |
| 4.16 | Invalid DOI | Non-existent DOI | 404 or error | [ ] |

---

## Labels API

### Label CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 5.1 | List labels | `GET /api/labels` | Returns user's labels | [ ] |
| 5.2 | Create label | `POST /api/labels` with name, color | Label created | [ ] |
| 5.3 | Create with hex color | Color "#FF5733" | Color stored | [ ] |
| 5.4 | Update label | `PUT /api/labels/{id}` | Label updated | [ ] |
| 5.5 | Delete label | `DELETE /api/labels/{id}` | Label removed | [ ] |

### Document Labeling
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 5.6 | Add label to doc | `POST /api/documents/{docId}/labels/{labelId}` | Label attached | [ ] |
| 5.7 | Remove label from doc | `DELETE /api/documents/{docId}/labels/{labelId}` | Label detached | [ ] |
| 5.8 | Multiple labels | Add 3 labels to doc | All 3 attached | [ ] |
| 5.9 | Labels in doc response | GET document | Labels included | [ ] |

---

## Teams API

### Team CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 6.1 | List teams | `GET /api/teams` | Returns user's teams | [ ] |
| 6.2 | Create team | `POST /api/teams` | Team created, user is owner | [ ] |
| 6.3 | Get team | `GET /api/teams/{id}` | Returns team details | [ ] |
| 6.4 | Update team | `PUT /api/teams/{id}` | Name/image updated | [ ] |
| 6.5 | Delete team | `DELETE /api/teams/{id}` | Team removed | [ ] |
| 6.6 | Only owner can delete | Non-owner tries delete | 403 Forbidden | [ ] |

### Team Members
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 6.7 | List members | `GET /api/teams/{id}/members` | Returns members | [ ] |
| 6.8 | Invite member | `POST /api/teams/{id}/members` | Member added | [ ] |
| 6.9 | Update member role | `PUT /api/teams/{id}/members/{userId}` | Role changed | [ ] |
| 6.10 | Remove member | `DELETE /api/teams/{id}/members/{userId}` | Member removed | [ ] |
| 6.11 | Cannot remove owner | Try remove owner | 400 error | [ ] |

### Team Documents
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 6.12 | List team documents | `GET /api/teams/{id}/documents` | Returns team docs | [ ] |
| 6.13 | Create doc in team | POST document with team_id | Doc in team | [ ] |

---

## Collaborators API

### User Collaborators
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 7.1 | List collaborators | `GET /api/documents/{docId}/collaborators` | Returns all | [ ] |
| 7.2 | Add user collaborator | `POST .../collaborators/users` | User added | [ ] |
| 7.3 | Add with editor role | role: "editor" | Can edit | [ ] |
| 7.4 | Add with viewer role | role: "viewer" | Read only | [ ] |
| 7.5 | Update user role | `PUT .../collaborators/users/{userId}` | Role changed | [ ] |
| 7.6 | Remove user | `DELETE .../collaborators/users/{userId}` | Access removed | [ ] |

### Group Collaborators
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 7.7 | Share with group | `POST .../collaborators/groups` | Group added | [ ] |
| 7.8 | Update group role | `PUT .../collaborators/groups/{groupId}` | Role changed | [ ] |
| 7.9 | Remove group | `DELETE .../collaborators/groups/{groupId}` | Access removed | [ ] |

### Permission Enforcement
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 7.10 | Viewer cannot edit | Viewer tries PUT | 403 Forbidden | [ ] |
| 7.11 | Editor can edit | Editor tries PUT | 200 OK | [ ] |
| 7.12 | Editor cannot delete | Editor tries DELETE doc | 403 Forbidden | [ ] |
| 7.13 | Owner can do all | Owner tries all ops | All succeed | [ ] |

---

## Versions API

### Version Management
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 8.1 | List versions | `GET /api/documents/{docId}/versions` | Returns versions | [ ] |
| 8.2 | Create version | `POST /api/documents/{docId}/versions` | Snapshot created | [ ] |
| 8.3 | Version has number | Check version_number | Sequential number | [ ] |
| 8.4 | Get version | `GET .../versions/{id}` | Returns snapshot | [ ] |
| 8.5 | Snapshot has content | Check snapshot JSON | Full document state | [ ] |
| 8.6 | Delete version | `DELETE .../versions/{id}` | Version removed | [ ] |

### Version Restore
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 8.7 | Restore version | `POST .../versions/{id}/restore` | Document reverted | [ ] |
| 8.8 | Restore preserves blocks | Check blocks after restore | Blocks match snapshot | [ ] |
| 8.9 | Restore creates new version | Check version list | New version added | [ ] |

---

## Templates API

### Template CRUD
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 9.1 | List templates | `GET /api/templates` | Returns templates | [ ] |
| 9.2 | List by category | `GET /api/templates?category=academic` | Filtered list | [ ] |
| 9.3 | Get template | `GET /api/templates/{id}` | Returns template | [ ] |
| 9.4 | List categories | `GET /api/templates/categories` | Returns categories | [ ] |

### Custom Templates
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 9.5 | Create template | `POST /api/templates` from doc | Template created | [ ] |
| 9.6 | Template has blocks | Check content | Blocks saved | [ ] |
| 9.7 | Update template | `PUT /api/templates/{id}` | Template updated | [ ] |
| 9.8 | Delete template | `DELETE /api/templates/{id}` | Template removed | [ ] |
| 9.9 | Cannot delete system | Try delete system template | 403 Forbidden | [ ] |

### Template Usage
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 9.10 | Use template | `POST /api/templates/{id}/use` | Document created | [ ] |
| 9.11 | Usage count increments | Check template after use | usage_count +1 | [ ] |

### System Templates (Seeded)
| # | Test | Expected | Status |
|---|------|----------|--------|
| 9.12 | Blank Document exists | Template in list | [ ] |
| 9.13 | Academic Paper exists | Template with sections | [ ] |
| 9.14 | Physics Homework exists | Template with equations | [ ] |
| 9.15 | Thesis Chapter exists | Template multi-section | [ ] |
| 9.16 | Math Proof exists | Template with theorems | [ ] |
| 9.17 | Research Paper exists | Template in list | [ ] |

---

## Assets API

### Asset Management
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 10.1 | List assets | `GET /api/documents/{docId}/assets` | Returns assets | [ ] |
| 10.2 | Request upload URL | `POST /api/documents/{docId}/assets` | Presigned URL returned | [ ] |
| 10.3 | Upload to URL | PUT file to presigned URL | Upload succeeds | [ ] |
| 10.4 | Get asset | `GET .../assets/{id}` | Returns asset info | [ ] |
| 10.5 | Asset has public URL | Check url field | Valid URL | [ ] |
| 10.6 | Delete asset | `DELETE .../assets/{id}` | Asset removed | [ ] |

### Local Storage (Development)
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 10.7 | Files in uploads/ | After upload | File exists locally | [ ] |
| 10.8 | Accessible via /uploads | GET /uploads/{path} | File served | [ ] |

---

## Preferences API

### User Preferences
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 11.1 | Get preferences | `GET /api/preferences` | Returns preferences | [ ] |
| 11.2 | Default values | New user | Sensible defaults | [ ] |
| 11.3 | Update theme | PUT with theme: "dark" | Theme saved | [ ] |
| 11.4 | Update auto-save | PUT auto_save settings | Settings saved | [ ] |

### Keyboard Shortcuts
| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 11.5 | Get shortcuts | `GET /api/preferences/shortcuts` | Returns shortcuts | [ ] |
| 11.6 | Update shortcuts | `PUT /api/preferences/shortcuts` | Shortcuts saved | [ ] |
| 11.7 | Custom shortcut | Add custom binding | Binding persists | [ ] |

---

## Error Handling

### HTTP Status Codes
| # | Test | Expected | Status |
|---|------|----------|--------|
| 12.1 | Invalid ID format | 400 Bad Request | [ ] |
| 12.2 | Resource not found | 404 Not Found | [ ] |
| 12.3 | No permission | 403 Forbidden | [ ] |
| 12.4 | Unauthenticated | 401 Unauthorized | [ ] |
| 12.5 | Validation error | 400 with details | [ ] |
| 12.6 | Server error | 500 with safe message | [ ] |

### Error Response Format
| # | Test | Expected | Status |
|---|------|----------|--------|
| 12.7 | Error has message | `{ "message": "..." }` | [ ] |
| 12.8 | Validation has details | `{ "errors": [...] }` | [ ] |

---

## Performance & Reliability

### Response Times
| # | Test | Expected | Status |
|---|------|----------|--------|
| 13.1 | Simple GET | < 100ms | [ ] |
| 13.2 | Document with 50 blocks | < 500ms | [ ] |
| 13.3 | List 100 documents | < 1s | [ ] |

### Concurrent Access
| # | Test | Expected | Status |
|---|------|----------|--------|
| 13.4 | Simultaneous edits | No data loss | [ ] |
| 13.5 | Optimistic locking | Conflict detected | [ ] |

---

## Health & Monitoring

| # | Test | Steps | Expected | Status |
|---|------|-------|----------|--------|
| 14.1 | Health endpoint | `GET /health` | 200 with status | [ ] |
| 14.2 | Swagger loads | `GET /` | Swagger UI | [ ] |
| 14.3 | Logs written | Check ./logs/ | Log files exist | [ ] |
| 14.4 | Request logging | Make request, check log | Request logged | [ ] |

---

## Test Results

```
Date: ____________________
Tester: __________________
Environment: Development / Staging
API Version: __________________

Section Results:
[ ] 1. Authentication (5/5 done + 2 pending)
[ ] 2. Documents (23 tests)
[ ] 3. Blocks (22 tests)
[ ] 4. Bibliography (16 tests)
[ ] 5. Labels (9 tests)
[ ] 6. Teams (13 tests)
[ ] 7. Collaborators (13 tests)
[ ] 8. Versions (9 tests)
[ ] 9. Templates (17 tests)
[ ] 10. Assets (8 tests)
[ ] 11. Preferences (7 tests)
[ ] 12. Error Handling (8 tests)
[ ] 13. Performance (5 tests)
[ ] 14. Health (4 tests)

Total: 159 tests
Passed: ___
Failed: ___
Blocked: ___

Critical Issues:
1. ____________________
2. ____________________
3. ____________________

Notes:
____________________
```

---

*Last updated: January 31, 2026*
