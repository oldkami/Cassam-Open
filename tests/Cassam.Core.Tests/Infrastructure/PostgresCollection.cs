using Xunit;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// xUnit collection definition that shares a single
/// <see cref="PostgresContainerFixture"/> across every
/// <c>[Collection("Postgres collection")]</c> test class. One
/// container per test run instead of one per class keeps CI
/// image-pull cost from compounding when more integration
/// tests are added in later PRs.
///
/// <para>
/// Usage:
/// <code>
/// [Collection("Postgres collection")]
/// public class MyEntityIntegrationTests
/// {
///     private readonly PostgresContainerFixture _fixture;
///
///     public MyEntityIntegrationTests(PostgresContainerFixture fixture)
///     {
///         _fixture = fixture;
///     }
///
///     [Fact]
///     public async Task My_test() { ... }
/// }
/// </code>
/// </para>
///
/// <para>
/// Test isolation between classes is preserved by having each test
/// create its own <see cref="CassamDbContextFactory.CreateContext"/>
/// (a fresh DbContext with an independent change tracker); the
/// underlying database is the same but the tests do not share
/// entities in the change tracker. Tests that need stronger
/// isolation can wrap individual operations in a transaction and
/// roll back, or clean the relevant tables in the arrange phase.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    /// <summary>Collection name used by <c>[Collection]</c> attributes.</summary>
    public const string Name = "Postgres collection";
}
