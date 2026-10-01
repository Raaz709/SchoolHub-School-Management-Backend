-- Merges the duplicate class rows left in the seed data, then enforces
-- uniqueness so the CRUD endpoints can rely on it.
--
-- Background: Classes had no unique constraint and the seed inserted each
-- grade twice ("Grade 10" as id 1 and id 4, "Grade 9" as 2 and 5, "Grade 8" as
-- 3 and 6). Sections were duplicated the same way, twice inside each grade.
-- That ambiguity is why the student pages had to match a class by name.
--
-- Only ids 4/5/6 hold student enrolments, so those are kept as the canonical
-- rows and 1/2/3 are merged into them. Everything pointing at 1/2/3 is
-- repointed first: sections, enrolments, class-subject links, attendance
-- sessions, and fee structures.
--
-- Idempotent: safe to run more than once. Re-running is a no-op once the
-- duplicates are gone and the indexes exist.

BEGIN;

-- --- 1. Sections: fold duplicates into the lowest section id per class ---

-- Grades 1/2/3 collapse onto 4/5/6. Reassign every child row of a duplicate
-- class onto the surviving row, then drop the empties.
UPDATE enrollments e SET classid = 4 WHERE e.classid = 1;
UPDATE enrollments e SET classid = 5 WHERE e.classid = 2;
UPDATE enrollments e SET classid = 6 WHERE e.classid = 3;

-- Sections move with their class, but the section ids collide with the ones
-- already on the surviving grade (class 1 had sections 1 and 5, class 4 already
-- owns 6 and 8). Sections are deduped per class below instead of being
-- repointed by id, so only the class ownership is updated here.
UPDATE sections s SET classid = 4 WHERE s.classid = 1;
UPDATE sections s SET classid = 5 WHERE s.classid = 2;
UPDATE sections s SET classid = 6 WHERE s.classid = 3;

-- Enrolments and sessions point at a specific section. Point them at the
-- lowest-id section of the same name in their (new) class, then delete the
-- duplicate section rows.
UPDATE enrollments e
   SET sectionid = d.keep
  FROM (
        SELECT cs.classid, cs.name, min(cs.id) AS keep
          FROM sections cs
         GROUP BY cs.classid, cs.name
       ) d
 WHERE e.sectionid IS NOT NULL
   AND d.classid = e.classid
   AND e.sectionid <> d.keep
   AND (SELECT s2.name FROM sections s2 WHERE s2.id = e.sectionid) = d.name;

UPDATE attendancesessions a
   SET sectionid = d.keep
  FROM (
        SELECT cs.classid, cs.name, min(cs.id) AS keep
          FROM sections cs
         GROUP BY cs.classid, cs.name
       ) d
 WHERE a.sectionid IS NOT NULL
   AND d.classid = a.classid
   AND a.sectionid <> d.keep
   AND (SELECT s2.name FROM sections s2 WHERE s2.id = a.sectionid) = d.name;

UPDATE timetableentries t
   SET sectionid = d.keep
  FROM (
        SELECT cs.classid, cs.name, min(cs.id) AS keep
          FROM sections cs
         GROUP BY cs.classid, cs.name
       ) d
 WHERE t.sectionid IS NOT NULL
   AND d.classid = t.classid
   AND t.sectionid <> d.keep
   AND (SELECT s2.name FROM sections s2 WHERE s2.id = t.sectionid) = d.name;

DELETE FROM sections s
 WHERE s.id NOT IN (SELECT min(id) FROM sections GROUP BY classid, lower(name));

-- --- 2. Other rows keyed on a class ---

-- classsubjects has a (classid, subjectid) primary key, so repointing 1 onto 4
-- can collide with a row that already exists. Drop the ones that would
-- duplicate before moving, otherwise the insert fails.
DELETE FROM classsubjects cs
 WHERE cs.classid IN (1, 2, 3)
   AND EXISTS (
       SELECT 1 FROM classsubjects k
        WHERE k.classid = CASE cs.classid WHEN 1 THEN 4 WHEN 2 THEN 5 ELSE 6 END
          AND k.subjectid = cs.subjectid
   );

UPDATE classsubjects SET classid = 4 WHERE classid = 1;
UPDATE classsubjects SET classid = 5 WHERE classid = 2;
UPDATE classsubjects SET classid = 6 WHERE classid = 3;

