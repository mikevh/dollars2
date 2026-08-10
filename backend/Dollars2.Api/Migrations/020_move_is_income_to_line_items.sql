-- Split from 019 (issue #75): SQL Server compiles a whole unseparated batch up front, so a later
-- statement in the same batch that references a column added earlier in that batch fails to bind
-- ("Invalid column name") even though the ALTER TABLE ran first. Migrations here never use GO, so
-- the ADD COLUMN has to live in its own prior script before this one can reference it.
IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '020_move_is_income_to_line_items')
BEGIN
    UPDATE li
    SET li.IsIncome = bg.IsIncome
    FROM LineItems li
    INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId;

    -- Pre-fix DeleteLineItemAsync had no guard against emptying a budget's income group entirely
    -- (only deleting the group itself was blocked) — that gap predates the CANNOT_DELETE_LAST_INCOME
    -- rule this migration exists to backfill for. Any budget already sitting in that state needs a
    -- synthetic income line item, or it silently violates "every budget keeps at least one income
    -- line item" from the moment this migration runs. This has to run before BudgetGroups.IsIncome
    -- is dropped below, since that's the only remaining way to identify which group was the income
    -- group for a budget that no longer has any income line items to check via LineItems.IsIncome.
    INSERT INTO LineItems (GroupId, Name, PlannedAmount, IsIncome, SortOrder, CreatedAt, UpdatedAt)
    SELECT bg.Id, 'Paycheck', 0, 1,
           ISNULL((SELECT MAX(li.SortOrder) FROM LineItems li WHERE li.GroupId = bg.Id), -1) + 1,
           SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM BudgetGroups bg
    WHERE bg.IsIncome = 1
      AND NOT EXISTS (SELECT 1 FROM LineItems li WHERE li.GroupId = bg.Id AND li.IsIncome = 1);

    ALTER TABLE BudgetGroups DROP CONSTRAINT DF_BudgetGroups_IsIncome;
    ALTER TABLE BudgetGroups DROP COLUMN IsIncome;

    INSERT INTO Migrations (ScriptName) VALUES ('020_move_is_income_to_line_items');
END
