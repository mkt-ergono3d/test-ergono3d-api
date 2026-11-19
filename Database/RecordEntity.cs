using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("record")]
    public class RecordEntity
    {

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("time")]
        public DateTime Time { get; set; }

    }
}
