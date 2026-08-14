IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '023_add_registration_key_and_passkey_credentials')
BEGIN
    ALTER TABLE Users ADD RegistrationKey NVARCHAR(200) NULL;

    CREATE TABLE PasskeyCredentials (
        Id INT IDENTITY(1,1) NOT NULL,
        UserId INT NOT NULL,
        CredentialId VARBINARY(900) NOT NULL,
        PublicKey VARBINARY(MAX) NOT NULL,
        AttestationObject VARBINARY(MAX) NOT NULL,
        ClientDataJson VARBINARY(MAX) NOT NULL,
        SignCount BIGINT NOT NULL,
        Transports NVARCHAR(200) NULL,
        IsUserVerified BIT NOT NULL,
        IsBackupEligible BIT NOT NULL,
        IsBackedUp BIT NOT NULL,
        Name NVARCHAR(256) NULL,
        CreatedAt DATETIME2 NOT NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_PasskeyCredentials_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PasskeyCredentials PRIMARY KEY (Id),
        CONSTRAINT FK_PasskeyCredentials_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
        CONSTRAINT UQ_PasskeyCredentials_CredentialId UNIQUE (CredentialId)
    );

    INSERT INTO Migrations (ScriptName) VALUES ('023_add_registration_key_and_passkey_credentials');
END
