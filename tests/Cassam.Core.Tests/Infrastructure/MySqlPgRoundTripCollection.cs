using Xunit;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// xUnit <see cref="ICollectionFixture{TFixture}"/> definition that
/// shares BOTH a <see cref="MySqlContainerFixture"/> and a
/// <see cref="PostgresContainerFixture"/> across the MySQL→PG
/// round-trip tests. xUnit cannot declare two collection fixtures on a
/// single class via the standard <c>[Collection(...)]</c> attribute, so
/// we wrap both fixtures in a single collection definition and pair the
/// test class with <c>[Collection(nameof(MySqlPgRoundTripCollection))]</c>.
///
/// <para>
/// Containers start lazily when the first test in the collection
/// requests the fixture and are torn down when the last test finishes.
/// Both containers are independent — they may run in parallel without
/// collision because xUnit serializes collections, not individual
/// containers.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class MySqlPgRoundTripCollection : ICollectionFixture<MySqlContainerFixture>,
                                                  ICollectionFixture<PostgresContainerFixture>
{
    /// <summary>Collection name referenced by <c>[Collection]</c>.</summary>
    public const string Name = "mysql-pg-roundtrip";
}