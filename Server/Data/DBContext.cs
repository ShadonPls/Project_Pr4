using Microsoft.EntityFrameworkCore;
using Server.Models;
using System;

namespace Server.Data
{
    public class DBContext : DbContext
    {
        public static readonly string ConnectionString = "Server=localhost;Port=3306;Database=pr4;Username=root;Password=;";

        public DbSet<User> Users { get; set; }
        public DbSet<UserLog> UsersLog { get; set; }

        public DBContext()
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 30)));
            //AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");

            modelBuilder.Entity<UserLog>().ToTable("UsersLog");
            modelBuilder.Entity<User>()
                .HasMany(u => u.UserLogs)
                .WithOne(ul => ul.User)
                .HasForeignKey(ul => ul.UserId);
        }
    }
}