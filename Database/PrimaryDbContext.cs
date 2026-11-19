using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ObjectPool;
using InsoleApi.Database.Converter;
using InsoleApi.Database;

namespace InsoleApi.Database
{
    public class PrimaryDbContext : DbContext
    {
        static public string NPGSQL_URL = "";

        public PrimaryDbContext(DbContextOptions<PrimaryDbContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(NPGSQL_URL);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RecordEntity>().HasKey(record => new { record.UserId, record.Time });
            modelBuilder.Entity<PaymentAttemptEntity>().HasIndex(attempt => attempt.StripeCheckoutSessionId).IsUnique();
            modelBuilder.Entity<PaymentAttemptEntity>().HasIndex(attempt => attempt.StripePaymentIntentId).IsUnique();
            modelBuilder.Entity<PaymentAttemptEntity>().HasIndex(attempt => attempt.UserId);
            modelBuilder.Entity<StripeWebhookEventEntity>().HasIndex(webhookEvent => webhookEvent.PaymentAttemptId);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>().HaveConversion<DateTimeZoneConverter>();
        }

        public DbSet<UserEntity> Users { get; set; }

        public DbSet<RegistinfoTempEntity> RegistinfoTemps { get; set; }

        public DbSet<ResetPasswordTempEntity> ResetPasswordTemps { get; set; }

        public DbSet<ParamsEntity> Params { get; set; }

        public DbSet<BlogEntity> Blogs { get; set; }

        public DbSet<STLEntity> STLs { get; set; }

        public DbSet<ParamCacheEntity> ParamCaches { get; set; }

        public DbSet<CostEntity> Costs { get; set; }

        public DbSet<RecordEntity> Records { get; set; }

        public DbSet<GenerationUsageDailyEntity> GenerationUsageDaily { get; set; }

        public DbSet<PaymentAttemptEntity> PaymentAttempts { get; set; }

        public DbSet<StripeWebhookEventEntity> StripeWebhookEvents { get; set; }

    }
}
