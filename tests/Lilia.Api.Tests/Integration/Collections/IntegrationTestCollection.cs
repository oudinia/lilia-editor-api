using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Collections;

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<TestDatabaseFixture>
{
}
