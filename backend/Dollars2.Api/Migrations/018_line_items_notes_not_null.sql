IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '018_line_items_notes_not_null')
BEGIN
    UPDATE LineItems SET Notes = '' WHERE Notes IS NULL;

    ALTER TABLE LineItems ALTER COLUMN Notes NVARCHAR(MAX) NOT NULL;

    ALTER TABLE LineItems ADD CONSTRAINT DF_LineItems_Notes DEFAULT '' FOR Notes;

    INSERT INTO Migrations (ScriptName) VALUES ('018_line_items_notes_not_null');
END
