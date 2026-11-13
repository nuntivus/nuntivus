using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.UnitTests.Contracts.Destinations;

public sealed class AllDestinationConfigurationTests
{
    public static IEnumerable<object[]> Configurations()
    {
        yield return [DestinationType.Http, new HttpDestinationConfig("https://hooks.example.com/events", new HttpAuthentication("bearer", new HttpCredential("inline", "fixture-credential")))];
        yield return [DestinationType.Kafka, new KafkaDestinationConfig("broker:9093", "events", Credentials: new KafkaCredentials(Password: new DestinationCredential("inline", "fixture-credential")))];
        yield return [DestinationType.Nats, new NatsDestinationConfig("tls://broker.example.com:4222", "events", Credentials: new NatsCredentials(Token: new DestinationCredential("inline", "fixture-credential")))];
        yield return [DestinationType.RabbitMq, new RabbitMqDestinationConfig("amqps://broker.example.com", "events", "events.created", Credentials: new RabbitMqCredentials(Password: new DestinationCredential("inline", "fixture-credential")))];
        yield return [DestinationType.Sqs, new SqsDestinationConfig("us-east-1", "https://sqs.us-east-1.amazonaws.com/123/events", Credentials: new SqsCredentials("fixture-access-id", new DestinationCredential("inline", "fixture-credential")))];
    }

    private static DestinationConfigDispatcher Dispatcher()
    {
        var protector = new TestProtector();
        return new DestinationConfigDispatcher([
            new HttpDestinationConfigHandler(protector), new KafkaDestinationConfigHandler(protector),
            new NatsDestinationConfigHandler(protector), new RabbitMqDestinationConfigHandler(protector),
            new SqsDestinationConfigHandler(protector)]);
    }

    [Fact]
    public void Fixtures_cover_every_destination_enum_value() =>
        Assert.Equal(Enum.GetValues<DestinationType>().Order(), Configurations().Select(row => (DestinationType)row[0]).Order());

    [Theory]
    [MemberData(nameof(Configurations))]
    public void Build_protects_credentials_and_redaction_never_exposes_input(DestinationType type, DestinationConfig config)
    {
        var dispatcher = Dispatcher();
        var built = dispatcher.Build(config);
        Assert.True(built.IsSuccess, built.IsFailure ? built.Errors[0].Message : null);
        Assert.Equal(type, built.Value.Type);
        Assert.DoesNotContain("fixture-credential", built.Value.Configuration);
        Assert.Contains("protected.", built.Value.Configuration);
        using var stored = JsonDocument.Parse(built.Value.Configuration);
        var redacted = dispatcher.Redact(built.Value);
        Assert.True(redacted.IsSuccess);
        Assert.DoesNotContain("fixture-credential", redacted.Value);
        using var visible = JsonDocument.Parse(redacted.Value);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void Missing_endpoint_and_out_of_range_timeout_are_rejected(DestinationType type, DestinationConfig config)
    {
        DestinationConfig missing = config switch
        {
            HttpDestinationConfig value => value with { Url = "" },
            KafkaDestinationConfig value => value with { BootstrapServers = "" },
            NatsDestinationConfig value => value with { Url = "" },
            RabbitMqDestinationConfig value => value with { Uri = "" },
            SqsDestinationConfig value => value with { QueueUrl = "" },
            _ => throw new InvalidOperationException(type.ToString())
        };
        DestinationConfig timeout = config switch
        {
            HttpDestinationConfig value => value with { TimeoutSeconds = 0 },
            KafkaDestinationConfig value => value with { TimeoutSeconds = 0 },
            NatsDestinationConfig value => value with { TimeoutSeconds = 0 },
            RabbitMqDestinationConfig value => value with { TimeoutSeconds = 0 },
            SqsDestinationConfig value => value with { TimeoutSeconds = 0 },
            _ => throw new InvalidOperationException(type.ToString())
        };
        Assert.True(Dispatcher().Build(missing).IsFailure);
        Assert.True(Dispatcher().Build(timeout).IsFailure);
    }

    private sealed class TestProtector : IDestinationCredentialProtector
    {
        public Result<string> Protect(string value) => Result.Success("protected.fixture.ciphertext");
        public Result<string> Unprotect(string value) => Result.Success("fixture-credential");
    }
}
