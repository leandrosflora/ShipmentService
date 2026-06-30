using ShipmentService.Domain;

namespace ShipmentService.UnitTest.Domain;

public class ShipmentAddressTests
{
    [Fact]
    public void Constructor_ValidArgs_SetsAllProperties()
    {
        var addr = new ShipmentAddress("João Silva", "Rua A", "10", "Ap 1", "Centro", "São Paulo", "SP", "01310-100", "BR", "11999999999");

        Assert.Equal("João Silva", addr.RecipientName);
        Assert.Equal("Rua A", addr.Street);
        Assert.Equal("10", addr.Number);
        Assert.Equal("Ap 1", addr.Complement);
        Assert.Equal("Centro", addr.District);
        Assert.Equal("São Paulo", addr.City);
        Assert.Equal("SP", addr.State);
        Assert.Equal("01310-100", addr.PostalCode);
        Assert.Equal("BR", addr.Country);
        Assert.Equal("11999999999", addr.Phone);
    }

    [Fact]
    public void Constructor_NullOptionalFields_Succeeds()
    {
        var addr = new ShipmentAddress("Maria", "Av B", "200", null, "Bairro", "Rio", "RJ", "20040-020", "BR", null);

        Assert.Null(addr.Complement);
        Assert.Null(addr.Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankRecipientName_Throws(string name) =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentAddress(name, "St", "1", null, "District", "City", "SP", "01310-100", "BR", null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankPostalCode_Throws(string postalCode) =>
        Assert.Throws<ArgumentException>(() =>
            new ShipmentAddress("Name", "St", "1", null, "District", "City", "SP", postalCode, "BR", null));
}
