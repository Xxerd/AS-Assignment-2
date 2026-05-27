namespace Nop.Core.Configuration;

/// <summary>
/// RabbitMQ connection configuration for the VerdeMart integration layer.
/// Bound from the <c>RabbitMqConfig</c> section of <c>appsettings.json</c>.
/// </summary>
public partial class RabbitMqConfig : IConfig
{
    /// <summary>
    /// RabbitMQ broker hostname (e.g. <c>rabbitmq</c> when running under docker-compose, <c>localhost</c> for local dev).
    /// </summary>
    public string Host { get; protected set; } = "localhost";

    /// <summary>
    /// AMQP port. Defaults to 5672.
    /// </summary>
    public int Port { get; protected set; } = 5672;

    /// <summary>
    /// Username for the broker.
    /// </summary>
    public string Username { get; protected set; } = "guest";

    /// <summary>
    /// Password for the broker.
    /// </summary>
    public string Password { get; protected set; } = "guest";

    /// <summary>
    /// AMQP virtual host. Defaults to <c>/</c>.
    /// </summary>
    public string VirtualHost { get; protected set; } = "/";

    /// <summary>
    /// Default topic exchange used for outbound commerce events
    /// (e.g. <c>OrderConfirmed</c>, <c>OrderAssigned</c>).
    /// </summary>
    public string ExchangeName { get; protected set; } = "commerce.events";

    /// <summary>
    /// Whether the integration layer should attempt to connect on startup.
    /// Setting this to <c>false</c> keeps the publisher inert in environments
    /// where the broker is not available (e.g. unit tests).
    /// </summary>
    public bool Enabled { get; protected set; } = true;
}
