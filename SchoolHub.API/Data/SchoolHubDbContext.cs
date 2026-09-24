using Microsoft.EntityFrameworkCore;
using SchoolHub.API.Models;

namespace SchoolHub.API.Data
{
    public class SchoolHubDbContext : DbContext
    {
        public SchoolHubDbContext(DbContextOptions<SchoolHubDbContext> options) : base(options) { }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ClassRoom> ClassRooms => Set<ClassRoom>();
        public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
        public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
        public DbSet<ParentProfile> ParentProfiles => Set<ParentProfile>();
        public DbSet<Subject> Subjects => Set<Subject>();
        public DbSet<Attendance> Attendances => Set<Attendance>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<ExamResult> ExamResults => Set<ExamResult>();
        public DbSet<Fee> Fees => Set<Fee>();
        public DbSet<Notice> Notices => Set<Notice>();
        public DbSet<EventItem> Events => Set<EventItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Tenant>().HasIndex(t => t.Identifier).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        }
    }
}
