using Dapper;
using Npgsql;
using System.Data;

namespace SchoolHub.API.Data
{
    public class DbInitializer
    {
        private readonly string _connectionString;

        public DbInitializer(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string not found in configuration");
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
                    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
                    Title VARCHAR(255) NOT NULL,
                    Message TEXT NOT NULL,
                    IsRead BOOLEAN DEFAULT FALSE,
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
        }
    }
}