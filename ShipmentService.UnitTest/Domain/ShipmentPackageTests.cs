using ShipmentService.Domain;

namespace ShipmentService.UnitTest.Domain;

public class ShipmentPackageTests
{
    static ShipmentPackageItem ValidItem() => new(Guid.NewGuid(), 1);

    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var pkg = new ShipmentPackage(2, 3.0m, 40m, 30m, 20m, [ValidItem(), new ShipmentPackageItem(Guid.NewGuid(), 2)]);

        Assert.Equal(2, pkg.Sequence);
        Assert.Equal(3.0m, pkg.WeightKg);
        Assert.Equal(40m, pkg.HeightCm);
        Assert.Equal(30m, pkg.WidthCm);
        Assert.Equal(20m, pkg.LengthCm);
        Assert.Equal(2, pkg.Items.Count);
        Assert.NotEqual(Guid.Empty, pkg.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveSequence_Throws(int sequence) =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(sequence, 1m, 10m, 10m, 10m, [ValidItem()]));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveWeight_Throws(int weight) =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(1, weight, 10m, 10m, 10m, [ValidItem()]));

    [Fact]
    public void Constructor_ZeroHeight_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(1, 1m, 0m, 10m, 10m, [ValidItem()]));

    [Fact]
    public void Constructor_ZeroWidth_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(1, 1m, 10m, 0m, 10m, [ValidItem()]));

    [Fact]
    public void Constructor_ZeroLength_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(1, 1m, 10m, 10m, 0m, [ValidItem()]));

    [Fact]
    public void Constructor_EmptyItems_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentPackage(1, 1m, 10m, 10m, 10m, []));
}

public class ShipmentPackageItemTests
{
    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var skuId = Guid.NewGuid();

        var item = new ShipmentPackageItem(skuId, 3);

        Assert.Equal(skuId, item.SkuId);
        Assert.Equal(3, item.Quantity);
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Fact]
    public void Constructor_EmptySkuId_Throws() =>
        Assert.Throws<ArgumentException>(() => new ShipmentPackageItem(Guid.Empty, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveQuantity_Throws(int qty) =>
        Assert.Throws<ArgumentException>(() => new ShipmentPackageItem(Guid.NewGuid(), qty));
}
