using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("reset_password_temp")]
    public class ResetPasswordTempEntity
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("create_time")]
        public DateTime CreateTime { get; set; }

    }
}
