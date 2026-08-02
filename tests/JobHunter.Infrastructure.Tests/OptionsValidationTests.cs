using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Scheduling;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Connection_strings_are_valid_when_the_required_pair_is_present()
    {
        var options = new ConnectionStringOptions { JobHunter = "Host=db", Messaging = "amqp://mq" };

        options.IsValid(out var error).ShouldBeTrue();
        error.ShouldBeNull();
    }

    [Fact]
    public void Connection_strings_fail_naming_the_missing_postgres_key()
    {
        var options = new ConnectionStringOptions { JobHunter = "", Messaging = "amqp://mq" };

        options.IsValid(out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("JobHunter");
    }

    [Fact]
    public void Connection_strings_fail_naming_the_missing_messaging_key()
    {
        var options = new ConnectionStringOptions { JobHunter = "Host=db", Messaging = "  " };

        options.IsValid(out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("Messaging");
    }

    [Fact]
    public void Cache_connection_string_is_optional()
    {
        var options = new ConnectionStringOptions { JobHunter = "Host=db", Messaging = "amqp://mq" };

        options.Cache.ShouldBeNull();
        options.IsValid(out _).ShouldBeTrue();
    }

    [Fact]
    public void Messaging_options_are_valid_with_a_connection_string()
    {
        var options = new MessagingOptions { ConnectionString = "amqp://mq" };

        options.IsValid(out var error).ShouldBeTrue();
        error.ShouldBeNull();
    }

    [Fact]
    public void Messaging_options_fail_without_a_connection_string()
    {
        var options = new MessagingOptions { ConnectionString = "" };

        options.IsValid(out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("ConnectionString");
    }

    [Fact]
    public void Messaging_options_default_the_operational_knobs()
    {
        var options = new MessagingOptions();

        options.AutoProvision.ShouldBeTrue();
        options.MaxDeliveryAttempts.ShouldBe(3);
        options.DeadLetterSuffix.ShouldBe(".dlq");
        options.ServiceName.ShouldBe("jobhunter-worker");
    }

    [Fact]
    public void Hangfire_options_default_to_the_hangfire_schema_and_a_disabled_server()
    {
        var options = new HangfireOptions();

        options.SchemaName.ShouldBe("hangfire");
        options.EnableServer.ShouldBeFalse();
        options.EnableDashboard.ShouldBeFalse();
        options.WorkerCount.ShouldBe(4);
    }

    [Fact]
    public void Infisical_options_are_complete_only_with_all_three_identity_fields()
    {
        new InfisicalOptions().IsComplete.ShouldBeFalse();
        new InfisicalOptions { ClientId = "id", ClientSecret = "secret" }.IsComplete.ShouldBeFalse();
        new InfisicalOptions { ClientId = "id", ClientSecret = "secret", ProjectId = "p" }
            .IsComplete.ShouldBeTrue();
    }
}
