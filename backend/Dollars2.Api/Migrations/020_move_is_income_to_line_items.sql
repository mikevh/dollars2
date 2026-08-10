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

    ALTER TABLE BudgetGroups DROP CONSTRAINT DF_BudgetGroups_IsIncome;
    ALTER TABLE BudgetGroups DROP COLUMN IsIncome;

    INSERT INTO Migrations (ScriptName) VALUES ('020_move_is_income_to_line_items');
END
