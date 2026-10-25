# Trinidad LGU Connect — Completion Checklist

Reference images: `Picture1.png` (7 resident screens), `Picture2.png` (8 admin screens + splash), `lgu.jpg` (official seal).
Screenshots of the finished application: [`docs/screenshots`](screenshots).

**How each item was verified**

| Code | Meaning |
|---|---|
| **UI** | Opened by the automated UI test with seeded data; the screen loaded its data without any error and was compared visually with the reference image. |
| **IT** | Covered by a database integration test (real MySQL 9.7, real MailKit against an in-process SMTP server). |
| **UT** | Covered by a unit test. |
| **Code** | Implemented and compiled, wired to a real handler, but the click itself was not automated. |
| **Manual** | Needs a person or external service to confirm (listed under *Not yet verified*). |

---

## 1. Screen-by-screen checklist from the reference images

### Branding (splash, admin screen 9)
| Element | Status | Verified |
|---|---|---|
| Official seal (from `lgu.jpg`, circular crop) | Done | UI |
| Landscape background with blue overlay | Done — extracted from the mock-up (only 270 px wide there); replaceable file | UI |
| "TRINIDAD LGU CONNECT", subtitle, divider, italic tagline | Done | UI |
| White and gold curved band | Done | UI |
| Portal buttons (Resident / Admin & LGU Personnel) and "Create an account" | Added so the splash is the "Main" screen the admin login returns to | UI, Code |

### Resident 1 — Login
| Element | Status | Verified |
|---|---|---|
| Title bar "Trinidad LGU Connect - Resident Login" | Done | UI |
| Left brand panel (seal, name, subtitle, tagline, town photo) | Done | UI |
| User icon, "Resident Login", "Welcome back! Please login to your account." | Done | UI |
| Username or Email field (icon, placeholder) | Done | UI, IT (username and email login) |
| Password field with show/hide eye | Done | UI, Code |
| Login button → shared `AuthService.LoginAsync` | Done | IT |
| "Don't have an account? Register here" | Done | Code |
| Forgot password? (required by spec) | Added | Code + IT for the workflow |
| Back to Main | Added | Code |

### Resident 2 — Registration
| Element | Status | Verified |
|---|---|---|
| Full Name, Email Address, Username, Password, Confirm Password (eye toggles) | Done | UI |
| Password rule hint | Added | UI |
| Register (validation, duplicate checks, hashing, verification email) | Done | IT |
| Cancel, "Already have an account? Login here" | Done | Code |

### Resident 3 — Dashboard
| Element | Status | Verified |
|---|---|---|
| Sidebar: avatar, name, "Resident", Dashboard / Report a Concern / My Reports / Profile / Settings / Logout | Done | UI |
| "Welcome, {name}!" and tagline | Done | UI |
| Date and time (live, updates every 15 s) | Done | UI |
| Notification bell with unread badge and list of status updates | Done | UI (badge "7" from seeded data), Code (menu) |
| Cards Total Reports / In Progress / Resolved / Closed from the resident's own data; click → filtered My Reports | Done | UI, IT (counts) |
| Recent Reports table (ID, Category, Location, Status badge, Date Reported, Actions) | Done | UI |
| View All, row double-click / Enter, View action | Done | Code |
| Empty state | Done | Code |

### Resident 4 — Report a Concern
| Element | Status | Verified |
|---|---|---|
| Header icon, title, subtitle | Done | UI |
| Category * (8 categories from the database), Subcategory (optional) | Done | UI, IT |
| Location * with pin button (barangays of Trinidad) | Done | UI, Code |
| Description * with character counter | Done | UI |
| Date Reported (calendar pop-up; no future dates; ≤ 1 year back) | Done | UI, IT (future date rejected) |
| Attach Photo (click or drag-and-drop) → Select and Attach Picture; thumbnails with remove and viewer | Done | UI, Code |
| Submit Report (validation, transaction, report ID shown, duplicate-click protection) | Done | IT (submit, duplicate key returns same report, invalid inputs rejected) |
| Cancel (confirms when there are unsaved changes) | Done | Code |
| Concern Categories panel with coloured icons (click selects the category) | Done | UI |

### Resident 5 — Select and Attach Picture
| Element | Status | Verified |
|---|---|---|
| Dashed drop zone, icon, "Click to choose a picture or drag and drop here", "JPG, PNG (Max size 5MB)" | Done (limit text comes from Settings) | UI |
| Real Windows file picker (multi-select), drag-and-drop | Done | Code (Manual to click) |
| Thumbnails with red × remove; click opens the viewer | Done | UI |
| Type/size/signature/decoding validation, re-encoding, duplicate detection | Done | UT |
| Cancel / Attach | Done | Code |

### Resident 6 — My Reports
| Element | Status | Verified |
|---|---|---|
| Header, search box, Filter toggle | Done | UI |
| Filters: category, status, location, date from/to, clear, refresh | Done | IT (query filters), UI |
| Table: ID, Category, Location, Status, Date Reported, Actions (view, photos) | Done | UI |
| Pagination | Done | UI, IT (paging) |
| Ownership enforced in SQL | Done | IT |

