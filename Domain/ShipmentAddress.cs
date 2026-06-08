namespace ShipmentService.Domain;

public sealed class ShipmentAddress
{
    public string RecipientName { get; private set; } = default!;
    public string Street { get; private set; } = default!;
    public string Number { get; private set; } = default!;
    public string? Complement { get; private set; }
    public string District { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string State { get; private set; } = default!;
    public string PostalCode { get; private set; } = default!;
    public string Country { get; private set; } = default!;
    public string? Phone { get; private set; }

    private ShipmentAddress() { }

    public ShipmentAddress(string recipientName, string street, string number, string? complement, string district, string city, string state, string postalCode, string country, string? phone)
    {
        if (string.IsNullOrWhiteSpace(recipientName)) throw new ArgumentException("RecipientName is required");
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("PostalCode is required");

        RecipientName = recipientName;
        Street = street;
        Number = number;
        Complement = complement;
        District = district;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        Phone = phone;
    }
}
