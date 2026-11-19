using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("stl")]
    public class STLEntity
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("create_time")]
        public DateTime CreateTime { get; set; }

        [Column("params")]
        public required string Params { get; set; }

        [Column("name")]
        public required string Name { get; set; }

    }
}