### Resident 7 — Concern Details / Status
| Element | Status | Verified |
|---|---|---|
| Concern Information: photo, ID, Category pill, Location, Date Reported, Status pill, Description, Attached Pictures (name, size) | Done | UI |
| Every picture opens the viewer with that report's actual images | Done | UI, IT (two different images returned) |
| Status Updates timeline from saved history (time, by, remarks; Pending/Skipped) | Done | UI, IT |
| "View all updates" (full chronological history) | Added | UI |
| Update Status panel: residents may only withdraw (Submitted → Closed) or confirm (Resolved → Closed); otherwise explains why | Done (security rule from the spec) | IT |

### Admin 1 — Admin / LGU Personnel Login
| Element | Status | Verified |
|---|---|---|
| Full background, seal, name, tagline | Done | UI |
| Card: title, subtitle, Username (icon), Password (icon, eye), Login, OR, Back to Main | Done | UI |
| Same authentication service; wrong-portal accounts are refused | Done | IT |
| Forgot password? | Added | Code |

### Admin 2 — Dashboard
| Element | Status | Verified |
|---|---|---|
| Sidebar "Trinidad LGU Connect / Admin Panel" with seal; Dashboard, Concerns, Reports, User Management, Settings, Logout (by permission) | Done | UI |
| "Dashboard", "Welcome, {name}! \| Trinidad, Bohol", account chip (menu: Settings, Change Password) | Done | UI |
| Cards Total / Submitted / In Progress / Resolved / Closed (click → filtered list) | Done | UI, IT |
| Concerns by Category bar chart (bar click → filtered list) | Done | UI, IT |
| Concerns by Status donut with total and legend (slice click → filtered list) | Done | UI |
| Zero data shows "No data" instead of misleading percentages | Done | Code |
| Refresh button, auto-refresh every 60 s | Added | UI |

### Admin 3 — Submitted Concerns
| Element | Status | Verified |
|---|---|---|
| Search, Filter, Category, Status, Date From, Date To (+ Location, Clear, Refresh) | Done | UI, IT |
| Table: ID, Resident, Category, Location, Date Reported, Status, Actions (view, photos, update) | Done | UI |
| "Showing 1 to 10 of 13 entries", page buttons | Done | UI |

### Admin 4 — Concern Details
| Element | Status | Verified |
|---|---|---|
| Photo, Concern ID, Category, Resident (+ email), Location, Date Reported, Status | Done | UI |
| Description, Attached Pictures (viewer) | Done | UI |
| Status History table | Added | UI |
| Update Status, Close (returns to the previous screen, refreshed) | Done | UI, Code |

### Admin 5 — Update Concern Status
| Element | Status | Verified |
|---|---|---|
| Concern ID and Current Status cards | Done | UI |
| New Status (only workflow-valid targets), Remarks, Update Date | Done | UI, IT |
| Status History table (Date, Status, Remarks, Updated By) | Done | UI, IT |
| Update Status: permission, transition, concurrency, transaction, history row, audit, resident email | Done | IT |
| Cancel (no database change) | Done | Code |

### Admin 6 — Reports
| Element | Status | Verified |
|---|---|---|
| Report Type, Date Range (presets + custom From/To), Generate | Done | UI, IT |
| Concerns by Status pie with % labels and legend "n (p%)"; category/date bar charts for other types | Done | UI |
| Summary: Total Concerns, Resolved, Closed (+ other statuses) | Done | UI |
| Reports Available: Status Report, Category Report, Date Range Report, Export to PDF | Done | Code (tiles), UT (PDF) |

### Admin 7 — User Management
| Element | Status | Verified |
|---|---|---|
| Title, subtitle, Add New User | Done | UI |
| Search, Filter (role, status) | Done | UI, IT |
| Table: ID, Name, Username, Email, Role, Status, Actions (edit, activate/deactivate, delete) | Done | UI |
| Add/Edit dialog (role assignment, active, verified, contact, optional new password) with confirmations | Done | UI, IT |
| Rules: no self-demotion/deactivation, last admin protected, only admins create admins, records-bearing users cannot be deleted | Done | IT |

### Admin 8 — Report Preview
| Element | Status | Verified |
|---|---|---|
| Seal, name, subtitle, Date Generated, report title, date range | Done | UI |
| Summary boxes, Concerns by Category table (plus status and concern-list sections, multi-page) | Done | UI, UT |
| Print, Print Preview, Export PDF / Excel / CSV | Done | UT (files), Code (dialogs) |

### Settings (not shown in the references — designed to match)
| Element | Status | Verified |
|---|---|---|
| System, security and notification settings saved in MySQL (validated, audited) | Done | UI, IT |
| Email (SMTP) status, Configure SMTP (DPAPI-encrypted password), Send Test Email | Done | UI, IT (test email path) |
| Database status and Test Connection | Done | UI |
| My Account / Change Password | Done | UI, Code |
| Audit log viewer | Added | UI |
| Resident settings: email notification preference, change password, about | Done | UI |

