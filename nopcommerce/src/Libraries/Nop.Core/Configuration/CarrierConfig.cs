namespace Nop.Core.Configuration;

public partial class CarrierConfig : IConfig
{
    /// <summary>Base URL of the external Carrier API (WireMock in dev/demo).</summary>
    public string BaseUrl { get; set; } = "http://carrier_api:8080";

    /// <summary>Per-request HTTP timeout in seconds before counting as a failure.</summary>
    public int TimeoutSeconds { get; set; } = 2;

    /// <summary>Number of failures in the sampling window before the circuit opens.</summary>
    public int CircuitBreakerFailuresBeforeBreaking { get; set; } = 3;

    /// <summary>How long (seconds) the circuit stays open before probing again.</summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public bool Enabled { get; set; } = true;
}
