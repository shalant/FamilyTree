# Validation Checklist — Before Full System Test

## Person Data Validations

### Name Fields
- [ ] FirstName is required and non-empty
- [ ] FirstName max length enforced (probably 100 chars)
- [ ] LastName is required and non-empty
- [ ] LastName max length enforced
- [ ] MiddleName is optional, max length enforced
- [ ] Names allow common special chars (hyphens, apostrophes, accents)
- [ ] Names reject invalid chars (leading/trailing spaces trimmed or rejected)

### Birth & Death Dates
- [ ] BirthDate is optional but if provided, must be a valid date
- [ ] DeathDate is optional but if provided, must be after BirthDate
- [ ] DeathDate cannot be in the future
- [ ] BirthDate cannot be unreasonably far in the past (e.g., before 1800)
- [ ] Age calculation is correct (accounts for leap years, month/day boundaries)
- [ ] IsDeceased flag is accurate (syncs with DeathDate presence)

### Birth & Death Places
- [ ] BirthPlace max length enforced (100-200 chars)
- [ ] DeathPlace max length enforced
- [ ] Empty strings treated same as null

### Biography & Media
- [ ] BiographyNotes max length is 5000 chars
- [ ] ProfilePhotoUrl max length is 500 chars
- [ ] ProfilePhotoUrl format validation (valid URL or null)
- [ ] ProfilePhotoUrl HTTPS only or allow HTTP in dev?

### Gender Enum
- [ ] Gender accepts only valid enum values (Unknown, Male, Female, Other)
- [ ] Gender defaults to Unknown or null appropriately
- [ ] Gender is displayed/sorted consistently

### Audit Fields
- [ ] CreatedAt is set on insert, immutable afterward
- [ ] UpdatedAt is set on insert and updated on each save
- [ ] UpdatedAt is always >= CreatedAt
- [ ] RowVersion increments on each update (concurrency control)

---

## Relationship Validations

### Logical Constraints
- [ ] A person cannot be related to themselves (PersonAId ≠ PersonBId)
- [ ] No duplicate relationships (DB constraint on (PersonAId, PersonBId, Type))
- [ ] Canonical ordering enforced (PersonAId < PersonBId for Spouse/Sibling)
- [ ] Reverse relationship not created manually (one direction only)

### Relationship Types
- [ ] Parent: child age < parent age, reasonable gap (e.g., parent at least 12 years older)
- [ ] Spouse: bidirectional relationship preserved on read
- [ ] Sibling: consistent age relationships (not enforced strictly?)
- [ ] Adopted: marked distinctly, doesn't affect age logic

### Relationship Dates
- [ ] StartDate optional, if provided must be a valid date
- [ ] EndDate optional, if provided must be after StartDate
- [ ] EndDate cannot be in future for historical relationships (e.g., divorce, death)
- [ ] Marriage date should make sense (both people alive, reasonable ages)

### Circular & Impossible Relationships
- [ ] A person cannot be their own ancestor (no circular parent chains)
- [ ] Cannot create Parent + Child relationship if it would form a cycle
- [ ] Warning or block if tree structure becomes logically impossible

---

## Medium (Photo/File) Validations

### File Handling
- [ ] File size limit enforced (probably 10-50 MB)
- [ ] File type whitelist enforced (JPG, PNG, WebP?)
- [ ] Blob upload fails gracefully with clear error message
- [ ] Missing blob connection string handled properly

### Media Metadata
- [ ] Caption max length enforced (probably 500 chars)
- [ ] URL is persisted correctly from Blob Storage
- [ ] Cascade delete works (deleting Person deletes associated Media)

---

## Form & API Validations

### PersonUpsertDto
- [ ] All required fields present in incoming request
- [ ] Validation attributes on DTO properties
- [ ] ValidationException returns 400 Bad Request with field-level errors
- [ ] Error messages are user-friendly (not internal property names)

### Relationship Creation
- [ ] Both PersonA and PersonB exist in DB
- [ ] Preventing duplicate edge cases (A→B vs B→A, especially for undirected)

---

## Concurrency & Data Integrity

### RowVersion (SQL Timestamp)
- [ ] Update fails with 409 Conflict if RowVersion doesn't match
- [ ] UI handles concurrency error gracefully (reload + retry or inform user)
- [ ] RowVersion increments on every write

### Transaction Integrity
- [ ] Creating a Spouse relationship doesn't leave DB in halfway state
- [ ] Relationship type constraints are enforced even under concurrent writes

---

## Edge Cases & Boundaries

### String Boundaries
- [ ] Empty string treated as null (or rejected per spec)
- [ ] Whitespace-only strings handled (trimmed or rejected)
- [ ] Very long strings (max length) rejected with 400
- [ ] Unicode & accented characters work in all fields
- [ ] Emoji in names (allow or block?)

### Date Boundaries
- [ ] Year 1900 → valid
- [ ] Year 2100 → valid
- [ ] Leap year Feb 29 → valid
- [ ] Invalid dates (Feb 30, Jun 31) → rejected

### Null & Required Field Handling
- [ ] Required field missing → 400 Bad Request
- [ ] Optional field as null vs omitted from JSON → consistent handling
- [ ] DTO default values sensible (e.g., Gender defaults to Unknown)

---

## Business Logic Validations

### Tree Consistency
- [ ] Age computed correctly across all nodes
- [ ] Parent-child generation gaps make sense (no 5-year-old parents)
- [ ] Spouse ages reasonable (not 60+ year gaps?)
- [ ] Deceased nodes marked correctly

### Search & Filter
- [ ] Name search case-insensitive
- [ ] Birth year filters work across optional dates
- [ ] Filtering on Gender enum doesn't break with bad input

---

## Security & Injection Checks

### Data Sanitization
- [ ] Names with SQL-like content ('; DROP TABLE ...) stored safely
- [ ] URLs don't execute JavaScript (XSS prevention)
- [ ] Special characters in biography don't break rendering

### Access Control (Post-Auth)
- [ ] User can only see their own tree
- [ ] User cannot edit other users' data
- [ ] Delete operations require confirmation

---

## UI Validation Feedback

### Form UX
- [ ] Real-time validation shows errors before submit
- [ ] Error messages are specific (not "Invalid input")
- [ ] Success toast appears on save
- [ ] Loading state prevents double-submit
- [ ] Validation clears when user starts fixing

### Error States
- [ ] Network error messages don't expose internal details
- [ ] Timeout errors handled gracefully
- [ ] Blob upload errors clearly communicated

---

## Sample Test Data

- [ ] Test with real names (international, hyphenated, apostrophes)
- [ ] Test with birth dates spanning 1800–2025
- [ ] Test with family tree depth 4+ generations
- [ ] Test with 50+ people to catch performance issues
- [ ] Test relationship edge cases (orphans, only children, large sibling groups)
