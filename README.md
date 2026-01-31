# Lilia Editor API

.NET 10 Web API for the Lilia document editor.

## Project Structure

```
lilia-editor-api/
├── src/
│   ├── Lilia.Api/           # Web API project
│   │   ├── Controllers/     # API endpoints
│   │   ├── Services/        # Business logic
│   │   └── Middleware/      # Custom middleware
│   ├── Lilia.Core/          # Domain entities and DTOs
│   │   ├── Entities/
│   │   ├── DTOs/
│   │   └── Interfaces/
│   └── Lilia.Infrastructure/ # Data access layer
│       ├── Data/
│       │   ├── LiliaDbContext.cs
│       │   └── Configurations/
│       └── Repositories/
├── database/                # SQL migration scripts
└── tests/
```

## API Endpoints

### Documents
- `GET /api/documents` - List user's documents
- `GET /api/documents/{id}` - Get document with blocks
- `POST /api/documents` - Create document
- `PUT /api/documents/{id}` - Update document metadata
- `DELETE /api/documents/{id}` - Delete document
- `POST /api/documents/{id}/duplicate` - Duplicate document
- `GET /api/documents/shared/{shareLink}` - Get shared document (public)
- `POST /api/documents/{id}/share` - Generate share link
- `DELETE /api/documents/{id}/share` - Revoke share link

### Blocks
- `GET /api/documents/{docId}/blocks` - List blocks
- `POST /api/documents/{docId}/blocks` - Create block
- `PUT /api/documents/{docId}/blocks/{id}` - Update block
- `DELETE /api/documents/{docId}/blocks/{id}` - Delete block
- `POST /api/documents/{docId}/blocks/batch` - Batch update blocks
- `PUT /api/documents/{docId}/blocks/reorder` - Reorder blocks

### Bibliography
- `GET /api/documents/{docId}/bibliography` - Get bibliography entries
- `POST /api/documents/{docId}/bibliography` - Add entry
- `PUT /api/documents/{docId}/bibliography/{id}` - Update entry
- `DELETE /api/documents/{docId}/bibliography/{id}` - Delete entry
- `POST /api/documents/{docId}/bibliography/import` - Import BibTeX
- `GET /api/documents/{docId}/bibliography/export` - Export BibTeX
- `POST /api/documents/{docId}/bibliography/doi` - Lookup DOI

### Labels
- `GET /api/labels` - List user's labels
- `POST /api/labels` - Create label
- `PUT /api/labels/{id}` - Update label
- `DELETE /api/labels/{id}` - Delete label
- `POST /api/documents/{docId}/labels/{labelId}` - Add label to document
- `DELETE /api/documents/{docId}/labels/{labelId}` - Remove label

### Teams
- `GET /api/teams` - List user's teams
- `GET /api/teams/{id}` - Get team details
- `POST /api/teams` - Create team
- `PUT /api/teams/{id}` - Update team
- `DELETE /api/teams/{id}` - Delete team
- `GET /api/teams/{id}/members` - List team members
- `POST /api/teams/{id}/members` - Invite user to team
- `PUT /api/teams/{id}/members/{userId}` - Update member role
- `DELETE /api/teams/{id}/members/{userId}` - Remove member
- `GET /api/teams/{id}/documents` - List team documents

### Collaborators
- `GET /api/documents/{docId}/collaborators` - List all collaborators
- `POST /api/documents/{docId}/collaborators/users` - Add user collaborator
- `POST /api/documents/{docId}/collaborators/groups` - Share with group
- `PUT /api/documents/{docId}/collaborators/users/{userId}` - Update user role
- `PUT /api/documents/{docId}/collaborators/groups/{groupId}` - Update group role
- `DELETE /api/documents/{docId}/collaborators/users/{userId}` - Remove user
- `DELETE /api/documents/{docId}/collaborators/groups/{groupId}` - Remove group

### Versions
- `GET /api/documents/{docId}/versions` - List versions
- `GET /api/documents/{docId}/versions/{id}` - Get version snapshot
- `POST /api/documents/{docId}/versions` - Create version snapshot
- `POST /api/documents/{docId}/versions/{id}/restore` - Restore version
- `DELETE /api/documents/{docId}/versions/{id}` - Delete version

### Templates
- `GET /api/templates` - List templates
- `GET /api/templates/{id}` - Get template
- `POST /api/templates` - Create template from document
- `PUT /api/templates/{id}` - Update template
- `DELETE /api/templates/{id}` - Delete template
- `POST /api/templates/{id}/use` - Create document from template
- `GET /api/templates/categories` - List categories

### Assets
- `GET /api/documents/{docId}/assets` - List assets
- `POST /api/documents/{docId}/assets` - Upload asset (presigned URL)
- `GET /api/documents/{docId}/assets/{id}` - Get asset URL
- `DELETE /api/documents/{docId}/assets/{id}` - Delete asset

### Preferences
- `GET /api/preferences` - Get user preferences
- `PUT /api/preferences` - Update preferences
- `GET /api/preferences/shortcuts` - Get keyboard shortcuts
- `PUT /api/preferences/shortcuts` - Update shortcuts

## Getting Started

### Prerequisites
- .NET 10 SDK
- PostgreSQL 15+

### Database Setup

1. Create the database:
```bash
createdb lilia_core
```

2. Run migrations:
```bash
psql -d lilia_core -f database/001_create_lilia_core.sql
psql -d lilia_core -f database/003_seed_templates.sql
```

### Configuration

Update `appsettings.json` with your settings:

```json
{
  "ConnectionStrings": {
    "LiliaCore": "Host=localhost;Database=lilia_core;Username=postgres;Password=..."
  },
  "Clerk": {
    "Authority": "https://clerk.your-domain.com",
    "Issuer": "https://clerk.your-domain.com"
  }
}
```

For production with Cloudflare R2:
```json
{
  "R2": {
    "Endpoint": "https://<account-id>.r2.cloudflarestorage.com",
    "AccessKeyId": "...",
    "SecretAccessKey": "...",
    "BucketName": "lilia-assets",
    "PublicUrl": "https://assets.lilia.app"
  }
}
```

### Run

```bash
cd src/Lilia.Api
dotnet run
```

The API will start on `http://localhost:5000`.

## Authentication

The API uses Clerk JWT authentication. Include the JWT token in the Authorization header:

```
Authorization: Bearer <clerk-jwt-token>
```

## Storage

- **Development**: Local file storage in `./uploads`
- **Production**: Cloudflare R2 with presigned URLs for uploads

## Roles

| Role | Permissions |
|------|-------------|
| owner | read, write, delete, manage, transfer |
| editor | read, write |
| viewer | read |
