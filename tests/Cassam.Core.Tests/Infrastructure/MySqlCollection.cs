using Xunit;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// xUnit <see cref="ICollectionFixture{TFixture}"/> that shares a single
/// <see cref="MySqlContainerFixture"/> across all the MySQL-touching
/// integration tests. Pairing with <see cref="PostgresCollection"/> (the
/// PostgreSQL equivalent) lets tests run both containers in parallel
/// without colliding on xUnit's per-collection serialization semantics.
///
/// <para>
/// Collection fixtures are scoped to the test class — every test in a
/// <c>[Collection("mysql")]</c>-decorated class shares one MySQL
/// container for the duration of the test run. Containers are torn down
/// when the last test class in the collection finishes.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlContainerFixture>
{
    /// <summary>Collection name referenced by <c>[Collection(...)]</c>.</summary>
    public const string Name = "mysql";
}