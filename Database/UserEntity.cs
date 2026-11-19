using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("user")]
    public class UserEntity
    {

        public enum Gender_ { Male = 0, Female = 1 }

        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("email")]
        public required string Email { get; set; }

        [Column("password")]
        public string? Password { get; set; }

        [Column("name")]
        public required string Name { get; set; }

        [Column("gender")]
        public Gender_? Gender { get; set; }

        [Column("age")]
        public short? Age { get; set; }

        /// <summary>
        /// 身高，cm，精确到个位
        /// </summary>
        [Column("height")]
        public short? Height { get; set; }

        /// <summary>
        /// 体重  kg  精确到个位
        /// </summary>
        [Column("weight")]
        public short? Weight { get; set; }

        [Column("create_time")]
        public DateTime CreateTime { get; set; }

        [Column("update_time")]
        public DateTime UpdateTime { get; set; }

        [Column("times")]
        public short Times { get; set; }

        [Column("is_preview")]
        public bool IsPreview { get; set; }

        [Column("expired_time")]
        public DateTime? ExpiredTime { get; set; }

        [Column("paid")]
        public bool Paid { get; set; } = false;

        [Column("mkt")]
        public int Mkt { get; set; } = 0;

    }
}
