-- Split from 021 (issue #86): SQL Server compiles a whole unseparated batch up front, so a later
-- statement in the same batch that references a column added earlier in that batch fails to bind
-- ("Invalid column name") even though the ALTER TABLE ran first. Migrations here never use GO, so
-- the ADD COLUMN has to live in its own prior script before this one can reference it.
IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '022_backfill_and_require_line_item_budget_id')
BEGIN
    UPDATE li
    SET li.BudgetId = bg.BudgetId
    FROM LineItems li
    INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId;

    ALTER TABLE LineItems ALTER COLUMN BudgetId INT NOT NULL;
    ALTER TABLE LineItems ADD CONSTRAINT FK_LineItems_Budgets FOREIGN KEY (BudgetId) REFERENCES Budgets(Id);

    INSERT INTO Migrations (ScriptName) VALUES ('022_backfill_and_require_line_item_budget_id');
END
