using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// A single <see cref="AuthTestFixture"/> is shared by every test class.
///
/// This must be a collection fixture, not a class fixture: <c>IClassFixture</c>
/// constructs one instance per test class, and xunit runs those classes in
/// parallel. Four instances would each delete and re-insert the same fixture
/// users at the same time, so tests failed with duplicate-key and
/// connection-lost errors depending on scheduling.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthTestCollection : ICollectionFixture<AuthTestFixture>
{
    public const string Name = "auth";
}