### Image Viewer
| Element | Status | Verified |
|---|---|---|
| Large preview, Previous/Next, Zoom In/Out, Fit, Actual Size, zoom %, file name, size, dimensions, Close | Done | UI |
| Keyboard (← → + − 0 1 Esc), mouse wheel, drag to pan, double-click toggles fit | Done | Code |
| Missing/corrupt image shows a message instead of crashing | Done | Code |

---

## 2. Interactive-element audit

Every visible control has a handler; there are no placeholder buttons. Informational elements (headings, labels, badges, the
clock, the summary numbers, the "Showing x to y" text) display data only.

| Area | Elements | Handler |
|---|---|---|
| Sidebar | every item, Logout (with confirmation) | `MainShellForm.NavigateRoot`, `AppController.LogoutAsync` |
| Dashboard cards | 4 resident + 5 admin cards | navigate to a filtered list |
| Charts | bars, donut slices, legend rows | navigate to a filtered list |
| Tables | row double-click, Enter, action icons | open details / photos / update status / edit / toggle / delete |
| Pagers | ‹ › and page numbers | reload the requested page from MySQL |
| Search boxes | typing (400 ms debounce) | filtered SQL query |
| Filters | every drop-down and date, Clear, Refresh | filtered SQL query |
| Thumbnails | click / Enter; × removes (before submission) | image viewer with that report's pictures |
| Forms | Submit, Cancel, Register, Login, Update Status, Save, Generate, Export, Print | service calls with validation and confirmation |
| Account | bell, account chip, Change Password, Forgot password, Verify, Resend code | dialogs and services |

---

## 3. Test results (run on 2026-10-16, Windows 11, MySQL 9.7.0, display at 150 %)

```
dotnet test TrinidadLGUConnect.slnx   (TLC_TEST_DB set)
Total tests: 52   Passed: 52   Failed: 0   Skipped: 0
```

| Group | Tests | What is exercised |
|---|---|---|
| Unit | 34 | hashing, validation, codes, workflow rules, schema safety, date ranges, CSV/Excel/PDF export, pagination, image validation |
| Integration | 17 | barangay rules (required, validated, stored, filtered, exported); profile and photo editing for admin, staff and resident; v1 to v2 upgrade keeps data; schema idempotency; one-time admin setup; shared login with portal checks; registration + email verification; end-to-end resident → admin → resident workflow with photos, filters, status history, notifications, dashboards and reports; ownership isolation; session tampering; password recovery (unknown email, wrong code, weak password, single use, old sessions revoked, rate limits); expired codes; SMTP failure; lockout; user-management rules; settings; logout; submission validation |
| UI | 1 | opens 28 screens/dialogs with seeded data and captures screenshots; fails on any error message |

Build: `dotnet build` — 0 warnings, 0 errors. Release publish (`win-x64`, self-contained) succeeded.
The real executable was started and showed the splash followed by the Database Setup wizard (no configuration present).

Bugs found and fixed by these tests included: a crash when any login screen opened (layout running before field
initialisation), ClosedXML refusing temporary file names, PDFsharp 6.5's AGPL licence enforcement, a missing font resolver for
PDF export, and many high-DPI layout defects found by comparing screenshots with the references.

## 4. Not yet verified (needs your environment)

| Item | How to complete |
|---|---|
| Delivery to a real mailbox (Gmail / Microsoft 365) | Configure SMTP in Settings and use **Send Test Email**, then register a test resident and run Forgot Password. The code path is tested against an SMTP server, but not against an external provider. |
| Printing to a physical printer and the Print Preview window | Open Reports → Status Report → **Print Preview** / **Print**. |
| Clicking through the Windows file picker, drag-and-drop from Explorer, and Save dialogs for exports | Attach photos in Report a Concern; export from Report Preview. |
| Session idle timeout (default 30 minutes) | Leave the app idle, or lower the timeout in Settings to 5 minutes. |
| Window sizes other than 1280 × 752 logical and display scaling other than 150 % | Resize/maximise the window on your target PCs. |
| Multi-workstation use against one shared MySQL server | Install on a second PC and point it at the same server. |

## 5. Added after the first delivery

| Feature | Verified |
|---|---|
| Barangay (type with auto-complete or pick) on Report a Concern, Registration, My Profile and User Management | IT, UI |
| Barangay filter on My Reports and Submitted Concerns; barangay in search, tables, details, CSV/Excel/PDF | IT, UI |
| My Profile for every role, with profile picture upload/remove (validated, cropped to 256 x 256) | IT, UI |
| Admin account chip shows the profile picture; resident sidebar shows it | UI |
| Schema v2 upgrade applied to the live database after a mysqldump backup | Manual (verified row counts) |
