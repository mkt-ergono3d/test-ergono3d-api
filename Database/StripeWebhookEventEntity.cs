using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database;

[Table("stripe_webhook_event")]
public class StripeWebhookEventEntity
{
    [Key]
    [Column("stripe_event_id")]
    [MaxLength(255)]
    public required string StripeEventId { get; set; }

    [Column("event_type")]
    [MaxLength(128)]
    public required string EventType { get; set; }

    [Column("payment_attempt_id")]
    public Guid? PaymentAttemptId { get; set; }

    [Column("processed_time")]
    public DateTime ProcessedTime { get; set; }
}
