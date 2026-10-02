-- Give fees a write path: settle a StudentFees row directly, and stop trusting
-- the free-text Status column.
--
-- Fees had no write path at all. StudentFees was empty in every database because
-- nothing inserted a row, so FeesController.RecordPayment was the only way money
-- entered the system and it required an invoice, while invoices were only ever
-- created by hand. Nothing recomputed either Status column, so an invoice read
-- 'Unpaid' after being paid in full and the fee-collection report grouped by a
-- value no code path maintained.
--
-- StudentFees becomes the receivable ledger: one row per fee a student owes.
-- Payments reference that row, so paid and outstanding are sums over Payments
-- rather than a stored flag that can contradict them.

BEGIN;

-- ON DELETE CASCADE, matching every other child table here: removing a student
-- removes their fee ledger, and removing a ledger row removes what it collected.
-- The endpoints refuse to delete an assignment or structure that carries
-- payments, so the cascade only ever runs from a student removal.
ALTER TABLE Payments
    ADD COLUMN IF NOT EXISTS StudentFeeId INT REFERENCES StudentFees(Id) ON DELETE CASCADE;

-- A payment has to say what it settles. Both were optional, so a row could exist
-- naming neither and would then be invisible to every balance query.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payments_target') THEN
        ALTER TABLE Payments
            ADD CONSTRAINT ck_payments_target
            CHECK (InvoiceId IS NOT NULL OR StudentFeeId IS NOT NULL);
    END IF;

    -- A zero or negative amount is not a charge. The balance would be met by
    -- nothing, or a payment would owe the student money.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_feestructures_amount') THEN
        ALTER TABLE FeeStructures
            ADD CONSTRAINT ck_feestructures_amount CHECK (Amount > 0);
    END IF;

    -- A zero or negative payment is not a payment. It would leave a fee looking
    -- partly settled while moving no money.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payments_amountpaid') THEN
        ALTER TABLE Payments
            ADD CONSTRAINT ck_payments_amountpaid CHECK (AmountPaid > 0);
    END IF;
END $$;

-- Both were nullable, so an assignment could name no student and no fee. It
-- would then appear in no student's ledger and in no structure's collection
-- total, while still sitting in the table to be counted.
--
-- StudentFees has been empty in every database so far, because there was no
-- write path, so there is nothing to backfill and nobody to invent. If a row
-- does exist, refuse and let someone decide who owes it.
DO $$
DECLARE orphans int;
BEGIN
    SELECT count(*) INTO orphans
      FROM StudentFees
     WHERE StudentId IS NULL OR FeeStructureId IS NULL;
    IF orphans > 0 THEN
        RAISE EXCEPTION
            'studentfees holds % row(s) naming no student or no fee. Set both on each before rerunning this migration.', orphans;
    END IF;
END $$;

ALTER TABLE StudentFees ALTER COLUMN StudentId SET NOT NULL;
ALTER TABLE StudentFees ALTER COLUMN FeeStructureId SET NOT NULL;

-- One charge per student per structure. The fee-collection report summed a
-- structure's amount once per assignment row, so charging the same fee twice
-- counted it twice, and there was no way to assign a fee to a class idempotently
-- without first reading which students already had it.
--
-- Repeat instalments are separate structures ("Term 1 Tuition", "Term 2
-- Tuition"), so nothing legitimate needs the same structure twice.
CREATE UNIQUE INDEX IF NOT EXISTS ux_studentfees_student_structure
    ON StudentFees (StudentId, FeeStructureId);

-- A structure name is how staff pick the right charge; two rows reading
-- 'Term Tuition' made the choice ambiguous and both charges live.
CREATE UNIQUE INDEX IF NOT EXISTS ux_feestructures_name
    ON FeeStructures (lower(btrim(Name)));

-- Drop the column rather than maintain it. Paid, outstanding and status are now
-- derived from Amount and Payments on every read, so a stored Status can only
-- ever disagree with the payments behind it.
--
-- Nothing needs carrying over first: the derived value supersedes it, and the
-- one endpoint that read it (/api/students/{id}/fees) is rewritten to compute it.
-- Invoices.Status is left alone deliberately — invoices are outside this
-- migration's write path and nothing reads them.
ALTER TABLE StudentFees DROP COLUMN IF EXISTS Status;

COMMIT;
