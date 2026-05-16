---
artifact_type: api_index
name: [APIName]
last_regenerated: YYYY-MM-DD by [agent name | user]
source_state: <git commit hash | "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - path/to/controllers/**
  - path/to/routes/**
stale_when:
  - any controller / route file modified
  - any DTO / request-response shape changed
---

# [APIName] — API Index

> Token budget: 200–800. List endpoints; cite source for shapes that are large or volatile.

## Endpoints
| Method | Path | Auth | Implemented in | Description |
|:---|:---|:---|:---|:---|
| GET | `/api/users/:id` | Bearer | `UserController.getById:42` | fetch user by id |
| POST | `/api/users` | Bearer | `UserController.create:88` | create user |
| DELETE | `/api/users/:id` | Bearer + admin | `UserController.delete:130` | hard delete |

## Auth model
[How auth works for this API surface. One paragraph. Cite the middleware file:line.]

## Request / response shapes
> Include shapes for endpoints whose contract is small/stable. For volatile or large shapes, cite the DTO file instead.

### POST /api/users
**Request:**
```json
{ "email": "string", "password": "string" }
```
**Response 201:**
```json
{ "id": "uuid", "email": "string", "createdAt": "iso8601" }
```
**Errors:** `409 EMAIL_EXISTS`, `422 VALIDATION_FAILED`

### GET /api/users/:id
**Shapes:** see `dto/UserDto.ts:1-40` (volatile — DTO covers this).

## Versioning / deprecations
- [List any deprecated endpoints + sunset date, or `none`.]

## Out of scope
- [Internal-only RPCs, admin endpoints, etc. — name where they're documented or `none`.]
