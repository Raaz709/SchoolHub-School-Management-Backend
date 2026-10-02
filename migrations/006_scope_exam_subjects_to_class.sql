-- Scope an exam subject to a class, and make the mark invariants the database
-- enforces rather than the controller.
--
-- ExamSubjects had no class link and no write path at all: the table was empty in
-- every database and nothing could insert a row, so ExamsController.EnterMarks
-- could never run — it read MaxMarks from a row that could not exist. With no
-- class, a mark was also accepted for any student, so a Grade 5 could be entered
-- against a Grade 11 paper and nothing would object.
--
-- ClassId goes on ExamSubjects rather than Exams so one exam can span classes
-- (a single Mid-Term paper sat by several grades) without duplicating the exam.
-- The exam's own class list is then the distinct set of its subjects' classes.

BEGIN;

-- ON DELETE RESTRICT, not CASCADE. A cascade here would let a deleted class take
-- its exam subjects and every mark under them with it, and marks are results.
ALTER TABLE ExamSubjects
    ADD COLUMN IF NOT EXISTS ClassId INT REFERENCES Classes(Id) ON DELETE RESTRICT;

-- ExamSubjects has been empty in every database so far, because there was no
-- write path, so there is nothing to backfill and no class to invent. If a row
-- does exist, refuse and let someone choose its class rather than filing marks
-- under a guess.
DO $$
DECLARE orphans int;
BEGIN
    SELECT count(*) INTO orphans FROM ExamSubjects WHERE ClassId IS NULL;
    IF orphans > 0 THEN
        RAISE EXCEPTION
            'examsubjects holds % row(s) with no class. Set ClassId on each before rerunning this migration.', orphans;
    END IF;
END $$;

ALTER TABLE ExamSubjects ALTER COLUMN ClassId SET NOT NULL;

-- One paper per subject per class per exam. Two rows for the same triple meant
-- two independent sets of marks for one sitting, and a student's result depended
-- on which row the report happened to read.
--
-- Carry the duplicate's marks onto the newest surviving row first, skipping
-- students that row already records, so collapsing them does not lose results.
-- ON DELETE CASCADE from the drop below would otherwise take them with it.
INSERT INTO Marks (ExamSubjectId, StudentId, MarksObtained, Grade, Remarks)
SELECT keep.Id, m.StudentId, m.MarksObtained, m.Grade, m.Remarks
  FROM ExamSubjects d
  JOIN ExamSubjects keep
    ON keep.ExamId = d.ExamId
   AND keep.ClassId = d.ClassId
   AND keep.SubjectId = d.SubjectId
   AND keep.Id > d.Id
  JOIN Marks m ON m.ExamSubjectId = d.Id
 WHERE NOT EXISTS (
       SELECT 1 FROM Marks existing
        WHERE existing.ExamSubjectId = keep.Id
          AND existing.StudentId = m.StudentId
 );

DELETE FROM ExamSubjects d
 WHERE EXISTS (
       SELECT 1 FROM ExamSubjects keep
        WHERE keep.ExamId = d.ExamId
          AND keep.ClassId = d.ClassId
          AND keep.SubjectId = d.SubjectId
          AND keep.Id > d.Id
 );

CREATE UNIQUE INDEX IF NOT EXISTS ux_examsubjects_exam_class_subject
    ON ExamSubjects (ExamId, ClassId, SubjectId);

-- One mark per student per paper. The bulk save upserts in place, which needs
-- this: without it a retried save would double the row and then report a
-- percentage computed from two marks.
DELETE FROM Marks a
 USING Marks b
 WHERE a.ExamSubjectId = b.ExamSubjectId
   AND a.StudentId = b.StudentId
   AND a.Id < b.Id;

CREATE UNIQUE INDEX IF NOT EXISTS ux_marks_examsubject_student
    ON Marks (ExamSubjectId, StudentId);

-- MaxMarks is what the grade percentage divides by, so a zero here was a divide
-- by zero and a negative one graded every mark above 100%.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_examsubjects_maxmarks') THEN
        ALTER TABLE ExamSubjects
            ADD CONSTRAINT ck_examsubjects_maxmarks CHECK (MaxMarks > 0);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_marks_nonnegative') THEN
        ALTER TABLE Marks
            ADD CONSTRAINT ck_marks_nonnegative CHECK (MarksObtained >= 0);
    END IF;

    -- PassingMarks is read as a percentage threshold for pass/fail. The column
    -- defaulted to 40 and nothing ever read it; a value above 100 or below 0
    -- would make every student pass or none of them.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_exams_passingmarks') THEN
        ALTER TABLE Exams
            ADD CONSTRAINT ck_exams_passingmarks
            CHECK (PassingMarks IS NULL OR (PassingMarks >= 0 AND PassingMarks <= 100));
    END IF;

    -- An exam whose window ends before it starts is always empty, and the list
    -- showed both dates as though it were scheduled.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_exams_dates') THEN
        ALTER TABLE Exams
            ADD CONSTRAINT ck_exams_dates
            CHECK (EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate);
    END IF;
END $$;

COMMIT;
