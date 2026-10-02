-- Give the timetable a write path and make the bell schedule honest.
--
-- TimetableEntries was empty in every database because nothing inserted a row:
-- the only writer required a TimeSlotId that no endpoint could create, and the
-- read endpoint demanded a class and section the learner pages had no way to
-- resolve. TimeSlots themselves had no create endpoint either, so the schedule
-- was a fixed, seeded four periods.
--
-- The endpoints that follow manage both tables. These constraints are the
-- database's half of the deal: a slot that ends before it starts, a section in
-- two places at once, a teacher in two places at once, or a day number that is
-- not a real weekday are all states no read path could make sense of.

BEGIN;

-- A period that ends before it starts has no length, so a clash check against it
-- can never fire and it renders as a negative block on the grid.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_timeslots_range') THEN
        ALTER TABLE TimeSlots
            ADD CONSTRAINT ck_timeslots_range CHECK (EndTime > StartTime);
    END IF;
END $$;

-- A section sits in one subject per period per day. Without this, two rows for
-- the same section and period both render stacked on the grid, and the second is
-- unreachable. The app refuses the clash with a 409; this is the fallback for any
-- other writer.
CREATE UNIQUE INDEX IF NOT EXISTS ux_timetableentries_section_slot
    ON TimetableEntries (ClassId, SectionId, TimeSlotId, DayOfWeek);

-- A teacher cannot teach two classes in the same period on the same day. Partial
-- because TeacherId is optional: an unassigned entry is nobody's clash.
CREATE UNIQUE INDEX IF NOT EXISTS ux_timetableentries_teacher_slot
    ON TimetableEntries (TeacherId, TimeSlotId, DayOfWeek)
    WHERE TeacherId IS NOT NULL;

-- DayOfWeek is an int, so any value was accepted and the grid silently dropped
-- rows it had no column for. Constrain it to ISO-8601: Monday (1) to Sunday (7).
-- The table has always been empty, so there is nothing to normalise first.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_timetableentries_day') THEN
        ALTER TABLE TimetableEntries
            ADD CONSTRAINT ck_timetableentries_day CHECK (DayOfWeek BETWEEN 1 AND 7);
    END IF;
END $$;

COMMIT;
