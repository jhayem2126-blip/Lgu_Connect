-- =====================================================================
-- Trinidad LGU Connect - least-privilege MySQL account (recommended)
--
-- 1. Create the schema first (Database Setup wizard -> "Create / Update Schema",
--    using an administrative MySQL account, or run schema.sql by hand).
-- 2. Run this script as an administrative MySQL user, after replacing the
--    password placeholder with a long random password.
-- 3. In the Database Setup wizard, switch the connection to this account.
--
-- The application only needs data access. It never creates, alters or drops
-- tables at run time, so it must not run with CREATE/DROP/ALTER privileges.
-- Restrict the host part ('%') to the LGU network or workstation addresses.
-- =====================================================================

CREATE USER IF NOT EXISTS 'tlc_app'@'%' IDENTIFIED BY 'REPLACE-WITH-A-LONG-RANDOM-PASSWORD';

GRANT SELECT, INSERT, UPDATE, DELETE ON trinidad_lgu_connect.* TO 'tlc_app'@'%';

-- Example of a tighter host restriction instead of '%':
-- CREATE USER IF NOT EXISTS 'tlc_app'@'192.168.1.%' IDENTIFIED BY '...';
-- GRANT SELECT, INSERT, UPDATE, DELETE ON trinidad_lgu_connect.* TO 'tlc_app'@'192.168.1.%';
