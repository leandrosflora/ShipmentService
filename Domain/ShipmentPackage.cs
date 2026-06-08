namespace ShipmentService.Domain;

public sealed class ShipmentPackage
{
    public Guid Id { get; private set; }
    public int Sequence { get; private set; }
    public decimal WeightKg { get; private set; }
    public decimal HeightCm { get; private set; }
    public decimal WidthCm { get; private set; }
    public decimal LengthCm { get; private set; }
    public List<ShipmentPackageItem> Items { get; private set; } = [];

    private ShipmentPackage() { }

    public ShipmentPackage(int sequence, decimal weightKg, decimal heightCm, decimal widthCm, decimal lengthCm, IEnumerable<ShipmentPackageItem> items)
    {
        var itemList = items.ToList();
        if (sequence <= 0) throw new ArgumentException("Sequence must be positive");
        if (weightKg <= 0) throw new ArgumentException("Weight must be positive");
        if (heightCm <= 0 || widthCm <= 0 || lengthCm <= 0) throw new ArgumentException("Dimensions must be positive");
        if (itemList.Count == 0) throw new ArgumentException("Package must contain items");

        Id = Guid.NewGuid();
        Sequence = sequence;
        WeightKg = weightKg;
        HeightCm = heightCm;
        WidthCm = widthCm;
        LengthCm = lengthCm;
        Items = itemList;
    }
}

public sealed class ShipmentPackageItem
{
    public Guid Id { get; private set; }
    public Guid SkuId { get; private set; }
    public int Quantity { get; private set; }

    private ShipmentPackageItem() { }

    public ShipmentPackageItem(Guid skuId, int quantity)
    {
        if (skuId == Guid.Empty) throw new ArgumentException("SkuId is required");
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive");

        Id = Guid.NewGuid();
        SkuId = skuId;
        Quantity = quantity;
    }
}
