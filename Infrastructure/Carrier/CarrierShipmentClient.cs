using System.Net;
using System.Net.Http.Json;
using ShipmentService.Application.Ports;
using ShipmentService.Contracts;

namespace ShipmentService.Infrastructure.Carrier;

public sealed class CarrierShipmentClient : ICarrierShipmentClient
{
    private readonly HttpClient _httpClient;

    public CarrierShipmentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CreateCarrierShipmentResponse> CreateAsync(CreateCarrierShipmentRequest body, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/carrier-shipments")
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new PermanentCarrierException(error);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Carrier Service returned {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<CreateCarrierShipmentResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Carrier Service returned an empty response");
    }
}

public sealed class PermanentCarrierException : Exception
{
    public PermanentCarrierException(string message) : base(message) { }
}
