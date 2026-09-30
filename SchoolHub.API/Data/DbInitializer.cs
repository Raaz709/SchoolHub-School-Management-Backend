using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Data;

namespace SchoolHub.API.Data
{
    public class DbInitializer
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DbInitializer> _logger;

        public DbInitializer(string connectionString, IConfiguration configuration, ILogger<DbInitializer> logger)
        {
            _connectionString = connectionString;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            using IDbConnection db = new NpgsqlConnection(_connectionString);
            db.Open();

            var schemaSql = @"
                -- ==========================================
                -- AUTH & SYSTEM
                -- ==========================================
                CREATE TABLE IF NOT EXISTS Roles (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(50) UNIQUE NOT NULL
                );

                INSERT INTO Roles (Name) VALUES ('Admin'), ('Teacher'), ('Student'), ('Parent')
                ON CONFLICT (Name) DO NOTHING;

                CREATE TABLE IF NOT EXISTS Users (
                    Id SERIAL PRIMARY KEY,
                    Username VARCHAR(100) UNIQUE NOT NULL,
                    Email VARCHAR(255) UNIQUE NOT NULL,
                    PasswordHash VARCHAR(255) NOT NULL,
                    Role VARCHAR(50) DEFAULT 'Student',
                    ProfilePictureUrl VARCHAR(500),
                    IsActive BOOLEAN DEFAULT TRUE,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS PasswordResetTokens (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    Token VARCHAR(255) NOT NULL,
                    Expires TIMESTAMP WITH TIME ZONE NOT NULL,
                    IsUsed BOOLEAN DEFAULT FALSE
                );

                CREATE TABLE IF NOT EXISTS UserRoles (
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    RoleId INT REFERENCES Roles(Id) ON DELETE CASCADE,
                    PRIMARY KEY (UserId, RoleId)
                );

                CREATE TABLE IF NOT EXISTS RefreshTokens (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    Token VARCHAR(500) NOT NULL,
                    Expires TIMESTAMP WITH TIME ZONE NOT NULL,
                    Created TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    IsRevoked BOOLEAN DEFAULT FALSE
                );

                -- ==========================================
                -- PEOPLE
                -- ==========================================
                CREATE TABLE IF NOT EXISTS Departments (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(100) UNIQUE NOT NULL,
                    Description TEXT
                );

                CREATE TABLE IF NOT EXISTS Teachers (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    DepartmentId INT REFERENCES Departments(Id),
                    EmployeeCode VARCHAR(50) UNIQUE NOT NULL,
                    HireDate DATE
                );

                CREATE TABLE IF NOT EXISTS Parents (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    Occupation VARCHAR(100)
                );

                CREATE TABLE IF NOT EXISTS Students (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    RollNumber VARCHAR(50) UNIQUE NOT NULL,
                    AdmissionDate DATE
                );

                CREATE TABLE IF NOT EXISTS StudentParents (
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    ParentId INT REFERENCES Parents(Id) ON DELETE CASCADE,
                    Relationship VARCHAR(50),
                    PRIMARY KEY (StudentId, ParentId)
                );

                -- ==========================================
                -- ACADEMIC
                -- ==========================================
                CREATE TABLE IF NOT EXISTS AcademicYears (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(50) NOT NULL,
                    StartDate DATE NOT NULL,
                    EndDate DATE NOT NULL,
                    IsCurrent BOOLEAN DEFAULT FALSE
                );

                CREATE TABLE IF NOT EXISTS Classes (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Sections (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(50) NOT NULL,
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Subjects (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL,
                    Code VARCHAR(50) UNIQUE NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ClassSubjects (
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE,
                    SubjectId INT REFERENCES Subjects(Id) ON DELETE CASCADE,
                    PRIMARY KEY (ClassId, SubjectId)
                );

                CREATE TABLE IF NOT EXISTS Enrollments (
                    Id SERIAL PRIMARY KEY,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE,
                    SectionId INT REFERENCES Sections(Id),
                    AcademicYearId INT REFERENCES AcademicYears(Id),
                    EnrolledAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- ==========================================
                -- SCHEDULE
                -- ==========================================
                CREATE TABLE IF NOT EXISTS TimeSlots (
                    Id SERIAL PRIMARY KEY,
                    StartTime TIME NOT NULL,
                    EndTime TIME NOT NULL,
                    Label VARCHAR(50)
                );

                CREATE TABLE IF NOT EXISTS TimetableEntries (
                    Id SERIAL PRIMARY KEY,
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE,
                    SectionId INT REFERENCES Sections(Id) ON DELETE CASCADE,
                    SubjectId INT REFERENCES Subjects(Id) ON DELETE CASCADE,
                    TeacherId INT REFERENCES Teachers(Id) ON DELETE SET NULL,
                    TimeSlotId INT REFERENCES TimeSlots(Id) ON DELETE CASCADE,
                    DayOfWeek INT NOT NULL
                );

                -- ==========================================
                -- ATTENDANCE
                -- ==========================================
                CREATE TABLE IF NOT EXISTS AttendanceSessions (
                    Id SERIAL PRIMARY KEY,
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE,
                    SectionId INT REFERENCES Sections(Id) ON DELETE CASCADE,
                    TeacherId INT REFERENCES Teachers(Id) ON DELETE SET NULL,
                    Date DATE NOT NULL,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS AttendanceRecords (
                    Id SERIAL PRIMARY KEY,
                    SessionId INT REFERENCES AttendanceSessions(Id) ON DELETE CASCADE,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    Status VARCHAR(20) NOT NULL,
                    Remarks TEXT
                );

                -- ==========================================
                -- ASSIGNMENTS
                -- ==========================================
                CREATE TABLE IF NOT EXISTS Assignments (
                    Id SERIAL PRIMARY KEY,
                    SubjectId INT REFERENCES Subjects(Id) ON DELETE CASCADE,
                    TeacherId INT REFERENCES Teachers(Id) ON DELETE CASCADE,
                    Title VARCHAR(255) NOT NULL,
                    Description TEXT,
                    DueDate TIMESTAMP WITH TIME ZONE NOT NULL,
                    MaxScore DECIMAL(5,2),
                    AttachmentUrl VARCHAR(500)
                );

                CREATE TABLE IF NOT EXISTS AssignmentSubmissions (
                    Id SERIAL PRIMARY KEY,
                    AssignmentId INT REFERENCES Assignments(Id) ON DELETE CASCADE,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    FilePath VARCHAR(500),
                    SubmittedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    Score DECIMAL(5,2),
                    Feedback TEXT
                );

                -- ==========================================
                -- EXAMS
                -- ==========================================
                CREATE TABLE IF NOT EXISTS GradeScales (
                    Id SERIAL PRIMARY KEY,
                    Grade VARCHAR(10) NOT NULL,
                    MinPercentage DECIMAL(5,2) NOT NULL,
                    MaxPercentage DECIMAL(5,2) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Exams (
                    Id SERIAL PRIMARY KEY,
                    Title VARCHAR(255) NOT NULL,
                    AcademicYearId INT REFERENCES AcademicYears(Id) ON DELETE CASCADE,
                    StartDate DATE,
                    EndDate DATE,
                    PassingMarks DECIMAL(5,2) DEFAULT 40.00
                );

                CREATE TABLE IF NOT EXISTS ExamSubjects (
                    Id SERIAL PRIMARY KEY,
                    ExamId INT REFERENCES Exams(Id) ON DELETE CASCADE,
                    SubjectId INT REFERENCES Subjects(Id) ON DELETE CASCADE,
                    MaxMarks DECIMAL(5,2) NOT NULL,
                    ExamDate TIMESTAMP WITH TIME ZONE
                );

                CREATE TABLE IF NOT EXISTS Marks (
                    Id SERIAL PRIMARY KEY,
                    ExamSubjectId INT REFERENCES ExamSubjects(Id) ON DELETE CASCADE,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    MarksObtained DECIMAL(5,2) NOT NULL,
                    Grade VARCHAR(10),
                    Remarks TEXT
                );

                -- ==========================================
                -- FEES
                -- ==========================================
                CREATE TABLE IF NOT EXISTS FeeStructures (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL,
                    Amount DECIMAL(10,2) NOT NULL,
                    ClassId INT REFERENCES Classes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS StudentFees (
                    Id SERIAL PRIMARY KEY,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    FeeStructureId INT REFERENCES FeeStructures(Id) ON DELETE CASCADE,
                    DueDate DATE NOT NULL,
                    Status VARCHAR(50) DEFAULT 'Unpaid'
                );

                CREATE TABLE IF NOT EXISTS Invoices (
                    Id SERIAL PRIMARY KEY,
                    StudentId INT REFERENCES Students(Id) ON DELETE CASCADE,
                    TotalAmount DECIMAL(10,2) NOT NULL,
                    IssuedDate DATE NOT NULL,
                    DueDate DATE NOT NULL,
                    Status VARCHAR(50) DEFAULT 'Unpaid'
                );

                CREATE TABLE IF NOT EXISTS Payments (
                    Id SERIAL PRIMARY KEY,
                    InvoiceId INT REFERENCES Invoices(Id) ON DELETE CASCADE,
                    AmountPaid DECIMAL(10,2) NOT NULL,
                    PaymentDate TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    PaymentMethod VARCHAR(50),
                    TransactionReference VARCHAR(255)
                );

                -- ==========================================
                -- COMMUNICATION
                -- ==========================================
                CREATE TABLE IF NOT EXISTS Announcements (
                    Id SERIAL PRIMARY KEY,
                    Title VARCHAR(255) NOT NULL,
                    Content TEXT NOT NULL,
                    TargetRole VARCHAR(50),
                    ClassId INT REFERENCES Classes(Id) ON DELETE SET NULL,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS Notifications (
                    Id SERIAL PRIMARY KEY,
                    Title VARCHAR(255) NOT NULL,
                    Message TEXT NOT NULL,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS NotificationRecipients (
                    NotificationId INT REFERENCES Notifications(Id) ON DELETE CASCADE,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    IsRead BOOLEAN DEFAULT FALSE,
                    PRIMARY KEY (NotificationId, UserId)
                );

                CREATE TABLE IF NOT EXISTS UserDevices (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    DeviceToken VARCHAR(500) NOT NULL,
                    Platform VARCHAR(50)
                );

                -- ==========================================
                -- EVENTS
                -- ==========================================
                CREATE TABLE IF NOT EXISTS Events (
                    Id SERIAL PRIMARY KEY,
                    Title VARCHAR(255) NOT NULL,
                    Description TEXT,
                    EventDate TIMESTAMP WITH TIME ZONE NOT NULL,
                    Location VARCHAR(255)
                );

                CREATE TABLE IF NOT EXISTS EventParticipants (
                    EventId INT REFERENCES Events(Id) ON DELETE CASCADE,
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    Status VARCHAR(50) DEFAULT 'Attending',
                    PRIMARY KEY (EventId, UserId)
                );

                -- ==========================================
                -- FILE MANAGEMENT & SYSTEM AUDIT
                -- ==========================================
                CREATE TABLE IF NOT EXISTS FilesMetadata (
                    Id SERIAL PRIMARY KEY,
                    FileName VARCHAR(255) NOT NULL,
                    FilePath VARCHAR(500) NOT NULL,
                    ContentType VARCHAR(100),
                    UploadedById INT REFERENCES Users(Id) ON DELETE SET NULL,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id SERIAL PRIMARY KEY,
                    UserId INT REFERENCES Users(Id) ON DELETE SET NULL,
                    Action VARCHAR(100) NOT NULL,
                    Entity VARCHAR(100),
                    EntityId INT,
                    Details TEXT,
                    IpAddress VARCHAR(50),
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
            ";

            await db.ExecuteAsync(schemaSql);

            await EnsureBootstrapAdminAsync(db);
        }

        /// <summary>
        /// Guarantees the system has exactly one usable administrator.
        ///
        /// Previously the only admin came from a seed script with a malformed
        /// BCrypt hash, so the account existed but could never log in, and the
        /// only way in was hand-made accounts with published passwords.
        ///
        /// The password is read from configuration (user-secrets or the
        /// SCHOOLHUB_BOOTSTRAP_ADMIN_PASSWORD environment variable) and is never
        /// committed. Without it the database is left untouched rather than
        /// seeded with a well-known credential, and the warning explains what to
        /// do instead.
        /// </summary>
        private async Task EnsureBootstrapAdminAsync(IDbConnection db)
        {
            var password = _configuration["BootstrapAdmin:Password"];

            if (string.IsNullOrWhiteSpace(password))
            {
                // Only warn when there is genuinely no usable admin, so a
                // correctly configured deployment stays quiet.
                if (!await HasUsableAdminAsync(db))
                {
                    _logger.LogWarning(
                        "No usable administrator account found. Set BootstrapAdmin:Password " +
                        "(user-secrets) or the SCHOOLHUB_BOOTSTRAP_ADMIN_PASSWORD environment " +
                        "variable and restart to create the 'admin' account.");
                }
                return;
            }

            if (password.Length < 12)
            {
                _logger.LogError(
                    "BootstrapAdmin:Password must be at least 12 characters; skipping admin bootstrap.");
                return;
            }

            var hash = BCrypt.Net.BCrypt.HashPassword(password);

            var adminId = await db.ExecuteScalarAsync<int?>(
                """
                SELECT u.Id
                FROM users u
                JOIN userroles ur ON ur.UserId = u.Id
                JOIN roles r ON r.Id = ur.RoleId
                WHERE r.Name = 'Admin'
                ORDER BY u.Id
                LIMIT 1
                """);

            if (adminId is null)
            {
                var newId = await db.ExecuteScalarAsync<int?>(
                    """
                    INSERT INTO users (username, email, passwordhash, role, isactive)
                    VALUES ('admin', 'admin@schoolhub.local', @hash, 'Admin', TRUE)
                    ON CONFLICT (username) DO NOTHING
                    RETURNING id
                    """,
                    new { hash });

                if (newId is null)
                {
                    _logger.LogWarning("A user named 'admin' exists without the Admin role; not modifying it.");
                    return;
                }

                await GrantAdminRoleAsync(db, newId.Value);
                _logger.LogInformation("Created bootstrap administrator 'admin'.");
                return;
            }

            // An admin exists. Repair it only if the stored hash is unusable, so
            // a deliberate password change is never silently undone on restart.
            if (await IsHashUsableAsync(db, adminId.Value))
            {
                _logger.LogInformation("Administrator account is present and its password hash is valid.");
                return;
            }

            var repaired = await db.ExecuteAsync(
                "UPDATE users SET passwordhash = @hash, isactive = TRUE WHERE id = @id",
                new { hash, id = adminId.Value });

            if (repaired > 0)
            {
                _logger.LogInformation(
                    "Repaired the administrator password hash, which was stored in an unusable format.");
            }
        }

        private async Task<bool> HasUsableAdminAsync(IDbConnection db)
        {
            var hashes = await db.QueryAsync<string>(
                """
                SELECT u.passwordhash
                FROM users u
                JOIN userroles ur ON ur.UserId = u.Id
                JOIN roles r ON r.Id = ur.RoleId
                WHERE r.Name = 'Admin'
                """);

            foreach (var hash in hashes)
            {
                if (IsUsableHash(hash)) return true;
            }
            return false;
        }

        private async Task GrantAdminRoleAsync(IDbConnection db, int userId)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO userroles (userid, roleid)
                SELECT @userId, id FROM roles WHERE name = 'Admin'
                ON CONFLICT DO NOTHING
                """,
                new { userId });
        }

        private async Task<bool> IsHashUsableAsync(IDbConnection db, int userId)
        {
            var hash = await db.ExecuteScalarAsync<string>(
                "SELECT passwordhash FROM users WHERE id = @id", new { id = userId });
            return IsUsableHash(hash);
        }

        /// <summary>
        /// True when BCrypt can actually parse the stored hash. The old seed
        /// stored a placeholder, so the account existed but every login failed.
        ///
        /// A real <c>Verify</c> is used rather than a shape check: it is what
        /// the login path does, so this cannot disagree with it.
        /// </summary>
        private static bool IsUsableHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return false;
            try
            {
                // Returns false for a wrong password, but throws when the hash
                // itself is malformed, which is the case being detected.
                BCrypt.Net.BCrypt.Verify("probe", hash);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}