using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database;

[Table("payment_attempt")]
public class PaymentAttemptEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("product_type")]
    [MaxLength(32)]
    public required string ProductType { get; set; }

    [Column("source_page")]
    [MaxLength(512)]
    public required string SourcePage { get; set; }

    [Column("cta_placement")]
    [MaxLength(128)]
    public required string CtaPlacement { get; set; }

    [Column("language")]
    [MaxLength(32)]
    public required string Language { get; set; }

    [Column("design_type")]
    [MaxLength(128)]
    public string? DesignType { get; set; }

    [Column("return_path")]
    [MaxLength(1024)]
    public required string ReturnPath { get; set; }

    [Column("status")]
    [MaxLength(32)]
    public required string Status { get; set; }

    [Column("stripe_checkout_session_id")]
    [MaxLength(255)]
    public string? StripeCheckoutSessionId { get; set; }

    [Column("stripe_payment_intent_id")]
    [MaxLength(255)]
    public string? StripePaymentIntentId { get; set; }

    [Column("amount")]
    public long? Amount { get; set; }

    [Column("currency")]
    [MaxLength(12)]
    public string? Currency { get; set; }

    [Column("failure_code")]
    [MaxLength(128)]
    public string? FailureCode { get; set; }

    [Column("created_time")]
    public DateTime CreatedTime { get; set; }

    [Column("updated_time")]
    public DateTime UpdatedTime { get; set; }
}

public static class PaymentAttemptStatus
{
    public const string Pending = "pending";
    public const string CheckoutCreated = "checkout_created";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
}
