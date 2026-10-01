-- 003: one enrolment per student
--
-- Two problems came out of building the student management UI.
--
-- 1. Enrollments had no uniqueness on StudentId, so the seed left every student
--    with six rows spanning three different class/section pairs. A student was
--    therefore enrolled in three classes at once.
--
-- 2. GET /api/students LEFT JOINs Enrollments, so each extra row produced
--    another copy of the student in the roster: four students rendered as
--    eighteen rows. GET /api/students/search hid this with SELECT DISTINCT, but
--    that only picks an arbitrary ClassName/SectionName for the duplicates.
--
-- AssignStudentToClass was written assuming a single row: it SELECTs one Id then
-- UPDATEs every row for that student, which is how the duplicates spread.

-- Keep the most recent enrolment for each student and drop the rest. The newest
-- row wins because it is the one the last admin action wrote.
DELETE FROM enrollments e
USING enrollments newer
WHERE e.studentid = newer.studentid
  AND e.id < newer.id;

-- Enforce the invariant the application already assumed. AcademicYearId is
-- deliberately not part of the key: AssignStudentToClass treats a student as
-- having exactly one current enrolment, and a nullable column would not
-- participate in the uniqueness check anyway.
CREATE UNIQUE INDEX IF NOT EXISTS ux_enrollments_studentid
    ON enrollments (studentid);

-- Verify: every student should now report exactly 0 or 1 enrolments.
--   SELECT s.id, s.rollnumber, count(e.id) AS enrolments
--   FROM students s LEFT JOIN enrollments e ON e.studentid = s.id
--   GROUP BY s.id, s.rollnumber ORDER BY s.id;
