using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("generation_usage_daily")]
    public class GenerationUsageDailyEntity
    {
        [Key]
        [Column("day_utc")]
        public DateOnly DayUtc { get; set; }

        [Column("user_generation_count")]
        public long UserGenerationCount { get; set; }

        [Column("failed_check_retry_count")]
        public long FailedCheckRetryCount { get; set; }

        [Column("created_at_utc")]
        public DateTime CreatedAtUtc { get; set; }

        [Column("updated_at_utc")]
        public DateTime UpdatedAtUtc { get; set; }
    }
}
