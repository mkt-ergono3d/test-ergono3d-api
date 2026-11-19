using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("params")]
    public class ParamsEntity
    {

        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("step1")]
        public required string Step1 { get; set; }

        [Column("step2")]
        public required string Step2 { get; set; }

        [Column("step3")]
        public required string Step3 { get; set; }
    }
}
