-- Renames every system-named constraint to the project convention (issue #87):
--   PK_<Table>  FK_<Table>_<RefTable>  UQ_<Table>_<Cols>  DF_<Table>_<Col>  CK_<Table>_<Col>
--
-- Migrations 000-016 are deliberately NOT edited: they never re-run against existing databases,
-- so editing them would leave migrated and freshly-created databases with different names. A
-- rename sweep converges both. The is_system_named filter makes the body itself re-runnable --
-- a second pass simply finds nothing to rename.
IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '017_name_existing_constraints')
BEGIN
    DECLARE @Renames TABLE (OldName NVARCHAR(776), NewName NVARCHAR(128));

    -- DEFAULT constraints -> DF_<Table>_<Column>
    INSERT INTO @Renames (OldName, NewName)
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(dc.name),
           'DF_' + t.name + '_' + c.name
    FROM sys.default_constraints dc
    INNER JOIN sys.tables t ON t.object_id = dc.parent_object_id
    INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.is_system_named = 1;

    -- PRIMARY KEY -> PK_<Table>; UNIQUE -> UQ_<Table>_<Columns>. Renaming a key constraint
    -- renames its backing index too, which is what the convention wants.
    INSERT INTO @Renames (OldName, NewName)
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(kc.name),
           CASE WHEN kc.type = 'PK' THEN 'PK_' + t.name
                ELSE 'UQ_' + t.name + '_' + (
                    SELECT STRING_AGG(c.name, '_') WITHIN GROUP (ORDER BY ic.key_ordinal)
                    FROM sys.index_columns ic
                    INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE ic.object_id = kc.parent_object_id
                      AND ic.index_id = kc.unique_index_id
                      AND ic.is_included_column = 0)
           END
    FROM sys.key_constraints kc
    INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
    WHERE kc.is_system_named = 1;

    -- FOREIGN KEY -> FK_<Table>_<ReferencedTable>. Unique for every FK in the schema today;
    -- a table with two FKs to the same table would need FK_<Table>_<Column> instead.
    INSERT INTO @Renames (OldName, NewName)
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(fk.name),
           'FK_' + t.name + '_' + rt.name
    FROM sys.foreign_keys fk
    INNER JOIN sys.tables t ON t.object_id = fk.parent_object_id
    INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
    WHERE fk.is_system_named = 1;

    -- CHECK -> CK_<Table>_<Column>. None exist today; included so the sweep stays complete.
    INSERT INTO @Renames (OldName, NewName)
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(cc.name),
           'CK_' + t.name + '_' + ISNULL(c.name, 'Table')
    FROM sys.check_constraints cc
    INNER JOIN sys.tables t ON t.object_id = cc.parent_object_id
    LEFT JOIN sys.columns c ON c.object_id = cc.parent_object_id AND c.column_id = cc.parent_column_id
    WHERE cc.is_system_named = 1;

    DECLARE @OldName NVARCHAR(776), @NewName NVARCHAR(128);
    DECLARE ConstraintRenames CURSOR LOCAL FAST_FORWARD FOR SELECT OldName, NewName FROM @Renames;

    OPEN ConstraintRenames;
    FETCH NEXT FROM ConstraintRenames INTO @OldName, @NewName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Deliberately not guarded: a collision with an existing name means the convention needs
        -- a decision, so let sp_rename fail loudly rather than skip silently.
        EXEC sp_rename @objname = @OldName, @newname = @NewName, @objtype = 'OBJECT';

        FETCH NEXT FROM ConstraintRenames INTO @OldName, @NewName;
    END

    CLOSE ConstraintRenames;
    DEALLOCATE ConstraintRenames;

    INSERT INTO Migrations (ScriptName) VALUES ('017_name_existing_constraints');
END
