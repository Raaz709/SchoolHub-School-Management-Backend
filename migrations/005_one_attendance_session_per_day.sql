-- One attendance session per class, section and date.
--
-- A teacher could mark the same section twice on the same day. Nothing stopped
-- it, so the history list showed both sessions, a student's percentage counted
-- that day twice, and days with no session at all were never counted. The
-- controller now rejects a second mark and points at the existing session, and
-- this index makes the guarantee hold at the database level too.

BEGIN;

-- Carry each duplicate's marks across to the newest session for that day,
-- skipping students the newer session already records. Without this the
-- duplicates would cascade their records away and lose real marks.
INSERT INTO AttendanceRecords (SessionId, StudentId, Status, Remarks)
SELECT keep.Id, a.StudentId, a.Status, a.Remarks
  FROM AttendanceSessions s
  JOIN AttendanceSessions keep
    ON keep.ClassId = s.ClassId
   AND keep.SectionId IS NOT DISTINCT FROM s.SectionId
   AND keep.Date = s.Date
   AND keep.Id > s.Id
  JOIN AttendanceRecords a ON a.SessionId = s.Id
 WHERE NOT EXISTS (
       SELECT 1 FROM AttendanceRecords existing
        WHERE existing.SessionId = keep.Id
          AND existing.StudentId = a.StudentId
 );

-- Safe to delete now: ON DELETE CASCADE takes the duplicates' own records.
DELETE FROM AttendanceSessions s
 WHERE EXISTS (
       SELECT 1 FROM AttendanceSessions newer
        WHERE newer.ClassId = s.ClassId
          AND newer.SectionId IS NOT DISTINCT FROM s.SectionId
          AND newer.Date = s.Date
          AND newer.Id > s.Id
 );

-- One mark per student per session. The re-mark path updates in place instead of
-- insert-then-delete, which needs this to be safe.
DELETE FROM AttendanceRecords a
 USING AttendanceRecords b
 WHERE a.StudentId = b.StudentId
   AND a.SessionId = b.SessionId
   AND a.Id < b.Id;

CREATE UNIQUE INDEX IF NOT EXISTS ux_attendancesessions_day
    ON AttendanceSessions (ClassId, SectionId, Date);

CREATE UNIQUE INDEX IF NOT EXISTS ux_attendancerecords_student
    ON AttendanceRecords (SessionId, StudentId);

COMMIT;