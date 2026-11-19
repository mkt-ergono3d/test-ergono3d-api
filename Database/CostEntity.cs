using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("cost")]
    public class CostEntity
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("remain")]
        public short Remain { get; set; }

    }
}
