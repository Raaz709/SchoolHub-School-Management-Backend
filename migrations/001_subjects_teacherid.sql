-- 001: teacher <-> subject link
--
-- AdminManagementController.AssignTeacherSubjects runs
--   UPDATE Subjects SET TeacherId = @TeacherId WHERE Id = @SubjectId
-- but the subjects table had no teacherid column, so the endpoint always
-- returned 500 (42703: column "teacherid" of relation "subjects" does not exist).
--
-- Identifier casing note: this schema stores table and column names in
-- lowercase, and the application SQL relies on that by using unquoted
-- identifiers which PostgreSQL folds to lowercase. Quoting them with capitals
-- ("Subjects") fails with "relation does not exist", so everything below is
-- deliberately unquoted to match.
--
-- Modelling note: this makes a subject belong to exactly one teacher. If a
-- subject is taught by different teachers in different sections, replace this
-- with a join table (subjectid, teacherid, classid, sectionid) and rewrite
-- AssignTeacherSubjects to target it.

ALTER TABLE subjects
    ADD COLUMN IF NOT EXISTS teacherid integer NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'subjects_teacherid_fkey'
    ) THEN
        ALTER TABLE subjects
            ADD CONSTRAINT subjects_teacherid_fkey
            FOREIGN KEY (teacherid) REFERENCES teachers(id)
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_subjects_teacherid ON subjects (teacherid);
