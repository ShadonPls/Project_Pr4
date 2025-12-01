using Microsoft.EntityFrameworkCore;
using Server.Models;
using System;

namespace Server.Data
{
    public class DBContext : DbContext
    {
        public static readonly string ConnectionString = "Host=localhost;Port=5432;Database=pr4;Username=postgres;Password=123;";

        public DbSet<User> Users { get; set; }
        public DbSet<UserLog> UsersLog { get; set; }

        public DBContext()
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(ConnectionString);
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Явно указываем имена таблиц
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<UserLog>().ToTable("UsersLog"); // Исправлено на UsersLog

            // Настройка отношения User -> UserLog (один ко многим)
            modelBuilder.Entity<User>()
                .HasMany(u => u.UserLogs)
                .WithOne(ul => ul.User)
                .HasForeignKey(ul => ul.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Настройка DateTime для PostgreSQL
            modelBuilder.Entity<UserLog>()
                .Property(ul => ul.Date)
                .HasColumnType("timestamp without time zone");
        }
    }
}