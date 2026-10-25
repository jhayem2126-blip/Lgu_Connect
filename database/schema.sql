-- =====================================================================
-- Trinidad LGU Connect - Community Concern Reporting and Monitoring System
-- MySQL schema, version 2 (v2 adds barangays and profile photos)
--
-- Requirements: MySQL 8.0.16+ (tested target: MySQL 9.x), InnoDB, utf8mb4.
--
-- This script is idempotent and NON-DESTRUCTIVE:
--   * every table uses CREATE TABLE IF NOT EXISTS
--   * every seed row uses INSERT IGNORE
--   * nothing is ever dropped, truncated or deleted
-- It is embedded in the application and executed by the database setup
-- wizard, and it can also be run by hand:
--   mysql -u <admin user> -p trinidad_lgu_connect < database/schema.sql
-- All timestamps are stored in UTC.
-- =====================================================================

CREATE TABLE IF NOT EXISTS schema_migrations (
    version      INT          NOT NULL PRIMARY KEY,
    description  VARCHAR(200) NOT NULL,
    applied_at   DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP())
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Roles and permissions
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS roles (
    id           TINYINT UNSIGNED NOT NULL PRIMARY KEY,
    name         VARCHAR(30)      NOT NULL,
    description  VARCHAR(200)     NULL,
    UNIQUE KEY uq_roles_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS permissions (
    id           SMALLINT UNSIGNED NOT NULL PRIMARY KEY,
    code         VARCHAR(60)       NOT NULL,
    description  VARCHAR(200)      NULL,
    UNIQUE KEY uq_permissions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS role_permissions (
    role_id        TINYINT UNSIGNED  NOT NULL,
    permission_id  SMALLINT UNSIGNED NOT NULL,
    PRIMARY KEY (role_id, permission_id),
    CONSTRAINT fk_role_permissions_role FOREIGN KEY (role_id) REFERENCES roles (id),
    CONSTRAINT fk_role_permissions_permission FOREIGN KEY (permission_id) REFERENCES permissions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Barangays of Trinidad, Bohol (used by concerns and user profiles)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS barangays (
    id          TINYINT UNSIGNED NOT NULL PRIMARY KEY,
    name        VARCHAR(60)      NOT NULL,
    sort_order  TINYINT          NOT NULL,
    is_active   TINYINT(1)       NOT NULL DEFAULT 1,
    UNIQUE KEY uq_barangays_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Users (one role per user; residents, staff and administrators share
-- this table and the same authentication service)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id                   INT UNSIGNED     NOT NULL AUTO_INCREMENT PRIMARY KEY,
    full_name            VARCHAR(120)     NOT NULL,
    email                VARCHAR(254)     NOT NULL,
    username             VARCHAR(30)      NOT NULL,
    password_hash        VARCHAR(255)     NOT NULL,
    role_id              TINYINT UNSIGNED NOT NULL,
    contact_number       VARCHAR(20)      NULL,
    address              VARCHAR(200)     NULL,
    barangay_id          TINYINT UNSIGNED NULL,
    profile_photo        MEDIUMBLOB       NULL,
    profile_photo_updated_at DATETIME     NULL,
    is_active            TINYINT(1)       NOT NULL DEFAULT 1,
    email_verified       TINYINT(1)       NOT NULL DEFAULT 0,
    notify_email         TINYINT(1)       NOT NULL DEFAULT 1,
    failed_login_count   INT              NOT NULL DEFAULT 0,
    lockout_until        DATETIME         NULL,
    last_login_at        DATETIME         NULL,
    password_changed_at  DATETIME         NULL,
    notifications_seen_at DATETIME        NULL,
    created_by           INT UNSIGNED     NULL,
    created_at           DATETIME         NOT NULL DEFAULT (UTC_TIMESTAMP()),
    updated_at           DATETIME         NOT NULL DEFAULT (UTC_TIMESTAMP()),
    UNIQUE KEY uq_users_email (email),
    UNIQUE KEY uq_users_username (username),
    KEY ix_users_role (role_id),
    KEY ix_users_full_name (full_name),
    KEY ix_users_barangay (barangay_id),
    CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles (id),
    CONSTRAINT fk_users_barangay FOREIGN KEY (barangay_id) REFERENCES barangays (id),
    CONSTRAINT fk_users_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Server-side session records. Every privileged operation re-validates the
-- session against this table and re-reads the user's role and status, so
-- changing values in client memory cannot grant extra privileges.
CREATE TABLE IF NOT EXISTS user_sessions (
    id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id       INT UNSIGNED    NOT NULL,
    token_hash    CHAR(64)        NOT NULL,
    machine_name  VARCHAR(100)    NULL,
    created_at    DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    last_seen_at  DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    expires_at    DATETIME        NOT NULL,
    revoked_at    DATETIME        NULL,
    UNIQUE KEY uq_user_sessions_token (token_hash),
    KEY ix_user_sessions_user (user_id),
    CONSTRAINT fk_user_sessions_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Only SHA-256 hashes of codes are stored. Codes are single-use and expire.
CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id          INT UNSIGNED    NOT NULL,
    token_hash       CHAR(64)        NOT NULL,
    expires_at       DATETIME        NOT NULL,
    used_at          DATETIME        NULL,
    revoked_at       DATETIME        NULL,
    failed_attempts  INT             NOT NULL DEFAULT 0,
    created_at       DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    UNIQUE KEY uq_password_reset_tokens_hash (token_hash),
    KEY ix_password_reset_tokens_user (user_id, created_at),
    CONSTRAINT fk_password_reset_tokens_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS email_verification_tokens (
    id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id          INT UNSIGNED    NOT NULL,
    token_hash       CHAR(64)        NOT NULL,
    expires_at       DATETIME        NOT NULL,
    used_at          DATETIME        NULL,
    revoked_at       DATETIME        NULL,
    failed_attempts  INT             NOT NULL DEFAULT 0,
    created_at       DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    UNIQUE KEY uq_email_verification_tokens_hash (token_hash),
    KEY ix_email_verification_tokens_user (user_id, created_at),
    CONSTRAINT fk_email_verification_tokens_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Rate limiting for logins, recovery requests and code checks.
-- identifier_hash is a SHA-256 of the normalised identifier, never the raw value.
CREATE TABLE IF NOT EXISTS auth_attempts (
    id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    attempt_type     VARCHAR(30)     NOT NULL,
    identifier_hash  CHAR(64)        NOT NULL,
    succeeded        TINYINT(1)      NOT NULL DEFAULT 0,
    machine_name     VARCHAR(100)    NULL,
    created_at       DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    KEY ix_auth_attempts_lookup (attempt_type, identifier_hash, created_at),
    KEY ix_auth_attempts_machine (attempt_type, machine_name, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Concern reference data
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS concern_categories (
    id          TINYINT UNSIGNED NOT NULL PRIMARY KEY,
    code        VARCHAR(40)      NOT NULL,
    name        VARCHAR(80)      NOT NULL,
    short_name  VARCHAR(40)      NOT NULL,
    color_hex   CHAR(7)          NOT NULL,
    sort_order  TINYINT          NOT NULL,
    is_active   TINYINT(1)       NOT NULL DEFAULT 1,
    UNIQUE KEY uq_concern_categories_code (code),
    UNIQUE KEY uq_concern_categories_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS concern_subcategories (
    id           SMALLINT UNSIGNED NOT NULL PRIMARY KEY,
    category_id  TINYINT UNSIGNED  NOT NULL,
    name         VARCHAR(80)       NOT NULL,
    sort_order   TINYINT           NOT NULL,
    is_active    TINYINT(1)        NOT NULL DEFAULT 1,
    UNIQUE KEY uq_concern_subcategories_name (category_id, name),
    CONSTRAINT fk_concern_subcategories_category FOREIGN KEY (category_id) REFERENCES concern_categories (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS concern_statuses (
    id          TINYINT UNSIGNED NOT NULL PRIMARY KEY,
    code        VARCHAR(20)      NOT NULL,
    name        VARCHAR(30)      NOT NULL,
    sort_order  TINYINT          NOT NULL,
    is_final    TINYINT(1)       NOT NULL DEFAULT 0,
    UNIQUE KEY uq_concern_statuses_code (code),
    UNIQUE KEY uq_concern_statuses_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- The configured workflow. actor_scope: 'staff' (Administrator/Staff with
-- concern.update_status) or 'resident' (owner of the concern).
CREATE TABLE IF NOT EXISTS concern_status_transitions (
    from_status_id  TINYINT UNSIGNED NOT NULL,
    to_status_id    TINYINT UNSIGNED NOT NULL,
    actor_scope     VARCHAR(10)      NOT NULL,
    PRIMARY KEY (from_status_id, to_status_id, actor_scope),
    CONSTRAINT fk_transitions_from FOREIGN KEY (from_status_id) REFERENCES concern_statuses (id),
    CONSTRAINT fk_transitions_to FOREIGN KEY (to_status_id) REFERENCES concern_statuses (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Concerns, attachments and status history
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS concerns (
    id               INT UNSIGNED      NOT NULL AUTO_INCREMENT PRIMARY KEY,
    report_code      VARCHAR(20)       NULL,
    submission_key   CHAR(36)          NOT NULL,
    resident_id      INT UNSIGNED      NOT NULL,
    category_id      TINYINT UNSIGNED  NOT NULL,
    subcategory_id   SMALLINT UNSIGNED NULL,
    barangay_id      TINYINT UNSIGNED  NULL,
    location         VARCHAR(200)      NOT NULL,
    description      VARCHAR(2000)     NOT NULL,
    reported_at      DATETIME          NOT NULL,
    status_id        TINYINT UNSIGNED  NOT NULL DEFAULT 1,
    created_at       DATETIME          NOT NULL DEFAULT (UTC_TIMESTAMP()),
    updated_at       DATETIME          NOT NULL DEFAULT (UTC_TIMESTAMP()),
    resolved_at      DATETIME          NULL,
    closed_at        DATETIME          NULL,
    UNIQUE KEY uq_concerns_report_code (report_code),
    UNIQUE KEY uq_concerns_submission_key (submission_key),
    KEY ix_concerns_resident (resident_id, reported_at),
    KEY ix_concerns_status (status_id),
    KEY ix_concerns_category (category_id),
    KEY ix_concerns_reported_at (reported_at),
    KEY ix_concerns_barangay (barangay_id),
    CONSTRAINT fk_concerns_resident FOREIGN KEY (resident_id) REFERENCES users (id),
    CONSTRAINT fk_concerns_category FOREIGN KEY (category_id) REFERENCES concern_categories (id),
    CONSTRAINT fk_concerns_subcategory FOREIGN KEY (subcategory_id) REFERENCES concern_subcategories (id),
    CONSTRAINT fk_concerns_barangay FOREIGN KEY (barangay_id) REFERENCES barangays (id),
    CONSTRAINT fk_concerns_status FOREIGN KEY (status_id) REFERENCES concern_statuses (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Image metadata and a small JPEG thumbnail. Full image bytes live in
-- concern_attachment_data so list queries stay light. Images are stored in
-- the database (not on a local disk) so every workstation sees the same files.
CREATE TABLE IF NOT EXISTS concern_attachments (
    id                  INT UNSIGNED     NOT NULL AUTO_INCREMENT PRIMARY KEY,
    concern_id          INT UNSIGNED     NOT NULL,
    original_file_name  VARCHAR(255)     NOT NULL,
    content_type        VARCHAR(50)      NOT NULL,
    file_size_bytes     INT UNSIGNED     NOT NULL,
    width               INT              NOT NULL,
    height              INT              NOT NULL,
    sha256              CHAR(64)         NOT NULL,
    thumbnail           MEDIUMBLOB       NOT NULL,
    sort_order          TINYINT          NOT NULL DEFAULT 0,
    uploaded_by         INT UNSIGNED     NOT NULL,
    created_at          DATETIME         NOT NULL DEFAULT (UTC_TIMESTAMP()),
    KEY ix_concern_attachments_concern (concern_id, sort_order),
    CONSTRAINT fk_concern_attachments_concern FOREIGN KEY (concern_id) REFERENCES concerns (id) ON DELETE CASCADE,
    CONSTRAINT fk_concern_attachments_user FOREIGN KEY (uploaded_by) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS concern_attachment_data (
    attachment_id  INT UNSIGNED NOT NULL PRIMARY KEY,
    data           LONGBLOB     NOT NULL,
    CONSTRAINT fk_concern_attachment_data_attachment FOREIGN KEY (attachment_id) REFERENCES concern_attachments (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Append-only: every status change is a new row.
CREATE TABLE IF NOT EXISTS concern_status_history (
    id              BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT PRIMARY KEY,
    concern_id      INT UNSIGNED     NOT NULL,
    from_status_id  TINYINT UNSIGNED NULL,
    to_status_id    TINYINT UNSIGNED NOT NULL,
    remarks         VARCHAR(1000)    NULL,
    effective_date  DATE             NULL,
    changed_by      INT UNSIGNED     NOT NULL,
    changed_at      DATETIME         NOT NULL DEFAULT (UTC_TIMESTAMP()),
    KEY ix_concern_status_history_concern (concern_id, changed_at, id),
    CONSTRAINT fk_status_history_concern FOREIGN KEY (concern_id) REFERENCES concerns (id) ON DELETE CASCADE,
    CONSTRAINT fk_status_history_from FOREIGN KEY (from_status_id) REFERENCES concern_statuses (id),
    CONSTRAINT fk_status_history_to FOREIGN KEY (to_status_id) REFERENCES concern_statuses (id),
    CONSTRAINT fk_status_history_user FOREIGN KEY (changed_by) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Settings and audit log
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS app_settings (
    setting_key    VARCHAR(80)   NOT NULL PRIMARY KEY,
    setting_value  VARCHAR(1000) NOT NULL,
    updated_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
    updated_by     INT UNSIGNED  NULL,
    CONSTRAINT fk_app_settings_user FOREIGN KEY (updated_by) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS audit_logs (
    id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id       INT UNSIGNED    NULL,
    action        VARCHAR(60)     NOT NULL,
    entity_type   VARCHAR(40)     NULL,
    entity_id     VARCHAR(40)     NULL,
    details       VARCHAR(1000)   NULL,
    machine_name  VARCHAR(100)    NULL,
    created_at    DATETIME        NOT NULL DEFAULT (UTC_TIMESTAMP()),
    KEY ix_audit_logs_created (created_at),
    KEY ix_audit_logs_user (user_id),
    KEY ix_audit_logs_action (action),
    CONSTRAINT fk_audit_logs_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- =====================================================================
-- Seed / reference data (INSERT IGNORE never overwrites existing rows)
-- =====================================================================
INSERT IGNORE INTO roles (id, name, description) VALUES
    (1, 'Administrator', 'Full access to all administrative modules'),
    (2, 'Staff', 'LGU personnel who review and update concerns'),
    (3, 'Resident', 'Community member who submits and tracks concerns');

INSERT IGNORE INTO permissions (id, code, description) VALUES
    (1,  'concern.create',           'Submit a new concern'),
    (2,  'concern.view_own',         'View own concerns'),
    (3,  'concern.resident_update',  'Withdraw own concern or confirm its resolution'),
    (10, 'concern.view_all',         'View all submitted concerns'),
    (11, 'concern.update_status',    'Update the status of any concern'),
    (12, 'dashboard.admin',          'View the administrative dashboard'),
    (20, 'report.generate',          'Generate reports and analytics'),
    (21, 'report.export',            'Export and print reports'),
    (30, 'user.view',                'View user accounts'),
    (31, 'user.manage',              'Create, edit, activate and deactivate users'),
    (32, 'user.manage_admins',       'Create administrators and change administrator roles'),
    (40, 'settings.manage',          'Change system settings'),
    (41, 'audit.view',               'View the audit log');

INSERT IGNORE INTO role_permissions (role_id, permission_id) VALUES
    (3, 1), (3, 2), (3, 3),
    (2, 10), (2, 11), (2, 12), (2, 20), (2, 21),
    (1, 1), (1, 10), (1, 11), (1, 12), (1, 20), (1, 21), (1, 30), (1, 31), (1, 32), (1, 40), (1, 41);

INSERT IGNORE INTO barangays (id, name, sort_order) VALUES
    (1, 'Banlasan', 1), (2, 'Bongbong', 2), (3, 'Catoogan', 3), (4, 'Guinobatan', 4),
    (5, 'Hinlayagan Ilaud', 5), (6, 'Hinlayagan Ilaya', 6), (7, 'Kauswagan', 7), (8, 'Kinan-oan', 8),
    (9, 'La Union', 9), (10, 'La Victoria', 10), (11, 'Mabuhay Cabigohan', 11), (12, 'Mahagbu', 12),
    (13, 'Manuel M. Roxas', 13), (14, 'Poblacion', 14), (15, 'San Isidro', 15), (16, 'San Vicente', 16),
    (17, 'Santo Tomas', 17), (18, 'Soom', 18), (19, 'Tagum Norte', 19), (20, 'Tagum Sur', 20);

INSERT IGNORE INTO concern_categories (id, code, name, short_name, color_hex, sort_order) VALUES
    (1, 'WASTE',         'Waste Management',       'Waste Management',       '#22A06B', 1),
    (2, 'FLOODING',      'Flooding & Drainage',    'Flooding & Drainage',    '#1BA9E1', 2),
    (3, 'ROADS',         'Roads & Transportation', 'Roads & Transportation', '#E5484D', 3),
    (4, 'STREETLIGHTS',  'Streetlights',           'Streetlights',           '#F5A524', 4),
    (5, 'WATER',         'Water Concerns',         'Water Concerns',         '#1E6FE8', 5),
    (6, 'FACILITIES',    'Public Facilities',      'Public Facilities',      '#3F5A7A', 6),
    (7, 'ENVIRONMENTAL', 'Environmental Concerns', 'Environmental',          '#2FA84F', 7),
    (8, 'OTHER',         'Other Concerns',         'Others',                 '#8A97AB', 8);

INSERT IGNORE INTO concern_subcategories (id, category_id, name, sort_order) VALUES
    (101, 1, 'Uncollected Garbage', 1), (102, 1, 'Illegal Dumping', 2), (103, 1, 'Overflowing Bins', 3), (104, 1, 'Open Burning of Waste', 4),
    (201, 2, 'Clogged Drainage', 1), (202, 2, 'Flooded Street', 2), (203, 2, 'Damaged Canal', 3),
    (301, 3, 'Potholes', 1), (302, 3, 'Damaged Road', 2), (303, 3, 'Road Obstruction', 3), (304, 3, 'Missing Traffic Sign', 4),
    (401, 4, 'Broken Streetlight', 1), (402, 4, 'Flickering Light', 2), (403, 4, 'No Streetlight', 3),
    (501, 5, 'No Water Supply', 1), (502, 5, 'Leaking Pipe', 2), (503, 5, 'Dirty Water', 3),
    (601, 6, 'Damaged Facility', 1), (602, 6, 'Restroom Maintenance', 2), (603, 6, 'Park or Plaza Maintenance', 3),
    (701, 7, 'Illegal Tree Cutting', 1), (702, 7, 'Air Pollution', 2), (703, 7, 'Noise Pollution', 3), (704, 7, 'Water Pollution', 4),
    (801, 8, 'General Concern', 1);

INSERT IGNORE INTO concern_statuses (id, code, name, sort_order, is_final) VALUES
    (1, 'SUBMITTED',    'Submitted',    1, 0),
    (2, 'UNDER_REVIEW', 'Under Review', 2, 0),
    (3, 'IN_PROGRESS',  'In Progress',  3, 0),
    (4, 'RESOLVED',     'Resolved',     4, 0),
    (5, 'CLOSED',       'Closed',       5, 1);

INSERT IGNORE INTO concern_status_transitions (from_status_id, to_status_id, actor_scope) VALUES
    (1, 2, 'staff'), (1, 5, 'staff'),
    (2, 3, 'staff'), (2, 5, 'staff'),
    (3, 4, 'staff'), (3, 2, 'staff'),
    (4, 5, 'staff'), (4, 3, 'staff'),
    (1, 5, 'resident'),
    (4, 5, 'resident');

INSERT IGNORE INTO app_settings (setting_key, setting_value) VALUES
    ('app.municipality',                      'Trinidad, Bohol'),
    ('app.items_per_page',                    '10'),
    ('security.require_email_verification',  'true'),
    ('security.session_timeout_minutes',      '30'),
    ('security.max_failed_logins',            '5'),
    ('security.lockout_minutes',              '15'),
    ('security.code_expiry_minutes',          '15'),
    ('attachments.max_size_mb',               '5'),
    ('attachments.max_count',                 '5'),
    ('notifications.email_on_status_change',  'true'),
    ('notifications.email_on_submission',     'true'),
    ('setup.completed',                       'false');

INSERT IGNORE INTO schema_migrations (version, description) VALUES (1, 'Initial schema');
