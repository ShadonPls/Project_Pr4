// Server/Models/UserLog.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class UserLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [StringLength(500)]
        public string Command { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public virtual User User { get; set; }
    }
}