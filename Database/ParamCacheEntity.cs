using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("param_cache")]
    public class ParamCacheEntity
    {

        [Column("id")]
        public Guid Id { get; set; }

        [Column("param")]
        public required string Param { get; set; }

        [Column("free_count")]
        public short FreeCount { get; set; }

    }
}
