-- Give announcements a real lifecycle: author attribution, a bounded audience,
-- and a target class for class-scoped notices.
--
-- Announcements could only be listed and created. `SELECT *` returned every row
-- to every role, so a notice addressed to Parents was shown to students as well,
-- and there was no way to correct or withdraw one. `TargetRole` was free text and
-- could hold a role no page filters on, and the `ClassId` column existed but was
-- never written.
--
-- What the endpoints that follow rely on, enforced here as well:
--   * a title and a body both say something;
--   * the audience is one of the five the UI offers;
--   * who posted it is recorded so a teacher can edit their own and no one else's.

BEGIN;

-- Attribution. Nullable and ON DELETE SET NULL: removing a staff account must
-- not delete the notices they wrote.
ALTER TABLE Announcements
    ADD COLUMN IF NOT EXISTS AuthorId INT REFERENCES Users(Id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_announcements_authorid ON Announcements (AuthorId);

-- Free text becomes a bounded audience. An unknown or blank value is widened to
-- 'All' rather than blocking the migration.
UPDATE Announcements
   SET TargetRole = 'All'
 WHERE TargetRole IS NULL
    OR btrim(TargetRole) = ''
    OR TargetRole NOT IN ('All', 'Admin', 'Teacher', 'Student', 'Parent');

ALTER TABLE Announcements ALTER COLUMN TargetRole SET DEFAULT 'All';
ALTER TABLE Announcements ALTER COLUMN TargetRole SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_announcements_title') THEN
        ALTER TABLE Announcements ADD CONSTRAINT ck_announcements_title CHECK (btrim(Title) <> '');
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_announcements_content') THEN
        ALTER TABLE Announcements ADD CONSTRAINT ck_announcements_content CHECK (btrim(Content) <> '');
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_announcements_targetrole') THEN
        ALTER TABLE Announcements
            ADD CONSTRAINT ck_announcements_targetrole
            CHECK (TargetRole IN ('All', 'Admin', 'Teacher', 'Student', 'Parent'));
    END IF;
END $$;

-- The list is ordered by date and narrowed by class for the learner scopes.
CREATE INDEX IF NOT EXISTS ix_announcements_createdat ON Announcements (CreatedAt);
CREATE INDEX IF NOT EXISTS ix_announcements_classid ON Announcements (ClassId);

COMMIT;
