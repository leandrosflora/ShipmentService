using Microsoft.EntityFrameworkCore;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Outbox;

namespace ShipmentService.Infrastructure.Persistence;

public sealed class ShipmentDbContext : DbContext
{
    public ShipmentDbContext(DbContextOptions<ShipmentDbContext> options) : base(options) { }

    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.ToTable("shipments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ShipmentRequestId).HasColumnName("shipment_request_id");
            entity.Property(x => x.OrderId).HasColumnName("order_id");
            entity.Property(x => x.BuyerId).HasColumnName("buyer_id");
            entity.Property(x => x.SellerId).HasColumnName("seller_id");
            entity.Property(x => x.ShippingPromiseId).HasColumnName("shipping_promise_id").HasMaxLength(200).IsRequired();
            entity.Property(x => x.RouteId).HasColumnName("route_id").HasMaxLength(200).IsRequired();
            entity.Property(x => x.CarrierCode).HasColumnName("carrier_code").HasMaxLength(80).IsRequired();
            entity.Property(x => x.ServiceLevelCode).HasColumnName("service_level_code").HasMaxLength(80).IsRequired();
            entity.Property(x => x.OriginNodeId).HasColumnName("origin_node_id");
            entity.Property(x => x.PromisedDeliveryDate).HasColumnName("promised_delivery_date");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
            entity.Property(x => x.ExternalShipmentId).HasColumnName("external_shipment_id").HasMaxLength(200);
            entity.Property(x => x.TrackingCode).HasColumnName("tracking_code").HasMaxLength(200);
            entity.Property(x => x.LabelObjectKey).HasColumnName("label_object_key").HasMaxLength(500);
            entity.Property(x => x.LabelSha256).HasColumnName("label_sha256").HasMaxLength(64);
            entity.Property(x => x.BookingAttempts).HasColumnName("booking_attempts");
            entity.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
            entity.Property(x => x.ProcessingToken).HasColumnName("processing_token");
            entity.Property(x => x.ProcessingLeaseUntil).HasColumnName("processing_lease_until");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.ReadyAt).HasColumnName("ready_at");
            entity.Property(x => x.CancelledAt).HasColumnName("cancelled_at");

            entity.HasIndex(x => x.ShipmentRequestId).IsUnique();
            entity.HasIndex(x => new { x.OrderId, x.Status });
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.ProcessingLeaseUntil });
            entity.HasIndex(x => x.ExternalShipmentId).IsUnique();

            entity.OwnsOne(x => x.Destination, address =>
            {
                address.Property(x => x.RecipientName).HasColumnName("recipient_name").HasMaxLength(200).IsRequired();
                address.Property(x => x.Street).HasColumnName("street").HasMaxLength(300).IsRequired();
                address.Property(x => x.Number).HasColumnName("number").HasMaxLength(50).IsRequired();
                address.Property(x => x.Complement).HasColumnName("complement").HasMaxLength(200);
                address.Property(x => x.District).HasColumnName("district").HasMaxLength(200).IsRequired();
                address.Property(x => x.City).HasColumnName("city").HasMaxLength(200).IsRequired();
                address.Property(x => x.State).HasColumnName("state").HasMaxLength(50).IsRequired();
                address.Property(x => x.PostalCode).HasColumnName("destination_postal_code").HasMaxLength(20).IsRequired();
                address.Property(x => x.Country).HasColumnName("country").HasMaxLength(3).IsRequired();
                address.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(50);
            });

            entity.OwnsMany(x => x.Packages, package =>
            {
                package.ToTable("shipment_packages");
                package.WithOwner().HasForeignKey("shipment_id");
                package.HasKey(x => x.Id);
                package.Property(x => x.Id).HasColumnName("id");
                package.Property(x => x.Sequence).HasColumnName("sequence");
                package.Property(x => x.WeightKg).HasColumnName("weight_kg").HasPrecision(12, 3);
                package.Property(x => x.HeightCm).HasColumnName("height_cm").HasPrecision(12, 2);
                package.Property(x => x.WidthCm).HasColumnName("width_cm").HasPrecision(12, 2);
                package.Property(x => x.LengthCm).HasColumnName("length_cm").HasPrecision(12, 2);
                package.HasIndex("shipment_id", nameof(ShipmentPackage.Sequence)).IsUnique();

                package.OwnsMany(x => x.Items, item =>
                {
                    item.ToTable("shipment_package_items");
                    item.WithOwner().HasForeignKey("shipment_package_id");
                    item.HasKey(x => x.Id);
                    item.Property(x => x.Id).HasColumnName("id");
                    item.Property(x => x.SkuId).HasColumnName("sku_id");
                    item.Property(x => x.Quantity).HasColumnName("quantity");
                });
            });
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasColumnName("message_id");
            entity.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(200).IsRequired();
            entity.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
            entity.Property(x => x.AggregateKey).HasColumnName("aggregate_key").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            entity.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
        });
    }
}
