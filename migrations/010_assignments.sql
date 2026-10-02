-- Give assignments a real lifecycle and stop the list leaking the whole school.
--
-- The controller could create an assignment and accept a submission, but that
-- was all: no edit, no delete, no way to read or grade what was handed in, and
-- GET returned every assignment in the database to every student regardless of
-- their class. AssignmentSubmissions could also hold a second row for the same
-- student on the same assignment, because the old code relied on a check-then-
-- insert that two concurrent submissions could both pass.
--
-- What the endpoints rely on, enforced here as well:
--   * an assignment always names a subject (nullable before, matches nothing);
--   * a title is non-blank and a max score, when set, is positive;
--   * a submission always names its assignment and student, and a student has
--     at most one submission per assignment (the upsert target).

BEGIN;

-- A blank title has nothing for the list to show.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_assignments_title') THEN
        ALTER TABLE Assignments ADD CONSTRAINT ck_assignments_title CHECK (btrim(Title) <> '');
    END IF;
END $$;

-- MaxScore is nullable (not every task is scored), but a zero or negative
-- maximum is nonsense.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_assignments_maxscore') THEN
        ALTER TABLE Assignments
            ADD CONSTRAINT ck_assignments_maxscore
            CHECK (MaxScore IS NULL OR MaxScore > 0);
    END IF;
END $$;

-- An assignment with no subject cannot be scoped to a class. The table is empty
-- in every database, so dropping orphan rows first is a defensive no-op.
DELETE FROM Assignments WHERE SubjectId IS NULL;
ALTER TABLE Assignments ALTER COLUMN SubjectId SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_assignments_subjectid ON Assignments (SubjectId);
CREATE INDEX IF NOT EXISTS ix_assignments_duedate ON Assignments (DueDate);

-- Submissions gain the same shape: always attached, always timed.
UPDATE AssignmentSubmissions SET SubmittedAt = CURRENT_TIMESTAMP WHERE SubmittedAt IS NULL;
DELETE FROM AssignmentSubmissions WHERE AssignmentId IS NULL OR StudentId IS NULL;
ALTER TABLE AssignmentSubmissions ALTER COLUMN AssignmentId SET NOT NULL;
ALTER TABLE AssignmentSubmissions ALTER COLUMN StudentId SET NOT NULL;
ALTER TABLE AssignmentSubmissions ALTER COLUMN SubmittedAt SET NOT NULL;
ALTER TABLE AssignmentSubmissions ALTER COLUMN SubmittedAt SET DEFAULT CURRENT_TIMESTAMP;

-- One submission per student per assignment; this is the ON CONFLICT target.
CREATE UNIQUE INDEX IF NOT EXISTS ux_assignmentsubmissions_assignment_student
    ON AssignmentSubmissions (AssignmentId, StudentId);

CREATE INDEX IF NOT EXISTS ix_assignmentsubmissions_studentid
    ON AssignmentSubmissions (StudentId);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_assignmentsubmissions_score') THEN
        ALTER TABLE AssignmentSubmissions
            ADD CONSTRAINT ck_assignmentsubmissions_score
            CHECK (Score IS NULL OR Score >= 0);
    END IF;
END $$;

COMMIT;
