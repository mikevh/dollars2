IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '023_add_user_registration_key')
BEGIN
    ALTER TABLE Users ADD RegistrationKey NVARCHAR(200) NULL;

    INSERT INTO Migrations (ScriptName) VALUES ('023_add_user_registration_key');
END