UPDATE attendancesessions SET classid = 4 WHERE classid = 1;
UPDATE attendancesessions SET classid = 5 WHERE classid = 2;
UPDATE attendancesessions SET classid = 6 WHERE classid = 3;

UPDATE timetableentries SET classid = 4 WHERE classid = 1;
UPDATE timetableentries SET classid = 5 WHERE classid = 2;
UPDATE timetableentries SET classid = 6 WHERE classid = 3;

UPDATE announcements SET classid = 4 WHERE classid = 1;
UPDATE announcements SET classid = 5 WHERE classid = 2;
UPDATE announcements SET classid = 6 WHERE classid = 3;

-- Fee structures are named per grade ("Tuition Fee - Grade 10"). The class
-- move has to happen first: the duplicate grade carried a second copy of each
-- structure, and they only become duplicates once both sit on the same classid.
UPDATE feestructures SET classid = 4 WHERE classid = 1;
UPDATE feestructures SET classid = 5 WHERE classid = 2;
UPDATE feestructures SET classid = 6 WHERE classid = 3;

-- studentfees reference these by id, so repoint them onto the surviving
-- structure before the duplicates go. The window function keeps each row's own
-- id next to the surviving id for its name.
UPDATE studentfees sf
   SET feestructureid = d.keep
  FROM (
        SELECT id, min(id) OVER (PARTITION BY classid, lower(name)) AS keep
          FROM feestructures
       ) d
 WHERE sf.feestructureid = d.id
   AND d.keep <> d.id;

DELETE FROM feestructures f
 WHERE f.id NOT IN (SELECT min(id) FROM feestructures GROUP BY classid, lower(name));

-- --- 3. Drop the now-empty duplicate classes ---

-- Guarded: only classes nobody references are removed, so re-running after a
-- partial failure cannot strand a row that still points here.
DELETE FROM classes c
 WHERE c.id IN (1, 2, 3)
   AND NOT EXISTS (SELECT 1 FROM sections s WHERE s.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM enrollments e WHERE e.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM classsubjects cs WHERE cs.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM attendancesessions a WHERE a.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM timetableentries t WHERE t.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM announcements an WHERE an.classid = c.id)
   AND NOT EXISTS (SELECT 1 FROM feestructures f WHERE f.classid = c.id);

-- --- 4. Enforce uniqueness ---

-- A class name identifies a grade to the user, and duplicate names are what
-- made name-based lookups ambiguous in the first place.
CREATE UNIQUE INDEX IF NOT EXISTS ux_classes_name
    ON Classes (lower(Name));

-- Section names are unique per class, not globally: "A" is valid in every grade.
CREATE UNIQUE INDEX IF NOT EXISTS ux_sections_class_name
    ON Sections (ClassId, lower(Name));

-- --- 5. Attendance sessions ---

-- The seed created the same session twice: once on the duplicate class and once
-- on the surviving one, both for the same section and date. Students were then
-- recorded repeatedly against it (72 rows for a single student in one session).
-- Collapse to one session per class/section/date, then keep one record per
-- student within it, so attendance totals mean something.
DELETE FROM attendancerecords ar USING attendancesessions a
 WHERE ar.sessionid = a.id
   AND EXISTS (
       SELECT 1 FROM attendancerecords k
        WHERE k.sessionid = a.id AND k.studentid = ar.studentid AND k.id < ar.id
   );

-- Survivor is the session that actually holds marks, highest id as the
-- tie-break. Picking max(id) alone is wrong: the seed put the duplicate first
-- and the empty one second, so that would keep the empty session and discard
-- every record.
DELETE FROM attendancesessions a
 WHERE a.id NOT IN (
       SELECT max(id) FROM (
           SELECT s.id,
                  row_number() OVER (
                      PARTITION BY s.classid, s.sectionid, s.date
                      ORDER BY count(r.id) DESC, s.id DESC
                  ) AS rank
             FROM attendancesessions s
             LEFT JOIN attendancerecords r ON r.sessionid = s.id
            GROUP BY s.id, s.classid, s.sectionid, s.date
       ) ranked
        WHERE ranked.rank = 1
 );

COMMIT;