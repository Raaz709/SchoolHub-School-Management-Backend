using Microsoft.EntityFrameworkCore;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.Schedule;
using SchoolHub.API.Models.Attendance;
using SchoolHub.API.Models.Assignments;
using SchoolHub.API.Models.Exams;
using SchoolHub.API.Models.Fees;
using SchoolHub.API.Models.Communication;
using SchoolHub.API.Models.Events;
using SchoolHub.API.Models.System;

namespace SchoolHub.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<StudentParent> StudentParents => Set<StudentParent>();
        public DbSet<Teacher> Teachers => Set<Teacher>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
        public DbSet<Class> Classes => Set<Class>();
        public DbSet<Section> Sections => Set<Section>();
        public DbSet<Subject> Subjects => Set<Subject>();
        public DbSet<ClassSubject> ClassSubjects => Set<ClassSubject>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
        public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
        public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<ExamSubject> ExamSubjects => Set<ExamSubject>();
        public DbSet<Mark> Marks => Set<Mark>();
        public DbSet<GradeScale> GradeScales => Set<GradeScale>();
        public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
        public DbSet<StudentFee> StudentFees => Set<StudentFee>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
        public DbSet<UserDevice> UserDevices => Set<UserDevice>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
                entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
                entity.Property(e => e.ProfilePictureUrl).HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
                entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(255);
                entity.HasData(
                    new Role { Id = 1, Name = "Admin", Description = "System Administrator" },
                    new Role { Id = 2, Name = "Teacher", Description = "Teacher" },
                    new Role { Id = 3, Name = "Student", Description = "Student" },
                    new Role { Id = 4, Name = "Parent", Description = "Parent" }
                );
            });

            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.RoleId });
                entity.HasOne(e => e.User).WithMany(u => u.UserRoles).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Role).WithMany(r => r.UserRoles).HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.Property(e => e.Token).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Created).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.User).WithMany(u => u.RefreshTokens).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
            });

            modelBuilder.Entity<Teacher>(entity =>
            {
                entity.HasIndex(e => e.EmployeeCode).IsUnique();
                entity.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
                entity.Property(e => e.HireDate).HasDefaultValueSql("CURRENT_DATE");
                entity.HasOne(e => e.User).WithOne(u => u.Teacher).HasForeignKey<Teacher>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Department).WithMany(d => d.Teachers).HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Parent>(entity =>
            {
                entity.Property(e => e.Occupation).HasMaxLength(100);
                entity.HasOne(e => e.User).WithOne(u => u.Parent).HasForeignKey<Parent>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Student>(entity =>
            {
                entity.HasIndex(e => e.RollNumber).IsUnique();
                entity.Property(e => e.RollNumber).HasMaxLength(50).IsRequired();
                entity.Property(e => e.AdmissionDate).HasDefaultValueSql("CURRENT_DATE");
                entity.HasOne(e => e.User).WithOne(u => u.Student).HasForeignKey<Student>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Parent).WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<StudentParent>(entity =>
            {
                entity.HasKey(e => new { e.StudentId, e.ParentId });
                entity.Property(e => e.Relationship).HasMaxLength(50);
                entity.HasOne(e => e.Student).WithMany(s => s.StudentParents).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Parent).WithMany(p => p.StudentParents).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AcademicYear>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            });

            modelBuilder.Entity<Class>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            });

            modelBuilder.Entity<Section>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
                entity.HasOne(e => e.Class).WithMany(c => c.Sections).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Subject>(entity =>
            {
                entity.HasIndex(e => e.Code).IsUnique();
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
                entity.HasOne(e => e.Teacher).WithMany(t => t.Subjects).HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ClassSubject>(entity =>
            {
                entity.HasKey(e => new { e.ClassId, e.SubjectId });
                entity.HasOne(e => e.Class).WithMany(c => c.ClassSubjects).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Subject).WithMany(s => s.ClassSubjects).HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Enrollment>(entity =>
            {
                entity.Property(e => e.EnrolledAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Student).WithMany(s => s.Enrollments).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Class).WithMany(c => c.Enrollments).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Section).WithMany(s => s.Enrollments).HasForeignKey(e => e.SectionId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.AcademicYear).WithMany(ay => ay.Enrollments).HasForeignKey(e => e.AcademicYearId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TimeSlot>(entity =>
            {
                entity.Property(e => e.Label).HasMaxLength(50);
            });

            modelBuilder.Entity<TimetableEntry>(entity =>
            {
                entity.HasOne(e => e.Class).WithMany(c => c.TimetableEntries).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Section).WithMany(s => s.TimetableEntries).HasForeignKey(e => e.SectionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Subject).WithMany(s => s.TimetableEntries).HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Teacher).WithMany(t => t.TimetableEntries).HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.TimeSlot).WithMany(ts => ts.TimetableEntries).HasForeignKey(e => e.TimeSlotId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AttendanceSession>(entity =>
            {
                entity.Property(e => e.Date).HasDefaultValueSql("CURRENT_DATE");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Class).WithMany(c => c.AttendanceSessions).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Section).WithMany(s => s.AttendanceSessions).HasForeignKey(e => e.SectionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Teacher).WithMany(t => t.AttendanceSessions).HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AttendanceRecord>(entity =>
            {
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Present");
                entity.Property(e => e.Remarks).HasMaxLength(500);
                entity.HasOne(e => e.Session).WithMany(s => s.Records).HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Student).WithMany(s => s.AttendanceRecords).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Assignment>(entity =>
            {
                entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
                entity.Property(e => e.MaxScore).HasColumnType("decimal(5,2)");
                entity.Property(e => e.AttachmentUrl).HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Subject).WithMany(s => s.Assignments).HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Teacher).WithMany(t => t.Assignments).HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AssignmentSubmission>(entity =>
            {
                entity.Property(e => e.FilePath).HasMaxLength(500);
                entity.Property(e => e.Score).HasColumnType("decimal(5,2)");
                entity.Property(e => e.SubmittedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Assignment).WithMany(a => a.Submissions).HasForeignKey(e => e.AssignmentId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Student).WithMany(s => s.AssignmentSubmissions).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Exam>(entity =>
            {
                entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
                entity.Property(e => e.PassingMarks).HasColumnType("decimal(5,2)").HasDefaultValue(40.00m);
                entity.HasOne(e => e.AcademicYear).WithMany(ay => ay.Exams).HasForeignKey(e => e.AcademicYearId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExamSubject>(entity =>
            {
                entity.Property(e => e.MaxMarks).HasColumnType("decimal(5,2)").IsRequired();
                entity.HasOne(e => e.Exam).WithMany(e => e.ExamSubjects).HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Subject).WithMany(s => s.ExamSubjects).HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Mark>(entity =>
            {
                entity.Property(e => e.MarksObtained).HasColumnType("decimal(5,2)").IsRequired();
                entity.Property(e => e.Grade).HasMaxLength(10);
                entity.Property(e => e.Remarks).HasMaxLength(500);
                entity.HasOne(e => e.ExamSubject).WithMany(es => es.Marks).HasForeignKey(e => e.ExamSubjectId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Student).WithMany(s => s.Marks).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GradeScale>(entity =>
            {
                entity.Property(e => e.Grade).HasMaxLength(10).IsRequired();
                entity.Property(e => e.MinPercentage).HasColumnType("decimal(5,2)").IsRequired();
                entity.Property(e => e.MaxPercentage).HasColumnType("decimal(5,2)").IsRequired();
            });

            modelBuilder.Entity<FeeStructure>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Amount).HasColumnType("decimal(10,2)").IsRequired();
                entity.HasOne(e => e.Class).WithMany(c => c.FeeStructures).HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StudentFee>(entity =>
            {
                entity.Property(e => e.Status).HasMaxLength(50).IsRequired().HasDefaultValue("Unpaid");
                entity.HasOne(e => e.Student).WithMany(s => s.StudentFees).HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.FeeStructure).WithMany(fs => fs.StudentFees).HasForeignKey(e => e.FeeStructureId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(10,2)").IsRequired();
                entity.Property(e => e.Status).HasMaxLength(50).IsRequired().HasDefaultValue("Unpaid");
                entity.Property(e => e.IssuedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.Property(e => e.AmountPaid).HasColumnType("decimal(10,2)").IsRequired();
                entity.Property(e => e.PaymentDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.PaymentMethod).HasMaxLength(50);
                entity.Property(e => e.TransactionReference).HasMaxLength(255);
                entity.HasOne(e => e.Invoice).WithMany(i => i.Payments).HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Announcement>(entity =>
            {
                entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
                entity.Property(e => e.TargetRole).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.Class).WithMany().HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NotificationRecipient>(entity =>
            {
                entity.HasKey(e => new { e.NotificationId, e.UserId });
                entity.HasOne(e => e.Notification).WithMany().HasForeignKey(e => e.NotificationId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserDevice>(entity =>
            {
                entity.Property(e => e.DeviceToken).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Platform).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Event>(entity =>
            {
                entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
                entity.Property(e => e.Location).HasMaxLength(255);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<EventParticipant>(entity =>
            {
                entity.HasKey(e => new { e.EventId, e.UserId });
                entity.Property(e => e.Status).HasMaxLength(50).IsRequired().HasDefaultValue("Attending");
                entity.HasOne(e => e.Event).WithMany(e => e.Participants).HasForeignKey(e => e.EventId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Entity).HasMaxLength(100);
                entity.Property(e => e.IpAddress).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}