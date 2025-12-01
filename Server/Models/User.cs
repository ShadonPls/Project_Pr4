using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Login { get; set; }

        [Required]
        [StringLength(50)]
        public string Password { get; set; }

        [Required]
        public string Src { get; set; }

        public virtual ICollection<UserLog> UserLogs { get; set; }

        public User()
        {
            UserLogs = new HashSet<UserLog>();
        }
    }
}