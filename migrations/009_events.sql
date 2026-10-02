-- Give events a real lifecycle: edit and remove them, and let people respond.
--
-- Events had only a list and a create. Nothing could correct or cancel an event,
-- and EventParticipants was write-only in practice: it was seeded by hand and no
-- endpoint could add, change or remove a response. A participant Status was also
-- free text, so the column could hold anything the UI had no rendering for.
--
-- What the endpoints that follow rely on, enforced here as well:
--   * an event has a non-blank title;
--   * the same title on the same date is one event, not two;
--   * a response is one of the four the UI understands, and always set.

BEGIN;

-- A blank title has nothing for the list to show and nothing to match on.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_events_title') THEN
        ALTER TABLE Events ADD CONSTRAINT ck_events_title CHECK (btrim(Title) <> '');
    END IF;
END $$;

-- Same title and timestamp is a duplicate, not a repeat: an event that recurs
-- runs on a different date. The endpoint refuses this with a 409 before insert;
-- the index is the fallback for any other writer.
CREATE UNIQUE INDEX IF NOT EXISTS ux_events_title_date
    ON Events (lower(btrim(Title)), EventDate);

CREATE INDEX IF NOT EXISTS ix_events_eventdate ON Events (EventDate);

-- Responses are free text today, so normalise before constraining: an unknown
-- value becomes the neutral 'Invited' rather than blocking the migration.
UPDATE EventParticipants
   SET Status = 'Invited'
 WHERE Status IS NULL
    OR Status NOT IN ('Invited', 'Attending', 'Not Attending', 'Maybe');

ALTER TABLE EventParticipants ALTER COLUMN Status SET DEFAULT 'Invited';
ALTER TABLE EventParticipants ALTER COLUMN Status SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_eventparticipants_status') THEN
        ALTER TABLE EventParticipants
            ADD CONSTRAINT ck_eventparticipants_status
            CHECK (Status IN ('Invited', 'Attending', 'Not Attending', 'Maybe'));
    END IF;
END $$;

COMMIT;
