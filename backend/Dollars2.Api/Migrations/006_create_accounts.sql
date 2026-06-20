IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '006_create_accounts')
BEGIN
    CREATE TABLE Accounts (
        Id int IDENTITY(1,1) PRIMARY KEY,
        UserId int NOT NULL REFERENCES Users(Id),
        Name nvarchar(256) NOT NULL,
        SourceType nvarchar(50) NOT NULL,
        ConnectionDetailsJson nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    INSERT INTO Migrations (ScriptName) VALUES ('006_create_accounts');
END
